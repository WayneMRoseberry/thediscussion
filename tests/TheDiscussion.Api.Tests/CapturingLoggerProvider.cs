using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace TheDiscussion.Api.Tests;

/// <summary>
/// Records every log entry the API writes. Each entry keeps its structured fields and can be rendered
/// with the same <see cref="ConsoleFormatter"/> the console logger uses, so tests can inspect the real JSON output.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLogEntry> entries = new();

    public IReadOnlyCollection<CapturedLogEntry> Entries => entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<string, object?>();

            sink.Enqueue(new CapturedLogEntry(
                category,
                logLevel,
                eventId,
                fields,
                consoleFormatter =>
                {
                    using var writer = new StringWriter();
                    var entry = new LogEntry<TState>(logLevel, category, eventId, state, exception, formatter);
                    consoleFormatter.Write(in entry, new LoggerExternalScopeProvider(), writer);
                    return writer.ToString();
                }));
        }
    }
}

public sealed record CapturedLogEntry(
    string Category,
    LogLevel Level,
    EventId EventId,
    IReadOnlyDictionary<string, object?> Fields,
    Func<ConsoleFormatter, string> Render);
