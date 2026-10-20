using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>
///     A loaded MSBuild solution together with the workspace that produced it. Dispose it to release the
///     workspace and its out-of-process build host; the <see cref="Solution" /> stays readable afterwards.
/// </summary>
/// <remarks>
///     The project lists say how completely the solution loaded. Each is ordinal-sorted and means what its
///     namesake on <see cref="WorkspaceDiagnostics" /> means, the value <see cref="LoadDiagnosticsWith" />
///     builds.
/// </remarks>
internal sealed class LoadedSolution : IDisposable
{
    private readonly MSBuildWorkspace _workspace;

    internal LoadedSolution(
        MSBuildWorkspace workspace, Solution solution,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        ProjectLoadReport? report = null,
        IReadOnlyList<string>? restoreFailedProjects = null)
    {
        _workspace = workspace;
        Solution = solution;
        TargetFrameworks = targetFrameworks ?? TargetFrameworkMaps.None;
        FailedProjects = (report ?? ProjectLoadReport.Empty).Failed;
        UncheckedProjects = (report ?? ProjectLoadReport.Empty).Unchecked;
        UnsupportedProjects = (report ?? ProjectLoadReport.Empty).Unsupported;
        RestoreFailedProjects = restoreFailedProjects ?? [];
    }

    /// <summary>
    ///     Gets the loaded solution: references that did not resolve have been dropped, and a multi-targeted
    ///     project's several compilations all carry the one project name, told apart by
    ///     <see cref="TargetFrameworks" />.
    /// </summary>
    public Solution Solution { get; }

    /// <summary>
    ///     Gets the target framework each multi-targeted project was loaded for, keyed by
    ///     <see cref="ProjectId" />; empty when every project targets one framework.
    /// </summary>
    public IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; }

    /// <summary>Gets the absolute <c>.csproj</c> paths of the projects that failed to load.</summary>
    public IReadOnlyList<string> FailedProjects { get; }

    /// <summary>
    ///     Gets the absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model.
    /// </summary>
    public IReadOnlyList<string> RestoreFailedProjects { get; }

    /// <summary>
    ///     Gets the absolute <c>.csproj</c> paths this solution declares that the load did not read; non-empty
    ///     only when a <c>.slnf</c> filter left members out.
    /// </summary>
    public IReadOnlyList<string> UncheckedProjects { get; }

    /// <summary>
    ///     The projects this solution declares that no extractor reaches, each with its
    ///     <see cref="UnsupportedProjectKind" />, read off the solution file rather than the load, since a
    ///     project that was never going to load leaves no shape in one.
    /// </summary>
    internal IReadOnlyList<UnsupportedProject> UnsupportedProjects { get; }

    /// <summary>
    ///     This load's verdict as the one value every surface reads. <paramref name="loadFailures" /> are the
    ///     failure messages the loader wrote to the caller's sink, which never land on this type; the merge's
    ///     notes and multi-targeted projects stay empty, since only extraction produces them.
    /// </summary>
    internal WorkspaceDiagnostics LoadDiagnosticsWith(IReadOnlyList<string> loadFailures)
    {
        return new WorkspaceDiagnostics(
            loadFailures, [], FailedProjects, UncheckedProjects, RestoreFailedProjects, UnsupportedProjects, []);
    }

    public void Dispose()
    {
        _workspace.Dispose();
    }
}
