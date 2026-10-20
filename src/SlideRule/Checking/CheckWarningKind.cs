namespace SlideRule.Checking;

/// <summary>What a <see cref="CheckWarning" /> is about.</summary>
internal enum CheckWarningKind
{
    /// <summary>
    ///     A forbidden-set dependency verb's target matched no types at all, so the rule can never fire and
    ///     is guarding nothing: either the target is wrong or what it forbids does not exist yet. A rule
    ///     carries at most one of these, and never beside a <see cref="DeadOperand" /> warning. A
    ///     target named by <c>typeof</c> and absent from the codebase warns about nothing: that is the rule
    ///     working. The <c>MustOnly</c> verbs never raise this one, since an allow-list that matches nothing
    ///     shows up as violations instead; each of their patterns that matched nothing raises
    ///     <see cref="DeadOperand" />.
    /// </summary>
    InertTarget,

    /// <summary>
    ///     A file changed since the diff base (<c>check --diff-base</c>) declares a type inside a quarantined
    ///     scope. The warning asks whether the task requires editing inside that scope at all.
    /// </summary>
    QuarantinedScopeTouched,

    /// <summary>
    ///     A file changed since the diff base (<c>check --diff-base</c>) declares a type inside a cautioned
    ///     scope. The warning asks that the scope's <c>Dragons</c> prose or <c>DragonsDoc</c> document be read
    ///     before editing; nothing about a cautioned scope ever fails the check.
    /// </summary>
    CautionedScopeTouched,

    /// <summary>
    ///     A pattern in one of the rule's lists matched no types, so the rule was not measured as written: a
    ///     forbidden target beside targets that matched some, an allow-list pattern of a <c>MustOnly</c> verb or
    ///     of a quarantine's sanctioned surface, a membership of <c>MustBelongTo</c> or the glob or project name
    ///     of <c>MustResideInNamespace</c> and <c>MustResideInProject</c>, or a part of
    ///     <c>MustHaveExactlyOneCounterpart</c>'s <c>among:</c>.
    /// </summary>
    /// <remarks>
    ///     Each distinct pattern gets one warning. A pattern can be one piece of an operand: a part of a union, a
    ///     layer's definition, or one name of a <c>Named</c> list, quoted as the operand would read with that name
    ///     alone. A <c>typeof</c> operand absent from the codebase warns about nothing, and the memberships, the
    ///     reside pair's glob or project name and the <c>among:</c> parts raise none on a run over only part of
    ///     the solution. Whichever list the pattern sat in, a rule carrying one of these was not measured as
    ///     written (<see cref="RuleResult.MeasuredInFull" />).
    /// </remarks>
    DeadOperand,

    /// <summary>
    ///     A ratcheted rule failed with no baseline captured while a baseline section filed under a rule ID the spec
    ///     does not declare sits in the solution: the section a rename leaves behind under the rule's old ID. Each
    ///     such section gets one warning.
    /// </summary>
    OrphanedBaseline,

    /// <summary>
    ///     A part of the rule's exemplar, the type a fix is pointed at, matched no type the solution declares, or
    ///     the exemplar could not be resolved at all. The exemplar never decides the verdict, so the rule's status
    ///     is unaffected and its counts were measured in full: nothing here stops a promotion or a baseline
    ///     capture. Each dead part gets one warning: a union operand, a name of a <c>Named</c> list quoted as the
    ///     selection would read with that name alone, or the whole exemplar where the whole is dead. A run over a
    ///     solution filter resolves the exemplar but raises none of these, since a project the filter left out may
    ///     declare the type.
    /// </summary>
    DeadExemplar
}
