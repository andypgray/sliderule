using System.CommandLine;
using System.Runtime.CompilerServices;
using SlideRule.Cli;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Replay;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>The result of an in-process CLI invocation.</summary>
internal sealed record CliResult(int Exit, string Out, string Err)
{
    /// <summary>The workspace loads of this one invocation, exact whatever runs beside it.</summary>
    internal long Loads { get; private init; }

    /// <summary>
    ///     The MSBuild-gate branch of this one invocation, exact whatever runs beside it; <c>null</c> when the
    ///     invocation never reaches the gate (explain, render, baseline, or a runner driven directly).
    /// </summary>
    internal GateAcquisition? Gate { get; private init; }

    /// <summary>
    ///     Runs <paramref name="invoke" /> against a fresh writer pair and captures both channels, the workspace
    ///     loads and the gate branch beside the exit code — the shape every in-process driver shares, whether it
    ///     goes through the whole CLI or constructs one runner directly to inject a seam the command line cannot
    ///     spell.
    /// </summary>
    internal static async Task<CliResult> CapturedAsync(Func<TextWriter, TextWriter, Task<int>> invoke)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        StrongBox<long> loads = WorkspaceLoader.CountLoadsOnThisFlow();
        StrongBox<GateAcquisition?> gate = MsBuildGate.RecordAcquisitionOnThisFlow();

        int exit = await invoke(output, error);

        return new CliResult(exit, output.ToString(), error.ToString()) { Loads = loads.Value, Gate = gate.Value };
    }
}

/// <summary>
///     Drives the CLI in-process through the real <see cref="CliEntry" /> and command tree, capturing
///     stdout/stderr via a redirected <see cref="InvocationConfiguration" /> (no child process). Also
///     surfaces the MyApp solution path and the fixture spec DLL paths the build bakes into this
///     assembly's metadata.
/// </summary>
/// <remarks>
///     <b>Warm by default.</b> <see cref="InvokeAsync(string[])" /> hands the CLI the shared
///     <see cref="WarmWorkspacePool" /> as its host source, so a class's many invocations reuse one loaded
///     workspace (reconciled against disk on every call) instead of opening one each. Nothing else about
///     the run changes: the same command tree, the same runners, the same full extraction, the same
///     stdout/stderr. A test whose subject <em>is</em> the loading — a load-count pin, a
///     workspace-acquisition count — calls <see cref="InvokeColdAsync(string[])" /> instead and gets a fresh
///     one-shot workspace per invocation.
/// </remarks>
internal static class CliRunner
{
    public static string MyAppSolution => FixtureBeds.MyAppSolution;

    public static string ViolatedSpecDll => FixtureBeds.BakedPath("ViolatedSpecPath");

    public static string CleanSpecDll => FixtureBeds.BakedPath("CleanSpecPath");

    public static string RenderSpecDll => FixtureBeds.BakedPath("RenderSpecPath");

    public static string LayerSpecDll => FixtureBeds.BakedPath("LayerSpecPath");

    public static string QuarantinedSpecDll => FixtureBeds.BakedPath("QuarantinedSpecPath");

    public static string DerivedSpecDll => FixtureBeds.BakedPath("DerivedSpecPath");

    /// <summary>The spec that references Core alone — the one built outside <c>tests/Fixtures</c>' globs.</summary>
    public static string SpecFixtureDll => FixtureBeds.BakedPath("SpecFixturePath");

    /// <summary>The spec for the polyglot bed, whose one rule holds — so its report is clean.</summary>
    public static string PolyglotSpecDll => FixtureBeds.BakedPath("PolyglotSpecPath");

    /// <summary>The net10 spec whose <c>Define()</c> anchors a type from a .NET shared framework.</summary>
    public static string SharedFrameworkSpecDll => FixtureBeds.BakedPath("SharedFrameworkSpecPath");

    /// <summary>The net48 spec DLL, built at the C# 7.3 default.</summary>
    public static string LegacySpecDll => FixtureBeds.BakedPath("LegacySpecPath");

    /// <summary>The net48 spec DLL whose <c>Define()</c> anchors a type that cannot load on .NET.</summary>
    public static string LegacyBrokenSpecDll => FixtureBeds.BakedPath("LegacyBrokenSpecPath");

    /// <summary>The net48 pattern-only spec that drives <c>check</c> against the ClassicApp solution.</summary>
    public static string ClassicAppSpecDll => FixtureBeds.BakedPath("ClassicAppSpecPath");

    /// <summary>The MyApp domain assembly — an ordinary product DLL, carrying no spec at all.</summary>
    public static string MyAppDomainDll => FixtureBeds.BakedPath("MyAppDomainPath");

    /// <summary>The non-SDK-style .NET Framework fixture solution, copied to the test output as content.</summary>
    public static string ClassicAppSolution =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "LegacySolutions", "ClassicApp", "ClassicApp.sln");

    /// <summary>
    ///     Every fixture spec that governs a bed of its own: the name its model file records, its DLL, and the
    ///     bed's solution. The model-file corpus and the parity pin both read this one list.
    /// </summary>
    public static IReadOnlyList<(string Name, string SpecDll, string Solution)> BeddedSpecs =>
    [
        ("MyAppViolatedSpec", ViolatedSpecDll, MyAppSolution),
        ("MyAppCleanSpec", CleanSpecDll, MyAppSolution),
        ("MyAppRenderSpec", RenderSpecDll, MyAppSolution),
        ("MyAppLayerSpec", LayerSpecDll, MyAppSolution),
        ("MyAppQuarantinedSpec", QuarantinedSpecDll, MyAppSolution),
        ("MyAppDerivedSpec", DerivedSpecDll, MyAppSolution),
        ("ClassicAppSpec", ClassicAppSpecDll, ClassicAppSolution),
        ("PolyglotAppSpec", PolyglotSpecDll, FixtureBeds.PolyglotAppSolution)
    ];

    /// <summary>Runs the CLI over the shared warm workspace pool — the default; see the type's remarks.</summary>
    public static Task<CliResult> InvokeAsync(params string[] args)
    {
        return RunAsync(WarmWorkspacePool.Source, new SystemEnvironment(), args);
    }

    /// <summary>
    ///     <see cref="InvokeAsync(string[])" />, with every variable the run reads taken from
    ///     <paramref name="environment" /> instead of real process state — so a row can set one without a write the
    ///     whole process can see.
    /// </summary>
    public static Task<CliResult> InvokeAsync(IEnvironment environment, params string[] args)
    {
        return RunAsync(WarmWorkspacePool.Source, environment, args);
    }

    /// <summary>
    ///     Runs the CLI over a fresh <see cref="ColdSolutionSource" />, so every workspace command opens (and disposes) its
    ///     own
    ///     <c>MSBuildWorkspace</c>. For tests that read <see cref="CliResult.Loads" /> or otherwise measure
    ///     whether a design-time build ran: the warm pool would answer from a session it already loaded.
    /// </summary>
    public static Task<CliResult> InvokeColdAsync(params string[] args)
    {
        return RunAsync(new ColdSolutionSource(), new SystemEnvironment(), args);
    }

    /// <summary>
    ///     <see cref="InvokeColdAsync(string[])" />, with the cache-fronted verbs reading their cache-root
    ///     override from <paramref name="environment" /> instead of real process state — so a class can
    ///     isolate its persisted caches per invocation without a variable write the whole process can see.
    /// </summary>
    /// <remarks>
    ///     Cold, because the tests that need per-invocation cache isolation are the ones measuring
    ///     acquisition; a row that wants the environment seam over the warm pool calls
    ///     <see cref="InvokeAsync(IEnvironment, string[])" /> instead.
    /// </remarks>
    public static Task<CliResult> InvokeColdAsync(IEnvironment environment, params string[] args)
    {
        return RunAsync(new ColdSolutionSource(), environment, args);
    }

    /// <summary>
    ///     <see cref="InvokeAsync(string[])" /> with <paramref name="input" /> piped in on stdin and the environment
    ///     read from <paramref name="environment" />: how the <c>hook</c> verb, which reads its payload from stdin, is
    ///     driven in process.
    /// </summary>
    /// <remarks>
    ///     The environment is required rather than defaulted, because the hook reads <c>CLAUDE_PROJECT_DIR</c> and
    ///     its state root through it, and a test process running under Claude Code has a real project directory
    ///     that would otherwise decide which tree a row checks.
    /// </remarks>
    public static Task<CliResult> InvokeWithInputAsync(string input, IEnvironment environment, params string[] args)
    {
        return RunAsync(WarmWorkspacePool.Source, environment, args, new StringReader(input));
    }

    private static Task<CliResult> RunAsync(
        ISolutionSource hostSource, IEnvironment environment, string[] args, TextReader? input = null)
    {
        return CliResult.CapturedAsync((output, error) => CliEntry.InvokeAsync(
            args, new InvocationConfiguration { Output = output, Error = error }, hostSource, environment, input));
    }
}
