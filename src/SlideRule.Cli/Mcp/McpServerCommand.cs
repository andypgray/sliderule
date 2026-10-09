using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Cli.Mcp.Prompts;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.SpecLoading;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Roslyn.Sessions;

namespace SlideRule.Cli.Mcp;

/// <summary>
///     The <c>sliderule mcp</c> entry point: a stdio MCP server bound to one solution + spec, exposing
///     the <c>arch_*</c> tools (thin shells over the same runners the CLI commands use).
/// </summary>
internal static class McpServerCommand
{
    /// <summary>
    ///     Runs the stdio server until <paramref name="ct" /> is cancelled or the transport closes,
    ///     returning the process exit code.
    /// </summary>
    /// <param name="binding">The solution + spec this server answers for, and the run policy its tools apply.</param>
    /// <param name="error">Where the interactive-launch refusal is written; never stdout, which carries JSON-RPC.</param>
    /// <param name="ct">Cancels the host run.</param>
    public static async Task<int> RunAsync(McpServerBinding binding, TextWriter error, CancellationToken ct)
    {
        if (!Console.IsInputRedirected)
        {
            // A human ran the tool at a terminal: don't hang on a silent stdio server.
            await error.WriteLineAsync("sliderule mcp is an MCP stdio server; it is started by an MCP client, not interactively.");
            await error.WriteLineAsync("Configure it in your MCP client (command \"sliderule mcp <solution> --spec <spec>\").");
            return ExitCodes.Error;
        }

        // The one env seam, built here because the bind check, the logger and both watchdogs are configured from
        // process state before there is a host to inject it from. The bind check takes the seam and the others
        // take their values as arguments rather than reading at the leaf, so the reads that shape a server's
        // lifetime all happen on this stretch of the startup path.
        var environment = new SystemEnvironment();

        // Fail fast on an argument that cannot be discovered; keep the reason and start anyway when there was
        // no argument to be wrong about. See ResolveBoundSolution for why the two halves differ.
        string? bindingFailure = ResolveBoundSolution(binding, environment);

        // A real MCP client launched us over piped stdio. Bring up the file logger and crash handlers
        // before host building so a catastrophic startup failure still lands in the post-mortem log.
        SerilogConfiguration.InitializeFileLogger(
            environment.GetVariable(SlideRuleEnvVars.LogLevel),
            environment.GetVariable(SlideRuleEnvVars.ClaudeCodeSessionId));
        SerilogConfiguration.RegisterCrashHandlers();

        // Watchers first, so they cover the whole lifetime including MSBuild registration.
        ParentProcessWatcher.Start(IsTrue(environment, SlideRuleEnvVars.DisableParentWatch));
        IdleTimeoutWatchdog.Start(environment.GetVariable(SlideRuleEnvVars.IdleTimeoutMinutes));

        EnsureMsBuildRegistered();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.AddSerilogLogging();

        builder.Services.AddSingleton<IEnvironment>(environment);
        builder.Services.AddSingleton(binding);
        AddToolServices(builder.Services);

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInstructions = ServerInstructions.For(bindingFailure);
                options.ServerInfo = new Implementation
                {
                    Name = "sliderule",
                    Title = "SlideRule architecture spec",
                    Version = ServerVersion.SemVer
                };
            })
            .WithStdioServerTransport()
            .WithCoercingTools()
            .WithPrompts<ArchPrompts>()
            .WithGlobalCallToolFilter(bindingFailure);

        await builder.Build().RunAsync(ct);
        return ExitCodes.Clean;
    }

    /// <summary>
    ///     Registers everything the <c>arch_*</c> tools resolve — the warm-workspace services and the
    ///     response-fitting pair — in one place, so the production server and the in-process test harness
    ///     compose the same graph. A service added here reaches both; a service added at a call site reaches
    ///     one, which is the drift this method exists to prevent.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The <see cref="ISolutionSource" /> is warm by default — one session reconciled per call across the
    ///         server's lifetime, one store paired with it — unless
    ///         <see cref="SlideRuleEnvVars.DisableWarmWorkspace" /> is <c>true</c>, which resolves the cold
    ///         one-shot <see cref="ColdSolutionSource" /> instead. That flag is read through the
    ///         <see cref="IEnvironment" /> seam, never <c>System.Environment</c>, so the test harness can drive
    ///         both paths without touching real process state. The warm branch registers the session's disposer with
    ///         <see cref="ServerShutdown" /> lazily, from inside the factory, so a run that never builds a warm
    ///         source wires no disposer.
    ///     </para>
    ///     <para>
    ///         The <see cref="IResponseFitter" /> is the budgeted one — this is the surface with a channel to
    ///         overrun — and <see cref="IResponseBudget" /> supplies the cap it fits against. Singletons both,
    ///         like everything else here: <c>di/no-captive-dependencies</c> holds because they depend only on
    ///         the singleton <see cref="IEnvironment" />.
    ///     </para>
    /// </remarks>
    internal static void AddToolServices(IServiceCollection services)
    {
        services.AddSingleton<IResponseBudget, ResponseBudget>();
        services.AddSingleton<IResponseFitter, BudgetedResponseFitter>();

        services.AddSingleton(provider =>
        {
            ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger<WorkspaceSession>();
            return new WorkspaceSession(message => logger.LogInformation("{ReconcileEvent}", message));
        });

        // The store and the two spec caches live as long as the one session, and are registered even when the
        // cold source is chosen, so a test can resolve them; none is populated until the warm source is built
        // and a tool call runs.
        services.AddSingleton(_ => new SessionFragmentStore());

        services.AddSingleton(_ => new SpecModelCache());

        services.AddSingleton(_ => new SpecResolutionCache());

        services.AddSingleton<ISolutionSource>(provider =>
        {
            var environment = provider.GetRequiredService<IEnvironment>();
            if (IsTrue(environment, SlideRuleEnvVars.DisableWarmWorkspace)) return new ColdSolutionSource();

            var session = provider.GetRequiredService<WorkspaceSession>();
            var store = provider.GetRequiredService<SessionFragmentStore>();
            var specs = provider.GetRequiredService<SpecModelCache>();
            var resolutions = provider.GetRequiredService<SpecResolutionCache>();
            ServerShutdown.RegisterDisposer(session.DisposeAsync);
            return new WarmSolutionSource(session, store, specs, resolutions);
        });
    }

    // The spelling every SLIDERULE_* opt-out takes: the literal "true", case-insensitively, and nothing
    // else — so a value nobody meant as a flag leaves the behaviour on.
    private static bool IsTrue(IEnvironment environment, string variable)
    {
        return string.Equals(environment.GetVariable(variable), "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Discovers the bound solution (an explicit path, a directory, or a walk-up), returning null on
    ///     success and the refusal text when a walk-up with no argument found nothing.
    /// </summary>
    /// <remarks>
    ///     The two halves differ, and the difference is the point. <b>An argument that does not resolve is fatal</b>:
    ///     the operator named something and it is wrong, so the
    ///     <see cref="UserErrorException" /> propagates to exit 2 at <c>CommandEntryPoint</c>. Starting
    ///     instead would give a server that errors on every tool call and, with stdin redirected but never
    ///     closed under a test host, hang the whole run. <b>No argument is the documented-optional path</b>,
    ///     so its walk-up failing is a condition to report, not a launch to abort: dying here reaches the
    ///     client as "the server failed to start" and no reason at all, since the process is gone before
    ///     <c>initialize</c> can answer. The server starts unbound instead and says why through
    ///     <see cref="ServerInstructions.For" /> and through every tool call, which re-runs the identical
    ///     discovery and gets the identical message, followed by the CLI recovery
    ///     <see cref="GlobalCallToolFilter.WithGlobalCallToolFilter" /> appends whenever this value is
    ///     non-null. One returned value feeds both channels, which is why it is held in a local rather than
    ///     recomputed per consumer: the handshake and the tool replies cannot then disagree about the bind.
    ///     Whitespace splits with null because <see cref="ModelPipeline.DiscoverSolution" /> already treats
    ///     the two alike: what makes an argument fatal is discovery actually honouring it.
    ///     NoInlining keeps it consistent with the quarantine style below; SolutionDiscovery is pure file I/O
    ///     with no MSBuildWorkspace touch, so it is safe before registration.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? ResolveBoundSolution(McpServerBinding binding, IEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(binding.Solution))
        {
            ModelPipeline.DiscoverSolution(binding.Solution, binding.WorkingDirectory, environment);
            return null;
        }

        try
        {
            ModelPipeline.DiscoverSolution(null, binding.WorkingDirectory, environment);
            return null;
        }
        catch (UserErrorException ex)
        {
            return ex.Message;
        }
    }

    // JIT quarantine: this NoInlining wrapper is the one sanctioned touch of a Roslyn/MSBuild type in the
    // Mcp path. It registers MSBuildLocator before the host builds; everything else in Mcp\ reaches a
    // Roslyn workspace type only through the runners inside the tool methods (JITted at first call, after
    // registration has run). Mirrors the CLI's MsBuildGate.EnsureMsBuildRegistered.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnsureMsBuildRegistered()
    {
        MsBuildBootstrap.EnsureInitialized();
    }
}
