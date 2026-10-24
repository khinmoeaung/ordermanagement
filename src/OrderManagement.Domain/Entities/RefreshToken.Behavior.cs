using OrderManagement.Domain.Common;

namespace OrderManagement.Domain.Entities;

public partial class RefreshToken
{
    public static RefreshToken Create(Guid userId, string hash, DateTime expiresAt, DateTime utcNow)
    {
        Guard.NotBlank(hash, "Token hash");
        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            CreatedAt = utcNow
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAt == null && utcNow < ExpiresAt;

    public void Revoke(DateTime utcNow, string? replacedByHash = null)
    {
        if (RevokedAt != null) return;
        RevokedAt = utcNow;
        ReplacedByTokenHash = replacedByHash;
    }
}
