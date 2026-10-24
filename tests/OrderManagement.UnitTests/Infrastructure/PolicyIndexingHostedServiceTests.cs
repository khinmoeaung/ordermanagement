using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderManagement.Application.Policies;
using OrderManagement.Infrastructure.Policies;
using Shouldly;

namespace OrderManagement.UnitTests.Infrastructure;

public class PolicyIndexingHostedServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class FakeIndexer(Func<CancellationToken, Task<ReindexResult>>? behavior = null) : IPolicyIndexer
    {
        public int Calls;

        public Task<ReindexResult> ReindexAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return behavior is null ? Task.FromResult(new ReindexResult(1, 0, 0, 0, 1)) : behavior(ct);
        }
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ListLogger : ILogger<PolicyIndexingHostedService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " | " + exception)));
        }
    }

    private static async Task RunToCompletionAsync(PolicyIndexingHostedService service)
    {
        await service.StartAsync(Ct);
        if (service.ExecuteTask is { } task) await task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await service.StopAsync(Ct);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Docker")]
    public async Task Indexes_once_in_development_and_in_docker(string environment)
    {
        var indexer = new FakeIndexer();

        await RunToCompletionAsync(new PolicyIndexingHostedService(indexer, new FakeEnvironment(environment), new ListLogger()));

        indexer.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public async Task Does_nothing_in_testing_and_in_production(string environment)
    {
        var indexer = new FakeIndexer();

        await RunToCompletionAsync(new PolicyIndexingHostedService(indexer, new FakeEnvironment(environment), new ListLogger()));

        indexer.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task An_indexer_exception_is_swallowed_so_startup_continues()
    {
        var logger = new ListLogger();
        var indexer = new FakeIndexer(_ => throw new PolicySearchUnavailableException(new HttpRequestException("secret-host:11434 refused")));

        await RunToCompletionAsync(new PolicyIndexingHostedService(indexer, new FakeEnvironment("Development"), logger));

        indexer.Calls.ShouldBe(1);
        var warning = logger.Entries.Single(e => e.Level == LogLevel.Warning);
        warning.Message.ShouldContain(nameof(PolicySearchUnavailableException));
        warning.Message.ShouldContain(nameof(HttpRequestException));
        warning.Message.ShouldNotContain("secret-host");   // type names only: messages can carry URLs
    }

    [Fact]
    public async Task Cancellation_is_not_logged_as_a_failure()
    {
        var logger = new ListLogger();
        var started = new TaskCompletionSource();
        var indexer = new FakeIndexer(async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return new ReindexResult(0, 0, 0, 0, 0);
        });
        var service = new PolicyIndexingHostedService(indexer, new FakeEnvironment("Development"), logger);

        await service.StartAsync(Ct);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        await service.StopAsync(Ct);

        logger.Entries.ShouldNotContain(e => e.Level >= LogLevel.Warning);
    }
}
