namespace SlideRule.Checking;

/// <summary>The outcome of evaluating one rule against the codebase.</summary>
internal enum RuleStatus
{
    /// <summary>
    ///     The rule holds. It may still carry warnings, and a baseline may grandfather every violation it has.
    /// </summary>
    Passed,

    /// <summary>
    ///     The rule does not hold: it carries a violation no baseline grandfathers, its subject matched nothing,
    ///     or it errored while being evaluated.
    /// </summary>
    Failed,

    /// <summary>
    ///     The run reached no verdict for this rule: a scope's tripwire with no diff base to compare against
    ///     (pass <c>--diff-base</c> to <c>check</c> to arm it), or a rule whose subject sits in projects the
    ///     run did not cover.
    /// </summary>
    Skipped
}
