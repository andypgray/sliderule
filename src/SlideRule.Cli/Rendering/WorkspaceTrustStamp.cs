using SlideRule.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     One declared project the run did not reach, and why — the entry shape of
///     <see cref="WorkspaceTrustStamp.UnsupportedProjects" />.
/// </summary>
/// <param name="Project">The project, solution-relative and forward-slashed like every other path.</param>
/// <param name="Reason">Why the run did not reach it.</param>
internal sealed record UnsupportedProjectStamp(string Project, string Reason)
{
    /// <summary>
    ///     The entry as a human line reads it: the project, then why. Deferred to the one owner of that join,
    ///     so the survey's own section, the stamp a verb writes above its answer and the adapter's skip
    ///     cannot spell the same entry three ways.
    /// </summary>
    internal string Describe()
    {
        return UnsupportedProjectsNotice.Entry(Project, Reason);
    }
}

/// <summary>
///     The seven facts a verb's document can stamp about how far its own contents can be trusted; each
///     document carries the ones that apply to it.
/// </summary>
/// <remarks>
///     <para>
///         Three of the seven are adjacent lists of the same type, which is a swap the compiler cannot see: a
///         reorder exchanging what failed to load with what a solution filter left unchecked type-checks
///         cleanly and reaches the wire. Composed once here, off one <see cref="WorkspaceDiagnostics" /> and
///         by name, that reorder is untypeable — and no document decides on its own which list is which.
///         <see cref="UnsupportedProjects" /> is a different type for a different reason, but it lands here
///         for the same one: this is where a path becomes the thing a document prints.
///     </para>
///     <para>
///         The empty-to-null policy is the other half. A list that is empty says nothing, so it is
///         carried as <see langword="null" /> and the key is omitted rather than written as <c>[]</c> — which
///         is what keeps a clean document byte-identical to the one it always was. Every document still
///         declares its own slots and their order; SARIF reads the same stamp and tests the same nulls,
///         because an absent list and an empty one emit no notification there alike, which is the same
///         omission.
///     </para>
///     <para>
///         <b>The reason text is read here, once.</b> Four surfaces state it — three JSON documents and the
///         SARIF log — and asking for it at each would let the wire key and the prose disagree about what the
///         run reached. The sentence itself belongs to <see cref="UnsupportedProjectsNotice" />, beside the
///         human stamps that have to agree with it; what this type owns is the pairing, because this is the
///         only place that knows the path is about to be relativized, which is what the reader will actually
///         see.
///     </para>
/// </remarks>
/// <param name="ModelIncomplete">
///     <see langword="true" /> when a project failed to load or a project's packages did not resolve, or
///     <see langword="null" /> when the model is whole.
/// </param>
/// <param name="FailedProjects">The projects that failed to load, or <see langword="null" /> when none did.</param>
/// <param name="UncheckedProjects">
///     The declared projects a solution filter left unchecked, or <see langword="null" /> when the run
///     covered the whole solution.
/// </param>
/// <param name="RestoreFailedProjects">
///     The projects whose NuGet packages are not in the model, or <see langword="null" /> when they all are.
/// </param>
/// <param name="UnsupportedProjects">
///     The declared projects no extractor reached, each with its reason, or <see langword="null" /> when the
///     run reached them all. Unlike its three siblings this is not a verdict about the load — nothing here
///     was ever going to load — so it never reaches <see cref="ModelIncomplete" />.
/// </param>
/// <param name="MultiTargetedProjects">
///     The projects one <c>.csproj</c> of which yielded several compilations, or <see langword="null" /> when
///     every project targets one framework — which is every project of most solutions. It takes
///     <see cref="UnsupportedProjects" />' posture rather than <see cref="FailedProjects" />': the model is
///     whole and every rule answered, over one framework's members and hierarchy for the types these projects'
///     frameworks share, so it never reaches <see cref="ModelIncomplete" /> either. Carried through unchanged
///     rather than remade the way the four
///     path lists are — these are project names, so there is nothing to relativize and no second shape for
///     the wire to disagree with the merge about.
/// </param>
/// <param name="PackageAudit">
///     The NuGet audit advisories the load raised, one entry per distinct message, or
///     <see langword="null" /> when it raised none. The one load fact here that is not a verdict about the
///     projects: it rides the stamp because grouping it walks every load line, and every surface that stamps
///     the six above renders it too — so reading it off the load once is what keeps four renderers from
///     regrouping the same lines and deciding the empty-to-null policy apiece.
/// </param>
internal readonly record struct WorkspaceTrustStamp(
    bool? ModelIncomplete,
    IReadOnlyList<string>? FailedProjects,
    IReadOnlyList<string>? UncheckedProjects,
    IReadOnlyList<string>? RestoreFailedProjects,
    IReadOnlyList<UnsupportedProjectStamp>? UnsupportedProjects,
    IReadOnlyList<MultiTargetedProject>? MultiTargetedProjects,
    IReadOnlyList<PackageAuditEntry>? PackageAudit)
{
    /// <summary>
    ///     Reads the stamp off a load's own verdict, relativizing every project path.
    /// </summary>
    /// <param name="diagnostics">The load's verdict, carrying the four project lists and its own raw lines.</param>
    /// <param name="solutionDirectory">
    ///     The directory the paths are shown relative to — they land solution-relative and forward-slashed,
    ///     like every other path in the document, so a machine path never reaches a golden.
    /// </param>
    internal static WorkspaceTrustStamp From(WorkspaceDiagnostics diagnostics, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return new WorkspaceTrustStamp(
            diagnostics.IsIncomplete ? true : null,
            Relative(diagnostics.FailedProjects, relativizer),
            Relative(diagnostics.UncheckedProjects, relativizer),
            Relative(diagnostics.RestoreFailedProjects, relativizer),
            Unsupported(diagnostics.UnsupportedProjects, relativizer),
            diagnostics.MultiTargetedProjects.Count == 0 ? null : diagnostics.MultiTargetedProjects,
            diagnostics.PackageAudit is { Count: > 0 } audit ? audit : null);
    }

    private static IReadOnlyList<string>? Relative(
        IReadOnlyList<string> projects, PathFormat.Relativizer relativizer)
    {
        return projects.Count == 0
            ? null
            : projects.Select(relativizer.Relative)
                .ToList();
    }

    private static IReadOnlyList<UnsupportedProjectStamp>? Unsupported(
        IReadOnlyList<UnsupportedProject> projects, PathFormat.Relativizer relativizer)
    {
        return projects.Count == 0
            ? null
            : projects.Select(project => new UnsupportedProjectStamp(
                    relativizer.Relative(project.Path), UnsupportedProjectsNotice.Reason(project.Kind)))
                .ToList();
    }
}
