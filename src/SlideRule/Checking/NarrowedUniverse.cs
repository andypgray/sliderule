using SlideRule.Internal;

namespace SlideRule.Checking;

/// <summary>
///     The fact that a run saw less than the solution, which turns a rule's empty subject from a spec
///     defect into an absence the run cannot speak to, and an inert target's cure from advice about its
///     shape into a pointer at what the run left out (GRAMMAR §4.1). Two kinds: a solution filter answered
///     the run over part of the solution, or one project's compilation is all the run was given.
/// </summary>
/// <remarks>
///     <para>
///         Null on every run whose universe is the whole solution, a filtered run that narrowed nothing
///         included, so no such run skips a rule or replaces a cure for narrowing.
///     </para>
///     <para>
///         The filter kind's two sentences arrive composed rather than assembled here: the narrowing
///         lexicon lives with the stamps every verb writes, so a wording minted here would drift from them
///         silently. The filter's name and the count it left out are therefore the composer's inputs rather
///         than this value's contents: what a checked rule needs is the sentence.
///     </para>
///     <para>
///         The one-project kind's two sentences are constants here, for the same reason turned round: no
///         verb stamps that view and no surface prints its sentences, so there is no lexicon for them to
///         drift from. That kind also cuts a rule's subject down to the parts this view can see before the
///         rule is evaluated, which the filter kind never does: a filter leaves out projects the spec author
///         can name, while one project's view leaves out nearly all of them by construction.
///     </para>
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

    /// <summary>
    ///     The narrowing a run over one project's compilation carries — one immutable value for every such
    ///     run, its two sentences fixed.
    /// </summary>
    internal static NarrowedUniverse OneProject { get; } =
        new(NarrowingKind.OneProject, OneProjectRuleSkipReason, OneProjectInertTargetHint);

    /// <summary>Builds the narrowing a solution filter answers a run under, from its two sentences.</summary>
    /// <param name="ruleSkipReason">The one-line reason a rule skipped for narrowing carries.</param>
    /// <param name="inertTargetHint">The cure an inert target carries in place of its shape advice.</param>
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

    /// <summary>Which narrowing this is — what the checker reads to decide whether a subject is cut.</summary>
    internal NarrowingKind Kind { get; }

    /// <summary>
    ///     The one-line reason a narrowing-skipped rule reports. Under the filter kind it names the filter
    ///     and the count but not the projects themselves: those ride the stamp above the report, one place
    ///     to read them rather than one copy per rule.
    /// </summary>
    internal string RuleSkipReason { get; }

    /// <summary>
    ///     The cure an inert target reports under this narrowing. It <em>replaces</em> the shape advice rather
    ///     than trailing it, as <see cref="IncompleteModel.EmptySelectionHint" /> does: the target may name
    ///     types in a project this run never checked, so advice about how it was spelled is advice about the
    ///     wrong thing. Like the skip reason, the filter kind's counts the unchecked projects and never lists
    ///     them.
    /// </summary>
    internal string InertTargetHint { get; }
}
