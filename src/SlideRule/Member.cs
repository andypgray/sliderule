using System.Reflection;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Validation;

namespace SlideRule;

/// <summary>
///     A member to ban with <c>MustNotUse</c>: a declaring type plus a member name, created by the
///     <c>Member</c> methods on <see cref="Arch" />. A ban covers every overload of the name, and it
///     matches accesses whose compile-time receiver is the declaring type: a ban on a concrete member
///     does not catch calls made through an interface-typed receiver, and a ban on an interface member
///     does not catch direct calls on the concrete type. Not a <see cref="Selection" />: it takes no
///     adjectives and no verbs, and it may be reused across rules built on the same
///     <see cref="Arch" />.
/// </summary>
// A target-only leaf outside the Selection hierarchy on purpose (GRAMMAR §3.2): adjectives and modal
// verbs must stay uncompilable on a member. Owner-stamped like a selection so the fresh-instance
// contract (§8 item 13) covers it.
public sealed class Member
{
    private readonly TypeAnchor? _declaringAnchor;
    private readonly bool _isMethod;
    private readonly string? _name;

    internal Member(Arch owner, Type declaringType, string name, SpecSourceLocation? location = null)
    {
        Owner = owner;
        Location = location;
        Guard.NotNull(declaringType, nameof(declaringType));
        _name = Guard.NotNull(name, nameof(name));
        _isMethod = ResolveIsMethod(declaringType, _name);
        _declaringAnchor = TypeAnchor.FromType(declaringType);
    }

    // A member read back from a model file: the declaring type arrives as an anchor and IsMethod as a
    // fact, because both were settled by reflection when the spec was built and neither can be recovered
    // from names alone.
    internal Member(Arch owner, TypeAnchor declaringAnchor, string name, bool isMethod)
    {
        Owner = owner;
        _declaringAnchor = Guard.NotNull(declaringAnchor, nameof(declaringAnchor));
        _name = Guard.NotNull(name, nameof(name));
        _isMethod = isMethod;
    }

    // Poison ctor for a lambda MemberExpressionResolver could not reduce to a declared (type, name) (GRAMMAR §8,
    // MemberExpressionUnresolvable). It records only the diagnostic core; DeclaringType, DeclaringAnchor, Name
    // and IsMethod throw rather than hand back defaults a reader could mistake for a resolved member. The whole
    // spec is validated before any node is projected or rendered, so the poison is reported first; Owner is
    // stamped so the foreign-Arch check (§8 item 13) still precedes that report.
    internal Member(Arch owner, string poisonError, SpecSourceLocation? location = null)
    {
        Owner = owner;
        PoisonError = poisonError;
        Location = location;
    }

    /// <summary>
    ///     The <see cref="Arch" /> this member was minted on (GRAMMAR §3.2 fresh-instance contract,
    ///     member flavor).
    /// </summary>
    internal Arch Owner { get; }

    /// <summary>
    ///     The spec-source position of the <c>arch.Member(...)</c> call that minted this leaf (GRAMMAR §8),
    ///     or null for a verb-minted member (the <c>MustNotUse(() =&gt; ...)</c> expression forms cannot carry
    ///     caller info past <c>params</c>) — whose errors then attribute to the consuming rule's anchor.
    /// </summary>
    internal SpecSourceLocation? Location { get; }

    /// <summary>
    ///     The diagnostic core of a member minted from an unresolvable anchor expression, which
    ///     <see cref="Validation.SpecValidator" /> reports as
    ///     <see cref="Validation.SpecValidationErrorCode.MemberExpressionUnresolvable" />; null when resolved.
    /// </summary>
    internal string? PoisonError { get; }

    /// <summary>
    ///     The declaring type exactly as authored — the <c>typeof(...)</c> operand. Reading it throws on a
    ///     member that was read back from a model file, which carries the anchor's names and no reflected
    ///     type; <see cref="DeclaringAnchor" /> is what every check-time and render-time reader asks for.
    /// </summary>
    internal Type DeclaringType
    {
        get
        {
            if (PoisonError is not null) throw PoisonRead();

            return _declaringAnchor!.Type ?? throw ParsedRead();
        }
    }

    /// <summary>
    ///     The declaring type as a <see cref="TypeAnchor" />, minted once at construction: the names a
    ///     check-time or render-time reader compares on, so neither has to reflect over the operand again.
    /// </summary>
    internal TypeAnchor DeclaringAnchor => PoisonError is null ? _declaringAnchor! : throw PoisonRead();

    /// <summary>The member name — the <c>nameof(...)</c> operand.</summary>
    internal string Name => PoisonError is null ? _name! : throw PoisonRead();

    /// <summary>
    ///     Whether the anchored member resolves to a method, decided once at construction against the
    ///     normalized generic definition.
    /// </summary>
    internal bool IsMethod => PoisonError is null ? _isMethod : throw PoisonRead();

    private static InvalidOperationException PoisonRead()
    {
        return new InvalidOperationException(
            "A poisoned member anchor's DeclaringType/DeclaringAnchor/Name/IsMethod must not be read; " +
            "the anchor expression was unresolvable and its PoisonError is reported at spec build first.");
    }

    // A parsed member has an anchor and no reflected type, so handing back null here would let a caller
    // mistake "no type" for "this type". Fail closed instead, and name the accessor that does answer.
    private static InvalidOperationException ParsedRead()
    {
        return new InvalidOperationException(
            "A member read from a model file carries no reflected DeclaringType; read its DeclaringAnchor.");
    }

    // The lookup is DeclaredMember's, the one validation asks whether the anchor declares the member at all,
    // so the two cannot disagree. A blank or typo'd name yields false here and is rejected by validation later
    // (GRAMMAR §8 items 11–12).
    private static bool ResolveIsMethod(Type declaringType, string name)
    {
        foreach (MemberInfo hit in DeclaredMember.Of(declaringType, name))
            if (hit is MethodInfo)
                return true;

        return false;
    }
}
