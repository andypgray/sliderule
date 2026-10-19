using SlideRule.Checking;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     How <c>status</c> and <c>baseline</c> name the dead patterns of a rule the run did not measure as
///     written: <c>the target operand "A"</c>, <c>the allow-list operands "A" and "B"</c>, and what clears them.
/// </summary>
/// <remarks>
///     One owner for both verbs, because both name the same patterns of the same rule in the same run, and
///     a reader who meets the two sentences side by side should find one spelling. Double quotes, as the
///     check's own warning quotes the same reference, so the two can be matched by eye. The noun for each
///     role is the one the check warning uses, with a target's "target selection operand" shortened to
///     "target operand".
/// </remarks>
internal static class DeadOperandWording
{
    /// <summary>The dead patterns as one noun phrase, inflected for how many there are.</summary>
    /// <param name="role">The position the patterns held; never <see cref="DeadOperandRole.None" />.</param>
    /// <param name="references">The patterns, each as its warning quotes it, in operand order.</param>
    internal static string NamedOperands(this DeadOperandRole role, IReadOnlyList<string> references)
    {
        List<string> quoted = references.Select(reference => $"\"{reference}\"")
            .ToList();
        string noun = Plurals.Noun(quoted.Count, Noun(role));

        return $"the {noun} {ProseFormat.JoinReferencesAnd(quoted)}";
    }

    /// <summary>
    ///     What clears dead patterns in <paramref name="role" />, as the end of "until each dead operand …":
    ///     <c>matches or moves to a rule of its own</c> for a target, <c>matches or is removed</c> otherwise.
    /// </summary>
    internal static string UntilClause(this DeadOperandRole role)
    {
        return IsTarget(role) ? "matches or moves to a rule of its own" : "matches or is removed";
    }

    /// <summary>
    ///     The instruction that clears dead patterns in <paramref name="role" />, ahead of ", then re-run.":
    ///     <c>Fix each dead operand or move it to a rule of its own</c> for a target, and
    ///     <c>Fix or remove each dead operand</c> otherwise.
    /// </summary>
    internal static string ClearingInstruction(this DeadOperandRole role)
    {
        return IsTarget(role) ? "Fix each dead operand or move it to a rule of its own" : "Fix or remove each dead operand";
    }

    // A target pattern may move to a rule of its own, since the rest of the ban holds without it; every other
    // pattern is part of what its rule allows or requires, so the cure is to fix or remove it.
    private static bool IsTarget(DeadOperandRole role)
    {
        return role switch
        {
            DeadOperandRole.Target => true,
            DeadOperandRole.Among or DeadOperandRole.AllowList or DeadOperandRole.Membership => false,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
    }

    private static string Noun(DeadOperandRole role)
    {
        return role switch
        {
            DeadOperandRole.Target => "target operand",
            DeadOperandRole.Among => "among operand",
            DeadOperandRole.AllowList => "allow-list operand",
            DeadOperandRole.Membership => "membership",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
    }
}
