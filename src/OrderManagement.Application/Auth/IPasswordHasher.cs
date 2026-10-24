using OrderManagement.Domain.Entities;

namespace OrderManagement.Application.Auth;

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}
