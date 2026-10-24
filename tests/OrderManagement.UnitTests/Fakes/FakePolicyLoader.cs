using OrderManagement.Application.Policies;

namespace OrderManagement.UnitTests.Fakes;

public sealed class FakePolicyLoader : IPolicyLoader
{
    public List<PolicySourceFile> Files { get; } = [];

    public Task<IReadOnlyList<PolicySourceFile>> LoadAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PolicySourceFile>>(Files.ToList());
}
