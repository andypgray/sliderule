using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>
///     One warning a build reports: which rule broke and how, what broke it, the exact place to put the
///     squiggle, and the single line to print there.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="FilePath" /> is the violation's own string, untouched — not relativized, not
///         normalized, not case-folded. The caller looks a syntax tree up by this exact string, so any
///         tidying here would lose the finding rather than move it.
///     </para>
///     <para>
///         The three identity slots are <c>Violation.BaselineIdentity()</c> flattened: an edge identity
///         fills <see cref="Source" /> and <see cref="Target" />, a subject identity fills
///         <see cref="Subject" />, and the rest stay null. They are taken from the identity rather than
///         from the violation's own slots so that what a finding is keyed by and what a baseline would
///         record cannot drift apart.
///     </para>
/// </remarks>
internal sealed class BuildFinding
{
    // The identity and the site arrive whole rather than as five loose strings: Source, Target and Subject
    // are same-typed and adjacent, which is a transposition no compiler can catch, and taking the identity
    // itself is also what keeps this projection honest about where the three slots come from.
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

    /// <summary>Gets whether this finding reports a broken law or newly grown debt.</summary>
    internal BuildFindingKind Kind { get; }

    /// <summary>Gets the ID of the rule that was broken, as the spec declared it.</summary>
    internal string RuleId { get; }

    /// <summary>Gets what kind of violation this finding reports.</summary>
    internal ViolationKind ViolationKind { get; }

    /// <summary>Gets the near end of the offending edge's baseline identity; null for a shape violation.</summary>
    internal string? Source { get; }

    /// <summary>Gets the far end of the offending edge's baseline identity; null for a shape violation.</summary>
    internal string? Target { get; }

    /// <summary>Gets the offending subject's baseline identity; null for an edge violation.</summary>
    internal string? Subject { get; }

    /// <summary>Gets the site's path exactly as the violation carries it.</summary>
    internal string FilePath { get; }

    /// <summary>Gets the site's 1-based line number.</summary>
    internal int Line { get; }

    /// <summary>
    ///     Gets the extent of the reference on that line, or null where the site carries none — which is
    ///     where a warning falls back to covering the line.
    /// </summary>
    internal SourceSpan? Span { get; }

    /// <summary>Gets the one line to print: which rule broke, what broke it, why the rule exists, how to fix it.</summary>
    internal string Message { get; }
}
