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
    /// <remarks>
    ///     <paramref name="environment" /> is the process's one environment seam. The logger and the watchdogs start
    ///     before the host exists, so their settings are read off it here and handed in, and the container then
    ///     serves the same instance to every tool. <paramref name="error" /> takes the interactive-launch refusal,
    ///     never stdout, which carries JSON-RPC.
    /// </remarks>
    public static async Task<int> RunAsync(
        McpServerBinding binding, IEnvironment environment, TextWriter error, CancellationToken ct)
    {
        if (!Console.IsInputRedirected)
        {
            // A human ran the tool at a terminal: don't hang on a silent stdio server.
            await error.WriteLineAsync("sliderule mcp is an MCP stdio server; it is started by an MCP client, not interactively.");
            await error.WriteLineAsync("Configure it in your MCP client (command \"sliderule mcp <solution> --spec <spec>\").");
            return ExitCodes.Error;
        }

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

        // Registered under the seam interface by name, as every interface-typed service here is, rather than
        // under whatever type the argument happens to be declared as.
        // ReSharper disable once RedundantTypeArgumentsOfMethod
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
    ///     Registers everything the <c>arch_*</c> tools resolve in one place, so the server and the in-process test
    ///     harness compose the same graph: a service added at a call site would reach only one of them.
    /// </summary>
    /// <remarks>
    ///     The warm source registers the session's disposer with <see cref="ServerShutdown" /> from inside its
    ///     factory, so a run that never builds one wires no disposer. The fitter is the budgeted one because this
    ///     surface has a channel to overrun.
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
    ///     <para>
    ///         An argument that does not resolve is fatal: the <see cref="UserErrorException" /> propagates to
    ///         exit 2 at <c>CommandEntryPoint</c>. Starting instead would give a server that errors on every tool
    ///         call and, with stdin redirected but never closed under a test host, hangs the run.
    ///     </para>
    ///     <para>
    ///         With no argument, a failed walk-up is reported rather than fatal, because a process that exits
    ///         before <c>initialize</c> reaches the agent as a failed server, its stderr only in the client's own
    ///         log (verified 2026-10-20 on Claude Code 2.1.300). Every
    ///         tool call re-runs the same discovery and gets the same message, and the caller hands the one
    ///         returned value to <see cref="ServerInstructions.For" /> and to
    ///         <see cref="GlobalCallToolFilter.WithGlobalCallToolFilter" />, so the handshake and the tool replies
    ///         cannot disagree about the bind.
    ///     </para>
    ///     <para>
    ///         Whitespace splits with null because <see cref="ModelPipeline.DiscoverSolution" /> treats the two
    ///         alike: an argument is fatal only when discovery honours it. NoInlining matches the quarantine
    ///         style below; SolutionDiscovery is file I/O with no MSBuildWorkspace touch, so it is safe before
    ///         registration.
    ///     </para>
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
