using Microsoft.CodeAnalysis;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>
///     An immutable view of a <see cref="WorkspaceSession" />'s solution as of one
///     <see cref="WorkspaceSession.GetCurrentAsync" /> call: the reconciled solution, with references that did
///     not resolve dropped, and the facts of the load that produced it.
/// </summary>
/// <remarks>
///     The solution is a Roslyn immutable snapshot and stays readable after the session reloads or is
///     disposed. Every member but the solution and <see cref="ProjectEditVersions" /> describes the load, not
///     any one file: a full (re)load replaces it and bumps <see cref="Generation" />, and the document edits a
///     reconcile folds in leave it alone. Such an edit adds or removes no project, and the files that decide
///     restore and membership (the solution or filter file, the solution a filter references, each
///     <c>project.assets.json</c>, absent ones included) are structural inputs of the reconcile sweep, so a
///     change to one forces a full reload. The fail-closed gate keys on <see cref="FailedProjects" /> and
///     <see cref="RestoreFailedProjects" />; <see cref="Diagnostics" />, the workspace's failure messages, only
///     renders.
/// </remarks>
internal sealed record WorkspaceSnapshot(Solution Solution, IReadOnlyList<string> Diagnostics)
{
    private static readonly IReadOnlyDictionary<string, int> NoEditVersions =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>The session load generation that produced this snapshot; work is reusable only within one.</summary>
    internal long Generation { get; init; }

    /// <summary>
    ///     Per-project (by name) edit counters within <see cref="Generation" />: a project's value rises each
    ///     time the reconcile sweep rewrites one of its documents.
    /// </summary>
    /// <remarks>
    ///     Any rewritten byte moves the counter, comments included: fragments carry <c>file:line</c> sites, so a
    ///     comment that shifts lines must still dirty its project.
    /// </remarks>
    internal IReadOnlyDictionary<string, int> ProjectEditVersions { get; init; } = NoEditVersions;

    /// <summary>
    ///     The target framework each multi-targeted project was loaded for, the discriminator
    ///     <see cref="SolutionExtensions.NormalizeProjectNames" /> took out of the project names; empty when
    ///     every project targets one framework. Keyed by <see cref="ProjectId" />, which survives
    ///     <c>Solution.WithDocumentText</c>, so the map holds across the generation's edits.
    /// </summary>
    internal IReadOnlyDictionary<ProjectId, string> TargetFrameworks { get; init; } = TargetFrameworkMaps.None;

    /// <summary>The absolute <c>.csproj</c> paths of the projects that failed to load.</summary>
    internal IReadOnlyList<string> FailedProjects { get; init; } = [];

    /// <summary>
    ///     The absolute <c>.csproj</c> paths the solution declares that the load did not check; non-empty only
    ///     under a solution filter. It scopes the verdict rather than gating it.
    /// </summary>
    internal IReadOnlyList<string> UncheckedProjects { get; init; } = [];

    /// <summary>The absolute <c>.csproj</c> paths of the projects whose NuGet packages are not in the model.</summary>
    internal IReadOnlyList<string> RestoreFailedProjects { get; init; } = [];

    /// <summary>
    ///     The projects the solution declares that no extractor reaches, each with its
    ///     <see cref="UnsupportedProjectKind" />. It states coverage and is never a gate input.
    /// </summary>
    internal IReadOnlyList<UnsupportedProject> UnsupportedProjects { get; init; } = [];

    /// <summary>
    ///     This snapshot's load verdict as the one value every surface reads. The merge's notes and
    ///     multi-targeted projects stay empty: only extraction produces them.
    /// </summary>
    internal WorkspaceDiagnostics LoadDiagnostics =>
        new(Diagnostics, [], FailedProjects, UncheckedProjects, RestoreFailedProjects, UnsupportedProjects, []);
}
