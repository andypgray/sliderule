using SlideRule.Baselines;

namespace SlideRule.Checking;

/// <summary>
///     A note about something the check noticed without failing anything: a warning never changes its
///     rule's <see cref="RuleResult.Status" /> or the check's exit code, so it can sit on a rule that passed
///     or on one that failed.
/// </summary>
internal sealed class CheckWarning
{
    private CheckWarning(
        CheckWarningKind kind,
        string message,
        string? file,
        string? hint,
        string? key,
        DeadOperandRole role = DeadOperandRole.None)
    {
        Kind = kind;
        Message = message;
        File = file;
        Hint = hint;
        Key = key;
        Role = role;
    }

    /// <summary>Gets what raised the warning.</summary>
    public CheckWarningKind Kind { get; }

    /// <summary>Gets the line saying what was noticed.</summary>
    public string Message { get; }

    /// <summary>
    ///     Gets what to change in response to the warning, or null where there is nothing general to advise:
    ///     every warning about a file that changed, and a warning that the exemplar could not be resolved,
    ///     whose message already carries the fault. On a warning about a selection, a pattern or an exemplar
    ///     part that matched nothing it carries that selection's own semantics and the choice left to make; on
    ///     a warning about a baseline section no rule names it gives the <c>sliderule baseline rename</c> that
    ///     would move the section to the rule.
    /// </summary>
    public string? Hint { get; }

    /// <summary>
    ///     Gets the file the warning is about, relative to the solution directory and with forward slashes:
    ///     the same path <see cref="Message" /> names, held apart for in-process readers.
    ///     <c>check --json</c> does not carry it, since the warning's message there names the path. On an
    ///     <see cref="CheckWarningKind.OrphanedBaseline" /> warning it is the baseline file holding the section.
    ///     Null when the warning is about the spec rather than about the code or a baseline, which is every
    ///     <see cref="CheckWarningKind.InertTarget" />, <see cref="CheckWarningKind.DeadOperand" /> and
    ///     <see cref="CheckWarningKind.DeadExemplar" /> warning.
    /// </summary>
    public string? File { get; }

    /// <summary>
    ///     Gets what keeps two warnings of one kind on one rule apart: the pattern a
    ///     <see cref="CheckWarningKind.DeadOperand" /> warning is about, spelled exactly as <see cref="Message" />
    ///     quotes it (for example <c>the Legacy layer</c>, or <c>types named `B`</c> for one name of a <c>Named</c>
    ///     list that matched nothing); the rule ID the section an <see cref="CheckWarningKind.OrphanedBaseline" />
    ///     warning names is filed under; or the exemplar part a <see cref="CheckWarningKind.DeadExemplar" />
    ///     warning is about, spelled as <see cref="Message" /> quotes it, or the whole exemplar's reference on a
    ///     warning that it could not be resolved. Null on every other kind. <c>check --json</c> does not carry it,
    ///     since the message quotes the same string, except on a warning that the exemplar could not be resolved,
    ///     whose message carries the fault instead.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    ///     Which position the dead pattern held, and so which way the rule now errs, on a
    ///     <see cref="CheckWarningKind.DeadOperand" /> warning; <see cref="DeadOperandRole.None" /> on every
    ///     other kind.
    /// </summary>
    internal DeadOperandRole Role { get; }

    /// <summary>A warning that a rule's whole target selection matched no types.</summary>
    internal static CheckWarning InertTarget(string message, string? hint)
    {
        return new CheckWarning(CheckWarningKind.InertTarget, message, file: null, hint: hint, key: null);
    }

    /// <summary>
    ///     A warning that the pattern <paramref name="operand" /> matched no types in the position
    ///     <paramref name="role" /> names.
    /// </summary>
    internal static CheckWarning DeadOperand(DeadOperandRole role, string operand, string? hint)
    {
        string message = role switch
        {
            // The target-position twin of ConstraintEvaluator.EmptyOperandMessage (GRAMMAR §4.1, §5.1). Its own
            // wording rather than the whole-target one, because the rule is still enforcing what its other
            // operands name: what has stopped is one clause of the ban, not the ban.
            DeadOperandRole.Target =>
                $"This rule is partly inert: its target selection operand \"{operand}\" matched no types.",
            DeadOperandRole.AllowList =>
                $"This rule is stricter than written: its allow-list operand \"{operand}\" matched no types.",
            DeadOperandRole.Membership =>
                $"This rule is stricter than written: its membership \"{operand}\" matched no types.",
            DeadOperandRole.Among =>
                $"This rule is not measured as written: its among operand \"{operand}\" matched no types.",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Not a dead operand's role.")
        };
        return new CheckWarning(CheckWarningKind.DeadOperand, message, file: null, hint: hint, key: operand, role: role);
    }

    /// <summary>
    ///     A warning that the changed <paramref name="file" /> declares a type inside the scope
    ///     <paramref name="scopeId" />, cautioned or, where <paramref name="caution" /> is false, quarantined, whose
    ///     tripwire is the rule <paramref name="ruleId" />.
    /// </summary>
    internal static CheckWarning ScopeTouched(bool caution, string file, string scopeId, string ruleId)
    {
        CheckWarningKind kind = caution
            ? CheckWarningKind.CautionedScopeTouched
            : CheckWarningKind.QuarantinedScopeTouched;
        // The command names the RULE, which is the tripwire's own id, rather than respelling how one is minted from
        // a scope: text the reader is told to paste must not go stale against that mint.
        string message = caution
            ? $"Changed file '{file}' is inside cautioned scope '{scopeId}' — read the dragons before " +
              $"editing: sliderule explain {ruleId}."
            : $"Changed file '{file}' is inside quarantined scope '{scopeId}' — does the task actually " +
              $"require editing dragon territory? Dragons: sliderule explain {ruleId}.";
        return new CheckWarning(kind, message, file: file, hint: null, key: null);
    }

    /// <summary>
    ///     A warning that <paramref name="ruleId" /> failed with no baseline captured beside
    ///     <paramref name="orphan" />, a section filed under an ID no rule declares. The hint names the rename that
    ///     would move that section to the rule, and <c>sliderule baseline init</c> as well where
    ///     <paramref name="offerInit" /> is true: init skips a rule the run did not measure in full.
    /// </summary>
    internal static CheckWarning OrphanedBaseline(string ruleId, BaselineOrphan orphan, bool offerInit)
    {
        var message = $"No baseline is captured for this rule, and {orphan.Describe()}.";
        string rename =
            $"If this rule was renamed from '{orphan.RuleId}', run '{orphan.RenameCommand(ruleId)}' "
            + "to keep its entries";
        string hint = offerInit ? $"{rename}; if the rule is new, run 'sliderule baseline init'." : $"{rename}.";
        return new CheckWarning(
            CheckWarningKind.OrphanedBaseline, message, file: orphan.Path, hint: hint, key: orphan.RuleId);
    }

    /// <summary>
    ///     A warning that the exemplar part <paramref name="reference" /> matched no solution-declared types.
    /// </summary>
    internal static CheckWarning DeadExemplar(string reference, string? hint)
    {
        var message = $"This rule's exemplar \"{reference}\" matched no solution-declared types.";
        return new CheckWarning(CheckWarningKind.DeadExemplar, message, file: null, hint: hint, key: reference);
    }

    /// <summary>
    ///     A warning that the exemplar <paramref name="reference" /> could not be resolved at all, carrying the
    ///     evaluation's own <paramref name="fault" />: a closed generic where the open definition is wanted, or a
    ///     predicate that threw. No hint, since the fault already says what is wrong.
    /// </summary>
    internal static CheckWarning ExemplarFault(string reference, string fault)
    {
        var message = $"This rule's exemplar could not be resolved: {fault}";
        return new CheckWarning(CheckWarningKind.DeadExemplar, message, file: null, hint: null, key: reference);
    }
}
