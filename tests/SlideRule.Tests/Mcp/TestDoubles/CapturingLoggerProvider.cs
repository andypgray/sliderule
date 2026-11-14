using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SlideRule.Tests.Mcp.TestDoubles;

/// <summary>
///     An <see cref="ILoggerProvider" /> that records every logger call into a thread-safe queue, so a test
///     can assert on what the server logged. It exists to pin the central error contract in
///     <c>GlobalCallToolFilter</c>: a <c>UserErrorException</c> is surfaced without logging, while
///     any other exception is logged as exactly one warning. It also supplies the <see cref="Transcript" /> a
///     refused answer's red prints. Registered as the host's only logging provider (all built-in providers
///     cleared), so <see cref="Warnings" /> reflects the filter alone.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    /// <summary>The captured entries at <see cref="LogLevel.Warning" /> — the level the filter uses for unexpected failures.</summary>
    public IReadOnlyList<LogEntry> Warnings => _entries.Where(entry => entry.Level == LogLevel.Warning)
        .ToList();

    /// <summary>
    ///     Every captured entry in arrival order, one block each: level, category and message, then the exception's
    ///     <see cref="Exception.ToString" />, stack included, when the entry carries one; <c>(nothing logged)</c> when the
    ///     log is empty.
    /// </summary>
    internal string Transcript()
    {
        List<string> blocks = _entries.Select(Describe)
            .ToList();

        return blocks.Count == 0 ? "(nothing logged)" : string.Join(Environment.NewLine, blocks);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new CapturingLogger(categoryName, _entries);
    }

    public void Dispose()
    {
    }

    private static string Describe(LogEntry entry)
    {
        var head = $"{entry.Level} {entry.Category}: {entry.Message}";

        return entry.Exception is null ? head : head + Environment.NewLine + entry.Exception;
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), exception, category));
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
