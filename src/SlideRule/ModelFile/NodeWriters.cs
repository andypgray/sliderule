using SlideRule.Fluent;
using SlideRule.Model;

namespace SlideRule.ModelFile;

/// <summary>
///     How each node of the reified model is written, one arm per class, looked up by the class itself.
/// </summary>
/// <remarks>
///     <para>
///         A lookup rather than a type switch, so the set of classes that can be written is a value a test
///         can read without running a single arm. A class with no arm is a node that shipped without being
///         taught to the file: dispatch throws naming it rather than writing something plausible, because
///         a file silently missing a rule's operand would enforce less than the rule says.
///     </para>
///     <para>
///         The <c>Where</c> adjectives and the <c>Must</c> verbs are deliberately absent, because each
///         holds a delegate, and a delegate is code. They are listed in <see cref="DelegateTypes" />, which
///         is what parts "not yet written" from "cannot be written". The selection classes are absent
///         because they carry no discriminator at all: a selection is written by its shape, and those are
///         listed in <see cref="ShapeWrittenTypes" />.
///     </para>
/// </remarks>
internal static class NodeWriters
{
    private static readonly Dictionary<Type, Action<NodeWriter, object>> Arms = BuildArms();

    private static readonly HashSet<Type> ShapeWritten =
    [
        typeof(RefinedSelection),
        typeof(UnionSelection),
        typeof(Layer),
        typeof(KindMemberSelection),
        typeof(MethodSelection),
        typeof(PropertySelection),
        typeof(FieldSelection),
        typeof(RefinedProjectSelection)
    ];

    /// <summary>Every node class that has a write arm — the discriminated ones the file can carry.</summary>
    internal static IReadOnlyCollection<Type> WritableTypes => Arms.Keys;

    /// <summary>The classes that hold a delegate, and so can never be written.</summary>
    internal static IReadOnlyCollection<Type> DelegateTypes => PredicateNodes.Types;

    /// <summary>The selection classes written by their shape, which carry no discriminator.</summary>
    internal static IReadOnlyCollection<Type> ShapeWrittenTypes => ShapeWritten;

    /// <summary>Whether a class holds a delegate, and so is unwritable rather than unregistered.</summary>
    internal static bool IsDelegateNode(Type nodeType)
    {
        return PredicateNodes.Contains(nodeType);
    }

    /// <summary>
    ///     The discriminator a class is written under: its name less a trailing <c>Constraint</c>,
    ///     <c>Adjective</c>, <c>Noun</c> or <c>Selection</c>, with the first letter lowered and nothing
    ///     else changed.
    /// </summary>
    internal static string DiscriminatorOf(Type nodeType)
    {
        string name = nodeType.Name;
        string[] tails = ["Constraint", "Adjective", "Noun", "Selection"];
        foreach (string tail in tails)
        {
            if (!name.EndsWith(tail, StringComparison.Ordinal)) continue;

            name = name.Substring(0, name.Length - tail.Length);
            break;
        }

        return Camel(name);
    }

    /// <summary>The camelCase spelling of a Pascal-cased name — the file's one casing rule.</summary>
    internal static string Camel(string name)
    {
        if (name.Length == 0) return name;

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    /// <summary>
    ///     Writes <paramref name="node" /> whole, discriminator first. A class that holds a delegate is
    ///     refused instead, for the <c>Must</c> reason when it is a constraint and the <c>Where</c> reason
    ///     otherwise. A class with no arm throws: the evaluator's own dispatch fails closed for the same
    ///     reason, and so does this.
    /// </summary>
    internal static void Write(NodeWriter writer, object node)
    {
        if (writer.Refusal is not null) return;

        Type type = node.GetType();
        if (IsDelegateNode(type))
        {
            writer.Refuse(node is Constraint ? OmissionReasons.Must : OmissionReasons.Where);
            return;
        }

        if (!Arms.TryGetValue(type, out Action<NodeWriter, object> arm))
            throw new InvalidOperationException($"Unhandled model node '{type.Name}'.");

        arm(writer, node);
    }

    private static Dictionary<Type, Action<NodeWriter, object>> BuildArms()
    {
        var arms = new Dictionary<Type, Action<NodeWriter, object>>();
        AddNouns(arms);
        AddAdjectives(arms);
        AddMemberAdjectives(arms);
        AddProjectAdjectives(arms);
        AddTypeVerbs(arms);
        AddMemberVerbs(arms);
        AddProjectVerbs(arms);
        return arms;
    }

    private static void AddNouns(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        Add<TypesNoun>(arms, (_, _) => { });
        Add<TypeNoun>(arms, (w, n) => w.DefinitionAnchor("anchor", n.Anchor));
        Add<NamespaceNoun>(arms, (w, n) => w.Field("glob", n.Glob));
        Add<ProjectNoun>(arms, (w, n) => w.Field("name", n.Name));
        Add<LayerNoun>(arms, (w, n) => w.LayerName("name", n.Name));
        Add<RegisteredNoun>(arms, (w, n) =>
        {
            if (n.Lifetime is { } lifetime) w.EnumField("lifetime", lifetime);
        });
        Add<EachNoun>(arms, (w, n) =>
        {
            if (n.Layers is { } cells) w.LayerCells(cells);
            else w.ProjectCells(n.Projects!);
        });
    }

    private static void AddAdjectives(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        Add<InNamespaceAdjective>(arms, (w, a) => w.Field("glob", a.Glob));
        Add<OfKindAdjective>(arms, (w, a) => w.EnumField("kind", a.Kind));
        Add<WithSuffixAdjective>(arms, (w, a) => w.Field("suffix", a.Suffix));
        Add<WithPrefixAdjective>(arms, (w, a) => w.Field("prefix", a.Prefix));
        Add<WithNameMatchingAdjective>(arms, (w, a) => w.Field("glob", a.Glob));
        Add<NamedAdjective>(arms, (w, a) => w.StringsField("names", a.Names));
        Add<ImplementingAdjective>(arms, (w, a) => w.ConstructionAnchor("anchor", a.Anchor));
        Add<DerivedFromAdjective>(arms, (w, a) => w.ConstructionAnchor("anchor", a.Anchor));
        Add<AttributedWithAdjective>(arms, (w, a) => w.ConstructionAnchor("anchor", a.Anchor));
        Add<ExceptAdjective>(arms, (w, a) =>
        {
            w.Key("payload");
            w.WriteSelection(a.Payload);
        });
        Add<AuthoredAdjective>(arms, (_, _) => { });
        Add<AccessibilityAdjective>(arms, (w, a) => w.EnumField("accessibility", a.Accessibility));
    }

    private static void AddMemberAdjectives(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        Add<MemberWithSuffixAdjective>(arms, (w, a) => w.Field("suffix", a.Suffix));
        Add<MemberWithPrefixAdjective>(arms, (w, a) => w.Field("prefix", a.Prefix));
        Add<MemberWithNameMatchingAdjective>(arms, (w, a) => w.Field("glob", a.Glob));
        Add<ReturningAdjective>(arms, (w, a) => w.DefinitionAnchors("anchors", a.Anchors));
        Add<MemberAttributedWithAdjective>(arms, (w, a) => w.ConstructionAnchor("anchor", a.Anchor));
        Add<MemberThatAreStaticAdjective>(arms, (_, _) => { });
    }

    private static void AddProjectAdjectives(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        Add<ProjectNamedAdjective>(arms, (w, a) => w.StringsField("names", a.Names));
        Add<ProjectMatchingAdjective>(arms, (w, a) => w.StringsField("globs", a.Globs));
        Add<ProjectPackableAdjective>(arms, (_, _) => { });
        Add<ProjectExceptAdjective>(arms, (w, a) =>
        {
            w.Key("payload");
            w.WriteProjectSelection(a.Payload);
        });
    }

    private static void AddTypeVerbs(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        Subject<MustBeSealedConstraint>(arms);
        Subject<MustBeStaticConstraint>(arms);
        Subject<MustBeAbstractConstraint>(arms);
        Subject<MustBePublicConstraint>(arms);
        Subject<MustBeInternalConstraint>(arms);
        Subject<MustBeRegisteredConstraint>(arms);
        Subject<MustNotReferenceEachOtherConstraint>(arms);
        Subject<MustNotHaveCircularReferencesConstraint>(arms);
        Subject<MustOnlyReferenceItselfConstraint>(arms);
        Subject<MustOnlyBeReferencedByItselfConstraint>(arms);

        Operands<MustNotReferenceConstraint>(arms);
        Operands<MustNotBeReferencedByConstraint>(arms);
        Operands<MustOnlyReferenceConstraint>(arms);
        Operands<MustOnlyBeReferencedByConstraint>(arms);
        Operands<MustNotConstructConstraint>(arms);
        Operands<MustNotInjectConstraint>(arms);
        Operands<MustNotCatchConstraint>(arms);
        Operands<MustNotCatchUnfilteredConstraint>(arms);
        Operands<MustNotSwallowConstraint>(arms);
        Operands<MustNotThrowConstraint>(arms);
        Operands<MustOnlyThrowConstraint>(arms);
        Operands<MustNotExposeConstraint>(arms);
        Operands<MustBelongToConstraint>(arms);

        Add<MustImplementConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchor("anchor", c.Anchor);
        });
        Add<MustDeriveFromConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchor("anchor", c.Anchor);
        });
        Add<MustBeAttributedWithConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchor("anchor", c.Anchor);
        });
        Add<MustNotImplementConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchors("anchors", c.Anchors);
        });
        Add<MustNotDeriveFromConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchors("anchors", c.Anchors);
        });
        Add<MustNotBeAttributedWithConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.ConstructionAnchors("anchors", c.Anchors);
        });

        Add<MustResideInNamespaceConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Field("glob", c.Glob);
        });
        Add<MustResideInProjectConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Field("projectName", c.ProjectName);
        });
        Add<MustHaveSuffixConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Field("suffix", c.Suffix);
        });
        Add<MustHavePrefixConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Field("prefix", c.Prefix);
        });
        Add<MustHaveNameMatchingConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Field("glob", c.Glob);
        });

        Add<MustHaveExactlyOneCounterpartConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Operands(c.Among);
            w.Field("template", c.Template);
        });
        Add<MustNotUseConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Members(c.MemberOperands);
        });
    }

    private static void AddMemberVerbs(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        MemberSubject<MemberMustBePublicConstraint>(arms);
        MemberSubject<MemberMustBeInternalConstraint>(arms);
        MemberSubject<MemberMustBePrivateConstraint>(arms);
        MemberSubject<MemberMustBeStaticConstraint>(arms);
        MemberSubject<MemberMustBeAbstractConstraint>(arms);
        MemberSubject<MemberMustBeVirtualConstraint>(arms);
        MemberSubject<MemberMustBeGetOnlyConstraint>(arms);
        MemberSubject<MemberMustBeReadonlyConstraint>(arms);

        Add<MemberMustHaveSuffixConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.Field("suffix", c.Suffix);
        });
        Add<MemberMustHavePrefixConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.Field("prefix", c.Prefix);
        });
        Add<MemberMustHaveNameMatchingConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.Field("glob", c.Glob);
        });
        Add<MemberMustBeAttributedWithConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.ConstructionAnchor("anchor", c.Anchor);
        });
        Add<MemberMustNotBeAttributedWithConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.ConstructionAnchors("anchors", c.Anchors);
        });
        Add<MemberMustAcceptParameterConstraint>(arms, (w, c) =>
        {
            w.MemberSubject(c.MemberSubject);
            w.DefinitionAnchor("anchor", c.Anchor);
        });
    }

    private static void AddProjectVerbs(Dictionary<Type, Action<NodeWriter, object>> arms)
    {
        ProjectSubject<MustReferenceNoPackagesConstraint>(arms);
        ProjectSubject<MustLockPackagesConstraint>(arms);
        ProjectSubject<MustNotBePackableConstraint>(arms);

        Add<MustOnlyTargetConstraint>(arms, (w, c) =>
        {
            w.ProjectSubject(c.ProjectSubject);
            w.StringsField("frameworks", c.Frameworks);
        });
        Add<MustTargetConstraint>(arms, (w, c) =>
        {
            w.ProjectSubject(c.ProjectSubject);
            w.Field("framework", c.Framework);
        });
        Add<MustOnlyReferencePackagesConstraint>(arms, (w, c) =>
        {
            w.ProjectSubject(c.ProjectSubject);
            w.StringsField("patterns", c.Patterns);
        });
        Add<MustNotReferencePackagesConstraint>(arms, (w, c) =>
        {
            w.ProjectSubject(c.ProjectSubject);
            w.StringsField("patterns", c.Patterns);
        });
        Add<MustSetPropertyConstraint>(arms, (w, c) =>
        {
            w.ProjectSubject(c.ProjectSubject);
            w.Field("property", c.Name);
            w.Field("value", c.Value);
        });
    }

    private static void Subject<TConstraint>(Dictionary<Type, Action<NodeWriter, object>> arms)
        where TConstraint : Constraint
    {
        Add<TConstraint>(arms, (w, c) => w.Subject(c.Subject!));
    }

    private static void Operands<TConstraint>(Dictionary<Type, Action<NodeWriter, object>> arms)
        where TConstraint : Constraint
    {
        Add<TConstraint>(arms, (w, c) =>
        {
            w.Subject(c.Subject!);
            w.Operands(c.Operands);
        });
    }

    private static void MemberSubject<TConstraint>(Dictionary<Type, Action<NodeWriter, object>> arms)
        where TConstraint : MemberConstraint
    {
        Add<TConstraint>(arms, (w, c) => w.MemberSubject(c.MemberSubject));
    }

    private static void ProjectSubject<TConstraint>(Dictionary<Type, Action<NodeWriter, object>> arms)
        where TConstraint : ProjectConstraint
    {
        Add<TConstraint>(arms, (w, c) => w.ProjectSubject(c.ProjectSubject));
    }

    private static void Add<TNode>(Dictionary<Type, Action<NodeWriter, object>> arms, Action<NodeWriter, TNode> write)
        where TNode : class
    {
        string discriminator = DiscriminatorOf(typeof(TNode));
        arms.Add(typeof(TNode), (writer, node) =>
        {
            writer.BeginNode(discriminator);
            write(writer, (TNode)node);
            writer.EndNode();
        });
    }
}
