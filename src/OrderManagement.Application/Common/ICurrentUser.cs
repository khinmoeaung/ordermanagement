using OrderManagement.Domain.Enums;

namespace OrderManagement.Application.Common;

public interface ICurrentUser
{
    Guid? UserId { get; }
    UserRole? Role { get; }
    bool IsAdmin { get; }
}
