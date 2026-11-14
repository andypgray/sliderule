namespace SlideRule.Checking;

/// <summary>
///     Which position a pattern that matched nothing, or an exception that removed nothing, held in its rule, and
///     so which way the rule now errs (GRAMMAR §4.1).
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
    Membership,

    /// <summary>
    ///     An exception from a subject, or from a forbidden-set target: removing nothing, it spares nothing, so it
    ///     only adds violations.
    /// </summary>
    BanException,

    /// <summary>
    ///     An exception from an allow-list or a membership: removing nothing, it forbids nothing, so it removes
    ///     violations.
    /// </summary>
    AllowException,

    /// <summary>
    ///     An exception from an <c>among:</c> part: removing nothing, it keeps a counterpart it was meant to drop,
    ///     which can add a violation or remove one.
    /// </summary>
    AmongException
}

/// <summary>The direction a <see cref="DeadOperandRole" /> errs in.</summary>
internal static class DeadOperandRoles
{
    /// <summary>Whether a dead operand in <paramref name="role" /> can only add violations, never hide one.</summary>
    internal static bool IsStricter(this DeadOperandRole role)
    {
        return role is DeadOperandRole.AllowList or DeadOperandRole.Membership or DeadOperandRole.BanException;
    }

    /// <summary>Whether <paramref name="role" /> is an exception's rather than an operand's.</summary>
    internal static bool IsException(this DeadOperandRole role)
    {
        return role is DeadOperandRole.BanException or DeadOperandRole.AllowException or DeadOperandRole.AmongException;
    }
}
