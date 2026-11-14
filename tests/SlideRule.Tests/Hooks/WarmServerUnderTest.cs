using Microsoft.Extensions.Logging;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Hooks.Handoff;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.SpecLoading;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Sessions;
using SlideRule.Tests.Mcp.TestDoubles;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     A real <see cref="HandoffServer" /> in this process, over a real <see cref="WorkspaceSession" /> bound to one
///     solution, owning the real endpoint for its identity: the server a parity row's hook reaches.
/// </summary>
/// <remarks>
///     Composed as <c>sliderule mcp</c> composes it, from the same types, with a capturing logger so a row can read
///     the outcome the server logged for each request.
/// </remarks>
internal sealed class WarmServerUnderTest : IAsyncDisposable
{
    private readonly ILoggerFactory _loggers;
    private readonly Task _run;
    private readonly CancellationTokenSource _stop;

    private WarmServerUnderTest(
        WorkspaceSession session, WarmWorkAdmission admission, HandoffBinding binding,
        FakeEnvironment environment, CapturingLoggerProvider logs, ILoggerFactory loggers, CancellationTokenSource stop,
        Task run)
    {
        Session = session;
        Admission = admission;
        Binding = binding;
        Environment = environment;
        Logs = logs;
        _loggers = loggers;
        _stop = stop;
        _run = run;
    }

    public WorkspaceSession Session { get; }

    public WarmWorkAdmission Admission { get; }

    public HandoffBinding Binding { get; }

    public FakeEnvironment Environment { get; }

    public CapturingLoggerProvider Logs { get; }

    /// <summary>
    ///     Each request's outcome as the server logged it, in order: <c>served</c>, or <c>declined</c> and the
    ///     reason.
    /// </summary>
    public IReadOnlyList<string> Outcomes => Logs.Entries
        .Select(static entry => entry.Message)
        .Where(static message => message.StartsWith("Hand-off served", StringComparison.Ordinal)
                                 || message.StartsWith("Hand-off declined", StringComparison.Ordinal))
        .Select(static message => message["Hand-off ".Length..message.IndexOf(" in ", StringComparison.Ordinal)])
        .ToList();

    /// <summary>
    ///     Starts a server bound to <paramref name="solution" /> and <paramref name="spec" /> from
    ///     <paramref name="workingDirectory" />, and, unless <paramref name="warm" /> is false, loads its workspace
    ///     through the binding's warm-up check before returning.
    /// </summary>
    public static async Task<WarmServerUnderTest> StartAsync(
        string solution, string? spec, string workingDirectory, bool warm = true)
    {
        var environment = new FakeEnvironment();
        var logs = new CapturingLoggerProvider();
        ILoggerFactory loggers = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var session = new WorkspaceSession(static _ => { });
        var source = new WarmSolutionSource(session, new SessionFragmentStore(), new SpecModelCache(), new SpecResolutionCache());
        var admission = new WarmWorkAdmission();
        HandoffBinding binding = HandoffBinding.For(new McpServerBinding(solution, spec, workingDirectory), environment);
        var stop = new CancellationTokenSource();
        var server = new HandoffServer(
            session, source, admission, environment, binding, loggers.CreateLogger<HandoffServer>(), stop.Token);

        // Ownership is taken before the server's first await, so the endpoint is there once this returns.
        Task run = server.RunAsync(CancellationToken.None);
        var started = new WarmServerUnderTest(session, admission, binding, environment, logs, loggers, stop, run);
        if (warm) await new InProcessHookCheck(source, environment).RunAsync(binding.WarmUp, Ct);

        return started;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _run;
        await Session.DisposeAsync();
        Admission.Dispose();
        _loggers.Dispose();
        _stop.Dispose();
    }
}
