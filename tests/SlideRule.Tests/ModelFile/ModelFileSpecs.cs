using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Stubs;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     The one inline spec the composer, round-trip and reader rows are all written against, and the
///     project list they pass by hand.
/// </summary>
/// <remarks>
///     It is small enough to pin whole and wide enough to be worth pinning: two layers, one by glob with
///     a purpose and one by a selection; a subject narrowed by <c>Except</c> against a union that mixes a
///     bare layer with a namespace; a member rule carrying a <c>Returning</c> anchor list; a banned
///     member; and one rule whose subject narrows by a predicate, so the omitted list is never empty.
/// </remarks>
internal static class ModelFileSpecs
{
    private const string SpecName = "MyApp architecture";

    internal static readonly IReadOnlyList<ModelDocumentProject> Projects =
    [
        new("MyApp.Web", ["MyApp.Web"]),
        new("MyApp.Domain", ["MyApp.Domain"])
    ];

    private static ArchitectureModel Model()
    {
        return Checker.Model(Define);
    }

    internal static ModelDocument Document()
    {
        return ModelDocument.From(Model(), SpecName, Projects);
    }

    internal static ModelDocument DocumentOf(Action<Arch> define)
    {
        return ModelDocument.From(Checker.Model(define), SpecName, Projects);
    }

    internal static string TextOf(Action<Arch> define)
    {
        return ModelDocumentComposer.Compose(DocumentOf(define));
    }

    internal static string SampleText()
    {
        return ModelDocumentComposer.Compose(Document());
    }

    /// <summary>A quarantine scope, which desugars to a containment rule and the tripwire beside it.</summary>
    internal static void DefineScope(Arch arch)
    {
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(arch.Type(typeof(IBillingFacade)))
            .Dragons("Rounding happens per line item.")
            .Because("b");
    }

    /// <summary>
    ///     The node shapes the sample spec and the canonical one between them leave out: a migration with
    ///     a citation, both registration nouns, the project stratum, and the head-placed adjectives.
    /// </summary>
    internal static void DefineAssorted(Arch arch)
    {
        arch.Rule("area/migrating")
            .Migrate("Controllers open connections directly.", arch.Types.WithSuffix("Controller")
                .MustBeSealed())
            .Because("b")
            .Fix("Seal it.")
            .Citation("https://learn.microsoft.com/dotnet")
            .Baseline("arch/baseline.json")
            .WhileYoureThere(MigrationPolicy.NeverMigrate);

        arch.Rule("area/singletons")
            .Enforce(arch.Registered(Lifetime.Singleton)
                .MustBeSealed())
            .Because("b");

        arch.Rule("area/registrations")
            .Enforce(arch.Registered()
                .MustBeInternal())
            .Because("b");

        arch.Rule("area/packages")
            .Enforce(arch.Projects.Matching("MyApp.*")
                .Except(arch.Projects.Named("MyApp.Tests"))
                .MustLockPackages())
            .Because("b");

        arch.Rule("area/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface)
                .Authored()
                .MustHavePrefix("I"))
            .Because("b");

        arch.Rule("area/static-members")
            .Enforce(arch.Types.Methods.WithSuffix("Async")
                .ThatAreStatic()
                .MustBePublic())
            .Because("b");
    }

    /// <summary>A family subject, which is the one noun whose cells are layers by name.</summary>
    internal static void DefineFamily(Arch arch)
    {
        Layer alpha = arch.Layer("Alpha", "MyApp.Alpha.*");
        Layer beta = arch.Layer("Beta", "MyApp.Beta.*");

        arch.Rule("layering/leaves-independent")
            .Enforce(arch.Each(alpha, beta)
                .MustNotReferenceEachOther())
            .Because("Leaves are independent.");
    }

    private static void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*")
            .Purpose("What the business knows.");
        Layer handlers = arch.Layer("Handlers", arch.Types.WithSuffix("Handler"));

        arch.Rule("layering/domain-independent")
            .Enforce(domain.Except(arch.Types.WithSuffix("Dto"))
                .MustNotReference(arch.AnyOf(handlers, arch.Namespace("MyApp.Web.*"))))
            .Because("The domain must not know the web.")
            .Fix("Move the type into the Domain layer.");

        arch.Rule("api/handlers-are-public")
            .Enforce(handlers.Methods.Returning(typeof(string))
                .MustBePublic())
            .Because("A handler nobody can call is dead code.");

        arch.Rule("time/no-ambient-clock")
            .Enforce(domain.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .Because("Ambient time cannot be tested.");

        arch.Rule("naming/long-names-are-sealed")
            .Enforce(arch.Types.Where(t => t.Name.Length > 3, description: "whose name is long")
                .MustBeSealed())
            .Because("A predicate is code, not data.");
    }
}
