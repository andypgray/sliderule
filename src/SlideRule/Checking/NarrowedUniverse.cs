using SlideRule.Internal;

namespace SlideRule.Checking;

/// <summary>
///     The fact that a run saw less than the solution, which turns a rule's empty subject from a spec
///     defect into an absence the run cannot speak to, and an inert target's cure from advice about its
///     shape into a pointer at what the run left out (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     Null on every run whose universe is the whole solution, a filtered run that narrowed nothing included.
///     The filter kind's two sentences arrive composed, in the lexicon of the stamps every verb writes, and a
///     wording minted here would drift from them silently; both name the filter and count the projects it left
///     out, never listing them, since the stamp above the report does. The one-project kind's are constants
///     here, since no verb stamps that view.
/// </remarks>
internal sealed class NarrowedUniverse
{
    private const string OneProjectRuleSkipReason =
        "This run checked one project's compilation, and this rule's subject matched no type in it, so the "
        + "rule was not measured here and this is not a clean result for it; a check over the whole solution "
        + "answers it.";

    private const string OneProjectInertTargetHint =
        "This run checked one project's compilation, so this rule's target may match types in a project it "
        + "did not see; check the rule over the whole solution before deciding whether to keep it.";

    /// <summary>The narrowing every run over one project's compilation carries.</summary>
    internal static NarrowedUniverse OneProject { get; } =
        new(NarrowingKind.OneProject, OneProjectRuleSkipReason, OneProjectInertTargetHint);

    /// <summary>Builds the narrowing a solution filter answers a run under, from its two sentences.</summary>
    internal NarrowedUniverse(string ruleSkipReason, string inertTargetHint)
        : this(NarrowingKind.SolutionFilter, ruleSkipReason, inertTargetHint)
    {
    }

    private NarrowedUniverse(NarrowingKind kind, string ruleSkipReason, string inertTargetHint)
    {
        Kind = kind;
        RuleSkipReason = Guard.NotNull(ruleSkipReason, nameof(ruleSkipReason));
        InertTargetHint = Guard.NotNull(inertTargetHint, nameof(inertTargetHint));
    }

    internal NarrowingKind Kind { get; }

    /// <summary>The one-line reason a narrowing-skipped rule reports.</summary>
    internal string RuleSkipReason { get; }

    /// <summary>The cure an inert target reports under this narrowing, in place of its shape advice.</summary>
    internal string InertTargetHint { get; }
}
