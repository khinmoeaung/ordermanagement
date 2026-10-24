using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Policies;

namespace OrderManagement.Infrastructure.Policies;

/// <summary>
/// Indexes the policies once in the background at startup, in Development and Docker only. If Qdrant or the
/// embedding server is down the API still starts; policy questions then answer 503 until a re-index succeeds.
/// </summary>
public sealed class PolicyIndexingHostedService(
    IPolicyIndexer indexer, IHostEnvironment environment, ILogger<PolicyIndexingHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Docker")) return;

        await Task.Yield();   // never hold up the host's startup, whatever the indexer does before its first await

        try
        {
            await indexer.ReindexAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Type names only: provider messages can carry URLs and keys.
            logger.LogWarning(
                "Startup policy indexing failed ({ExceptionType}, inner {InnerExceptionType}); policy search stays limited until a re-index succeeds",
                ex.GetType().Name, ex.InnerException?.GetType().Name ?? "none");
        }
    }
}
