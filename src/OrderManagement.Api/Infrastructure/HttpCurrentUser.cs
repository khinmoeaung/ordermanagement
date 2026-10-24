using OrderManagement.Application.Common;
using OrderManagement.Domain.Enums;

namespace OrderManagement.Api.Infrastructure;

/// <summary>Reads the caller from the validated JWT (<c>sub</c> and <c>role</c> claims).</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out Guid id) ? id : null;

    // Exact names only: Enum.TryParse alone would also accept numeric strings such as "0".
    public UserRole? Role =>
        accessor.HttpContext?.User.FindFirst("role")?.Value is { } value
        && Enum.TryParse(value, ignoreCase: false, out UserRole role)
        && role.ToString() == value
            ? role
            : null;

    public bool IsAdmin => Role == UserRole.Admin;
}
