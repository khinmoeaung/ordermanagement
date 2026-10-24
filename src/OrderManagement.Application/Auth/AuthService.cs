using OrderManagement.Application.Common;
using OrderManagement.Application.Customers;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Auth;

public class AuthService(
    IUserRepository users,
    ICustomerRepository customers,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAuthService
{
    private const string InvalidCredentials = "Invalid credentials.";
    private const string InvalidRefreshToken = "Invalid refresh token.";

    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        var email = User.NormalizeEmail(r.Email);
        if (await users.EmailExistsAsync(email, ct))
            throw new ConflictException($"A user with email '{email}' already exists.");

        var now = UtcNow;
        var user = User.Create(email, UserRole.Customer, now);
        user.SetPasswordHash(passwordHasher.Hash(user, r.Password));
        var customer = Customer.Create(user, r.FullName, r.Phone, r.ShippingAddress);

        var response = IssueTokens(user, now, out var refreshToken);
        users.Add(user);
        customers.Add(customer);
        refreshTokens.Add(refreshToken);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest r, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(User.NormalizeEmail(r.Email), ct);
        if (user is null || !passwordHasher.Verify(user, r.Password))
            throw new AuthenticationFailedException(InvalidCredentials);

        var response = IssueTokens(user, UtcNow, out var refreshToken);
        refreshTokens.Add(refreshToken);
        await unitOfWork.SaveChangesAsync(ct);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest r, CancellationToken ct)
    {
        var now = UtcNow;
        var old = await refreshTokens.GetByHashAsync(tokenService.Hash(r.RefreshToken), ct)
                  ?? throw new AuthenticationFailedException(InvalidRefreshToken);

        if (old.RevokedAt is not null)
        {
            // Reuse of a rotated token: treat as stolen and revoke the whole family. Persist before throwing.
            foreach (var active in await refreshTokens.GetActiveByUserIdAsync(old.UserId, now, ct))
                active.Revoke(now);
            await unitOfWork.SaveChangesAsync(ct);
            throw new AuthenticationFailedException(InvalidRefreshToken);
        }

        if (!old.IsActive(now))
            throw new AuthenticationFailedException(InvalidRefreshToken);

        var user = await users.GetByIdAsync(old.UserId, ct)
                   ?? throw new AuthenticationFailedException(InvalidRefreshToken);

        var response = IssueTokens(user, now, out var replacement);
        old.Revoke(now, replacement.TokenHash);
        refreshTokens.Add(replacement);
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            // A concurrent request already rotated this token.
            throw new AuthenticationFailedException(InvalidRefreshToken);
        }

        return response;
    }

    public async Task LogoutAsync(LogoutRequest r, CancellationToken ct)
    {
        var now = UtcNow;
        var token = await refreshTokens.GetByHashAsync(tokenService.Hash(r.RefreshToken), ct);
        if (token is null || token.UserId != currentUser.UserId || !token.IsActive(now))
            return;

        token.Revoke(now);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private AuthResponse IssueTokens(User user, DateTime now, out RefreshToken refreshToken)
    {
        var access = tokenService.CreateAccessToken(user);
        var issued = tokenService.CreateRefreshToken();
        refreshToken = RefreshToken.Create(user.Id, issued.Hash, issued.ExpiresAt, now);
        return new AuthResponse(access.Token, access.ExpiresAt, issued.Token, issued.ExpiresAt);
    }
}
