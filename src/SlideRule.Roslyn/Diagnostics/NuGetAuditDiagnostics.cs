using System.Text.RegularExpressions;
using SlideRule.Internal;
using SlideRule.Prose;
using SlideRule.Rendering;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     One NuGet audit advisory as a run reports it: NuGet's own message, once, with the projects whose load
///     raised it.
/// </summary>
/// <remarks>
///     It rides the check, status and graph documents as it is, so its declaration order is the wire order —
///     <c>message</c>, then <c>projects</c> — and a null <see cref="Projects" /> is what omits that key.
/// </remarks>
/// <param name="Message">
///     The advisory text after Roslyn's project-load frame, verbatim. A <c>Warning As Error: </c> prefix stays
///     in it, so an escalated advisory is an entry of its own.
/// </param>
/// <param name="Projects">
///     The distinct names of the projects that reported it, ordinal-ordered, or <see langword="null" /> when the
///     lines carrying it named no project: a frame that did not parse says nothing about which project spoke,
///     and an empty list would claim that none did.
/// </param>
internal sealed record PackageAuditEntry(string Message, IReadOnlyList<string>? Projects)
{
    /// <summary>
    ///     The entry as one line — the message, then the projects that reported it, quoted as the merge notes
    ///     quote them (<c>… (2 projects: 'App' and 'App.Tests')</c>) — or the message alone when no project is
    ///     known. What stderr and SARIF print for it.
    /// </summary>
    internal string Describe()
    {
        if (Projects is null) return Message;

        string counted = Plurals.Counted(Projects.Count, "project");
        string names = ProseFormat.JoinQuotedAnd(Projects);
        return $"{Message} ({counted}: {names})";
    }
}

/// <summary>
///     Recognises the NuGet audit diagnostic family (NU19xx) among the workspace-load diagnostics and groups it,
///     one entry per distinct advisory, so a run reports an advisory once rather than once per project per
///     framework.
/// </summary>
/// <remarks>
///     <para>
///         <b>What this decides: which key a line lands in, never a verdict.</b> The fail-closed gate reads
///         project structure, never diagnostics (<see cref="WorkspaceDiagnostics.Gates" />), so an advisory
///         cannot flip an exit code whether this recognises it or not. A line recognised here renders inside a
///         <see cref="PackageAuditEntry" />; a line it misses renders in the diagnostic stream, verbatim. Both
///         reach every surface, so a miss costs the grouping and nothing else.
///     </para>
///     <para>
///         <b>The code is not in the text, so NuGet's text is what this matches.</b> Roslyn's project-load
///         reporting records the message and never the code, so <c>NU1902</c> is structurally absent from
///         every diagnostic that reaches a host; what arrives is NuGet's own words inside Roslyn's frame. That
///         frame carries the project's path, which can say anything, so both patterns run over what
///         <see cref="Unframe" /> leaves: a load failure in a project under a <c>NU1903-repro</c> directory
///         stays a load failure. The code match is kept for the paths that do carry one; the advisory match is
///         what does the work.
///     </para>
///     <para>
///         <b>A frame that does not parse still groups, without projects.</b> Roslyn's frame is English and its
///         resource is localized. A line in another language's frame, or one that never had a frame (a binlog
///         replay's coded line, a console logger's), is matched whole and forms an entry of its own with no
///         projects. A localized <em>advisory</em> matches nothing and stays in the stream, which is where a
///         line nothing recognises belongs.
///     </para>
///     <para>
///         The merge notes never come through here. They are this product's sentences rather than the load's,
///         and a type named <c>Acme.NU1903</c> would otherwise read as an advisory.
///     </para>
/// </remarks>
internal static partial class NuGetAuditDiagnostics
{
    // Roslyn's project-load frame. Split at the separator rather than at the path's closing quote, so a path
    // carrying an apostrophe still parses; the first separator is the frame's, because the path comes first.
    private const string FramePrefix = "Msbuild failed when processing the file '";
    private const string FrameSeparator = "' with message: ";

    /// <summary>
    ///     Whether <paramref name="diagnostic" /> is a NuGet audit advisory — NU1900 (the audit fetch itself
    ///     failed), NU1901–1904 (low/moderate/high/critical severity advisories), NU1905, and any future
    ///     NU19xx code — recognised in what follows Roslyn's frame, by the advisory text NuGet emits or by the
    ///     code on the paths that keep one. Both patterns stay deliberately narrow — phrases NuGet owns, and a
    ///     URL prefix nothing else emits — so that a false positive cannot move a genuine load diagnostic out
    ///     of the stream a reader looks for it in.
    /// </summary>
    internal static bool IsAudit(string diagnostic)
    {
        string message = Unframe(diagnostic).Message;
        return IsAuditMessage(message);
    }

    /// <summary>
    ///     The advisories among <paramref name="loadFailures" />, one entry per distinct message, each naming
    ///     the projects whose frames carried it. Lines whose frame did not parse group apart, into one entry per
    ///     message with no projects, placed after the named entry for the same message. Entries are otherwise
    ///     ordinal-ordered by message, so the result does not depend on the order the load reported in; lines
    ///     that are not advisories are left out.
    /// </summary>
    internal static IReadOnlyList<PackageAuditEntry> Group(IReadOnlyList<string> loadFailures)
    {
        IEnumerable<(string Message, string? Project)> advisories = loadFailures
            .Select(Unframe)
            .Where(line => IsAuditMessage(line.Message))
            .Select(line => (line.Message, ProjectName(line.Path)));

        return advisories
            .GroupBy(
                advisory => (advisory.Message, Named: advisory.Project is not null),
                advisory => advisory.Project)
            .OrderBy(group => group.Key.Message, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Named ? 0 : 1)
            .Select(ToEntry)
            .ToList();
    }

    /// <summary>
    ///     Splits Roslyn's project-load frame off <paramref name="diagnostic" />: the project path it names and
    ///     the message after it, or no path and the whole line when the frame does not parse.
    /// </summary>
    private static (string? Path, string Message) Unframe(string diagnostic)
    {
        if (!diagnostic.StartsWith(FramePrefix, StringComparison.Ordinal)) return (null, diagnostic);

        int separator = diagnostic.IndexOf(FrameSeparator, FramePrefix.Length, StringComparison.Ordinal);
        if (separator < 0) return (null, diagnostic);

        string path = diagnostic[FramePrefix.Length..separator];
        string message = diagnostic[(separator + FrameSeparator.Length)..];
        return (path, message);
    }

    /// <summary>
    ///     Whether <paramref name="code" /> is a NuGet audit code — the same NU19xx family
    ///     <see cref="IsAudit" /> recognises, matched on the code alone.
    /// </summary>
    /// <remarks>
    ///     For the one caller that has a real code to match rather than a sentence:
    ///     <see cref="RestoreFailures" /> reads <c>project.assets.json</c>, where NuGet records <c>code</c> and
    ///     <c>level</c> as separate invariant fields. That makes the carve-out this class could never perform
    ///     on the diagnostic stream — where <c>.Code</c> is discarded before a host sees it — exact and
    ///     language-independent there, and it is essential there in a way it is not here: it keeps an
    ///     advisory promoted to an error by <c>TreatWarningsAsErrors</c> from refusing a solution whose
    ///     resolution succeeded and whose model is complete.
    /// </remarks>
    internal static bool IsAuditCode(string code)
    {
        return AuditCode().IsMatch(code);
    }

    private static bool IsAuditMessage(string message)
    {
        return AuditText().IsMatch(message) || AuditCode().IsMatch(message);
    }

    // One entry per group: a named group's projects distinct and ordinal-ordered, an unnamed group's absent.
    private static PackageAuditEntry ToEntry(IGrouping<(string Message, bool Named), string?> group)
    {
        if (!group.Key.Named) return new PackageAuditEntry(group.Key.Message, null);

        List<string> projects = group
            .Select(project => project!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(project => project, StringComparer.Ordinal)
            .ToList();
        return new PackageAuditEntry(group.Key.Message, projects);
    }

    // The name the model gives a project: its file name without the extension. Two project files that share
    // a name are one name here, as they are in the model.
    private static string? ProjectName(string? path)
    {
        if (path is null) return null;

        string name = Path.GetFileNameWithoutExtension(PathFormat.Leaf(path));
        return name.Length > 0 ? name : null;
    }

    /// <summary>
    ///     The advisory itself: the GHSA link every NU1901–1904 message carries, the severity phrasing, and
    ///     the two NU1900 audit-fetch failures. Case-sensitive, and anchored on wording NuGet owns rather
    ///     than on any package or severity name a project could coincidentally produce.
    /// </summary>
    [GeneratedRegex(
        @"https://github\.com/advisories/"
        + @"|\bhas a known (low|moderate|high|critical) severity vulnerability\b"
        + @"|\bError occurred while getting package vulnerability data\b"
        + @"|\bwhile running a security audit\b")]
    private static partial Regex AuditText();

    /// <summary>
    ///     The code token, for any path that preserves one (a binlog replay, a future Roslyn that carries
    ///     <c>.Code</c>). Word-bounded on both sides — so <c>XNU1903</c>, a bare <c>NU19</c>, and
    ///     <c>NU19034</c> all miss — <c>[0-9]</c> rather than <c>\d</c> for ASCII-ordinal intent, and
    ///     case-sensitive.
    /// </summary>
    [GeneratedRegex(@"\bNU19[0-9]{2}\b")]
    private static partial Regex AuditCode();
}
