using SlideRule.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     One declared project the run did not reach, solution-relative and forward-slashed, and why — the entry
///     shape of <see cref="WorkspaceTrustStamp.UnsupportedProjects" />.
/// </summary>
internal sealed record UnsupportedProjectStamp(string Project, string Reason)
{
    /// <summary>The entry as a human line reads it: the project, then why.</summary>
    internal string Describe()
    {
        return UnsupportedProjectsNotice.Entry(Project, Reason);
    }
}

/// <summary>
///     The facts a verb's document can stamp about how far its own contents can be trusted; each document
///     carries the ones that apply to it.
/// </summary>
/// <remarks>
///     <para>
///         Each is <see cref="WorkspaceDiagnostics" />' fact of the same name, which says what it holds, with
///         every path made solution-relative and forward-slashed; <paramref name="ModelIncomplete" /> is
///         <see cref="WorkspaceDiagnostics.IsIncomplete" />. Three of them are adjacent lists of the same type,
///         which is a swap the compiler cannot see: a reorder exchanging what failed to load with what a solution
///         filter left unchecked type-checks cleanly and reaches the wire. Composed once here, off one
///         <see cref="WorkspaceDiagnostics" /> and by name, that reorder is untypeable — and no document decides
///         on its own which list is which.
///     </para>
///     <para>
///         A list that is empty says nothing, so it is carried as <see langword="null" /> and the key is omitted
///         rather than written as <c>[]</c>, which keeps a clean document byte-identical to the one it always was;
///         <paramref name="ModelIncomplete" /> is <see langword="true" /> or <see langword="null" /> on the same
///         policy. Every document still declares its own slots and their order.
///     </para>
///     <para>
///         The unsupported projects' reason is read here, from <see cref="UnsupportedProjectsNotice.Reason" />, so
///         the wire key and the human line state one sentence. <paramref name="PackageAudit" />, the one fact here
///         that is not a verdict about the projects, rides the stamp so a load's lines are grouped once.
///     </para>
/// </remarks>
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
    ///     Reads the stamp off a load's own verdict, relativizing every project path against
    ///     <paramref name="solutionDirectory" /> so a machine path never reaches a golden.
    /// </summary>
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
