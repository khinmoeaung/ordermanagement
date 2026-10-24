using Microsoft.AspNetCore.Identity;
using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Security;

public class AspNetPasswordHasher : Application.Auth.IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        _hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
}
