using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>One warning a build reports, with the exact place to put the squiggle.</summary>
/// <remarks>
///     The three identity slots are <c>Violation.BaselineIdentity()</c> flattened: an edge identity fills
///     <see cref="Source" /> and <see cref="Target" />, a subject identity fills <see cref="Subject" />, and the
///     rest stay null. They are taken from the identity rather than from the violation's own slots so that
///     what a finding is keyed by and what a baseline would record cannot drift apart.
/// </remarks>
internal sealed class BuildFinding
{
    // The identity and the site arrive whole: Source, Target and Subject as loose strings would be same-typed
    // and adjacent, a transposition no compiler can catch.
    internal BuildFinding(
        BuildFindingKind kind,
        string ruleId,
        ViolationKind violationKind,
        BaselineEntry? identity,
        SourceLocation site,
        string message)
    {
        Kind = kind;
        RuleId = ruleId;
        ViolationKind = violationKind;
        Source = identity?.Source;
        Target = identity?.Target;
        Subject = identity?.Subject;
        FilePath = site.FilePath;
        Line = site.Line;
        Span = site.Span;
        Message = message;
    }

    internal BuildFindingKind Kind { get; }

    internal string RuleId { get; }

    internal ViolationKind ViolationKind { get; }

    internal string? Source { get; }

    internal string? Target { get; }

    internal string? Subject { get; }

    /// <summary>
    ///     Gets the site's path exactly as the violation carries it: not relativized, normalized or case-folded.
    ///     The caller looks a syntax tree up by this exact string, so any tidying would lose the finding rather
    ///     than move it.
    /// </summary>
    internal string FilePath { get; }

    /// <summary>Gets the site's 1-based line number.</summary>
    internal int Line { get; }

    /// <summary>Gets the extent of the reference on that line, or null where the site carries none.</summary>
    internal SourceSpan? Span { get; }

    /// <summary>Gets the one line to print (<see cref="BuildFindingMessage" />).</summary>
    internal string Message { get; }
}
