using Microsoft.EntityFrameworkCore;

using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.Infrastructure.Persistence.Repositories;

public sealed class RefreshTokenRepository(AppDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string hash, CancellationToken ct) =>
        context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

    public async Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(
        Guid userId, DateTime utcNow, CancellationToken ct) =>
        await context.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > utcNow)
            .ToListAsync(ct);

    public void Add(RefreshToken token) => context.RefreshTokens.Add(token);
}
