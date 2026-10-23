using OrderManagement.Application.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.UnitTests.Fakes;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public UserRole? Role { get; set; }
    public bool IsAdmin => Role == UserRole.Admin;

    public static FakeCurrentUser Admin() => new() { UserId = Guid.CreateVersion7(), Role = UserRole.Admin };

    public static FakeCurrentUser Customer(Guid userId) => new() { UserId = userId, Role = UserRole.Customer };
}
