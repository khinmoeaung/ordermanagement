using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Auth;

public record RegisterRequest(string Email, string Password, string FullName, string Phone, string ShippingAddress);

public record LoginRequest(string Email, string Password);

public record RefreshRequest(string RefreshToken);

public record LogoutRequest(string RefreshToken);

public record AuthResponse(string AccessToken, DateTime AccessTokenExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct);
    void Add(User user);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string hash, CancellationToken ct);
    Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, DateTime utcNow, CancellationToken ct);
    void Add(RefreshToken token);
}
