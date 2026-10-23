namespace OrderManagement.Application.Common;

/// <summary>
/// Implementations translate concurrency and duplicate-key errors into ConflictException.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct);
}
