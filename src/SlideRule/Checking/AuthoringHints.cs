using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Checking;

/// <summary>
///     The cure that rides each authoring signal: what to change when a rule's own selection, rather than
///     the code it ranges over, is what went wrong (GRAMMAR §4.1).
/// </summary>
/// <remarks>
///     <para>
///         The advice is chosen from the selection that came up empty, and must stay that way: each shape
///         fails for its own reason, so one fixed sentence would be false on most of them.
///     </para>
///     <para>
///         Clauses are lower-cased and unterminated so one composer can stand a clause alone or hang a
///         tail on it; <see cref="Sentence" /> is the only place either happens, and a clause must
///         therefore open with a letter.
///     </para>
///     <para>
///         A partial model <em>replaces</em> every cure here rather than appending to one, and every entry
///         point therefore takes the fact. Telling a reader to check a selection against what the solution
///         declares is wrong when the reason it came up empty may be that a project never loaded, and a
///         sentence carrying both cures would break the single-semicolon shape these clauses are built to
///         keep.
///     </para>
///     <para>
///         A narrowing replaces the cures on a target or an allow-list for the same reason, and reaches those
///         entry points alone. The subject entry points take no narrowing, and that is not an omission: a rule
///         whose subject a narrowing emptied is skipped, so no subject cure is ever reported under one.
///     </para>
/// </remarks>
internal static class AuthoringHints
{
    /// <summary>
    ///     The cure on a member subject that resolved to nothing. It names both shapes that fail here,
    ///     because <c>EvaluateMember</c> dispatches ahead of the type-subject gate: a member rule whose
    ///     type selection matched nothing reports the member message, not the type one.
    /// </summary>
    private const string EmptyMemberSubject =
        "Either the type selection matched nothing or none of the members it reached survived; check the "
        + "types first, then .Methods, .Properties or .Fields and the filters after them.";

    /// <summary>
    ///     The cure on a project subject that resolved to nothing (GRAMMAR §4.10). It opens on the naming
    ///     rule rather than on the emptiness: the message it sits under has just said that.
    /// </summary>
    private const string EmptyProjectSubject =
        "A project is named by its csproj file name, and .Matching(...) globs that name with a `*` that has "
        + "no dot structure; check the names against the solution.";

    // Each clause states its shape's SEMANTICS and stops there; the caller adds the one action clause. The
    // split is what keeps a composed hint to a single semicolon — a clause carrying its own action reads as
    // a run-on the moment a tail joins it.
    private const string NamespaceClause =
        "a trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses "
        + "a dot";

    // The csproj file name, not the assembly name: an AssemblyName property or a renamed project file parts
    // the two, and the extraction keys projects on the former.
    private const string ProjectClause =
        "a project is named by its csproj file name, matched ordinally and case-sensitively, and an "
        + "AssemblyName property does not rename it";

    private const string TypeClause =
        "a typeof anchor reaches only a type this solution declares, never one supplied by a package or by "
        + "the spec project itself";

    private const string NarrowingClause =
        "every term narrows, so the set is no wider than the last one applied";

    private const string SubjectTail = "; check the selection against what the solution declares";

    // The consequence leads the choice because it is what makes the choice worth making: an inert rule is
    // green, so nothing will ever raise the question again.
    private const string InertTail =
        "; a rule whose target matches nothing passes forever, so decide before keeping it";

    // The same choice for one dead pattern beside live ones. Passing forever would be false here, since the
    // live targets still decide the rule; what the dead one costs is the part of the ban it was meant to be.
    private const string InertOperandTail =
        "; a pattern that matches nothing bans nothing, so decide before keeping it";

    // What an exact name is: the shape advice of any selection that carries a list of its own, one dead name
    // of a Named list spelled alone included. A dead piece whose advice this is takes the name tails, never
    // the pattern ones: GRAMMAR §4.2 keeps the two apart.
    private const string NamedClause =
        "a name in a `Named` list is the whole simple name, matched exactly, without its namespace, generic "
        + "arity or containing type";

    private const string InertNameTail =
        "; a name that matches nothing bans nothing, so decide before keeping it";

    // What a path glob is: the shape advice of any selection carrying an Under list, one dead glob spelled alone
    // included. A glob is a pattern, so its dead pieces take the pattern tails.
    private const string PathGlobClause =
        "a path glob is relative to the solution directory with `/` between segments, and reaches a type when any "
        + "file declaring it is under the glob";

    // The member twin of NamedClause. A member's name has no namespace or arity to leave out; what a reader
    // reaches for by mistake is the declaring type or the signature.
    private const string MemberNamedClause =
        "a name in a member `Named` list is the member's own name, matched exactly, without its declaring type "
        + "or signature";

    // The allow-list twins. A dead allowed pattern errs the other way: nothing is quieter, and what it was
    // meant to allow reds under the names of the callers, which is why the tail says where the reds come from.
    private const string AllowedOperandTail =
        "; a pattern that matches nothing allows nothing, so what it was meant to allow is reported as a violation";

    private const string AllowedNameTail =
        "; a name that matches nothing allows nothing, so what it was meant to allow is reported as a violation";

    // The membership twins: a subject-side permit list errs as an allow-list does, but what reds is the
    // subject itself, so the tail names the subjects rather than the callers.
    private const string MembershipTail =
        "; a pattern that matches nothing holds no type, so each subject it was meant to hold is reported as a "
        + "violation";

    private const string MembershipNameTail =
        "; a name that matches nothing holds no type, so each subject it was meant to hold is reported as a "
        + "violation";

    // The among twins. A dead counterpart pattern errs both ways, which is why the tail names both: a subject
    // whose one counterpart it held reds, and a subject it would have made ambiguous passes.
    private const string AmongTail =
        "; a pattern that matches nothing holds no counterpart, so a subject whose counterpart it was meant to "
        + "hold reads as having none, and one it was meant to make ambiguous passes";

    private const string AmongNameTail =
        "; a name that matches nothing holds no counterpart, so a subject whose counterpart it was meant to "
        + "hold reads as having none, and one it was meant to make ambiguous passes";

    /// <summary>
    ///     The action clause on a dead exemplar part. It says what the warning does not cost, since a reader's
    ///     first question about a warning on a rule is whether the verdict can be trusted.
    /// </summary>
    private const string ExemplarTail =
        "; the verdict never depends on it, so point it at a type the solution declares or remove it";

    /// <summary>What to change when <paramref name="subject" /> selected nothing.</summary>
    internal static string ForSubject(Selection subject, IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? Sentence(ShapeAdvice(subject) + SubjectTail);
    }

    /// <summary>
    ///     What to change when a member subject resolved to nothing — the one fixed clause, or the partial
    ///     model where there is one. The member and project cures read off no selection shape, so these two
    ///     entry points exist only for the replacement: without them a failed project would reach half the
    ///     raise sites and leave the other half advising a spec fix for a load failure.
    /// </summary>
    internal static string ForMemberSubject(IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? EmptyMemberSubject;
    }

    /// <summary>
    ///     What to change when one name of a member subject's <c>Named</c> list matched no member while the
    ///     others matched, or the partial model where there is one.
    /// </summary>
    internal static string ForMemberName(IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? Sentence(MemberNamedClause + SubjectTail);
    }

    /// <summary>
    ///     What to change when a project subject resolved to nothing, or the partial model where there is one
    ///     (<see cref="ForMemberSubject" />'s reasoning).
    /// </summary>
    internal static string ForProjectSubject(IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? EmptyProjectSubject;
    }

    /// <summary>
    ///     What to change when a forbidden-set verb's target selected nothing. The advice is the first
    ///     pattern operand's, the same operand the inert gate itself tests for, so the sentence describes
    ///     the selection that raised the warning rather than a sibling that did not.
    /// </summary>
    internal static string ForInertTarget(
        IReadOnlyList<Selection> operands, IncompleteModel? model, NarrowedUniverse? narrowing)
    {
        Selection? pattern = operands.FirstOrDefault(SelectionEvaluator.IsPatternSelection);
        return InertReplacement(model, narrowing) ?? Sentence(ShapeAdvice(pattern) + InertTail);
    }

    /// <summary>
    ///     What to change when one of a forbidden-set verb's target operands selected nothing while another
    ///     selected something: advice read off <paramref name="operand" />, the one the warning names.
    /// </summary>
    internal static string ForInertTargetOperand(
        Selection operand, IncompleteModel? model, NarrowedUniverse? narrowing)
    {
        return ForDeadPiece(operand, InertOperandTail, InertNameTail, model, narrowing);
    }

    /// <summary>
    ///     What to change when one of an allow-list verb's operands selected nothing: advice read off
    ///     <paramref name="operand" /> alone.
    /// </summary>
    internal static string ForDeadAllowedOperand(
        Selection operand, IncompleteModel? model, NarrowedUniverse? narrowing)
    {
        return ForDeadPiece(operand, AllowedOperandTail, AllowedNameTail, model, narrowing);
    }

    /// <summary>
    ///     What to change when one membership a subject must belong to, or one piece of one, selected nothing:
    ///     advice read off <paramref name="piece" /> alone. It takes no narrowing, because a narrowed run raises
    ///     no warning in this position.
    /// </summary>
    internal static string ForDeadMembership(Selection piece, IncompleteModel? model)
    {
        return ForDeadPiece(piece, MembershipTail, MembershipNameTail, model, narrowing: null);
    }

    /// <summary>
    ///     What to change when one part of a counterpart verb's <c>among:</c> selected nothing: advice read off
    ///     <paramref name="piece" /> alone. It takes no narrowing, for <see cref="ForDeadMembership" />'s reason.
    /// </summary>
    internal static string ForDeadAmongOperand(Selection piece, IncompleteModel? model)
    {
        return ForDeadPiece(piece, AmongTail, AmongNameTail, model, narrowing: null);
    }

    /// <summary>
    ///     What to change when <paramref name="part" />, one part of a rule's exemplar, selected nothing: advice
    ///     read off the part alone, since it is the one the warning names. It takes no narrowing, because a run
    ///     over a solution filter raises no dead-exemplar warning at all.
    /// </summary>
    internal static string ForExemplar(Selection part, IncompleteModel? model)
    {
        return model?.EmptySelectionHint ?? Sentence(ShapeAdvice(part) + ExemplarTail);
    }

    private static string ForDeadPiece(
        Selection piece, string patternTail, string nameTail, IncompleteModel? model, NarrowedUniverse? narrowing)
    {
        string advice = ShapeAdvice(piece);
        string tail = advice == NamedClause ? nameTail : patternTail;
        return InertReplacement(model, narrowing) ?? Sentence(advice + tail);
    }

    // The two replacements every inert cure takes, owned once so their order cannot differ between them:
    // a broken model's repair comes before reading a smaller one.
    private static string? InertReplacement(IncompleteModel? model, NarrowedUniverse? narrowing)
    {
        return model?.EmptySelectionHint ?? narrowing?.InertTargetHint;
    }

    // Null covers an operand list with no pattern in it; the narrowing clause is true of every selection,
    // which is what makes it the safe floor.
    private static string ShapeAdvice(Selection? selection)
    {
        if (selection is null) return NarrowingClause;

        // A Named list says exactly which types it means, so a selection carrying one that matched nothing
        // failed on a name, whatever noun or region it was narrowed from: a Named after arch.Namespace(...)
        // is cured as a name, not as a glob. Only the selection's own list counts; one inside an Except
        // payload exempts, and a dead exemption empties nothing.
        if (selection.Adjectives.Any(adjective => adjective is NamedAdjective)) return NamedClause;

        // An Under list says where the types are declared, so a selection carrying one that matched nothing failed
        // on a path the same way: a glob relative to the wrong directory, or one naming a folder renamed since.
        if (selection.Adjectives.Any(adjective => adjective is UnderAdjective)) return PathGlobClause;

        return selection.NounOrNull() switch
        {
            // A layer is transparent to what defines it, so its cure is its definition's. The glob form has
            // no definition to reach and is namespaces by construction.
            LayerNoun { Definition: { } definition } => ShapeAdvice(definition),
            LayerNoun => NamespaceClause,
            NamespaceNoun => NamespaceClause,
            ProjectNoun => ProjectClause,
            TypeNoun => TypeClause,
            // A union answers no noun at all, and RegionOf declines it; what is left that names a namespace
            // region is arch.Types narrowed by exactly one InNamespace.
            _ => LayerNoun.RegionOf(selection).Count > 0 ? NamespaceClause : NarrowingClause
        };
    }

    private static string Sentence(string clause)
    {
        return ProseFormat.Capitalize(clause) + ".";
    }
}
