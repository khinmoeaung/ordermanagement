using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Auth;

public record AccessToken(string Token, DateTime ExpiresAt);

public record IssuedRefreshToken(string Token, string Hash, DateTime ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);
    IssuedRefreshToken CreateRefreshToken();
    string Hash(string refreshToken);
}
