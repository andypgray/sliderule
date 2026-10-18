using SlideRule.Fluent;
using SlideRule.Tests.Stubs;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Vocabulary;

/// <summary>
///     One name of the shipped fluent vocabulary as the generated table shows it: where the name sits, and
///     the fluent call whose node the sentence renderer turns into the row's fragment.
/// </summary>
/// <param name="Stratum">
///     What the name's receiver selects; for a noun, what the noun itself selects, except that
///     <c>arch.Member</c>, which names one member, is filed under types.
/// </param>
/// <param name="PartOfSpeech">The part the name plays in a spec sentence.</param>
/// <param name="Name">The member name as a spec spells it.</param>
/// <param name="Build">
///     The example call over a fresh <see cref="Arch" />, returning the node the call hands back: a selection,
///     a member selection, a project selection, a member, or a constraint.
/// </param>
internal sealed record VocabularyRow(
    VocabularyStratum Stratum,
    PartOfSpeech PartOfSpeech,
    string Name,
    Func<Arch, object> Build)
{
    /// <summary>This row's stratum, part of speech and name, which a sweep entry is matched on.</summary>
    internal VocabularyKey Key => new(Stratum, PartOfSpeech, Name);
}

/// <summary>
///     Every row of the generated vocabulary table, in the order the grammar teaches the names: type nouns,
///     type adjectives, type verbs, then the member projections, adjectives and verbs, then the project noun,
///     adjectives and verbs.
/// </summary>
/// <remarks>
///     A row names its operand and nothing else. Which renderer call prints the fragment is decided by the
///     node the call returns, so every fragment is the renderer's own output, and the overloads come from the
///     reflection sweep rather than from here. The one shipped name with no row is <see cref="Layer.Purpose" />:
///     a trailer that the adjective sweep sees only because it hands the layer back.
/// </remarks>
internal static class VocabularyCatalog
{
    /// <summary>The rows in catalog order.</summary>
    internal static IReadOnlyList<VocabularyRow> Rows { get; } =
    [
        .. TypeNounRows(),
        .. TypeAdjectiveRows(),
        .. TypeVerbRows(),
        .. ProjectionRows(),
        .. MemberAdjectiveRows(),
        .. MemberVerbRows(),
        .. ProjectNounRows(),
        .. ProjectAdjectiveRows(),
        .. ProjectVerbRows()
    ];

    // Each group's rows are built in a member of their own because the extractor's walk binds a single 99-row
    // initializer as one expression, in about 3.6 s; nine members cut that to about 40 ms.
    private static VocabularyRow[] TypeNounRows()
    {
        return
        [
            TypeNoun(nameof(Arch.Types), arch => arch.Types),
            TypeNoun(nameof(Arch.Layer), arch => arch.Layer("Domain", "MyApp.Domain.*")),
            TypeNoun(nameof(Arch.Namespace), arch => arch.Namespace("MyApp.Legacy.Billing.*")),
            TypeNoun(nameof(Arch.Project), arch => arch.Project("MyApp.Web")),
            TypeNoun(nameof(Arch.Type), arch => arch.Type(typeof(SqlConnection))),
            TypeNoun(nameof(Arch.Registered), arch => arch.Registered(Lifetime.Singleton)),
            TypeNoun(nameof(Arch.AnyOf), arch => arch.AnyOf(arch.Namespace("MyApp.Tracking.*"), arch.Namespace("MyApp.Billing.*"))),
            TypeNoun(nameof(Arch.Each), LayerFamily),
            TypeNoun(nameof(Arch.Member), arch => arch.Member(() => DateTime.Now))
        ];
    }

    private static VocabularyRow[] TypeAdjectiveRows()
    {
        return
        [
            TypeAdjective(nameof(SelectionAdjectives.InNamespace), arch => arch.Types.InNamespace("MyApp.Web.*")),
            TypeAdjective(nameof(SelectionAdjectives.OfKind), arch => arch.Types.OfKind(TypeKind.Interface)),
            TypeAdjective(nameof(SelectionAdjectives.WithSuffix), arch => arch.Types.WithSuffix("Controller")),
            TypeAdjective(nameof(SelectionAdjectives.WithPrefix), arch => arch.Types.WithPrefix("Legacy")),
            TypeAdjective(nameof(SelectionAdjectives.WithNameMatching), arch => arch.Types.WithNameMatching("*Repo*")),
            TypeAdjective(nameof(SelectionAdjectives.Named), arch => arch.Types.Named("OrdersRepository")),
            TypeAdjective(nameof(SelectionAdjectives.Implementing), arch => arch.Types.Implementing(typeof(IHandler<>))),
            TypeAdjective(nameof(SelectionAdjectives.DerivedFrom), arch => arch.Types.DerivedFrom(typeof(ControllerBase))),
            TypeAdjective(nameof(SelectionAdjectives.AttributedWith),
                arch => arch.Types.AttributedWith(typeof(ApiControllerAttribute))),
            TypeAdjective(nameof(SelectionAdjectives.Except), arch => arch.Types.Except(arch.Type<HandlerRegistry>())),
            TypeAdjective(nameof(SelectionAdjectives.Where),
                arch => arch.Types.Where(type => type.IsRecord, description: "that are records")),
            TypeAdjective(nameof(SelectionAdjectives.Authored), arch => arch.Types.Authored()),
            TypeAdjective(nameof(SelectionAdjectives.ThatArePublic), arch => arch.Types.ThatArePublic()),
            TypeAdjective(nameof(SelectionAdjectives.ThatAreInternal), arch => arch.Types.ThatAreInternal()),
            TypeAdjective(nameof(SelectionAdjectives.ThatArePrivate), arch => arch.Types.ThatArePrivate())
        ];
    }

    private static VocabularyRow[] TypeVerbRows()
    {
        return
        [
            TypeVerb(nameof(SelectionConstraints.MustNotReference),
                arch => arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*"))),
            TypeVerb(nameof(SelectionConstraints.MustOnlyReference), arch => arch.Types.MustOnlyReference(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustOnlyReferenceItself),
                arch => arch.Layer("Tracking", "MyApp.Tracking.*").MustOnlyReferenceItself()),
            TypeVerb(nameof(SelectionConstraints.MustNotBeReferencedBy), arch => arch.Types.MustNotBeReferencedBy(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustOnlyBeReferencedBy), arch => arch.Types.MustOnlyBeReferencedBy(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustOnlyBeReferencedByItself),
                arch => arch.Layer("Tracking", "MyApp.Tracking.*").MustOnlyBeReferencedByItself()),
            TypeVerb(nameof(SelectionConstraints.MustNotReferenceEachOther), arch => LayerFamily(arch).MustNotReferenceEachOther()),
            TypeVerb(nameof(SelectionConstraints.MustNotHaveCircularReferences),
                arch => LayerFamily(arch).MustNotHaveCircularReferences()),
            TypeVerb(nameof(SelectionConstraints.MustNotUse), arch => arch.Types.MustNotUse(arch.Member(() => DateTime.Now))),
            TypeVerb(nameof(SelectionConstraints.MustNotConstruct), arch => arch.Types.MustNotConstruct(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustNotInject), arch => arch.Types.MustNotInject(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustNotCatch), arch => arch.Types.MustNotCatch(typeof(InvalidOperationException))),
            TypeVerb(nameof(SelectionConstraints.MustNotCatchUnfiltered), arch => arch.Types.MustNotCatchUnfiltered(typeof(Exception))),
            TypeVerb(nameof(SelectionConstraints.MustNotSwallow), arch => arch.Types.MustNotSwallow(typeof(Exception))),
            TypeVerb(nameof(SelectionConstraints.MustNotThrow), arch => arch.Types.MustNotThrow(typeof(InvalidOperationException))),
            TypeVerb(nameof(SelectionConstraints.MustOnlyThrow), arch => arch.Types.MustOnlyThrow(typeof(InvalidOperationException))),
            TypeVerb(nameof(SelectionConstraints.MustNotExpose), arch => arch.Types.MustNotExpose(typeof(SqlConnection))),
            TypeVerb(nameof(SelectionConstraints.MustResideInNamespace), arch => arch.Types.MustResideInNamespace("MyApp.Web.*")),
            TypeVerb(nameof(SelectionConstraints.MustResideInProject), arch => arch.Types.MustResideInProject("MyApp.Web")),
            TypeVerb(nameof(SelectionConstraints.MustBelongTo),
                arch => arch.Types.MustBelongTo(arch.Layer("Domain", "MyApp.Domain.*"), arch.Layer("Web", "MyApp.Web.*"))),
            TypeVerb(nameof(SelectionConstraints.MustHaveExactlyOneCounterpart),
                arch => arch.Types.MustHaveExactlyOneCounterpart(among: arch.Types.OfKind(TypeKind.Interface), named: "I{Name}")),
            TypeVerb(nameof(SelectionConstraints.MustBeRegistered), arch => arch.Types.MustBeRegistered()),
            TypeVerb(nameof(SelectionConstraints.MustHaveSuffix), arch => arch.Types.MustHaveSuffix("Handler")),
            TypeVerb(nameof(SelectionConstraints.MustHavePrefix), arch => arch.Types.MustHavePrefix("I")),
            TypeVerb(nameof(SelectionConstraints.MustHaveNameMatching), arch => arch.Types.MustHaveNameMatching("*Repo*")),
            TypeVerb(nameof(SelectionConstraints.MustImplement), arch => arch.Types.MustImplement(typeof(IBillingFacade))),
            TypeVerb(nameof(SelectionConstraints.MustDeriveFrom), arch => arch.Types.MustDeriveFrom(typeof(ControllerBase))),
            TypeVerb(nameof(SelectionConstraints.MustBeAttributedWith),
                arch => arch.Types.MustBeAttributedWith(typeof(ApiControllerAttribute))),
            TypeVerb(nameof(SelectionConstraints.MustNotImplement),
                arch => arch.Types.MustNotImplement(typeof(IBillingFacade), typeof(IHandler<>))),
            TypeVerb(nameof(SelectionConstraints.MustNotDeriveFrom), arch => arch.Types.MustNotDeriveFrom(typeof(ControllerBase))),
            TypeVerb(nameof(SelectionConstraints.MustNotBeAttributedWith),
                arch => arch.Types.MustNotBeAttributedWith(typeof(ApiControllerAttribute), typeof(SerializableAttribute))),
            TypeVerb(nameof(SelectionConstraints.MustBeSealed), arch => arch.Types.MustBeSealed()),
            TypeVerb(nameof(SelectionConstraints.MustBeStatic), arch => arch.Types.MustBeStatic()),
            TypeVerb(nameof(SelectionConstraints.MustBeAbstract), arch => arch.Types.MustBeAbstract()),
            TypeVerb(nameof(SelectionConstraints.MustBePublic), arch => arch.Types.MustBePublic()),
            TypeVerb(nameof(SelectionConstraints.MustBeInternal), arch => arch.Types.MustBeInternal()),
            TypeVerb(nameof(SelectionConstraints.Must),
                arch => arch.Types.Must(type => type.Name.Length <= 40, description: "keep type names at or under 40 characters"))
        ];
    }

    private static VocabularyRow[] ProjectionRows()
    {
        return
        [
            Projection(nameof(Selection.Members), arch => arch.Types.Members),
            Projection(nameof(Selection.Methods), arch => arch.Types.Methods),
            Projection(nameof(Selection.Properties), arch => arch.Types.Properties),
            Projection(nameof(Selection.Fields), arch => arch.Types.Fields),
            Projection(nameof(Selection.Events), arch => arch.Types.Events)
        ];
    }

    private static VocabularyRow[] MemberAdjectiveRows()
    {
        return
        [
            MemberAdjective(nameof(MemberSelectionAdjectives.WithSuffix), arch => arch.Types.Methods.WithSuffix("Async")),
            MemberAdjective(nameof(MemberSelectionAdjectives.WithPrefix), arch => arch.Types.Methods.WithPrefix("Get")),
            MemberAdjective(nameof(MemberSelectionAdjectives.WithNameMatching), arch => arch.Types.Methods.WithNameMatching("*Handler*")),
            MemberAdjective(nameof(MethodSelection.Returning), arch => arch.Types.Methods.Returning(typeof(Task))),
            MemberAdjective(nameof(MemberSelectionAdjectives.AttributedWith),
                arch => arch.Types.Methods.AttributedWith(typeof(ApiControllerAttribute))),
            MemberAdjective(nameof(MemberSelectionAdjectives.ThatAreStatic), arch => arch.Types.Fields.ThatAreStatic()),
            MemberAdjective(nameof(MemberSelectionAdjectives.Where),
                arch => arch.Types.Methods.Where(member => member.IsAsync, description: "that are async"))
        ];
    }

    private static VocabularyRow[] MemberVerbRows()
    {
        return
        [
            MemberVerb(nameof(MemberSelectionConstraints.MustHaveSuffix), arch => arch.Types.Methods.MustHaveSuffix("Async")),
            MemberVerb(nameof(MemberSelectionConstraints.MustHavePrefix), arch => arch.Types.Methods.MustHavePrefix("Get")),
            MemberVerb(nameof(MemberSelectionConstraints.MustHaveNameMatching), arch => arch.Types.Methods.MustHaveNameMatching("*Async")),
            MemberVerb(nameof(MemberSelectionConstraints.MustBePublic), arch => arch.Types.Methods.MustBePublic()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBeInternal), arch => arch.Types.Methods.MustBeInternal()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBePrivate), arch => arch.Types.Methods.MustBePrivate()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBeStatic), arch => arch.Types.Methods.MustBeStatic()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBeAbstract), arch => arch.Types.Methods.MustBeAbstract()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBeVirtual), arch => arch.Types.Methods.MustBeVirtual()),
            MemberVerb(nameof(MemberSelectionConstraints.MustBeAttributedWith),
                arch => arch.Types.Methods.MustBeAttributedWith(typeof(ApiControllerAttribute))),
            MemberVerb(nameof(MemberSelectionConstraints.MustNotBeAttributedWith),
                arch => arch.Types.Methods.MustNotBeAttributedWith(typeof(ApiControllerAttribute), typeof(SerializableAttribute))),
            MemberVerb(nameof(MethodSelectionConstraints.MustAcceptParameter),
                arch => arch.Types.Methods.MustAcceptParameter(typeof(CancellationToken))),
            MemberVerb(nameof(PropertySelectionConstraints.MustBeGetOnly), arch => arch.Types.Properties.MustBeGetOnly()),
            MemberVerb(nameof(FieldSelectionConstraints.MustBeReadonly), arch => arch.Types.Fields.MustBeReadonly()),
            MemberVerb(nameof(MemberSelectionConstraints.Must),
                arch => arch.Types.Methods.Must(member => member.IsAsync, description: "return a Task"))
        ];
    }

    private static VocabularyRow[] ProjectNounRows()
    {
        return
        [
            ProjectNoun(nameof(Arch.Projects), arch => arch.Projects)
        ];
    }

    private static VocabularyRow[] ProjectAdjectiveRows()
    {
        return
        [
            ProjectAdjective(nameof(ProjectSelectionAdjectives.Named), arch => arch.Projects.Named("MyApp.Web")),
            ProjectAdjective(nameof(ProjectSelectionAdjectives.Matching), arch => arch.Projects.Matching("MyApp.*")),
            ProjectAdjective(nameof(ProjectSelectionAdjectives.Packable), arch => arch.Projects.Packable()),
            ProjectAdjective(nameof(ProjectSelectionAdjectives.Except),
                arch => arch.Projects.Except(arch.Projects.Named("MyApp.Tests"))),
            ProjectAdjective(nameof(ProjectSelectionAdjectives.Where),
                arch => arch.Projects.Where(project => project.TargetFrameworks.Contains("net8.0"), description: "that target net8.0"))
        ];
    }

    private static VocabularyRow[] ProjectVerbRows()
    {
        return
        [
            ProjectVerb(nameof(ProjectSelectionConstraints.MustOnlyTarget),
                arch => arch.Projects.Named("MyApp.Web").MustOnlyTarget("netstandard2.0")),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustTarget),
                arch => arch.Projects.Packable().MustTarget("netstandard2.0")),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustReferenceNoPackages),
                arch => arch.Projects.Named("MyApp.Web").MustReferenceNoPackages()),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustOnlyReferencePackages),
                arch => arch.Projects.Named("MyApp.Domain").MustOnlyReferencePackages("Microsoft.Bcl.*")),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustNotReferencePackages),
                arch => arch.Projects.Matching("MyApp.*").MustNotReferencePackages("Newtonsoft.Json")),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustLockPackages), arch => arch.Projects.Packable().MustLockPackages()),
            ProjectVerb(nameof(ProjectSelectionConstraints.MustNotBePackable), arch => arch.Projects.Matching("MyApp.*").MustNotBePackable()),
            ProjectVerb(nameof(ProjectSelectionConstraints.Must),
                arch => arch.Projects.Named("MyApp.Web").Must(project => project.IsPackable == true, description: "produce a package"))
        ];
    }

    // The family the noun row shows and the two cell verbs speak over: "the others" needs cells to name.
    private static Selection LayerFamily(Arch arch)
    {
        return arch.Each(arch.Layer("Host", "MyApp.Host.*"), arch.Layer("Adapter", "MyApp.Adapter.*"), arch.Layer("Pack", "MyApp.Pack.*"));
    }

    private static VocabularyRow TypeNoun(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Types, PartOfSpeech.Noun, name, build);
    }

    private static VocabularyRow TypeAdjective(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Types, PartOfSpeech.Adjective, name, build);
    }

    private static VocabularyRow TypeVerb(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Types, PartOfSpeech.Verb, name, build);
    }

    private static VocabularyRow Projection(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Types, PartOfSpeech.Projection, name, build);
    }

    private static VocabularyRow MemberAdjective(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Members, PartOfSpeech.Adjective, name, build);
    }

    private static VocabularyRow MemberVerb(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Members, PartOfSpeech.Verb, name, build);
    }

    private static VocabularyRow ProjectNoun(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Projects, PartOfSpeech.Noun, name, build);
    }

    private static VocabularyRow ProjectAdjective(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Projects, PartOfSpeech.Adjective, name, build);
    }

    private static VocabularyRow ProjectVerb(string name, Func<Arch, object> build)
    {
        return new VocabularyRow(VocabularyStratum.Projects, PartOfSpeech.Verb, name, build);
    }
}
