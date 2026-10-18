using SlideRule.Fluent;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.ModelFile;

/// <summary>
///     How each node of the reified model is read back, one arm per discriminator, within its family.
/// </summary>
/// <remarks>
///     <para>
///         Five tables rather than one, because a discriminator is only unique inside a stratum: a noun
///         and a verb may share a word without either meaning the other. A lookup with no arm is content,
///         not a bug — some file says a word this reader does not know — so it becomes a refusal naming
///         the word, and only the writer's side of the pair throws for a class it was never taught.
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

    // ---- nouns ---------------------------------------------------------------------------------

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>> BuildNouns()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionNoun?>>(StringComparer.Ordinal);

        map.Add(Key<TypesNoun>(), (r, v) => r.OnlyKeys(v, NounWhere, "node") ? TypesNoun.Instance : null);

        map.Add(Key<TypeNoun>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, NounWhere, "node", "anchor")) return null;

            TypeAnchor? anchor = r.RequiredAnchor(v, NounWhere, "anchor");
            return anchor is null ? null : new TypeNoun(anchor);
        });

        map.Add(Key<NamespaceNoun>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, NounWhere, "node", "glob")) return null;

            string? glob = r.RequiredNamespaceGlob(v, NounWhere, "glob");
            return glob is null ? null : new NamespaceNoun(glob);
        });

        map.Add(Key<ProjectNoun>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, NounWhere, "node", "name")) return null;

            string? name = r.RequiredText(v, NounWhere, "name");
            return name is null ? null : new ProjectNoun(name);
        });

        map.Add(Key<LayerNoun>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, NounWhere, "node", "name")) return null;

            Layer? layer = r.RequiredLayer(v, NounWhere, "name");
            return layer is null ? null : (LayerNoun)layer.Noun;
        });

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
            reader.RefuseAt(value,
                "an " + JsonText.Quote("each") + " noun takes exactly one of " + JsonText.Quote("layers")
                + " and " + JsonText.Quote("projects") + ".");
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

    // ---- selection adjectives ------------------------------------------------------------------

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>> BuildAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, SelectionAdjective?>>(
            StringComparer.Ordinal);

        map.Add(Key<InNamespaceAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "glob")) return null;

            string? glob = r.RequiredNamespaceGlob(v, AdjectiveWhere, "glob");
            return glob is null ? null : new InNamespaceAdjective(glob);
        });

        map.Add(Key<OfKindAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "kind")) return null;

            return r.RequiredEnum(v, AdjectiveWhere, "kind", "a type kind", out TypeKind kind)
                ? new OfKindAdjective(kind)
                : null;
        });

        map.Add(Key<WithSuffixAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "suffix")) return null;

            string? suffix = r.RequiredText(v, AdjectiveWhere, "suffix");
            return suffix is null ? null : new WithSuffixAdjective(suffix);
        });

        map.Add(Key<WithPrefixAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "prefix")) return null;

            string? prefix = r.RequiredText(v, AdjectiveWhere, "prefix");
            return prefix is null ? null : new WithPrefixAdjective(prefix);
        });

        map.Add(Key<WithNameMatchingAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "glob")) return null;

            string? glob = r.RequiredTypeNameGlob(v, AdjectiveWhere, "glob");
            return glob is null ? null : new WithNameMatchingAdjective(glob);
        });

        map.Add(Key<NamedAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "names")) return null;

            IReadOnlyList<string>? names = r.RequiredTexts(v, AdjectiveWhere, "names");
            return names is null ? null : new NamedAdjective(names);
        });

        map.Add(Key<ImplementingAdjective>(), (r, v) =>
        {
            TypeAnchor? anchor = OneAnchor(r, v, AdjectiveWhere);
            return anchor is null ? null : new ImplementingAdjective(anchor);
        });

        map.Add(Key<DerivedFromAdjective>(), (r, v) =>
        {
            TypeAnchor? anchor = OneAnchor(r, v, AdjectiveWhere);
            return anchor is null ? null : new DerivedFromAdjective(anchor);
        });

        map.Add(Key<AttributedWithAdjective>(), (r, v) =>
        {
            TypeAnchor? anchor = OneAnchor(r, v, AdjectiveWhere);
            return anchor is null ? null : new AttributedWithAdjective(anchor);
        });

        map.Add(Key<ExceptAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, AdjectiveWhere, "node", "payload")) return null;

            Selection? payload = r.RequiredSelection(v, AdjectiveWhere, "payload");
            return payload is null ? null : new ExceptAdjective(payload);
        });

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

    // ---- member adjectives ---------------------------------------------------------------------

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>> BuildMemberAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, MemberAdjective?>>(
            StringComparer.Ordinal);

        map.Add(Key<MemberWithSuffixAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, MemberAdjectiveWhere, "node", "suffix")) return null;

            string? suffix = r.RequiredText(v, MemberAdjectiveWhere, "suffix");
            return suffix is null ? null : new MemberWithSuffixAdjective(suffix);
        });

        map.Add(Key<MemberWithPrefixAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, MemberAdjectiveWhere, "node", "prefix")) return null;

            string? prefix = r.RequiredText(v, MemberAdjectiveWhere, "prefix");
            return prefix is null ? null : new MemberWithPrefixAdjective(prefix);
        });

        map.Add(Key<MemberWithNameMatchingAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, MemberAdjectiveWhere, "node", "glob")) return null;

            string? glob = r.RequiredTypeNameGlob(v, MemberAdjectiveWhere, "glob");
            return glob is null ? null : new MemberWithNameMatchingAdjective(glob);
        });

        map.Add(Key<ReturningAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, MemberAdjectiveWhere, "node", "anchors")) return null;

            IReadOnlyList<TypeAnchor>? anchors = r.RequiredAnchors(v, MemberAdjectiveWhere, "anchors");
            return anchors is null ? null : new ReturningAdjective(anchors);
        });

        map.Add(Key<MemberAttributedWithAdjective>(), (r, v) =>
        {
            TypeAnchor? anchor = OneAnchor(r, v, MemberAdjectiveWhere);
            return anchor is null ? null : new MemberAttributedWithAdjective(anchor);
        });

        map.Add(Key<MemberThatAreStaticAdjective>(),
            (r, v) => r.OnlyKeys(v, MemberAdjectiveWhere, "node") ? new MemberThatAreStaticAdjective() : null);

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

    // ---- project adjectives --------------------------------------------------------------------

    private static Dictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>> BuildProjectAdjectives()
    {
        var map = new Dictionary<string, Func<ModelNodeReader, StrictJsonValue, ProjectAdjective?>>(
            StringComparer.Ordinal);

        map.Add(Key<ProjectNamedAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ProjectAdjectiveWhere, "node", "names")) return null;

            IReadOnlyList<string>? names = r.RequiredTexts(v, ProjectAdjectiveWhere, "names");
            return names is null ? null : new ProjectNamedAdjective(names);
        });

        map.Add(Key<ProjectMatchingAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ProjectAdjectiveWhere, "node", "globs")) return null;

            IReadOnlyList<string>? globs = r.RequiredTexts(v, ProjectAdjectiveWhere, "globs");
            return globs is null ? null : new ProjectMatchingAdjective(globs);
        });

        map.Add(Key<ProjectPackableAdjective>(),
            (r, v) => r.OnlyKeys(v, ProjectAdjectiveWhere, "node") ? new ProjectPackableAdjective() : null);

        map.Add(Key<ProjectExceptAdjective>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ProjectAdjectiveWhere, "node", "payload")) return null;

            ProjectSelection? payload = r.RequiredProjectSelection(v, ProjectAdjectiveWhere, "payload");
            return payload is null ? null : new ProjectExceptAdjective(payload);
        });

        return map;
    }

    // ---- verbs ---------------------------------------------------------------------------------

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

        SubjectAnchor<MustImplementConstraint>(map, (s, a) => new MustImplementConstraint(s, a));
        SubjectAnchor<MustDeriveFromConstraint>(map, (s, a) => new MustDeriveFromConstraint(s, a));
        SubjectAnchor<MustBeAttributedWithConstraint>(map, (s, a) => new MustBeAttributedWithConstraint(s, a));
        SubjectAnchors<MustNotImplementConstraint>(map, (s, a) => new MustNotImplementConstraint(s, a));
        SubjectAnchors<MustNotDeriveFromConstraint>(map, (s, a) => new MustNotDeriveFromConstraint(s, a));
        SubjectAnchors<MustNotBeAttributedWithConstraint>(map, (s, a) => new MustNotBeAttributedWithConstraint(s, a));

        map.Add(Key<MustResideInNamespaceConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "subject", "glob")) return null;

            Selection? subject = r.RequiredSelection(v, ConstraintWhere, "subject");
            if (subject is null) return null;

            string? glob = r.RequiredNamespaceGlob(v, ConstraintWhere, "glob");
            return glob is null ? null : new MustResideInNamespaceConstraint(subject, glob);
        });

        map.Add(Key<MustResideInProjectConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "subject", "projectName")) return null;

            Selection? subject = r.RequiredSelection(v, ConstraintWhere, "subject");
            if (subject is null) return null;

            string? name = r.RequiredText(v, ConstraintWhere, "projectName");
            return name is null ? null : new MustResideInProjectConstraint(subject, name);
        });

        map.Add(Key<MustHaveSuffixConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "suffix");
            if (subject is null) return null;

            string? suffix = r.RequiredText(v, ConstraintWhere, "suffix");
            return suffix is null ? null : new MustHaveSuffixConstraint(subject, suffix);
        });

        map.Add(Key<MustHavePrefixConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "prefix");
            if (subject is null) return null;

            string? prefix = r.RequiredText(v, ConstraintWhere, "prefix");
            return prefix is null ? null : new MustHavePrefixConstraint(subject, prefix);
        });

        map.Add(Key<MustHaveNameMatchingConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "glob");
            if (subject is null) return null;

            string? glob = r.RequiredTypeNameGlob(v, ConstraintWhere, "glob");
            return glob is null ? null : new MustHaveNameMatchingConstraint(subject, glob);
        });

        map.Add(Key<MustHaveExactlyOneCounterpartConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "subject", "operands", "template")) return null;

            Selection? subject = r.RequiredSelection(v, ConstraintWhere, "subject");
            if (subject is null) return null;

            IReadOnlyList<Selection>? among = r.RequiredOperands(v, ConstraintWhere);
            if (among is null) return null;

            string? template = r.RequiredText(v, ConstraintWhere, "template");
            return template is null ? null : new MustHaveExactlyOneCounterpartConstraint(subject, among, template);
        });

        map.Add(Key<MustNotUseConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "subject", "members")) return null;

            Selection? subject = r.RequiredSelection(v, ConstraintWhere, "subject");
            if (subject is null) return null;

            IReadOnlyList<Member>? members = r.RequiredMembers(v, ConstraintWhere);
            return members is null ? null : new MustNotUseConstraint(subject, members);
        });
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
        MemberSubject<MemberMustBeGetOnlyConstraint>(map, s => new MemberMustBeGetOnlyConstraint(s));
        MemberSubject<MemberMustBeReadonlyConstraint>(map, s => new MemberMustBeReadonlyConstraint(s));

        map.Add(Key<MemberMustHaveSuffixConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "suffix");
            if (subject is null) return null;

            string? suffix = r.RequiredText(v, ConstraintWhere, "suffix");
            return suffix is null ? null : new MemberMustHaveSuffixConstraint(subject, suffix);
        });

        map.Add(Key<MemberMustHavePrefixConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "prefix");
            if (subject is null) return null;

            string? prefix = r.RequiredText(v, ConstraintWhere, "prefix");
            return prefix is null ? null : new MemberMustHavePrefixConstraint(subject, prefix);
        });

        map.Add(Key<MemberMustHaveNameMatchingConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "glob");
            if (subject is null) return null;

            string? glob = r.RequiredTypeNameGlob(v, ConstraintWhere, "glob");
            return glob is null ? null : new MemberMustHaveNameMatchingConstraint(subject, glob);
        });

        map.Add(Key<MemberMustBeAttributedWithConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "anchor");
            if (subject is null) return null;

            TypeAnchor? anchor = r.RequiredAnchor(v, ConstraintWhere, "anchor");
            return anchor is null ? null : new MemberMustBeAttributedWithConstraint(subject, anchor);
        });

        map.Add(Key<MemberMustAcceptParameterConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "anchor");
            if (subject is null) return null;

            TypeAnchor? anchor = r.RequiredAnchor(v, ConstraintWhere, "anchor");
            return anchor is null ? null : new MemberMustAcceptParameterConstraint(subject, anchor);
        });

        map.Add(Key<MemberMustNotBeAttributedWithConstraint>(), (r, v) =>
        {
            MemberSelection? subject = MemberSubjectWith(r, v, "anchors");
            if (subject is null) return null;

            IReadOnlyList<TypeAnchor>? anchors = r.RequiredAnchors(v, ConstraintWhere, "anchors");
            return anchors is null ? null : new MemberMustNotBeAttributedWithConstraint(subject, anchors);
        });
    }

    private static void AddProjectVerbs(Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map)
    {
        ProjectSubject<MustReferenceNoPackagesConstraint>(map, s => new MustReferenceNoPackagesConstraint(s));
        ProjectSubject<MustLockPackagesConstraint>(map, s => new MustLockPackagesConstraint(s));
        ProjectSubject<MustNotBePackableConstraint>(map, s => new MustNotBePackableConstraint(s));

        map.Add(Key<MustOnlyTargetConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "projectSubject", "frameworks")) return null;

            ProjectSelection? subject = r.RequiredProjectSelection(v, ConstraintWhere, "projectSubject");
            if (subject is null) return null;

            IReadOnlyList<string>? frameworks = r.RequiredTexts(v, ConstraintWhere, "frameworks");
            return frameworks is null ? null : new MustOnlyTargetConstraint(subject, frameworks);
        });
        map.Add(Key<MustTargetConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "projectSubject", "framework")) return null;

            ProjectSelection? subject = r.RequiredProjectSelection(v, ConstraintWhere, "projectSubject");
            if (subject is null) return null;

            string? framework = r.RequiredText(v, ConstraintWhere, "framework");
            return framework is null ? null : new MustTargetConstraint(subject, framework);
        });
        ProjectPatterns<MustOnlyReferencePackagesConstraint>(map, (s, patterns) => new MustOnlyReferencePackagesConstraint(s, patterns));
        ProjectPatterns<MustNotReferencePackagesConstraint>(map, (s, patterns) => new MustNotReferencePackagesConstraint(s, patterns));
        map.Add(Key<MustSetPropertyConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "projectSubject", "property", "value")) return null;

            ProjectSelection? subject = r.RequiredProjectSelection(v, ConstraintWhere, "projectSubject");
            if (subject is null) return null;

            string? name = r.RequiredText(v, ConstraintWhere, "property");
            if (name is null) return null;

            string? value = r.RequiredText(v, ConstraintWhere, "value");
            return value is null ? null : new MustSetPropertyConstraint(subject, name, value);
        });
    }

    // ---- the shapes several verbs share --------------------------------------------------------

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

    private static void SubjectAnchor<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<Selection, TypeAnchor, TConstraint> build)
        where TConstraint : Constraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "anchor");
            if (subject is null) return null;

            TypeAnchor? anchor = r.RequiredAnchor(v, ConstraintWhere, "anchor");
            return anchor is null ? null : build(subject, anchor);
        });
    }

    private static void SubjectAnchors<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<Selection, IReadOnlyList<TypeAnchor>, TConstraint> build)
        where TConstraint : Constraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            Selection? subject = SubjectWith(r, v, "anchors");
            if (subject is null) return null;

            IReadOnlyList<TypeAnchor>? anchors = r.RequiredAnchors(v, ConstraintWhere, "anchors");
            return anchors is null ? null : build(subject, anchors);
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

    // The package verbs' shape: a project subject and the list of identifier globs it is judged against.
    private static void ProjectPatterns<TConstraint>(
        Dictionary<string, Func<ModelNodeReader, StrictJsonValue, Constraint?>> map,
        Func<ProjectSelection, IReadOnlyList<string>, TConstraint> build)
        where TConstraint : ProjectConstraint
    {
        map.Add(Key<TConstraint>(), (r, v) =>
        {
            if (!r.OnlyKeys(v, ConstraintWhere, "node", "projectSubject", "patterns")) return null;

            ProjectSelection? subject = r.RequiredProjectSelection(v, ConstraintWhere, "projectSubject");
            if (subject is null) return null;

            IReadOnlyList<string>? patterns = r.RequiredTexts(v, ConstraintWhere, "patterns");
            return patterns is null ? null : build(subject, patterns);
        });
    }

    private static Selection? SubjectWith(ModelNodeReader reader, StrictJsonValue value, string field)
    {
        if (!reader.OnlyKeys(value, ConstraintWhere, "node", "subject", field)) return null;

        return reader.RequiredSelection(value, ConstraintWhere, "subject");
    }

    private static MemberSelection? MemberSubjectWith(ModelNodeReader reader, StrictJsonValue value, string field)
    {
        if (!reader.OnlyKeys(value, ConstraintWhere, "node", "memberSubject", field)) return null;

        return reader.RequiredMemberSelection(value, ConstraintWhere, "memberSubject");
    }

    private static TypeAnchor? OneAnchor(ModelNodeReader reader, StrictJsonValue value, string where)
    {
        if (!reader.OnlyKeys(value, where, "node", "anchor")) return null;

        return reader.RequiredAnchor(value, where, "anchor");
    }

    private static string Key<TNode>()
    {
        return NodeWriters.DiscriminatorOf(typeof(TNode));
    }
}
