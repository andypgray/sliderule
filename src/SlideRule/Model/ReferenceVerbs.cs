namespace SlideRule.Model;

/// <summary>Which reference verbs are inbound, and which are allow-lists that hold their own subject.</summary>
internal static class ReferenceVerbs
{
    /// <summary>Whether the subject is the referenced end, which is true of the three inbound verbs alone.</summary>
    internal static bool SubjectAtTarget(Constraint constraint)
    {
        return constraint is MustNotBeReferencedByConstraint
            or MustOnlyBeReferencedByConstraint
            or MustOnlyBeReferencedByItselfConstraint;
    }

    /// <summary>Whether the verb is a reference allow-list, which holds its own subject (GRAMMAR §4.1).</summary>
    internal static bool AllowsSubject(Constraint constraint)
    {
        // The four verbs whose evaluator arm folds the subject back into its own allow-set
        // (ConstraintEvaluator.AllowList, `operands.IncludingSelf(subject)`), which makes the subject an
        // exemption as much as a bound. MustOnlyThrow is deliberately absent: its arm resolves the operands
        // alone, so its subject only bounds which throws are owned.
        return constraint is MustOnlyReferenceConstraint
            or MustOnlyReferenceItselfConstraint
            or MustOnlyBeReferencedByConstraint
            or MustOnlyBeReferencedByItselfConstraint;
    }
}
