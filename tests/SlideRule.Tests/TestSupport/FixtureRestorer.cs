namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Restores the checked-in fixture solutions under <c>Fixtures/TestSolutions</c> in the test output
///     directory before any workspace fixture opens them.
/// </summary>
/// <remarks>
///     NuGet restore state (<c>obj/project.assets.json</c>) is gitignored, so a fresh clone — and
///     therefore CI — arrives with unrestored fixture projects. An unrestored fixture loads without its
///     references, silently degrading everything under test. <see cref="EnsureRestored" /> checks the
///     beds once per process, thread-safely, and restores them only when a project is unrestored or a
///     project file changed since the last sweep.
/// </remarks>
internal static class FixtureRestorer
{
    // Run-once, thread-safe: Lazy (ExecutionAndPublication) guarantees one check, and at most one restore, even
    // under concurrent entry; repeat calls are free.
    private static readonly Lazy<bool> RestoreGate = new(RestoreAll);

    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    // Written after every sweep that restored all the beds. It sits above every bed, so neither a leased copy's
    // sync nor a walk of one bed sees it.
    private static string SweepMarker => Path.Combine(FixtureBeds.TestSolutionsRoot, ".restored");

    /// <summary>Restores every fixture solution once, the first time it is called; free on repeat.</summary>
    internal static void EnsureRestored()
    {
        _ = RestoreGate.Value;
    }

    private static bool RestoreAll()
    {
        string testSolutionsDir = FixtureBeds.TestSolutionsRoot;
        if (!Directory.Exists(testSolutionsDir)) return true;

        // Every project kind, not just *.csproj. A bed holding a project in another language restores the
        // same way and needs the same assets file — and probing the C# half alone short-circuits the sweep
        // the moment those are restored, leaving the other project unrestored and blamed by the restore
        // detector, which refuses the run.
        bool anyUnrestored = Directory
            .EnumerateFiles(testSolutionsDir, "*.*proj", SearchOption.AllDirectories)
            .Any(projectPath => !File.Exists(
                Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json")));

        // An assets file says a project was restored once, not that it was restored since its project file
        // last changed, so a restore-relevant file newer than the last sweep sweeps again. Compared with the
        // marker rather than with each assets file, because a restore that changes nothing need not rewrite one.
        DateTime? swept = File.Exists(SweepMarker) ? File.GetLastWriteTimeUtc(SweepMarker) : null;
        bool anyChanged = swept is null || Directory
            .EnumerateFiles(testSolutionsDir, "*", SearchOption.AllDirectories)
            .Where(path => !TempFixtureWorkspace.IsBuildArtifact(Path.GetRelativePath(testSolutionsDir, path)))
            .Where(TempFixtureWorkspace.IsProjectFile)
            .Any(path => File.GetLastWriteTimeUtc(path) > swept);
        if (!anyUnrestored && !anyChanged) return true;

        IEnumerable<string> solutions = Directory
            .EnumerateFiles(testSolutionsDir, "*", SearchOption.AllDirectories)
            .Where(IsSolutionFile);
        foreach (string solutionPath in solutions) Restore(solutionPath);

        File.WriteAllText(SweepMarker, "");
        return true;
    }

    /// <summary>
    ///     Whether a fixture file is a solution this sweep restores: both full formats, and deliberately not
    ///     <c>.slnf</c>. Restoring a filter is restoring the selected projects of the solution it references,
    ///     and every solution a fixture filter references already stands under this root — while a filter
    ///     fixture whose whole job is to be unreadable would fail a restore, and startup must not depend on
    ///     one. Internal so the accepted set pins without shelling the SDK; matched on the extension rather
    ///     than through a search pattern because a three-character pattern is documented to match longer
    ///     extensions too, which would sweep the filters back in.
    /// </summary>
    internal static bool IsSolutionFile(string path)
    {
        string extension = Path.GetExtension(path);
        return SolutionExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Restores one solution with a clean SDK environment (via <see cref="DotnetCli" />).</summary>
    internal static void Restore(string solutionPath)
    {
        DotnetCli.Run($"restore \"{solutionPath}\"", Path.GetDirectoryName(solutionPath)!);
    }
}
