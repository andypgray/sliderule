using SlideRule.Internal;
using SlideRule.Rendering;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The wording each verb uses to say that the solution declares projects the run did not survey — the
///     human twin of the documents' <c>unsupportedProjects</c> slot.
/// </summary>
/// <remarks>
///     Every tail closes on the reading being denied rather than on a command, because no command would widen
///     the answer. There is no <c>graph</c> stamp: the survey lists these projects as a section of its own
///     roster, and a stamp as well would say it twice. The stamps take the entries <see cref="Relative" />
///     composes.
/// </remarks>
internal static class UnsupportedProjectsNotice
{
    /// <summary>
    ///     Why a project of this <paramref name="kind" /> is outside the model, as a reader reads it: the one
    ///     author of that sentence, on every surface.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         No line carries an em dash: <see cref="Entry" /> already joins on one, and a reason with a
    ///         second would leave the line with two.
    ///     </para>
    ///     <para>
    ///         The discard arm reads as <see cref="UnsupportedProjectKind.NotCsharp" />, the catch-all, so a kind
    ///         added without its own arm is described as the wrong thing. <c>UnsupportedProjectsNoticeTests</c>
    ///         asserts that every declared kind reads differently from the others, which reds on that omission;
    ///         dropping the discard arm instead would trade it for CS8524 on every unnamed value.
    ///     </para>
    /// </remarks>
    internal static string Reason(UnsupportedProjectKind kind)
    {
        return kind switch
        {
            UnsupportedProjectKind.SharedProject => "a shared project, compiled into the projects that import it",
            _ => "not a C# project"
        };
    }

    /// <summary>
    ///     The entry as a human line reads it: the project, then why. One owner, so no two surfaces spell the same
    ///     entry two ways.
    /// </summary>
    internal static string Entry(string project, string reason)
    {
        return $"{project} — {reason}";
    }

    /// <summary>
    ///     The unsupported projects as a notice shows them: one composed <see cref="Entry" /> per project,
    ///     each path solution-relative and forward-slashed like every path beside it, so a machine path never
    ///     lands in output a golden pins.
    /// </summary>
    /// <remarks>
    ///     It composes where <see cref="NarrowedUniverseNotice.Relative" /> only relativizes: a caller that had to
    ///     add the reason would be a second author for this file's sentence.
    /// </remarks>
    internal static IReadOnlyList<string> Relative(
        IReadOnlyList<UnsupportedProject> projects, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return projects
            .Select(project => Entry(relativizer.Relative(project.Path), Reason(project.Kind)))
            .ToList();
    }

    /// <summary>The stamp <c>check</c> writes above its report.</summary>
    internal static string CheckStamp(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "The verdict below covers only the projects this product can read, so a clean result here is not "
            + "a clean solution.");
    }

    /// <summary>The stamp <c>status</c> writes above its burndown.</summary>
    internal static string StatusStamp(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "The burndown below counts only the projects this product can read: these contribute no "
            + "violations and never will, so every count reads low by whatever they hold.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason over a solution declaring projects this run could not
    ///     reach, the adapter's twin of the CLI stamps.
    /// </summary>
    internal static string AdapterSkip(IReadOnlyList<string> unsupportedProjects)
    {
        return Block(
            unsupportedProjects,
            "Rule verdicts come from the projects this product can read, but a test by this name cannot pass "
            + "while the solution declares projects the model never held.");
    }

    private static string Block(IReadOnlyList<string> unsupportedProjects, string tail)
    {
        var lede = $"{Subject(unsupportedProjects.Count)}:";
        return EvidenceBlock.Compose(lede, unsupportedProjects, tail);
    }

    // The count as a clause every lede can take, in both numbers: the sentence a reader stops trusting is
    // "1 projects ... were not surveyed". It states no cause, because each entry beneath it says its own, and
    // "C# only" would be false about a shared project.
    private static string Subject(int unsupportedProjectCount)
    {
        string noun = Plurals.Noun(unsupportedProjectCount, "project");
        string copula = Plurals.PastVerb(unsupportedProjectCount);

        return $"{unsupportedProjectCount} {noun} the solution declares {copula} not surveyed";
    }
}
