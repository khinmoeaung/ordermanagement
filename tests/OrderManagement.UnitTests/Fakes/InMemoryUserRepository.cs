using OrderManagement.Application.Auth;
using OrderManagement.Domain.Entities;

namespace OrderManagement.UnitTests.Fakes;

public sealed class InMemoryUserRepository : IUserRepository
{
    public List<User> Items { get; } = [];

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Id == id));

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Items.FirstOrDefault(u => u.Email == normalizedEmail));

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken ct) =>
        Task.FromResult(Items.Any(u => u.Email == normalizedEmail));

    public void Add(User user) => Items.Add(user);
}
