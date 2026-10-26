using SlideRule.Internal;

namespace SlideRule;

/// <summary>
///     A complete rule sentence: a selection plus a <c>Must</c> verb, produced by the verb methods on
///     type, member and project selections and handed to <c>Enforce</c> or <c>Migrate</c>. Nothing on
///     it executes; it is data the check evaluates later.
/// </summary>
// A closed hierarchy with a private protected constructor: no foreign assembly can add a constraint
// node, so every constraint is walkable and renderable. Evaluation is bolted on without reshaping
// these nodes (GRAMMAR §2).
public abstract class Constraint
{
    // The null guard for every verb's receiver: each verb names its receiver subject, so the ParamName is
    // the one on the overload the caller called.
    private protected Constraint(Selection subject)
    {
        Subject = Guard.NotNull(subject, nameof(subject));
    }

    private protected Constraint()
    {
    }

    /// <summary>The type selection the constraint is asserted over, or null for a project constraint.</summary>
    /// <remarks>
    ///     Null exactly for a project constraint (GRAMMAR §4.10). A type-side reader dispatches on the constraint
    ///     type first, or runs downstream of a reader that did; the nullable-flow analysis warns on a forgotten
    ///     dispatch.
    /// </remarks>
    internal Selection? Subject { get; }

    /// <summary>The modal verb phrase, lowercase, beginning with "must" (GRAMMAR §5.3).</summary>
    internal abstract string VerbPhrase { get; }

    /// <summary>
    ///     Selection operands beyond the subject: the target or source list of a dependency verb. With the subject
    ///     they must reach every selection a rule names, for the foreign-selection walk (GRAMMAR §8 item 10).
    /// </summary>
    internal virtual IReadOnlyList<Selection> Operands => Array.Empty<Selection>();

    /// <summary>
    ///     The banned members of <c>MustNotUse</c> (GRAMMAR §4.5), which validation walks (GRAMMAR §8 items 11–13).
    /// </summary>
    internal virtual IReadOnlyList<Member> MemberOperands => Array.Empty<Member>();
}
