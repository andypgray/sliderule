using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The MSBuild tier of the shadowed-name split, over the real <c>ShadowedApp</c> fixture solution: a
///     product project binding <c>Microsoft.Extensions.DependencyInjection.ServiceDescriptor</c> from the
///     package, and a test project beside it declaring its own type of that full name. The fast-path bed in
///     <c>Extraction.PackageShadowedNameTests</c> pins the merge; this pins the half only a real workspace
///     can, that a project's assembly name reaches the fragment from its csproj at all — the fact the whole
///     split is decided by, and the one a hand-built input can never exercise. The product's package reference
///     is <c>PrivateAssets="all"</c>, which is the bed's premise — it is what keeps the package from reaching
///     the test project — so this is also where the private-reference fact is read back through a real
///     workspace and checked against <c>MustReferenceNoPackages</c>.
/// </summary>
public sealed class ShadowedPackageNameTests
{
    private const string Product = "ShadowedApp.Product";
    private const string Tests = "ShadowedApp.Product.Tests";
    private const string Shadowed = "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";

    private static readonly string SolutionPath = FixtureBeds.ShadowedAppSolution;

    private static Task<CodebaseModel> ShadowedCodebase => FixtureCodebase.Of(SolutionPath);

    [Fact]
    public async Task ExtractFromSolutionAsync_TheProductBindsThePackage_ReachesNoTypeOfTheTestProject()
    {
        CodebaseModel model = await ShadowedCodebase;

        // The guarantee stated as an absence: nothing the product references is attributed to the test
        // project, whatever names the two happen to share.
        model.Edges
            .Where(edge => edge.Source.ProjectName == Product)
            .Select(edge => edge.Target.ProjectName)
            .ShouldNotContain(Tests);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheShadowedName_CarriesTheDeclarationAndTheAssembly()
    {
        CodebaseModel model = await ShadowedCodebase;

        model.Types
            .Where(type => type.FullName == Shadowed)
            .Select(type => $"{type.ProjectName} external={type.IsExternal}")
            .ShouldBe([$"{Tests} external=False", "Microsoft.Extensions.DependencyInjection.Abstractions external=True"]);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheTestProjectUsesItsOwnStandIn_ReachesItsOwnDeclaration()
    {
        CodebaseModel model = await ShadowedCodebase;

        model.Edge("ShadowedApp.Product.Tests.RegistryTests", Shadowed)
            .Target.ProjectName.ShouldBe(Tests);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheGenuineProjectReference_Survives()
    {
        CodebaseModel model = await ShadowedCodebase;

        // The control the fixture's csproj comment names. A project reference records an external too, of the
        // referenced project's assembly, so a split decided on the wrong comparison would take this out.
        model.Edge("ShadowedApp.Product.Tests.RegistryTests", "ShadowedApp.Product.Registry")
            .Target.ProjectName.ShouldBe(Product);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheShadowedName_IsReportedAsAMergeNote()
    {
        CodebaseModel model = await ShadowedCodebase;

        model.MergeNotes.ShouldBe([
            $"Project '{Tests}' declares '{Shadowed}', which referenced assembly "
            + "'Microsoft.Extensions.DependencyInjection.Abstractions' also supplies; every reference resolves "
            + "to whichever of the two the referencing compilation bound, so a selection over project "
            + $"'{Tests}' reaches the declared one alone."
        ]);
    }

    [Fact]
    public async Task Summarize_TheShadowedName_IsStatedAsACoverageKey()
    {
        CodebaseModel model = await ShadowedCodebase;

        ShadowedTypeSummary shadowed = GraphSummarizer.Summarize(model)
            .ShadowedTypes.ShouldHaveSingleItem();

        shadowed.ShouldSatisfyAllConditions(
            () => shadowed.Type.ShouldBe(Shadowed),
            () => shadowed.DeclaredBy.ShouldBe(Tests),
            () => shadowed.SuppliedBy.ShouldBe(["Microsoft.Extensions.DependencyInjection.Abstractions"]),
            () => shadowed.BoundFromAssemblyBy.ShouldBe([Product]));
    }

    [Fact]
    public async Task Check_ProductMustNotReferenceTests_PassesOverTheRealWorkspace()
    {
        CodebaseModel model = await ShadowedCodebase;

        // The rule the field test wrote, over the real workspace. Green alone would prove nothing — an
        // empty subject or target set is green too — so the row below reds on the same two selections
        // inverted, which is only possible if both are live.
        Checker.Run(model, arch =>
                arch.Rule("layering/product-must-not-reference-tests")
                    .Enforce(arch.Project(Product).MustNotReference(arch.Project(Tests)))
                    .Because("Shipping code must not depend on test assemblies."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(model, arch =>
                arch.Rule("layering/inverted")
                    .Enforce(arch.Project(Tests).MustNotReference(arch.Project(Product)))
                    .Because("The inverse, so neither selection can be silently empty."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(
                ViolationKind.Reference, "ShadowedApp.Product.Tests.RegistryTests", "ShadowedApp.Product.Registry");
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_TheProductsPrivateAssetsReference_IsPrivate()
    {
        CodebaseModel model = await ShadowedCodebase;

        // The same PrivateAssets that keeps the package out of the test project keeps it out of the product's
        // own package, so the evaluated reference reads private, sited in the product's project file.
        PackageReference package = model.Project(Product)
            .PackageReferences.ShouldHaveSingleItem();

        package.ShouldSatisfyAllConditions(
            () => package.Name.ShouldBe("Microsoft.Extensions.DependencyInjection.Abstractions"),
            () => package.IsPrivate.ShouldBeTrue(),
            () => package.Site.FilePath.ShouldEndWith("ShadowedApp.Product.csproj"));
    }

    [Fact]
    public async Task Check_MustReferenceNoPackages_PassesTheProductsPrivateReference()
    {
        CodebaseModel model = await ShadowedCodebase;

        // Green alone would prove nothing — a project with no package at all is green too — so the strict
        // form below reds on the same project, which it can only do if the reference is really there.
        Checker.Run(model, arch =>
                arch.Rule("packaging/product-ships-no-dependencies")
                    .Enforce(arch.Projects.Named(Product).MustReferenceNoPackages())
                    .Because("The product's package must not impose a dependency on the solutions that take it."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(model, arch =>
                arch.Rule("packaging/product-declares-nothing")
                    .Enforce(arch.Projects.Named(Product)
                        .Must(project => project.PackageReferences.Count == 0, description: "declare no package references at all"))
                    .Because("The strict form, so the reference the verb passes cannot be silently absent."))
            .Single()
            .ShouldHaveFailedWithProjectSubjects([Product]);
    }
}
