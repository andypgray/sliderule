namespace SlideRule.Checking;

/// <summary>
///     Which position a pattern that matched nothing held in its rule, and so which way the rule now errs
///     (GRAMMAR §4.1).
/// </summary>
internal enum DeadOperandRole
{
    /// <summary>The warning is not about a dead operand.</summary>
    None,

    /// <summary>A target pattern of a forbidden-set verb: its death removes violations.</summary>
    Target,

    /// <summary>
    ///     A part of a counterpart verb's <c>among:</c>: its death can add a violation (a subject left with
    ///     no counterpart) or remove one (a subject left with one instead of two).
    /// </summary>
    Among,

    /// <summary>
    ///     A pattern a <c>MustOnly</c> verb or a quarantine's containment allows: its death only adds violations.
    /// </summary>
    AllowList,

    /// <summary>
    ///     A membership a subject must belong to, the reside pair's namespace glob or project name included: its
    ///     death only adds violations.
    /// </summary>
    Membership
}

/// <summary>The direction a <see cref="DeadOperandRole" /> errs in.</summary>
internal static class DeadOperandRoles
{
    /// <summary>Whether a dead operand in <paramref name="role" /> can only add violations, never hide one.</summary>
    internal static bool IsStricter(this DeadOperandRole role)
    {
        return role is DeadOperandRole.AllowList or DeadOperandRole.Membership;
    }
}
