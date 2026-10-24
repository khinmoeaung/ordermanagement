using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class FakeTokenService(TimeProvider time) : ITokenService
{
    private int _counter;

    public AccessToken CreateAccessToken(User user) =>
        new("at-" + user.Id, time.GetUtcNow().UtcDateTime.AddMinutes(15));

    public IssuedRefreshToken CreateRefreshToken()
    {
        var token = "rt-" + ++_counter;
        return new IssuedRefreshToken(token, Hash(token), time.GetUtcNow().UtcDateTime.AddDays(7));
    }

    public string Hash(string refreshToken) => "h(" + refreshToken + ")";
}
