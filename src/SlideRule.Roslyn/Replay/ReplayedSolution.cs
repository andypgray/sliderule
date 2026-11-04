using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Replay;

/// <summary>
///     A binlog-replayed solution paired with the in-memory workspace it was loaded into — the replay
///     analog of <see cref="LoadedSolution" />. <see cref="Solution" /> is the
///     unresolved-reference-stripped snapshot, identical in kind to the MSBuild path's output.
/// </summary>
/// <remarks>
///     <para>
///         There is no MSBuild <c>BuildHost</c> to release, because no design-time build ran. The
///         <see cref="SolutionReader" />'s lazy per-document text loaders back the solution's source, and its
///         analyzer host holds the on-disk analyzer assemblies. A Roslyn <see cref="Solution" /> outlives the
///         workspace that produced it, as with <see cref="LoadedSolution" />, so a caller may keep reading a
///         <see cref="Solution" /> already materialised by extraction after <see cref="Dispose" />.
///     </para>
///     <para>
///         The project lists of <see cref="LoadDiagnosticsWith" /> carry the same facts as
///         <see cref="LoadedSolution" />'s, absolute and ordinal-sorted, so the gate and every document read one
///         thing on both paths.
///     </para>
/// </remarks>
internal sealed class ReplayedSolution : IDisposable
{
    private readonly AdhocWorkspace _workspace;
    private readonly SolutionReader _reader;
    private readonly WorkspaceDiagnostics _loadVerdict;
    private bool _disposed;

    internal ReplayedSolution(
        AdhocWorkspace workspace, SolutionReader reader, Solution solution,
        IReadOnlyDictionary<ProjectId, string> targetFrameworks,
        CanonicalProjectFiles projectFiles,
        ProjectLoadReport report)
    {
        _workspace = workspace;
        _reader = reader;
        Solution = solution;
        TargetFrameworks = targetFrameworks;
        ProjectFiles = projectFiles;
        FailedProjects = report.Failed;
        UncheckedProjects = report.Unchecked;
        _loadVerdict = WorkspaceDiagnostics.OfLoad(
            loadFailures: [],
            failedProjects: FailedProjects,
            uncheckedProjects: UncheckedProjects,
            // Always empty on this path, because there is no moment to read: an assets file describes the restore
            // as it stands on disk now, so reading one here would stamp today's restore onto yesterday's model, and
            // a capture replayed because it was structurally valid is a capture of a build that compiled.
            restoreFailedProjects: [],
            // The solution file's entries, as on an MSBuild load, joined with the compiler invocations the C#-only
            // predicate declined from the capture.
            unsupportedProjects: report.Unsupported,
            missingProjects: report.Missing);
    }

    public Solution Solution { get; }

    /// <summary>
    ///     The target framework each multi-target-framework project was built for, read from the recorded output
    ///     paths (the replay reader applies no name discriminator of its own); absent for a project that
    ///     suppresses the framework segment of its output path.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>Each replayed project's canonical <c>.csproj</c> path, resolved once for the replay.</summary>
    public CanonicalProjectFiles ProjectFiles { get; }

    /// <summary>The <c>.csproj</c> paths of the projects that failed to load.</summary>
    /// <remarks>
    ///     On this path every entry is a project the solution declares, whose file is on disk, and the binlog never
    ///     built; one whose file is not on disk is a missing project instead. A replayed
    ///     project comes from a compiler invocation a real build actually made, so it carries an output path
    ///     and cannot present the loaded-but-empty shape; what can go missing is a whole member, when the
    ///     binlog is a build of less than the solution. A capture's ingest refuses such a binlog outright, so
    ///     this list is what holds the gate when no capture is ingested. Empty when the replay was given no
    ///     solution file.
    /// </remarks>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     The <c>.csproj</c> paths the solution's default configuration builds that a solution filter left out of
    ///     this run and the binlog did not build; empty unless the replay was given a solution filter.
    /// </summary>
    public IReadOnlyList<string> UncheckedProjects { get; }

    /// <summary>
    ///     This replay's verdict as the one value every surface reads: the project lists beside
    ///     <paramref name="loadFailures" />, the replay messages the caller's own sink collected, which never land
    ///     on this type. The merge's facts stay empty, as on the MSBuild path.
    /// </summary>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return _loadVerdict with { LoadFailures = loadFailures };
    }

    /// <summary>
    ///     Disposes the workspace and the binlog reader (releasing its stream and analyzer host). A second call is
    ///     a no-op.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        try
        {
            _workspace.Dispose();
        }
        finally
        {
            _reader.Dispose();
        }
    }
}
