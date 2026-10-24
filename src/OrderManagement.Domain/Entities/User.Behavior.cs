using OrderManagement.Domain.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Domain.Entities;

public partial class User
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static User Create(string email, UserRole role, DateTime utcNow)
    {
        Guard.NotBlank(email, "Email");
        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = NormalizeEmail(email),
            Role = role,
            CreatedAt = utcNow
        };
    }

    public void SetPasswordHash(string hash)
    {
        Guard.NotBlank(hash, "Password hash");
        PasswordHash = hash;
    }
}
