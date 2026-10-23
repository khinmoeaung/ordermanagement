using Microsoft.Extensions.Time.Testing;
using OrderManagement.Application.Auth;
using OrderManagement.Application.Common;
using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;
using OrderManagement.UnitTests.Fakes;
using Shouldly;

namespace OrderManagement.UnitTests.Application;

public class AuthServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly InMemoryUserRepository _users = new();
    private readonly InMemoryCustomerRepository _customers = new();
    private readonly InMemoryRefreshTokenRepository _tokens = new();
    private readonly FakeUnitOfWork _uow = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeTokenService _tokenService;
    private readonly FakePasswordHasher _hasher = new();

    public AuthServiceTests() => _tokenService = new FakeTokenService(_time);

    private AuthService Sut(FakeCurrentUser? user = null) =>
        new(_users, _customers, _tokens, _tokenService, _hasher, user ?? new FakeCurrentUser(), _uow, _time);

    private static RegisterRequest Reg(string email = "bob@example.com") =>
        new(email, "Passw0rd1", "Bob Smith", "555-1234", "1 Main St");

    [Fact]
    public async Task Register_creates_customer_user_profile_and_tokens()
    {
        var res = await Sut().RegisterAsync(Reg(), Ct);

        var user = _users.Items.ShouldHaveSingleItem();
        user.Role.ShouldBe(UserRole.Customer);
        user.PasswordHash.ShouldBe("hashed:Passw0rd1");
        var customer = _customers.Items.ShouldHaveSingleItem();
        customer.UserId.ShouldBe(user.Id);
        customer.FullName.ShouldBe("Bob Smith");
        var rt = _tokens.Items.ShouldHaveSingleItem();
        rt.UserId.ShouldBe(user.Id);
        rt.TokenHash.ShouldBe("h(rt-1)");
        res.RefreshToken.ShouldBe("rt-1");
        res.AccessToken.ShouldBe("at-" + user.Id);
        _uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Register_duplicate_email_case_insensitive_conflict()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg(" Bob@Example.com "), Ct);
        await Should.ThrowAsync<ConflictException>(() => sut.RegisterAsync(Reg("BOB@example.com"), Ct));
        _users.Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Login_with_different_case_email_succeeds()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg("bob@example.com"), Ct);
        var res = await sut.LoginAsync(new LoginRequest("BOB@Example.com", "Passw0rd1"), Ct);
        res.RefreshToken.ShouldBe("rt-2");
        _tokens.Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Login_unknown_email_and_wrong_password_same_error()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg(), Ct);
        var a = await Should.ThrowAsync<AuthenticationFailedException>(
            () => sut.LoginAsync(new LoginRequest("nobody@example.com", "Passw0rd1"), Ct));
        var b = await Should.ThrowAsync<AuthenticationFailedException>(
            () => sut.LoginAsync(new LoginRequest("bob@example.com", "WrongPass1"), Ct));
        a.Message.ShouldBe("Invalid credentials.");
        b.Message.ShouldBe(a.Message);
    }

    [Fact]
    public async Task Refresh_rotates_token()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg(), Ct);
        var res = await sut.RefreshAsync(new RefreshRequest("rt-1"), Ct);

        var old = _tokens.Items.Single(t => t.TokenHash == "h(rt-1)");
        old.RevokedAt.ShouldNotBeNull();
        old.ReplacedByTokenHash.ShouldBe("h(rt-2)");
        res.RefreshToken.ShouldBe("rt-2");
        res.AccessToken.ShouldBe("at-" + _users.Items.Single().Id);
        _tokens.Items.Single(t => t.TokenHash == "h(rt-2)").IsActive(Now.UtcDateTime).ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_with_revoked_token_revokes_all_and_fails()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg(), Ct);
        await sut.RefreshAsync(new RefreshRequest("rt-1"), Ct);
        var saves = _uow.SaveCount;

        var ex = await Should.ThrowAsync<AuthenticationFailedException>(
            () => sut.RefreshAsync(new RefreshRequest("rt-1"), Ct));

        ex.Message.ShouldBe("Invalid refresh token.");
        _tokens.Items.Single(t => t.TokenHash == "h(rt-2)").RevokedAt.ShouldNotBeNull();
        _uow.SaveCount.ShouldBe(saves + 1);
    }

    [Fact]
    public async Task Refresh_with_expired_or_unknown_token_fails()
    {
        var sut = Sut();
        await sut.RegisterAsync(Reg(), Ct);
        await Should.ThrowAsync<AuthenticationFailedException>(() => sut.RefreshAsync(new RefreshRequest("nope"), Ct));

        _time.Advance(TimeSpan.FromDays(8));
        var ex = await Should.ThrowAsync<AuthenticationFailedException>(
            () => sut.RefreshAsync(new RefreshRequest("rt-1"), Ct));
        ex.Message.ShouldBe("Invalid refresh token.");
    }

    [Fact]
    public async Task Logout_revokes_own_token_and_ignores_others()
    {
        await Sut().RegisterAsync(Reg("a@example.com"), Ct);
        await Sut().RegisterAsync(Reg("b@example.com"), Ct);
        var a = _users.Items.Single(u => u.Email == "a@example.com");

        await Sut(FakeCurrentUser.Customer(a.Id)).LogoutAsync(new LogoutRequest("rt-2"), Ct);
        _tokens.Items.Single(t => t.TokenHash == "h(rt-2)").RevokedAt.ShouldBeNull();

        await Sut(FakeCurrentUser.Customer(a.Id)).LogoutAsync(new LogoutRequest("nope"), Ct);

        await Sut(FakeCurrentUser.Customer(a.Id)).LogoutAsync(new LogoutRequest("rt-1"), Ct);
        await Sut(FakeCurrentUser.Customer(a.Id)).LogoutAsync(new LogoutRequest("rt-1"), Ct);
        _tokens.Items.Single(t => t.TokenHash == "h(rt-1)").RevokedAt.ShouldNotBeNull();
        _tokens.Items.Single(t => t.TokenHash == "h(rt-2)").RevokedAt.ShouldBeNull();
    }
}
