using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     Scoped placement: the scoped selection is evaluated in Subject position and its
///     types' declaration sites collapse to their deepest common ancestor directory, or to a null
///     directory with a skip reason when the scope matches no types. No MSBuild —
///     paths are synthesized through <see cref="CompilationFactory" />.
/// </summary>
/// <remarks>
///     Each posture places exactly one card per scope, and the single-item assertions are where that is
///     pinned: a quarantine has two children and only its containment bears the card, while a caution's
///     tripwire is both its only child and its card-bearer.
/// </remarks>
public class ScopedContextResolverTests
{
    // A hermetic quarantined scope over the billing namespace (no BoundaryOnlyVia needed to place it).
    private static ArchitectureModel Model()
    {
        return Checker.Model(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding is structural.")
                .Because("Replacement scheduled; not worth stabilizing."));
    }

    // The same shape under the other posture: one child, so the card can only come off the tripwire.
    private static ArchitectureModel CautionModel()
    {
        return Checker.Model(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .Dragons("Argument order is structural.")
                .Because("The helpers are public API for the whole solution."));
    }

    // Every scene resolves inside the shared solution directory, which the scenes' codebases declare their files under.
    private static IReadOnlyList<ScopePlacement> Resolve(ArchitectureModel model, CodebaseModel codebase)
    {
        return ScopedContextResolver.Resolve(
            model, new SelectionEvaluator(codebase), new DirectoryPlacement.Boundary(ContextFixtures.SolutionDirectory));
    }

    [Fact]
    public void Resolve_ScopeCoLocatedInOneDirectory_PicksThatDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            (ContextFixtures.Site("MyApp.Legacy.Billing", "BillingCalculator.cs"),
                "namespace MyApp.Legacy.Billing; public class BillingCalculator {}"),
            (ContextFixtures.Site("MyApp.Legacy.Billing", "RoundingMode.cs"), "namespace MyApp.Legacy.Billing; public class RoundingMode {}"));

        IReadOnlyList<ScopePlacement> placements = Resolve(Model(), codebase);

        ScopePlacement placement = placements.ShouldHaveSingleItem();
        placement.ScopeId.ShouldBe("legacy/billing");
        placement.Rule.Id.ShouldBe("legacy/billing/containment");
        ContextFixtures.Relative(placement.Destination.DirectoryPath)
            .ShouldBe("MyApp.Legacy.Billing");
        placement.Destination.SkipReason.ShouldBeNull();
    }

    [Fact]
    public void Resolve_SingleFileScope_PicksTheContainingDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Legacy.Billing",
            (ContextFixtures.Site("src", "MyApp.Legacy.Billing", "Only.cs"), "namespace MyApp.Legacy.Billing; public class Only {}"));

        ScopePlacement placement = Resolve(Model(), codebase)[0];

        ContextFixtures.Relative(placement.Destination.DirectoryPath)
            .ShouldBe("src/MyApp.Legacy.Billing");
    }

    [Fact]
    public void Resolve_ScopeScatteredAcrossProjects_PicksTheCommonRoot()
    {
        CodebaseModel codebase = CodebaseExtractor.ExtractFromCompilations([
            CompilationFactory.Compile("ProjA",
                (ContextFixtures.Site("src", "ProjA", "Calc.cs"), "namespace MyApp.Legacy.Billing; public class Calc {}")),
            CompilationFactory.Compile("ProjB",
                (ContextFixtures.Site("src", "ProjB", "Facade.cs"), "namespace MyApp.Legacy.Billing; public class Facade {}"))
        ]);

        ScopePlacement placement = Resolve(Model(), codebase)[0];

        ContextFixtures.Relative(placement.Destination.DirectoryPath)
            .ShouldBe("src");
    }

    [Fact]
    public void Resolve_CautionedScopeOverOneFile_PlacesOneCardOnItsContainingDirectory()
    {
        CodebaseModel codebase = CompilationFactory.Extract("MyApp.Shared",
            (ContextFixtures.Site("src", "MyApp.Shared", "Helpers.cs"), "namespace MyApp.Shared; public class Helpers {}"));

        ScopePlacement placement = Resolve(CautionModel(), codebase)
            .ShouldHaveSingleItem();

        placement.ScopeId.ShouldBe("shared/utilities");
        placement.Rule.Id.ShouldBe("shared/utilities/tripwire");
        ContextFixtures.Relative(placement.Destination.DirectoryPath)
            .ShouldBe("src/MyApp.Shared");
        placement.Destination.SkipReason.ShouldBeNull();
    }

    [Fact]
    public void Resolve_ScopeMatchingNoTypes_ReturnsNullDirectoryWithSkipReason()
    {
        ScopePlacement placement = Resolve(Model(), ContextFixtures.WebCodebase)[0];

        placement.Destination.DirectoryPath.ShouldBeNull();
        placement.Destination.SkipReason.ShouldBe("scope 'legacy/billing' matched no types; no scoped context emitted");
    }
}
