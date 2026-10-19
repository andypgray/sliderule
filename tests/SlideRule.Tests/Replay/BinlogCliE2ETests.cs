using System.CommandLine;
using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Replay;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Replay;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Replay;

/// <summary>
///     End-to-end tests for the CLI wiring of binlog replay: <c>--binlog</c> on <c>check</c>, <c>status</c>
///     and <c>graph</c>, and the capture it persists. Each scenario drives the real command
///     tree in-process through <see cref="CliRunner" /> against this class's own <see cref="BinlogFixtureWorkspace" />
///     binlog, isolating the persisted caches by pointing <c>SLIDERULE_CACHE_DIR</c> at a throwaway
///     directory per run — the same isolation knob the fragment-cache e2e suite uses, and through the same
///     <see cref="IEnvironment" /> seam, which <see cref="CliRunner.InvokeColdAsync(IEnvironment,string[])" />
///     threads into the real command tree so no process-wide variable is written.
/// </summary>
/// <remarks>
///     <para>
///         Two internal observables ride every run, never printed: <see cref="CliResult.Gate" /> (which
///         source-selection branch the gate took) and <see cref="CliResult.Loads" /> (whether a design-time
///         build ran) — together the "replayed, no MSBuild" pin. Each counts its own invocation alone, so the
///         class runs beside others. Output stays byte-identical to a cold run in every mode, which is the
///         headline guarantee.
///     </para>
///     <para>
///         The class has its own binlog fixture; any mutation of it is reverted byte- and mtime-safely in a
///         <c>finally</c> (a structural file left newer than the binlog would trip a later test's ingest
///         staleness check).
///     </para>
/// </remarks>
public sealed class BinlogCliE2ETests(BinlogFixtureWorkspace fixture)
    : IClassFixture<BinlogFixtureWorkspace>, IDisposable
{
    private readonly TempDirectory _cacheRootBase = TestTempRoot.Fresh("binlog-cli");

    private BinlogFixtureWorkspace Fixture => fixture;
    private string Sln => Fixture.SolutionPath;
    private string Binlog => Fixture.BinlogPath;
    private static string CleanSpec => CliRunner.CleanSpecDll;

    public void Dispose()
    {
        _cacheRootBase.Dispose();
    }

    // ── byte-parity trio + auto-replay ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_ExplicitBinlog_ByteIdenticalToColdThenPersistsAndAutoReplays()
    {
        // (a) plain cold check, fresh cache — the byte-parity baseline (opens a workspace, one design-time build).
        string coldCache = FreshCache();
        CliResult cold = await RunAsync(coldCache, "check", Sln, "--spec", CleanSpec);
        cold.Gate.ShouldBe(GateAcquisition.Cold);
        cold.Loads.ShouldBe(1);

        // (b) check --binlog, fresh cache — replays the user's binlog: byte-identical, no design-time build,
        //     and the capture (manifest + binlog copy) now sits beside the fragment cache the run also wrote.
        string replayCache = FreshCache();
        CliResult replay = await RunAsync(replayCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec);
        replay.ShouldMatchTheOutputOf(cold);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.Loads.ShouldBe(0);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, replayCache))
            .ShouldBeTrue();
        File.Exists(CacheLocations.CaptureBinlogPath(Sln, replayCache))
            .ShouldBeTrue();

        // (c) rerun WITHOUT --binlog on the same cache: (b) wrote fragments, so this is a fragment-cache hit —
        //     no workspace acquired at all, and the gate still decided the capture is usable. Byte-identical.
        CliResult autoHit = await RunAsync(replayCache, "check", Sln, "--spec", CleanSpec);
        autoHit.ShouldMatchTheOutputOf(cold);
        autoHit.Gate.ShouldBe(GateAcquisition.CaptureReplay);
        autoHit.Loads.ShouldBe(0);

        // Invalidate ONLY the fragment cache (delete cache.json), leave the capture: now the run must actually
        // replay the capture's binlog copy to get a solution — still no design-time build, still byte-identical.
        File.Delete(CacheLocations.CacheFilePath(Sln, replayCache));
        CliResult autoReplay = await RunAsync(replayCache, "check", Sln, "--spec", CleanSpec);
        autoReplay.ShouldMatchTheOutputOf(cold);
        autoReplay.Gate.ShouldBe(GateAcquisition.CaptureReplay);
        autoReplay.Loads.ShouldBe(0);
    }

    // ── invalid capture ⇒ one notice + cold fallback ─────────────────────────────────────────────────────

    [Fact]
    public async Task Check_StaleCapture_PrintsNoticeOnceAndFallsBackToDesignTimeBuild()
    {
        string cache = FreshCache();
        await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed the capture (replay)

        string csproj = Fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        using ScopedFile held = ScopedFile.Hold(csproj);

        // A harmless XML comment: a structural content change that invalidates the capture (stale) and the
        // fragment cache (a structural miss), without changing the extracted model — so stdout is unmoved.
        string edited = File.ReadAllText(csproj)
            .Replace("</Project>", "  <!-- capture stale probe -->\n</Project>");
        File.WriteAllText(csproj, edited);

        // Baseline: a plain cold run over the edited tree with no capture (fresh cache) — one build, silent.
        CliResult cold = await RunAsync(FreshCache(), "check", Sln, "--spec", CleanSpec);
        cold.Loads.ShouldBe(1);

        // The seeded cache now holds a stale capture: the gate notices once at acquisition, then builds.
        CliResult invalid = await RunAsync(cache, "check", Sln, "--spec", CleanSpec);
        var notice = $"warning: {BinlogCaptureStore.StaleNotice(Path.GetFullPath(csproj))}";

        // Not ShouldMatchTheOutputOf: two channels match the cold run and the third is composed from it.
        invalid.Out.ShouldBe(cold.Out); // stdout byte-identical to the cold run
        invalid.Exit.ShouldBe(cold.Exit);
        invalid.Loads.ShouldBe(1); // the design-time build ran
        invalid.Gate.ShouldBe(GateAcquisition.NoticeCold);
        // stderr is exactly the one notice line (printed first, at acquisition) followed by the cold stderr.
        invalid.Err.ShouldBe($"{notice}{Environment.NewLine}{cold.Err}");
    }

    // ── loud refusals: stale-at-ingest, missing, junk ────────────────────────────────────────────────────

    [Fact]
    public async Task Check_BinlogStaleAtIngest_ExitsTwoWithPredatesRefusalAndPersistsNothing()
    {
        string cache = FreshCache();
        string csproj = Fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        using ScopedFile held = ScopedFile.Hold(csproj);

        // Bump a csproj past the binlog with no content change: ingest must refuse the now-stale build.
        File.SetLastWriteTimeUtc(csproj, File.GetLastWriteTimeUtc(Binlog)
            .AddHours(1));

        CliResult run = await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec);

        // Not ShouldRefuseWith: that is a ShouldContain over fragments, and the refusal is claimed here
        // in full — the whole of stderr, and nothing else on it.
        run.Exit.ShouldBe(2);
        run.Err.Trim()
            .ShouldBe(BinlogCaptureStore.StaleAtIngestMessage(Binlog, Path.GetFullPath(csproj)));
        // The refusal fired before persistence: nothing was written.
        File.Exists(CacheLocations.CaptureManifestPath(Sln, cache))
            .ShouldBeFalse();
        File.Exists(CacheLocations.CaptureBinlogPath(Sln, cache))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_MissingBinlog_ExitsTwoWithWasNotFound()
    {
        CliResult run = await RunAsync(FreshCache(), "check", Sln, "--binlog", "nope.binlog", "--spec", CleanSpec);

        // Exact rather than ShouldRefuseWith's fragments, for the reason given on the stale-at-ingest row.
        run.Exit.ShouldBe(2);
        run.Err.Trim()
            .ShouldBe(BinlogReplayMessages.MissingFileMessage("nope.binlog"));
    }

    [Fact]
    public async Task Check_JunkBinlog_ExitsTwoWithCouldNotBeReplayed()
    {
        string junk = _cacheRootBase.PathOf("junk.binlog");
        await File.WriteAllTextAsync(junk, "this is not a binlog", Ct);

        CliResult run = await RunAsync(FreshCache(), "check", Sln, "--binlog", junk, "--spec", CleanSpec);

        run.ShouldRefuseWith(
            $"--binlog '{junk}' could not be replayed:",
            "Rebuild with -bl and pass the fresh binlog.");
    }

    // ── --no-cache composition ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_NoCache_ReplaysExplicitBinlogButPersistsNothingAndIgnoresCapture()
    {
        // With a persisted capture present, `--no-cache` runs cold and never reads it — no notice, capture untouched.
        string seededCache = FreshCache();
        await RunAsync(seededCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed (replay)
        byte[] captureBefore = await File.ReadAllBytesAsync(CacheLocations.CaptureBinlogPath(Sln, seededCache), Ct);

        CliResult cold = await RunAsync(seededCache, "check", Sln, "--spec", CleanSpec, "--no-cache");
        cold.Loads.ShouldBe(1); // cold: the design-time build ran
        cold.Gate.ShouldBe(GateAcquisition.Cold);
        cold.Err.ShouldNotContain("build capture"); // no capture notice on the --no-cache path
        (await File.ReadAllBytesAsync(CacheLocations.CaptureBinlogPath(Sln, seededCache), Ct)).ShouldBe(captureBefore);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, seededCache))
            .ShouldBeTrue();

        // An explicit --binlog under --no-cache still replays for this run (an input, not persisted state) —
        // byte-identical to the cold run above, no design-time build, and it writes nothing anywhere.
        string freshCache = FreshCache();
        CliResult replay = await RunAsync(freshCache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec, "--no-cache");
        replay.ShouldMatchTheOutputOf(cold);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.Loads.ShouldBe(0);
        File.Exists(CacheLocations.CaptureManifestPath(Sln, freshCache))
            .ShouldBeFalse();
        File.Exists(CacheLocations.CaptureBinlogPath(Sln, freshCache))
            .ShouldBeFalse();
        File.Exists(CacheLocations.CacheFilePath(Sln, freshCache))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_NoCache_BinlogMissingADeclaredMember_RefusesAsAnIncompleteModel()
    {
        // No capture is ingested under --no-cache, so the coverage refusal never runs; the replay's own
        // declared-membership answer is what keeps a binlog of less than the solution from checking green.
        using ScopedFile widerSolution = Fixture.WriteSolutionWithUnbuiltMember("MyApp.Unbuilt");
        string unbuilt = Fixture.PathOf("MyApp.Unbuilt", "MyApp.Unbuilt.csproj");

        CliResult run = await RunAsync(
            FreshCache(), "check", widerSolution.Path, "--binlog", Binlog, "--spec", CleanSpec, "--no-cache");

        run.ShouldRefuseWith(
            BinlogReplayer.UnbuiltMemberMessage(unbuilt),
            "error: the model is incomplete — 1 project failed to load, so check cannot pass:",
            unbuilt);
        run.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        run.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task Check_BinlogMissingADeclaredMember_IngestRefusesBeforeTheModelGate()
    {
        // The same binlog with a capture to ingest: the coverage refusal fires first and alone, so the replay's
        // failed-project answer changes nothing a user sees on this path.
        using ScopedFile widerSolution = Fixture.WriteSolutionWithUnbuiltMember("MyApp.Unbuilt");
        string unbuilt = Fixture.PathOf("MyApp.Unbuilt", "MyApp.Unbuilt.csproj");

        CliResult run = await RunAsync(FreshCache(), "check", widerSolution.Path, "--binlog", Binlog, "--spec", CleanSpec);

        // Exact, for the reason given on the stale-at-ingest row: the whole of stderr is the coverage
        // refusal, so no model-incomplete block follows it. The refusal is several lines, and the console
        // ends them its own way.
        run.Exit.ShouldBe(2);
        run.Err.Trim()
            .ReplaceLineEndings("\n")
            .ShouldBe(BinlogCaptureStore.MissingCoverageMessage(Binlog, [unbuilt])
                .ReplaceLineEndings("\n"));
    }

    // ── graph parity ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Graph_ExplicitBinlog_ByteIdenticalToCold()
    {
        CliResult cold = await RunAsync(FreshCache(), "graph", Sln);
        CliResult replay = await RunAsync(FreshCache(), "graph", Sln, "--binlog", Binlog);

        replay.ShouldMatchTheOutputOf(cold);
        replay.Gate.ShouldBe(GateAcquisition.ExplicitReplay);
        replay.Loads.ShouldBe(0);
        cold.Loads.ShouldBe(1);
    }

    // ── runtime replay failure of a usable capture ⇒ notice + cold re-run (no double-render) ──────────────

    [Fact]
    public async Task Check_UsableCaptureCopyCorrupt_NoticesAndFallsBackToColdOnce()
    {
        string cache = FreshCache();
        await RunAsync(cache, "check", Sln, "--binlog", Binlog, "--spec", CleanSpec); // seed a usable capture (replay)

        // Corrupt the binlog copy but leave the manifest valid: Validate never re-reads the copy's bytes, so it
        // still reports Usable — the failure only surfaces when the lazy source tries to replay it at runtime.
        await File.WriteAllTextAsync(CacheLocations.CaptureBinlogPath(Sln, cache), "corrupt", Ct);
        // Delete the fragment cache so the runner must actually acquire (and thus replay) rather than hit.
        File.Delete(CacheLocations.CacheFilePath(Sln, cache));

        CliResult run = await RunAsync(cache, "check", Sln, "--spec", CleanSpec);

        // Not ShouldSucceed: its fragments are checked against stdout, and these are on stderr.
        run.Exit.ShouldBe(0); // the cold re-run succeeded — a torn capture never breaks the run
        run.Gate.ShouldBe(GateAcquisition.CaptureReplayFellBackToCold);
        run.Loads.ShouldBe(1); // exactly one design-time build (the retry), never two renders
        run.Err.ShouldContain("warning: build capture could not be replayed (");
        run.Err.ShouldContain("); running a design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.");
    }

    // ── help-text pins for --binlog and --no-cache ───────────────────────────────────────────────────────

    [Fact]
    public void Help_BinlogAndRevisedNoCache_MatchDictatedTextOnAllThreeCommands()
    {
        const string binlogHelp =
            "A .binlog from a real build of this solution on this machine. Replays the captured structure "
            + "instead of a design-time build; the capture persists, and later runs replay it automatically "
            + "while structurally valid.";
        const string noCacheHelp =
            "Bypass the persisted caches (extraction fragments and the build capture): always load and extract "
            + "fresh, and write nothing back.";

        RootCommand root = CommandTree.Root();
        foreach (string commandName in new[] { "check", "status", "graph" })
        {
            Command command = root.Subcommands.First(c => c.Name == commandName);
            command.Options.First(o => o.Name == "--binlog")
                .Description.ShouldBe(binlogHelp);
            command.Options.First(o => o.Name == "--no-cache")
                .Description.ShouldBe(noCacheHelp);
        }
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────────────

    private string FreshCache()
    {
        return _cacheRootBase.UniqueChildPath();
    }

    // Drives the real command tree, isolating the persisted caches at SLIDERULE_CACHE_DIR; the returned result
    // carries both internal observables, the gate branch and the workspace loads.
    private static Task<CliResult> RunAsync(string cacheDir, params string[] args)
    {
        // The cache root rides the invocation's own IEnvironment, so this class isolates its caches without
        // writing a variable the rest of the process can see.
        FakeEnvironment environment = FakeEnvironment.WithCacheRoot(cacheDir);

        // Cold, deliberately: every fact here reads the load count to tell "replayed" from "built", so each
        // invocation has to open (or decline to open) its own workspace.
        return CliRunner.InvokeColdAsync(environment, args);
    }
}
