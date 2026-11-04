using SlideRule.Internal;
using SlideRule.Rendering;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The wording each verb uses to say that the run did not survey some of the solution's projects — the
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
            UnsupportedProjectKind.NotBuilt => "not built by the solution's default configuration",
            UnsupportedProjectKind.WebSite => "a Web Site project, which has no project file",
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
            .Select(project => Entry(Shown(project, relativizer), Reason(project.Kind)))
            .ToList();
    }

    /// <summary>
    ///     How an entry names its project on every surface: the name the solution gives it where it has one, and
    ///     its path relativized by <paramref name="relativizer" /> otherwise.
    /// </summary>
    internal static string Shown(UnsupportedProject project, PathFormat.Relativizer relativizer)
    {
        return project.Name ?? relativizer.Relative(project.Path);
    }

    /// <summary>
    ///     The name a selection gives <paramref name="project" />: the name the solution gives it where it has one,
    ///     and its file name without the extension otherwise, as a C# project is named by its csproj.
    /// </summary>
    internal static string SelectionName(UnsupportedProject project)
    {
        return project.Name ?? Path.GetFileNameWithoutExtension(project.Path);
    }

    /// <summary>
    ///     The cure a rule's selection reports when it is anchored on <paramref name="project" />, in place of advice
    ///     about how the selection is spelled, which no spelling could make match.
    /// </summary>
    /// <remarks>
    ///     It reads in the voice of the cures it replaces: one line, opening upper-case, diagnosis parted from cure by
    ///     a single semicolon, ending in a full stop, never in the second person. The remedy follows the kind,
    ///     because a project the default configuration skips is already C#.
    /// </remarks>
    internal static string UnsurveyedCure(UnsupportedProject project, PathFormat.Relativizer relativizer)
    {
        string remedy = project.Kind switch
        {
            UnsupportedProjectKind.SharedProject => "anchor the rule on the projects that import it",
            UnsupportedProjectKind.NotBuilt =>
                "build it in the solution's default configuration, or anchor the rule elsewhere",
            _ => "anchor the rule on C# projects, or remove it"
        };
        return $"{Shown(project, relativizer)} is a project this run does not survey ({Reason(project.Kind)}), so a "
               + $"selection anchored on it matches nothing; {remedy}.";
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
            + "violations to it, so every count reads low by whatever they hold.");
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

    /// <summary>
    ///     The clause every surface opens its list of unsupported projects with, inflected for
    ///     <paramref name="unsupportedProjectCount" />.
    /// </summary>
    /// <remarks>
    ///     It states no cause, because each entry beneath it says its own, and "C# only" would be false about a
    ///     shared project. Nor does it say the solution declares them: a replay lists a compiler call it declined
    ///     whether or not the solution names its project.
    /// </remarks>
    internal static string Subject(int unsupportedProjectCount)
    {
        return $"{Plurals.Counted(unsupportedProjectCount, "project")} "
               + $"{Plurals.PastVerb(unsupportedProjectCount)} not surveyed";
    }
}
