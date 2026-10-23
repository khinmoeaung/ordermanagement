using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    public List<RefreshToken> Items { get; } = [];

    public Task<RefreshToken?> GetByHashAsync(string hash, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(t => t.TokenHash == hash));

    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserIdAsync(Guid userId, DateTime utcNow, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>(Items.Where(t => t.UserId == userId && t.IsActive(utcNow)).ToList());

    public void Add(RefreshToken token) => Items.Add(token);
}
