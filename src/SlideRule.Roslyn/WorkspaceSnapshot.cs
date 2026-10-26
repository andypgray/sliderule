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
///     change to one forces a full reload.
/// </remarks>
internal sealed record WorkspaceSnapshot(Solution Solution, WorkspaceDiagnostics LoadDiagnostics)
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

    /// <summary>
    ///     Each loaded project's canonical <c>.csproj</c> path, resolved once for <see cref="Generation" />.
    /// </summary>
    internal required CanonicalProjectFiles ProjectFiles { get; init; }
}
