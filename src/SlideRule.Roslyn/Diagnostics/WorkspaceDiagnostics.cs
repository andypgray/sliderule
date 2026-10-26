using SlideRule.Codebase;
using SlideRule.Roslyn.MsBuild;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     One project file that arrived as several compilations: its name, every target framework it was
///     extracted from, and the first framework whose facts a type those frameworks share ended up carrying.
/// </summary>
/// <remarks>
///     <paramref name="Project" /> is a name, never a path, so nothing relativizes it on the way to the wire.
///     <paramref name="TargetFrameworks" /> is ordinal-ordered, the order a workspace load hands them over in, so a
///     shared type's facts fall to the first framework that declares it. <paramref name="FactsFollow" /> is null
///     when the frameworks share no type, the condition the merge's own note is raised under: a project whose
///     frameworks each declare their own types displaced nothing, and naming a winner would be false about all of
///     them.
/// </remarks>
internal sealed record MultiTargetedProject(
    string Project,
    IReadOnlyList<string> TargetFrameworks,
    string? FactsFollow);

/// <summary>
///     Reads <see cref="MultiTargetedProject" />s off a merged model — the one owner of the projection, so
///     every composer that holds a model fills the slot the same way.
/// </summary>
internal static class MultiTargetedProjects
{
    /// <summary>
    ///     The projects <paramref name="codebase" /> holds that arrived as several compilations; empty when every
    ///     project targets one framework.
    /// </summary>
    /// <remarks>
    ///     Wider than the merge notes, which are raised only where two frameworks declared the same type: a project
    ///     whose frameworks share nothing has no note and is still checked against one of them. A project listing
    ///     fewer than two frameworks is still included when the merge recorded a collapse.
    /// </remarks>
    internal static IReadOnlyList<MultiTargetedProject> Of(CodebaseModel codebase)
    {
        return codebase.Projects
            .Where(project => project.TargetFrameworks.Count > 1 || project.FactsFollow is not null)
            .Select(project => new MultiTargetedProject(
                project.Name, project.TargetFrameworks, project.FactsFollow))
            .ToList();
    }
}

/// <summary>
///     Why a project the solution declares is outside the model, as the producer that found it classified it.
///     The edge that composes the sentence reads this kind rather than testing the path again: a test that knows
///     only <c>.csproj</c> calls a shared project not C#.
/// </summary>
internal enum UnsupportedProjectKind
{
    /// <summary>
    ///     A project in a language this product has no extractor for — an <c>.fsproj</c>, a <c>.vbproj</c>,
    ///     a <c>.sqlproj</c>, a <c>.vcxproj</c>, or a compiler invocation the replay declined as non-C#.
    /// </summary>
    NotCsharp,

    /// <summary>
    ///     A shared project (<c>.shproj</c>): a language-neutral container whose <c>.projitems</c> files are
    ///     compiled into every project that imports it. Its code is very often C#, and where an importing
    ///     project is in the model that code is in the model too, so it is described by its shape alone.
    /// </summary>
    SharedProject
}

/// <summary>
///     One project the solution declares that no extractor reached, by the absolute <paramref name="Path" /> of its
///     project file, and the kind of reason it did not.
/// </summary>
internal sealed record UnsupportedProject(string Path, UnsupportedProjectKind Kind);

/// <summary>
///     Everything a run knows about how well its workspace loaded, as one value, and the gate decision every verb
///     makes from it.
/// </summary>
/// <remarks>
///     <para>
///         Every project path here is absolute, and every list of them ordinal-sorted. The fail-closed gate rests
///         on two of those lists, <see cref="FailedProjects" /> and <see cref="RestoreFailedProjects" />, read at
///         the load boundary by <see cref="ProjectLoadFailures" /> and <see cref="RestoreFailures" />, and never on
///         diagnostic text, which is neither a contract nor language-independent: the diagnostics render and
///         decide nothing. The two causes stay two lists, with separate remedies, and meet at
///         <see cref="IsIncomplete" />. The other lists scope the verdict and never reach <see cref="Gates" />: the
///         model they qualify is smaller, never wrong.
///     </para>
///     <para>
///         <see cref="IsIncomplete" /> and <see cref="Gates" /> take no list argument, so no rendered stream can be
///         handed to the gate.
///     </para>
/// </remarks>
/// <param name="LoadFailures">
///     The workspace-load failure diagnostics, raw, NuGet audit advisories among them, which is how the
///     extraction cache persists them. Never a gate input: MSBuild severity does not survive the trip, so an
///     entry can be a fatal evaluation error or a restore warning and nothing downstream can tell.
/// </param>
/// <param name="MergeNotes">The advisory notes the fragment merge raised.</param>
/// <param name="FailedProjects">The <c>.csproj</c> paths of the projects that failed to load.</param>
/// <param name="UncheckedProjects">
///     The <c>.csproj</c> paths the solution declares that this run did not check; non-empty only under a
///     solution filter that left members out.
/// </param>
/// <param name="RestoreFailedProjects">
///     The <c>.csproj</c> paths of the projects whose NuGet packages are not in the model, because restore failed
///     or never ran; disjoint from <see cref="FailedProjects" />. They loaded completely and are missing every
///     edge their package references would have produced, and both causes share one remedy
///     (<c>dotnet restore</c>).
/// </param>
/// <param name="UnsupportedProjects">
///     The projects the solution declares that no extractor reached, read off the solution file rather than off
///     the load.
/// </param>
/// <param name="MultiTargetedProjects">
///     The projects one <c>.csproj</c> of which yielded several compilations: the machine-readable half of
///     <see cref="MergeNotes" />, filled from the same merge on the same read, so the two cannot disagree about
///     what compiled twice. The model is complete over one framework's view of each.
/// </param>
internal readonly record struct WorkspaceDiagnostics(
    IReadOnlyList<string> LoadFailures,
    IReadOnlyList<string> MergeNotes,
    IReadOnlyList<string> FailedProjects,
    IReadOnlyList<string> UncheckedProjects,
    IReadOnlyList<string> RestoreFailedProjects,
    IReadOnlyList<UnsupportedProject> UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject> MultiTargetedProjects)
{
    internal static WorkspaceDiagnostics None { get; } = new([], [], [], [], [], [], []);

    /// <summary>
    ///     A load's verdict before any extraction: the merge's notes and multi-targeted projects stay empty, since
    ///     only extraction produces them, and <see cref="WithModel" /> fills them.
    /// </summary>
    internal static WorkspaceDiagnostics OfLoad(
        IReadOnlyList<string> loadFailures,
        IReadOnlyList<string> failedProjects,
        IReadOnlyList<string> uncheckedProjects,
        IReadOnlyList<string> restoreFailedProjects,
        IReadOnlyList<UnsupportedProject> unsupportedProjects)
    {
        return new WorkspaceDiagnostics(
            loadFailures, [], failedProjects, uncheckedProjects, restoreFailedProjects, unsupportedProjects, []);
    }

    /// <summary>
    ///     This verdict with <see cref="MergeNotes" /> and <see cref="MultiTargetedProjects" /> filled from the
    ///     one merged model <paramref name="codebase" />.
    /// </summary>
    internal WorkspaceDiagnostics WithModel(CodebaseModel codebase)
    {
        return this with
        {
            MergeNotes = codebase.MergeNotes,
            MultiTargetedProjects = Diagnostics.MultiTargetedProjects.Of(codebase)
        };
    }

    /// <summary>
    ///     The load failures that are not NuGet audit advisories, with the MSBuild-selection note appended; empty
    ///     when there are none.
    /// </summary>
    internal IReadOnlyList<string> Rendered => Compose(ActionableDiagnostics);

    /// <summary>
    ///     The same load failures and the merge notes, with the MSBuild-selection note appended, or empty when
    ///     there is neither: the rendering for a verb that prints violations, which a merge advisory can explain.
    /// </summary>
    internal IReadOnlyList<string> RenderedWithMergeNotes => Compose([.. ActionableDiagnostics, .. MergeNotes]);

    /// <summary>
    ///     The NuGet audit advisories among <see cref="LoadFailures" />, grouped: what a surface renders in place of
    ///     the raw lines <see cref="Rendered" /> leaves out.
    /// </summary>
    internal IReadOnlyList<PackageAuditEntry> PackageAudit => NuGetAuditDiagnostics.Group(LoadFailures);

    /// <summary>
    ///     The load diagnostics that say something about this solution: every one that is not a NuGet audit
    ///     advisory.
    /// </summary>
    /// <remarks>
    ///     An advisory's publication date and an audit fetch's network reachability are external, time-varying
    ///     inputs that say nothing about how this codebase is built, so a message that offers the load as a
    ///     possible explanation for something counts only these. The split decides presentation, never a verdict.
    /// </remarks>
    internal IReadOnlyList<string> ActionableDiagnostics =>
        LoadFailures.Where(diagnostic => !NuGetAuditDiagnostics.IsAudit(diagnostic))
            .ToList();

    /// <summary>
    ///     Whether a project failed to load or its NuGet packages did not resolve, whatever the operator opted
    ///     into: the JSON documents carry this, and the opt-in changes the exit code, never the truth about the
    ///     model.
    /// </summary>
    internal bool IsIncomplete => FailedProjects.Count > 0 || RestoreFailedProjects.Count > 0;

    /// <summary>
    ///     Whether a solution filter left a declared project unchecked. Never a gate input: a narrowed run is a
    ///     smaller true answer, and a verb that reads absence as evidence refuses on its own terms.
    /// </summary>
    internal bool IsNarrowed => UncheckedProjects.Count > 0;

    /// <summary>
    ///     Whether the fail-closed gate fires: the model is incomplete and the operator did not opt into the partial
    ///     model through <paramref name="allowWorkspaceDiagnostics" />. A pure predicate, because a verb that renders
    ///     before gating asks twice, for the verdict it stamps and for the exit code.
    /// </summary>
    internal bool Gates(bool allowWorkspaceDiagnostics)
    {
        return !allowWorkspaceDiagnostics && IsIncomplete;
    }

    // The MSBuild-selection note rides the composed list rather than the write, so it reaches the JSON document as
    // well as stderr, which MCP never reads. An empty input composes to an empty list: a clean run, or one whose
    // only diagnostics are NuGet audit advisories, says nothing about MSBuild, since an advisory raises no
    // question of which MSBuild opened a project.
    private static IReadOnlyList<string> Compose(IReadOnlyList<string> diagnostics)
    {
        return diagnostics.Count == 0 ? [] : [.. diagnostics, MsBuildBootstrap.SelectionNote()];
    }
}
