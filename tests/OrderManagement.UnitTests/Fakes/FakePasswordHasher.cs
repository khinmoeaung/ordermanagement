using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(User user, string password) => "hashed:" + password;

    public bool Verify(User user, string password) => user.PasswordHash == "hashed:" + password;
}
