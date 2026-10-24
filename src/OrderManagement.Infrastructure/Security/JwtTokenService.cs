using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Security;

public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email,
                ["role"] = user.Role.ToString(),
                ["jti"] = Guid.NewGuid().ToString()
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };
        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public IssuedRefreshToken CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var expires = clock.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);
        return new IssuedRefreshToken(token, Hash(token), expires);
    }

    public string Hash(string refreshToken) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
}
