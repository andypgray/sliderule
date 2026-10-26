using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.ModelFile;

/// <summary>
///     How each node of the reified model is read back, one arm per discriminator, within its family.
/// </summary>
/// <remarks>
///     <para>
///         One table per <see cref="NodeFamily" />: a noun and a verb may share a word without either meaning
///         the other. A lookup with no arm is content, not a bug (some file says a word this reader does not
///         know), so it becomes a refusal naming the word, and only the writer's side of the pair throws for a
///         class it was never taught.
///     </para>
///     <para>
///         The keys come from the same mechanical rule the writer spells its discriminators with, so the
///         two sides cannot drift apart by a typo in one of them. What each arm reads is the file's field
///         list for that node, and the arm refuses any other key before it reads a thing.
///     </para>
/// </remarks>
internal static class NodeReaders
{
    internal const string AdjectiveWhere = "an adjective";
    internal const string ConstraintWhere = "a constraint";
    internal const string MemberAdjectiveWhere = "a member adjective";
    internal const string NounWhere = "a noun";
    internal const string ProjectAdjectiveWhere = "a project adjective";

    private static readonly Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>> Nouns =
        BuildNouns();

    private static readonly Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>> Adjectives =
        BuildAdjectives();

    private static readonly Dictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>>
        MemberAdjectives = BuildMemberAdjectives();

    private static readonly Dictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>>
        ProjectAdjectives = BuildProjectAdjectives();

    private static readonly Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> Constraints =
        BuildConstraints();

    /// <summary>The discriminators one family answers to — the reader's half of the node inventory.</summary>
    internal static IReadOnlyCollection<string> Discriminators(NodeFamily family)
    {
        switch (family)
        {
            case NodeFamily.Noun:
                return Nouns.Keys;
            case NodeFamily.Adjective:
                return Adjectives.Keys;
            case NodeFamily.MemberAdjective:
                return MemberAdjectives.Keys;
            case NodeFamily.ProjectAdjective:
                return ProjectAdjectives.Keys;
            case NodeFamily.Constraint:
                return Constraints.Keys;
            default:
                throw new InvalidOperationException($"Unhandled node family '{family}'.");
        }
    }

    internal static IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>> NounArms =>
        Nouns;

    internal static IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>>
        AdjectiveArms => Adjectives;

    internal static IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>>
        MemberAdjectiveArms => MemberAdjectives;

    internal static IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>>
        ProjectAdjectiveArms => ProjectAdjectives;

    internal static IReadOnlyDictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> ConstraintArms =>
        Constraints;

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>> BuildNouns()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>>(StringComparer.Ordinal);

        map.Add(Key<TypesNoun>(), (r, v) => r.OnlyKeys(v, NounWhere, "node") ? TypesNoun.Instance : null);
        Field(map, NounWhere, "anchor", Anchor, anchor => new TypeNoun(anchor));
        Field(map, NounWhere, "glob", NamespaceGlob, glob => new NamespaceNoun(glob));
        Field(map, NounWhere, "name", Text, name => new ProjectNoun(name));
        Field(map, NounWhere, "name", LayerAt, layer => (LayerNoun)layer.Noun);

        map.Add(Key<RegisteredNoun>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, NounWhere, "node", "lifetime")) return null;

            return r.OptionalEnum(v, "lifetime", "a lifetime", out Lifetime? lifetime)
                ? new RegisteredNoun(lifetime)
                : null;
        });

        map.Add(Key<EachNoun>(), ReadEach);
        return map;
    }

    private static SelectionNoun? ReadEach(ModelNodeReader reader, StrictJsonValue value)
    {
        if (!reader.OnlyKeys(value, NounWhere, "node", "layers", "projects")) return null;

        StrictJsonValue? layers = reader.Optional(value, "layers");
        StrictJsonValue? projects = reader.Optional(value, "projects");
        if (layers is null == projects is null)
        {
            reader.RefuseExactlyOneOf(value, "an " + JsonText.Quote("each") + " noun", "layers", "projects");
            return null;
        }

        if (layers is not null)
        {
            IReadOnlyList<Layer>? cells = reader.RequiredLayers(value, NounWhere, "layers");
            return cells is null ? null : new EachNoun(cells);
        }

        ProjectSelection? selected = reader.RequiredProjectSelection(value, NounWhere, "projects");
        return selected is null ? null : new EachNoun(selected);
    }

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>> BuildAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>>(
            StringComparer.Ordinal);

        Field(map, AdjectiveWhere, "glob", NamespaceGlob, glob => new InNamespaceAdjective(glob));

        map.Add(Key<OfKindAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "kind")) return null;

            return r.RequiredEnum(v, AdjectiveWhere, "kind", "a type kind", out TypeKind kind)
                ? new OfKindAdjective(kind)
                : null;
        });

        Field(map, AdjectiveWhere, "suffix", Text, suffix => new WithSuffixAdjective(suffix));
        Field(map, AdjectiveWhere, "prefix", Text, prefix => new WithPrefixAdjective(prefix));
        Field(map, AdjectiveWhere, "glob", TypeNameGlob, glob => new WithNameMatchingAdjective(glob));
        Field(map, AdjectiveWhere, "names", Texts, names => new NamedAdjective(names));
        Field(map, AdjectiveWhere, "globs", PathGlobs, globs => new UnderAdjective(globs));
        Field(map, AdjectiveWhere, "anchor", Anchor, anchor => new ImplementingAdjective(anchor));
        Field(map, AdjectiveWhere, "anchor", Anchor, anchor => new DerivedFromAdjective(anchor));
        Field(map, AdjectiveWhere, "anchor", Anchor, anchor => new AttributedWithAdjective(anchor));
        Field(map, AdjectiveWhere, "payload", SelectionAt, payload => new ExceptAdjective(payload));

        map.Add(Key<AuthoredAdjective>(),
            (r, v) => r.OnlyKeys(v, AdjectiveWhere, "node") ? new AuthoredAdjective() : null);

        map.Add(Key<AccessibilityAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "accessibility")) return null;

            return r.RequiredEnum(v, AdjectiveWhere, "accessibility", "an accessibility",
                out Accessibility accessibility)
                ? new AccessibilityAdjective(accessibility)
                : null;
        });

        return map;
    }

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>> BuildMemberAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>>(
            StringComparer.Ordinal);

        Field(map, MemberAdjectiveWhere, "suffix", Text, suffix => new MemberWithSuffixAdjective(suffix));
        Field(map, MemberAdjectiveWhere, "prefix", Text, prefix => new MemberWithPrefixAdjective(prefix));
        Field(map, MemberAdjectiveWhere, "glob", TypeNameGlob, glob => new MemberWithNameMatchingAdjective(glob));
        Field(map, MemberAdjectiveWhere, "names", NonEmptyTexts, names => new MemberNamedAdjective(names));
        Field(map, MemberAdjectiveWhere, "anchors", Anchors, anchors => new ReturningAdjective(anchors));
        Field(map, MemberAdjectiveWhere, "anchor", Anchor, anchor => new MemberAttributedWithAdjective(anchor));

        map.Add(Key<MemberThatAreStaticAdjective>(),
            (r, v) => r.OnlyKeys(v, MemberAdjectiveWhere, "node") ? new MemberThatAreStaticAdjective() : null);

        Field(map, MemberAdjectiveWhere, "payloads", NonEmptyMemberSelections,
            payloads => new MemberExceptAdjective(payloads));

        map.Add(Key<MemberAccessibilityAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, MemberAdjectiveWhere, "node", "accessibility")) return null;

            return r.RequiredEnum(v, MemberAdjectiveWhere, "accessibility", "an accessibility",
                out Accessibility accessibility)
                ? new MemberAccessibilityAdjective(accessibility)
                : null;
        });

        return map;
    }

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>> BuildProjectAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>>(
            StringComparer.Ordinal);

        Field(map, ProjectAdjectiveWhere, "names", NonEmptyTexts, names => new ProjectNamedAdjective(names));
        Field(map, ProjectAdjectiveWhere, "globs", NonEmptyTexts, globs => new ProjectMatchingAdjective(globs));

        map.Add(Key<ProjectPackableAdjective>(),
            (r, v) => r.OnlyKeys(v, ProjectAdjectiveWhere, "node") ? new ProjectPackableAdjective() : null);

        Field(map, ProjectAdjectiveWhere, "payload", ProjectSelectionAt, payload => new ProjectExceptAdjective(payload));

        return map;
    }

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> BuildConstraints()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>>(StringComparer.Ordinal);
        AddTypeVerbs(map);
        AddMemberVerbs(map);
        AddProjectVerbs(map);
        return map;
    }

    private static void AddTypeVerbs(Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map)
    {
        Subject<MustBeSealedConstraint>(map, s => new MustBeSealedConstraint(s));
        Subject<MustBeStaticConstraint>(map, s => new MustBeStaticConstraint(s));
        Subject<MustBeAbstractConstraint>(map, s => new MustBeAbstractConstraint(s));
        Subject<MustBePublicConstraint>(map, s => new MustBePublicConstraint(s));
        Subject<MustBeInternalConstraint>(map, s => new MustBeInternalConstraint(s));
        Subject<MustBeRegisteredConstraint>(map, s => new MustBeRegisteredConstraint(s));
        Subject<MustNotReferenceEachOtherConstraint>(map, s => new MustNotReferenceEachOtherConstraint(s));
        Subject<MustNotHaveCircularReferencesConstraint>(map, s => new MustNotHaveCircularReferencesConstraint(s));
        Subject<MustOnlyReferenceItselfConstraint>(map, s => new MustOnlyReferenceItselfConstraint(s));
        Subject<MustOnlyBeReferencedByItselfConstraint>(map, s => new MustOnlyBeReferencedByItselfConstraint(s));

        Operands<MustNotReferenceConstraint>(map, (s, o) => new MustNotReferenceConstraint(s, o));
        Operands<MustNotBeReferencedByConstraint>(map, (s, o) => new MustNotBeReferencedByConstraint(s, o));
        Operands<MustOnlyReferenceConstraint>(map, (s, o) => new MustOnlyReferenceConstraint(s, o));
        Operands<MustOnlyBeReferencedByConstraint>(map, (s, o) => new MustOnlyBeReferencedByConstraint(s, o));
        Operands<MustNotConstructConstraint>(map, (s, o) => new MustNotConstructConstraint(s, o));
        Operands<MustNotInjectConstraint>(map, (s, o) => new MustNotInjectConstraint(s, o));
        Operands<MustNotCatchConstraint>(map, (s, o) => new MustNotCatchConstraint(s, o));
        Operands<MustNotCatchUnfilteredConstraint>(map, (s, o) => new MustNotCatchUnfilteredConstraint(s, o));
        Operands<MustNotSwallowConstraint>(map, (s, o) => new MustNotSwallowConstraint(s, o));
        Operands<MustNotThrowConstraint>(map, (s, o) => new MustNotThrowConstraint(s, o));
        Operands<MustOnlyThrowConstraint>(map, (s, o) => new MustOnlyThrowConstraint(s, o));
        Operands<MustNotExposeConstraint>(map, (s, o) => new MustNotExposeConstraint(s, o));
        Operands<MustBelongToConstraint>(map, (s, o) => new MustBelongToConstraint(s, o));

        SubjectField(map, "anchor", Anchor, (s, a) => new MustImplementConstraint(s, a));
        SubjectField(map, "anchor", Anchor, (s, a) => new MustDeriveFromConstraint(s, a));
        SubjectField(map, "anchor", Anchor, (s, a) => new MustBeAttributedWithConstraint(s, a));
        SubjectField(map, "anchors", Anchors, (s, a) => new MustNotImplementConstraint(s, a));
        SubjectField(map, "anchors", Anchors, (s, a) => new MustNotDeriveFromConstraint(s, a));
        SubjectField(map, "anchors", Anchors, (s, a) => new MustNotBeAttributedWithConstraint(s, a));

        SubjectField(map, "glob", NamespaceGlob, (s, glob) => new MustResideInNamespaceConstraint(s, glob));
        SubjectField(map, "projectName", Text, (s, name) => new MustResideInProjectConstraint(s, name));
        SubjectField(map, "suffix", Text, (s, suffix) => new MustHaveSuffixConstraint(s, suffix));
        SubjectField(map, "prefix", Text, (s, prefix) => new MustHavePrefixConstraint(s, prefix));
        SubjectField(map, "glob", TypeNameGlob, (s, glob) => new MustHaveNameMatchingConstraint(s, glob));

        map.Add(Key<MustHaveExactlyOneCounterpartConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "operands", "template");
            if (subject is null) return null;

            IReadOnlyList<Selection>? among = r.RequiredOperands(v, ConstraintWhere);
            if (among is null) return null;

            string? template = r.RequiredText(v, ConstraintWhere, "template");
            return template is null ? null : new MustHaveExactlyOneCounterpartConstraint(subject, among, template);
        });

        SubjectField(map, "members", NonEmptyMembers, (s, members) => new MustNotUseConstraint(s, members));
    }

    private static void AddMemberVerbs(Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map)
    {
        MemberSubject<MemberMustBePublicConstraint>(map, s => new MemberMustBePublicConstraint(s));
        MemberSubject<MemberMustBeInternalConstraint>(map, s => new MemberMustBeInternalConstraint(s));
        MemberSubject<MemberMustBeProtectedConstraint>(map, s => new MemberMustBeProtectedConstraint(s));
        MemberSubject<MemberMustBePrivateConstraint>(map, s => new MemberMustBePrivateConstraint(s));
        MemberSubject<MemberMustBeStaticConstraint>(map, s => new MemberMustBeStaticConstraint(s));
        MemberSubject<MemberMustBeAbstractConstraint>(map, s => new MemberMustBeAbstractConstraint(s));
        MemberSubject<MemberMustBeVirtualConstraint>(map, s => new MemberMustBeVirtualConstraint(s));
        MemberSubject<MemberMustBeOverridableConstraint>(map, s => new MemberMustBeOverridableConstraint(s));
        MemberSubject<MemberMustBeGetOnlyConstraint>(map, s => new MemberMustBeGetOnlyConstraint(s));
        MemberSubject<MemberMustBeReadonlyConstraint>(map, s => new MemberMustBeReadonlyConstraint(s));

        MemberSubjectField(map, "suffix", Text, (s, suffix) => new MemberMustHaveSuffixConstraint(s, suffix));
        MemberSubjectField(map, "prefix", Text, (s, prefix) => new MemberMustHavePrefixConstraint(s, prefix));
        MemberSubjectField(map, "glob", TypeNameGlob, (s, glob) => new MemberMustHaveNameMatchingConstraint(s, glob));
        MemberSubjectField(map, "anchor", Anchor, (s, a) => new MemberMustBeAttributedWithConstraint(s, a));
        MemberSubjectField(map, "anchor", Anchor, (s, a) => new MemberMustAcceptParameterConstraint(s, a));
        MemberSubjectField(map, "anchors", Anchors, (s, a) => new MemberMustNotBeAttributedWithConstraint(s, a));
    }

    private static void AddProjectVerbs(Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map)
    {
        ProjectSubject<MustReferenceNoPackagesConstraint>(map, s => new MustReferenceNoPackagesConstraint(s));
        ProjectSubject<MustLockPackagesConstraint>(map, s => new MustLockPackagesConstraint(s));
        ProjectSubject<MustNotBePackableConstraint>(map, s => new MustNotBePackableConstraint(s));

        ProjectSubjectField(map, "frameworks", Texts, (s, frameworks) => new MustOnlyTargetConstraint(s, frameworks));
        ProjectSubjectField(map, "framework", Text, (s, framework) => new MustTargetConstraint(s, framework));
        ProjectSubjectField(map, "patterns", Texts, (s, patterns) => new MustOnlyReferencePackagesConstraint(s, patterns));
        ProjectSubjectField(map, "patterns", Texts, (s, patterns) => new MustNotReferencePackagesConstraint(s, patterns));
        map.Add(Key<MustSetPropertyConstraint>(), (r, v) =>
        {
            ProjectSelection? subject = ProjectSubjectWith(r, v, "property", "value");
            if (subject is null) return null;

            string? name = r.RequiredText(v, ConstraintWhere, "property");
            if (name is null) return null;

            string? value = r.RequiredText(v, ConstraintWhere, "value");
            return value is null ? null : new MustSetPropertyConstraint(subject, name, value);
        });
    }

    // The arm of a node with one field beside its discriminator.
    private static void Field<TFamily, TNode, TValue>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, TFamily?>> map, string where, string key,
        Func<ModelNodeReader, StrictJsonValue, string, string, TValue?> read, Func<TValue, TNode> build)
        where TFamily : class
        where TNode : class, TFamily
        where TValue : class
    {
        map.Add(Key<TNode>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, where, "node", key)) return null;

            TValue? value = read(r, v, where, key);
            return value is null ? null : build(value);
        });
    }

    private static void Subject<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map, Func<Selection, TConstraint> build)
        where TConstraint : Constraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "subject")) return null;

            Selection? subject = r.RequiredSelection(v, ConstraintWhere, "subject");
            return subject is null ? null : build(subject);
        });
    }

    private static void Operands<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<Selection, IReadOnlyList<Selection>, TConstraint> build)
        where TConstraint : Constraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "operands");
            if (subject is null) return null;

            IReadOnlyList<Selection>? operands = r.RequiredOperands(v, ConstraintWhere);
            return operands is null ? null : build(subject, operands);
        });
    }

    private static void SubjectField<TConstraint, TValue>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map, string key,
        Func<ModelNodeReader, StrictJsonValue, string, string, TValue?> read, Func<Selection, TValue, TConstraint> build)
        where TConstraint : Constraint
        where TValue : class
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, key);
            if (subject is null) return null;

            TValue? value = read(r, v, ConstraintWhere, key);
            return value is null ? null : build(subject, value);
        });
    }

    private static void MemberSubject<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<MemberSelection, TConstraint> build)
        where TConstraint : MemberConstraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "memberSubject")) return null;

            MemberSelection? subject = r.RequiredMemberSelection(v, ConstraintWhere, "memberSubject");
            return subject is null ? null : build(subject);
        });
    }

    private static void MemberSubjectField<TConstraint, TValue>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map, string key,
        Func<ModelNodeReader, StrictJsonValue, string, string, TValue?> read,
        Func<MemberSelection, TValue, TConstraint> build)
        where TConstraint : MemberConstraint
        where TValue : class
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, key);
            if (subject is null) return null;

            TValue? value = read(r, v, ConstraintWhere, key);
            return value is null ? null : build(subject, value);
        });
    }

    private static void ProjectSubject<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<ProjectSelection, TConstraint> build)
        where TConstraint : ProjectConstraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "projectSubject")) return null;

            ProjectSelection? subject = r.RequiredProjectSelection(v, ConstraintWhere, "projectSubject");
            return subject is null ? null : build(subject);
        });
    }

    private static void ProjectSubjectField<TConstraint, TValue>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map, string key,
        Func<ModelNodeReader, StrictJsonValue, string, string, TValue?> read,
        Func<ProjectSelection, TValue, TConstraint> build)
        where TConstraint : ProjectConstraint
        where TValue : class
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            ProjectSelection? subject = ProjectSubjectWith(r, v, key);
            if (subject is null) return null;

            TValue? value = read(r, v, ConstraintWhere, key);
            return value is null ? null : build(subject, value);
        });
    }

    private static Selection? SubjectWith(ModelNodeReader reader, StrictJsonValue value, params string[] fields)
    {
        string[] allowed = ["node", "subject", .. fields];
        if (!reader.OnlyKeys(value, ConstraintWhere, allowed)) return null;

        return reader.RequiredSelection(value, ConstraintWhere, "subject");
    }

    private static MemberSelection? MemberSubjectWith(ModelNodeReader reader, StrictJsonValue value, string field)
    {
        if (!reader.OnlyKeys(value, ConstraintWhere, "node", "memberSubject", field)) return null;

        return reader.RequiredMemberSelection(value, ConstraintWhere, "memberSubject");
    }

    private static ProjectSelection? ProjectSubjectWith(ModelNodeReader reader, StrictJsonValue value, params string[] fields)
    {
        string[] allowed = ["node", "projectSubject", .. fields];
        if (!reader.OnlyKeys(value, ConstraintWhere, allowed)) return null;

        return reader.RequiredProjectSelection(value, ConstraintWhere, "projectSubject");
    }

    // The field readers the one-field registrars take: each reads `key` of the node `v`, refusing as `where`.
    // They are methods rather than delegate fields because the arm tables above are built by static field
    // initialisers, which run before any delegate field declared below them is assigned.

    private static string? Text(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredText(v, where, key);
    }

    private static IReadOnlyList<string>? Texts(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredTexts(v, where, key);
    }

    private static IReadOnlyList<string>? NonEmptyTexts(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        IReadOnlyList<string>? texts = r.RequiredTexts(v, where, key);
        return r.NonEmpty(texts, v, key);
    }

    private static string? NamespaceGlob(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredNamespaceGlob(v, where, key);
    }

    private static string? TypeNameGlob(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredTypeNameGlob(v, where, key);
    }

    private static IReadOnlyList<string>? PathGlobs(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredPathGlobs(v, where, key);
    }

    private static TypeAnchor? Anchor(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredAnchor(v, where, key);
    }

    private static IReadOnlyList<TypeAnchor>? Anchors(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredAnchors(v, where, key);
    }

    private static Selection? SelectionAt(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredSelection(v, where, key);
    }

    private static ProjectSelection? ProjectSelectionAt(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredProjectSelection(v, where, key);
    }

    private static IReadOnlyList<MemberSelection>? NonEmptyMemberSelections(
        ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        IReadOnlyList<MemberSelection>? selections = r.RequiredMemberSelections(v, where, key);
        return r.NonEmpty(selections, v, key);
    }

    private static Layer? LayerAt(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        return r.RequiredLayer(v, where, key);
    }

    // The member list's key is fixed by the reader, so `key` names it only for the emptiness refusal.
    private static IReadOnlyList<Member>? NonEmptyMembers(ModelNodeReader r, StrictJsonValue v, string where, string key)
    {
        IReadOnlyList<Member>? members = r.RequiredMembers(v, where);
        return r.NonEmpty(members, v, key);
    }

    private static string Key<TNode>()
    {
        return NodeWriters.DiscriminatorOf(typeof(TNode));
    }
}
