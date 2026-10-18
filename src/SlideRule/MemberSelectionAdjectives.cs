using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Validation;
using static SlideRule.Internal.Guard;

namespace SlideRule;

/// <summary>
///     The adjectives that narrow a member selection — the <c>Members</c>, <c>Methods</c>,
///     <c>Properties</c>, <c>Fields</c> or <c>Events</c> projection of a <see cref="Selection" />.
///     Each returns a new selection carrying one more condition and leaves the original untouched,
///     keeping the projection's own type: <c>Returning</c> and <c>MustAcceptParameter</c> stay
///     reachable on a <see cref="MethodSelection" /> after any adjective, <c>MustBeGetOnly</c> on a
///     <see cref="PropertySelection" /> and <c>MustBeReadonly</c> on a <see cref="FieldSelection" />.
///     Finish with a member verb such as <c>MustBePublic</c> or <c>MustHaveSuffix</c>.
/// </summary>
// The TSelf shape is what preserves the concrete member-selection type through a refinement. These
// live on a hierarchy disjoint from Selection, so the identically-named type-side adjectives never
// collide on overload resolution — the receiver type decides (GRAMMAR §5.7).
public static class MemberSelectionAdjectives
{
    /// <summary>
    ///     Narrows the selection to the members whose name ends with a suffix, such as
    ///     <c>arch.Types.Methods.WithSuffix("Async")</c>. The comparison is case-sensitive and literal
    ///     over the member's own name. Literal means the suffix is not a glob: a <c>*</c> in it matches
    ///     a <c>*</c>, so over a type declaring <c>LoadAsync</c>, <c>WithSuffix("*Async")</c> selects
    ///     nothing while <c>WithNameMatching("*Async")</c> selects it. It reads as a premodifier in the
    ///     rendered sentence: "`*Async` methods of types in <c>MyApp.*</c>". A blank suffix is reported when
    ///     the spec is loaded. <see cref="WithNameMatching{TSelf}" /> is the glob form beside this one.
    /// </summary>
    public static TSelf WithSuffix<TSelf>(this TSelf selection, [SpecSlot(nameof(SpecSlot.MemberSuffix))] string suffix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithSuffixAdjective(NotNull(suffix, nameof(suffix))));
    }

    /// <summary>
    ///     Narrows the selection to the members whose name starts with a prefix, such as
    ///     <c>arch.Types.Methods.WithPrefix("Get")</c>. The comparison is case-sensitive and literal
    ///     over the member's own name. Literal means the prefix is not a glob: a <c>*</c> in it matches
    ///     a <c>*</c>, so over a type declaring <c>GetOrder</c>, <c>WithPrefix("Get*")</c> selects
    ///     nothing while <c>WithNameMatching("Get*")</c> selects it. It reads as a premodifier in the
    ///     rendered sentence: "`Get*` methods of types in <c>MyApp.*</c>". A blank prefix is reported when
    ///     the spec is loaded. <see cref="WithNameMatching{TSelf}" /> is the glob form beside this one.
    /// </summary>
    public static TSelf WithPrefix<TSelf>(this TSelf selection, [SpecSlot(nameof(SpecSlot.MemberPrefix))] string prefix)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithPrefixAdjective(NotNull(prefix, nameof(prefix))));
    }

    /// <summary>
    ///     Narrows the selection to the members whose name matches a glob, such as
    ///     <c>arch.Types.Members.WithNameMatching("*Handler*")</c>. Matching is case-sensitive: <c>*</c>
    ///     matches any run of characters including none, every other character matches itself, a pattern
    ///     with no <c>*</c> is an exact name match, and a lone <c>*</c> matches every name. It reads as a
    ///     premodifier in the rendered sentence: "`*Handler*` members of types in <c>MyApp.*</c>". A blank
    ///     glob is reported when the spec is loaded.
    /// </summary>
    public static TSelf WithNameMatching<TSelf>(
        this TSelf selection, [SpecSlot(nameof(SpecSlot.MemberNamePattern))] string glob)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWithNameMatchingAdjective(NotNull(glob, nameof(glob))));
    }

    /// <summary>
    ///     Narrows the selection to the members with exactly these names, such as
    ///     <c>arch.Types.Named("Program").Methods.Named("Main")</c> or <c>.Methods.Named("Main", "Run")</c>,
    ///     which keeps the members carrying either name. The comparison is case-sensitive over the member's
    ///     own name, with no declaring type in front of it and no signature after it, so a name reaches every
    ///     overload of a method; a <c>*</c> in it is a literal character, and
    ///     <see cref="WithNameMatching{TSelf}" /> is the glob form beside this one. It reads as a premodifier in
    ///     the rendered sentence: "`Main` methods of types named <c>Program</c>". At least one name is required,
    ///     and a blank one is reported when the spec is loaded. Where the selection is a rule's subject, each
    ///     name is also checked on its own: one that matches no member while the others match fails the rule,
    ///     naming it.
    /// </summary>
    public static TSelf Named<TSelf>(
        this TSelf selection,
        [SpecSlot(nameof(SpecSlot.ExactMemberName))]
        string first,
        [SpecSlot(nameof(SpecSlot.ExactMemberName))]
        params string[] more)
        where TSelf : MemberSelection
    {
        IReadOnlyList<string> names = OperandList.OneOrMore(first, more, name => name);
        return Append(selection, new MemberNamedAdjective(names));
    }

    /// <summary>
    ///     Narrows the selection to the members with exactly the names in a list, such as
    ///     <c>arch.Types.Methods.Named(entryPoints)</c> over a list a helper method built, which keeps the
    ///     members carrying any of them. The comparison is case-sensitive over the member's own name, with no
    ///     declaring type in front of it and no signature after it, so a name reaches every overload of a
    ///     method, and a <c>*</c> in it is a literal character. It reads as a premodifier in the rendered
    ///     sentence: "`Main` or `Run` methods of types". The list is read once, when this is called. An empty
    ///     list or a blank name is reported when the spec is loaded, and a null list or a null name in it
    ///     throws. Where the selection is a rule's subject, each name is also checked on its own: one that
    ///     matches no member while the others match fails the rule, naming it.
    /// </summary>
    public static TSelf Named<TSelf>(
        this TSelf selection,
        [SpecSlot(nameof(SpecSlot.ExactMemberName))]
        IEnumerable<string> names)
        where TSelf : MemberSelection
    {
        IReadOnlyList<string> list = OperandList.ZeroOrMore(names, name => name, nameof(names));
        return Append(selection, new MemberNamedAdjective(list));
    }

    /// <summary>
    ///     Narrows the selection to the members carrying an attribute, such as
    ///     <c>arch.Types.Methods.AttributedWith(typeof(HttpGetAttribute))</c>. These are the members that
    ///     carry the attribute, not the members of types that carry it — for that, apply
    ///     <c>AttributedWith</c> to the type selection before projecting to members. Attributes written on
    ///     the member itself count and no others: an attribute on a property's <c>get</c> or <c>set</c>
    ///     accessor, and a <c>[return:]</c> attribute, are not seen. Pass an open generic definition to
    ///     match every construction of it, or a constructed attribute type to match that one exactly.
    ///     Nothing checks that the type is an attribute: given anything else it matches nothing,
    ///     and a rule whose subject matches no member fails the check.
    /// </summary>
    public static TSelf AttributedWith<TSelf>(this TSelf selection, Type attributeType)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromType(NotNull(attributeType, nameof(attributeType)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the members carrying the attribute with this name — the form to use
    ///     for an attribute the spec project cannot compile against, so it need not take a package
    ///     reference just to write the <c>typeof</c>. <paramref name="attributeFullName" /> is the
    ///     attribute definition's fully-qualified name with the <c>Attribute</c> suffix included
    ///     (<c>"ModelContextProtocol.Server.McpServerToolAttribute"</c>); it matches every construction of
    ///     that definition, and a constructed spelling matches nothing. Attributes written on the member
    ///     itself count and no others. Blankness is the only thing checked, and a blank name is reported
    ///     when the spec is loaded: a misspelt name is a legal name that matches nothing. Prefer the
    ///     <see cref="System.Type" /> overload whenever the attribute is referenceable, because the
    ///     compiler checks a <c>typeof</c> and nothing checks a string.
    /// </summary>
    public static TSelf AttributedWith<TSelf>(
        this TSelf selection, [SpecSlot(nameof(SpecSlot.AttributeName))] string attributeFullName)
        where TSelf : MemberSelection
    {
        TypeAnchor anchor = TypeAnchor.FromName(NotNull(attributeFullName, nameof(attributeFullName)));
        return Append(selection, new MemberAttributedWithAdjective(anchor));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>static</c>, such as
    ///     <c>arch.Types.Fields.ThatAreStatic()</c>. Instance members are dropped.
    /// </summary>
    public static TSelf ThatAreStatic<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberThatAreStaticAdjective());
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>public</c>, such as
    ///     <c>arch.Types.InNamespace("MyApp.Services.*").Methods.ThatArePublic()</c>. The accessibility is the
    ///     one the member's declaration writes, compared exactly, so a <c>protected internal</c> member is
    ///     not public; an interface member with no modifier is public. It reads as a premodifier in the
    ///     rendered sentence: "public methods of types in <c>MyApp.Services.*</c>".
    /// </summary>
    public static TSelf ThatArePublic<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.Public));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>internal</c>, such as
    ///     <c>arch.Types.Methods.ThatAreInternal()</c>. The accessibility is compared exactly, so a
    ///     <c>protected internal</c> member is not internal. It reads as a premodifier in the rendered
    ///     sentence: "internal methods of types".
    /// </summary>
    public static TSelf ThatAreInternal<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.Internal));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>protected</c>, such as
    ///     <c>arch.Types.InNamespace("MyApp.Services.*").Fields.ThatAreProtected()</c>. The accessibility is
    ///     compared exactly: <c>protected internal</c> and <c>private protected</c> are levels of their own,
    ///     selected by <see cref="ThatAreProtectedInternal{TSelf}" /> and
    ///     <see cref="ThatArePrivateProtected{TSelf}" />. It reads as a premodifier in the rendered sentence:
    ///     "protected fields of types in <c>MyApp.Services.*</c>".
    /// </summary>
    public static TSelf ThatAreProtected<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.Protected));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>private</c>, such as
    ///     <c>arch.Types.Fields.ThatArePrivate().MustHavePrefix("_")</c>. A member of a class or struct
    ///     written with no accessibility modifier is private, so this reaches it. The accessibility is
    ///     compared exactly, so a <c>private protected</c> member is not private. It reads as a premodifier
    ///     in the rendered sentence: "private fields of types".
    /// </summary>
    public static TSelf ThatArePrivate<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.Private));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>protected internal</c>, which a derived type or
    ///     any code in the declaring assembly can reach, such as
    ///     <c>arch.Types.Methods.ThatAreProtectedInternal()</c>. It reads as a premodifier in the rendered
    ///     sentence: "protected internal methods of types".
    /// </summary>
    public static TSelf ThatAreProtectedInternal<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.ProtectedInternal));
    }

    /// <summary>
    ///     Narrows the selection to the members declared <c>private protected</c>, which only a derived type
    ///     in the declaring assembly can reach, such as <c>arch.Types.Methods.ThatArePrivateProtected()</c>.
    ///     It reads as a premodifier in the rendered sentence: "private protected methods of types".
    /// </summary>
    public static TSelf ThatArePrivateProtected<TSelf>(this TSelf selection)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberAccessibilityAdjective(Accessibility.PrivateProtected));
    }

    // The generic twin is receiver-typed rather than TSelf-generic, and that asymmetry with every other
    // adjective in this file is deliberate: C# has no PARTIAL type inference, so a two-type-parameter
    // AttributedWith<TSelf, T>(this TSelf, ...) would force `selection.AttributedWith<MethodSelection,
    // MyAttribute>()` at every call site — the sugar's whole point is that the type argument is the only
    // thing written. One overload per receiver recovers what TSelf was there for, so the set has to cover
    // every concrete member selection there is; the MemberSelection overload serves .Members and .Events,
    // whose KindMemberSelection is internal. A new projection type ships its overload here or its kind-only
    // verb silently stops compiling after the sugar. Do not "fix" this back to TSelf.

    /// <summary>
    ///     Narrows the selection to the members carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Members.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. These are the members that
    ///     carry the attribute, not the members of types that carry it. Attributes written on the member
    ///     itself count and no others: an attribute on a property's <c>get</c> or <c>set</c> accessor, and
    ///     a <c>[return:]</c> attribute, are not seen. An open generic attribute has no type-argument
    ///     form: for <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static MemberSelection AttributedWith<T>(this MemberSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the methods carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Methods.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on the
    ///     method itself count and no others. The result is still a <see cref="MethodSelection" />, so
    ///     <c>Returning</c> and <c>MustAcceptParameter</c> stay reachable after it. An open generic
    ///     attribute has no type-argument form: for <c>MarkAttribute&lt;&gt;</c> use the
    ///     <see cref="System.Type" /> overload and pass <c>typeof(MarkAttribute&lt;&gt;)</c>, which
    ///     matches every construction of it.
    /// </summary>
    public static MethodSelection AttributedWith<T>(this MethodSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the properties carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Properties.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on
    ///     the property itself count and no others, so one on its <c>get</c> or <c>set</c> accessor is not
    ///     seen. The result is still a <see cref="PropertySelection" />, so <c>MustBeGetOnly</c> stays
    ///     reachable after it. An open generic attribute has no type-argument form: for
    ///     <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static PropertySelection AttributedWith<T>(this PropertySelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection to the fields carrying attribute <typeparamref name="T" />, such as
    ///     <c>arch.Types.Fields.AttributedWith&lt;ObsoleteAttribute&gt;()</c>. Attributes written on the
    ///     field itself count and no others. The result is still a <see cref="FieldSelection" />, so
    ///     <c>MustBeReadonly</c> stays reachable after it. An open generic attribute has no type-argument
    ///     form: for <c>MarkAttribute&lt;&gt;</c> use the <see cref="System.Type" /> overload and pass
    ///     <c>typeof(MarkAttribute&lt;&gt;)</c>, which matches every construction of it.
    /// </summary>
    public static FieldSelection AttributedWith<T>(this FieldSelection selection)
        where T : Attribute
    {
        return selection.AttributedWith(typeof(T));
    }

    /// <summary>
    ///     Narrows the selection by removing every member the given member selections name, such as
    ///     <c>arch.Types.Methods.Except(arch.Types.Named("Program").Methods.Named("Main"))</c>, which keeps
    ///     every method but <c>Program.Main</c>. Several exclusions remove the members any one of them names,
    ///     and at least one is required. Each exclusion is a member selection in its own right, drawn from its
    ///     own types, so it can name members of types the selection never reaches, and a <c>.Members</c>
    ///     exclusion removes members of every kind. An exclusion of a different single kind than this
    ///     selection, such as fields from a method selection, removes nothing and is reported when the spec is
    ///     loaded. The exclusion is rendered last in the rule's sentence, after any <see cref="Where{TSelf}" />
    ///     on the same selection: "methods of types, except `Main` methods of types named <c>Program</c>".
    /// </summary>
    public static TSelf Except<TSelf>(this TSelf selection, MemberSelection first, params MemberSelection[] more)
        where TSelf : MemberSelection
    {
        IReadOnlyList<MemberSelection> payloads = OperandList.OneOrMore(first, more, exclusion => exclusion);
        return Append(selection, new MemberExceptAdjective(payloads));
    }

    /// <summary>
    ///     Narrows the selection by removing every member the member selections in a list name, such as
    ///     <c>arch.Types.Methods.Except(exemptions)</c> over a list a helper method built; a
    ///     <c>List&lt;MethodSelection&gt;</c> is accepted as it is. The members any one exclusion names are
    ///     removed. Each exclusion is a member selection in its own right, drawn from its own types, and a
    ///     <c>.Members</c> exclusion removes members of every kind; one of a different single kind than this
    ///     selection, such as fields from a method selection, removes nothing and is reported when the spec
    ///     is loaded. The list is read once, when this is called. An empty list is reported when the spec is
    ///     loaded, and a null list or a null exclusion in it throws. The exclusion is rendered last in the
    ///     rule's sentence: "methods of types, except `Main` methods of types named <c>Program</c>".
    /// </summary>
    public static TSelf Except<TSelf>(this TSelf selection, IEnumerable<MemberSelection> exclusions)
        where TSelf : MemberSelection
    {
        IReadOnlyList<MemberSelection> payloads =
            OperandList.ZeroOrMore(exclusions, exclusion => exclusion, nameof(exclusions));
        return Append(selection, new MemberExceptAdjective(payloads));
    }

    /// <summary>
    ///     Narrows the selection with a predicate of your own, for what the member adjectives cannot say:
    ///     <c>arch.Types.Methods.Where(m =&gt; m.Parameters.Count &gt; 5, description: "with over 5 parameters")</c>.
    ///     The predicate reads the facts on <see cref="IMemberInfo" />, its
    ///     declaring type's included, and runs against each candidate member when the check runs, never
    ///     when the spec is loaded. <paramref name="description" /> is required and completes the subject
    ///     as a relative clause; it is rendered verbatim in the generated agent context in place of the
    ///     wording an adjective would produce, right after the member kind and ahead of the types:
    ///     "methods with over 5 parameters declared in types". Nothing checks that it describes what the
    ///     predicate does. A blank or multi-line description is reported when the spec is loaded.
    /// </summary>
    public static TSelf Where<TSelf>(
        this TSelf selection,
        Func<IMemberInfo, bool> predicate,
        [SpecSlot(nameof(SpecSlot.Description))]
        string description)
        where TSelf : MemberSelection
    {
        return Append(selection, new MemberWhereAdjective(NotNull(predicate, nameof(predicate)), NotNull(description, nameof(description))));
    }

    private static TSelf Append<TSelf>(TSelf selection, MemberAdjective adjective)
        where TSelf : MemberSelection
    {
        NotNull(selection, nameof(selection));
        return (TSelf)selection.Refined(adjective);
    }
}
