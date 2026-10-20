using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     Configures Serilog file logging for post-mortem debugging of catastrophic crashes that can't reach the MCP
///     client.
/// </summary>
internal static class SerilogConfiguration
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SessionId}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    /// <summary>Absolute path to the daily-rolling log directory, beside the extraction cache.</summary>
    internal static string LogDirectory => Path.Combine(CacheLocations.ProductDataRoot(), "logs");

    /// <summary>
    ///     Creates the static <see cref="Log.Logger" /> with a daily rolling file sink. Call before any host
    ///     building so crash handlers can use it immediately.
    /// </summary>
    /// <remarks>
    ///     <paramref name="rawLogLevel" /> is the unparsed <c>SLIDERULE_LOG_LEVEL</c> value, and
    ///     <paramref name="sessionId" /> the launching agent session's id, or null for a generated one; a
    ///     launcher-supplied id correlates the lines with the session that started the server.
    /// </remarks>
    public static void InitializeFileLogger(string? rawLogLevel, string? sessionId)
    {
        LogEventLevel minimumLevel = ParseLogLevel(rawLogLevel);
        string resolvedSessionId = sessionId is { Length: > 0 } ? sessionId : Guid.NewGuid().ToString("N")[..8];

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.WithProperty("SessionId", resolvedSessionId)
            .WriteTo.File(
                Path.Combine(LogDirectory, "sliderule-mcp-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                // Concurrent server processes write to the same daily file (a shared mutex serialises
                // writes); the [{SessionId}] field then disambiguates interleaved lines.
                shared: true,
                outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    /// <summary>Registers process-level crash handlers that log fatal errors and flush before exit.</summary>
    public static void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Log.Fatal(ex, "Unhandled exception");

            Log.CloseAndFlush();
        };

        // Non-fatal: log but don't close the logger — the process continues running.
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error(e.Exception, "Unobserved task exception");
    }

    /// <summary>
    ///     Adds Serilog and console (stderr) logging to the host builder. Console goes to stderr because
    ///     stdout is reserved for the MCP JSON-RPC protocol.
    /// </summary>
    public static void AddSerilogLogging(this HostApplicationBuilder builder)
    {
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSerilog();
    }

    /// <summary>
    ///     Parses a <c>SLIDERULE_LOG_LEVEL</c> value into a Serilog level, accepting both
    ///     <see cref="LogLevel" /> and <see cref="LogEventLevel" /> names and falling back to
    ///     <see cref="LogEventLevel.Warning" /> for null, blank, or unrecognised input.
    /// </summary>
    internal static LogEventLevel ParseLogLevel(string? envValue)
    {
        if (string.IsNullOrWhiteSpace(envValue)) return LogEventLevel.Warning;

        // Enum.TryParse binds a numeric string ("99") to an undefined value; Enum.IsDefined keeps it on the fallback.
        if (Enum.TryParse(envValue, true, out LogLevel msLevel) && Enum.IsDefined(msLevel)) return LevelConvert.ToSerilogLevel(msLevel);

        if (Enum.TryParse(envValue, true, out LogEventLevel serilogLevel) && Enum.IsDefined(serilogLevel)) return serilogLevel;

        return LogEventLevel.Warning;
    }
}
