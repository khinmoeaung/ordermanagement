using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OrderManagement.IntegrationTests;

public sealed record CapturedLog(string Category, LogLevel Level, string Message, string? ExceptionText);

/// <summary>
/// Records log entries, including the full text of any attached exception, so tests can assert what was (not) logged.
/// The app logs through Serilog, which replaces the standard logger providers, so a provider added to the host would
/// see nothing. Tests register <see cref="For{T}"/> as the <c>ILogger&lt;T&gt;</c> of the class they inspect instead.
/// </summary>
public sealed class CapturingLoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyCollection<CapturedLog> Entries => [.. _entries];

    public ILogger<T> For<T>() => new CapturingLogger<T>(typeof(T).FullName!, _entries);

    private sealed class CapturingLogger<T>(string category, ConcurrentQueue<CapturedLog> entries) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), exception?.ToString()));
    }
}
