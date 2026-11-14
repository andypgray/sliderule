using SlideRule.Checking;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     How a verb names the dead patterns of a rule the run did not measure as written:
///     <c>the target operand "A"</c>, <c>the allow-list operands "A" and "B"</c>, and what clears them.
/// </summary>
/// <remarks>
///     One owner, so two verbs naming the same patterns of the same rule in the same run spell them alike.
///     Double quotes, as the check's own warning quotes the same reference, so the two can be matched by eye.
///     The noun for each role is the one the check warning uses, with a target's "target selection operand"
///     shortened to "target operand".
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
    ///     What the dead patterns behind <paramref name="warnings" /> did, as one clause: each role's patterns named
    ///     as <see cref="NamedOperands" /> names them, then <c>matched no types</c>, or <c>removed nothing</c> for
    ///     exceptions, the groups in the order their first warning comes.
    /// </summary>
    internal static string Described(IReadOnlyList<CheckWarning> warnings)
    {
        IEnumerable<string> groups = RoleGroups(warnings)
            .Select(group =>
            {
                string outcome = group.Role.IsException() ? "removed nothing" : "matched no types";
                return $"{group.Role.NamedOperands(group.References)} {outcome}";
            });
        return string.Join(" and ", groups);
    }

    /// <summary>
    ///     What clears the dead patterns behind <paramref name="warnings" />, as the end of "until …":
    ///     <c>each dead operand</c> followed by the shared clause when every role takes the same clause; otherwise,
    ///     each distinct clause preceded by <c>each dead</c> and its roles' nouns, joined with "and".
    /// </summary>
    internal static string UntilClauses(IReadOnlyList<CheckWarning> warnings)
    {
        List<IGrouping<string, DeadOperandRole>> clauses = RoleGroups(warnings)
            .Select(group => group.Role)
            .GroupBy(UntilClause, StringComparer.Ordinal)
            .ToList();
        if (clauses.Count == 1) return $"each dead operand {clauses[0].Key}";

        IEnumerable<string> parts = clauses.Select(clause => $"each dead {RoleNouns(clause)} {clause.Key}");
        return string.Join(" and ", parts);
    }

    /// <summary>
    ///     The instruction that clears the dead patterns behind <paramref name="warnings" />, ahead of
    ///     ", then re-run.": <c>Fix each dead operand or move it to a rule of its own</c> for targets alone,
    ///     <c>Fix or remove each dead operand</c> for every other role, and both, each naming its roles, for a mix.
    /// </summary>
    internal static string ClearingInstructions(IReadOnlyList<CheckWarning> warnings)
    {
        List<IGrouping<bool, DeadOperandRole>> instructions = RoleGroups(warnings)
            .Select(group => group.Role)
            .GroupBy(IsTarget)
            .ToList();
        IEnumerable<string> parts = instructions.Count == 1
            ? [ClearingInstruction(instructions[0].Key, "operand")]
            : instructions.Select(instruction => ClearingInstruction(instruction.Key, RoleNouns(instruction)));
        return ProseFormat.Capitalize(string.Join(", and ", parts));
    }

    /// <summary>
    ///     Why a capture of the stricter-than-written rule behind <paramref name="warnings" /> would be wrong, as
    ///     the clause after "so": <c>what it was meant to allow is reported as violations</c> for one role, and
    ///     for a mix each role's fragment naming the role in place of the pronoun
    ///     (<c>the types the membership was meant to hold</c>), joined before <c>are reported as violations</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A warning's role is not a stricter one.</exception>
    internal static string StricterConsequence(IReadOnlyList<CheckWarning> warnings)
    {
        List<(DeadOperandRole Role, List<string> References)> groups = RoleGroups(warnings);
        if (groups.Count == 1)
        {
            (DeadOperandRole role, List<string> references) = groups[0];
            string pronoun = references.Count == 1 ? "it" : "they";
            string meant = MeantFor(role, $"{pronoun} {Plurals.PastVerb(references.Count)}");
            // "the types … are", "what … is".
            string verb = role == DeadOperandRole.Membership ? "are" : "is";
            return $"{meant} {verb} reported as violations";
        }

        List<string> fragments = groups
            .Select(group =>
            {
                int count = group.References.Count;
                var subject = $"the {Plurals.Noun(count, Noun(group.Role))} {Plurals.PastVerb(count)}";
                return MeantFor(group.Role, subject);
            })
            .ToList();
        return $"{ProseFormat.JoinReferencesAnd(fragments)} are reported as violations";
    }

    private static string MeantFor(DeadOperandRole role, string subject)
    {
        return role switch
        {
            DeadOperandRole.AllowList => $"what {subject} meant to allow",
            DeadOperandRole.Membership => $"the types {subject} meant to hold",
            DeadOperandRole.BanException => $"what {subject} meant to spare",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a stricter role.")
        };
    }

    // Exceptions, which can match a type and still remove nothing, are cleared by removing something.
    private static string UntilClause(DeadOperandRole role)
    {
        if (role.IsException()) return "removes something or is removed";

        return IsTarget(role) ? "matches or moves to a rule of its own" : "matches or is removed";
    }

    private static string ClearingInstruction(bool target, string noun)
    {
        return target ? $"fix each dead {noun} or move it to a rule of its own" : $"fix or remove each dead {noun}";
    }

    // Roles that share a noun, such as an allow-list's exception beside an among part's, name it once.
    private static string RoleNouns(IEnumerable<DeadOperandRole> roles)
    {
        List<string> nouns = roles.Select(Noun)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return string.Join(" or ", nouns);
    }

    // Each role's distinct references, the roles in the order their first warning comes.
    private static List<(DeadOperandRole Role, List<string> References)> RoleGroups(IReadOnlyList<CheckWarning> warnings)
    {
        return warnings
            .GroupBy(warning => warning.Role)
            .Select(group => (group.Key, group.Select(warning => warning.Key!).Distinct(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    // A target pattern may move to a rule of its own, since the rest of the ban holds without it; every other
    // pattern is part of what its rule allows or requires, so the cure is to fix or remove it.
    private static bool IsTarget(DeadOperandRole role)
    {
        return role switch
        {
            DeadOperandRole.Target => true,
            DeadOperandRole.Among or DeadOperandRole.AllowList or DeadOperandRole.Membership
                or DeadOperandRole.BanException or DeadOperandRole.AllowException
                or DeadOperandRole.AmongException => false,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
    }

    internal static string Noun(DeadOperandRole role)
    {
        return role switch
        {
            DeadOperandRole.Target => "target operand",
            DeadOperandRole.Among => "among operand",
            DeadOperandRole.AllowList => "allow-list operand",
            DeadOperandRole.Membership => "membership",
            DeadOperandRole.BanException or DeadOperandRole.AllowException or DeadOperandRole.AmongException =>
                "exception",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
    }
}
