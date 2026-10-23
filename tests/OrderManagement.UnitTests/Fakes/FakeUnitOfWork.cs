using OrderManagement.Application.Common;

namespace OrderManagement.UnitTests.Fakes;

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    /// <summary>When set, the next save throws this exception once.</summary>
    public Exception? ThrowOnNextSave { get; set; }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        if (ThrowOnNextSave is { } ex)
        {
            ThrowOnNextSave = null;
            throw ex;
        }

        SaveCount++;
        return Task.CompletedTask;
    }
}
