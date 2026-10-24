using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OrderManagement.Domain.Entities;
using OrderManagement.Domain.Enums;
using OrderManagement.Infrastructure.Security;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string Key = "0123456789abcdef0123456789abcdef";

    private static JwtOptions Options() => new()
    {
        Issuer = "iss",
        Audience = "aud",
        SigningKey = Key
    };

    private static JwtTokenService Make() =>
        new(Microsoft.Extensions.Options.Options.Create(Options()), new FakeTimeProvider(Start));

    [Fact]
    public async Task Access_token_contains_sub_email_role_jti_and_expiry()
    {
        var user = User.Create("A@Example.com", UserRole.Admin, Start.UtcDateTime);
        var access = Make().CreateAccessToken(user);

        access.ExpiresAt.ShouldBe(Start.UtcDateTime.AddMinutes(15));

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(access.Token, new TokenValidationParameters
        {
            ValidIssuer = "iss",
            ValidAudience = "aud",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            LifetimeValidator = (_, _, _, _) => true
        });
        result.IsValid.ShouldBeTrue();
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.GetClaim("sub").Value.ShouldBe(user.Id.ToString());
        jwt.GetClaim("email").Value.ShouldBe("a@example.com");
        jwt.GetClaim("role").Value.ShouldBe("Admin");
        Guid.TryParse(jwt.GetClaim("jti").Value, out _).ShouldBeTrue();
        jwt.ValidTo.ShouldBe(access.ExpiresAt);
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_hash_is_deterministic()
    {
        var svc = Make();
        var a = svc.CreateRefreshToken();
        var b = svc.CreateRefreshToken();

        a.Token.ShouldNotBe(b.Token);
        svc.Hash(a.Token).ShouldBe(svc.Hash(a.Token));
        svc.Hash(a.Token).ShouldBe(a.Hash);
        a.Hash.Length.ShouldBe(64);
        a.Hash.ShouldBe(a.Hash.ToLowerInvariant());
    }

    [Fact]
    public void Refresh_token_expires_after_configured_days()
    {
        Make().CreateRefreshToken().ExpiresAt.ShouldBe(Start.UtcDateTime.AddDays(7));
    }

    [Fact]
    public void JwtOptions_rejects_short_key()
    {
        var o = Options();
        o.SigningKey = new string('x', 31);
        JwtOptions.Validate(o).ShouldBeFalse();
        o.SigningKey = new string('x', 32);
        JwtOptions.Validate(o).ShouldBeTrue();
    }

    [Fact]
    public void AspNetPasswordHasher_verifies_right_password_and_rejects_wrong()
    {
        var hasher = new AspNetPasswordHasher();
        var user = User.Create("a@example.com", UserRole.Customer, Start.UtcDateTime);
        var hash = hasher.Hash(user, "Passw0rd!");
        user.SetPasswordHash(hash);

        hasher.Verify(user, "Passw0rd!").ShouldBeTrue();
        hasher.Verify(user, "wrong").ShouldBeFalse();
    }
}
