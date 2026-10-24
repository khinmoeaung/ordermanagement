using OrderManagement.Domain.Entities;
using Shouldly;

namespace OrderManagement.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RefreshToken_IsActive_false_when_revoked_or_expired()
    {
        var userId = Guid.CreateVersion7();
        var token = RefreshToken.Create(userId, "hash", Now.AddDays(7), Now);

        token.Id.ShouldNotBe(Guid.Empty);
        token.UserId.ShouldBe(userId);
        token.CreatedAt.ShouldBe(Now);
        token.IsActive(Now).ShouldBeTrue();
        token.IsActive(Now.AddDays(7)).ShouldBeFalse();

        token.Revoke(Now.AddHours(1), "next");
        token.IsActive(Now.AddHours(2)).ShouldBeFalse();
        token.RevokedAt.ShouldBe(Now.AddHours(1));
        token.ReplacedByTokenHash.ShouldBe("next");
    }

    [Fact]
    public void Revoking_an_already_revoked_token_changes_nothing()
    {
        var token = RefreshToken.Create(Guid.CreateVersion7(), "hash", Now.AddDays(7), Now);
        token.Revoke(Now.AddHours(1), "first");

        token.Revoke(Now.AddHours(2), "second");

        token.RevokedAt.ShouldBe(Now.AddHours(1));
        token.ReplacedByTokenHash.ShouldBe("first");
    }
}
