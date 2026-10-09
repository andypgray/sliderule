using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Checking;

namespace SlideRule.Tests.Checking;

/// <summary>
///     A rule's exemplar at check time: resolved against the solution after the verdict and never part of it.
///     A live one hands back the types it names with their declaration sites; a dead part, or an exemplar that
///     cannot be evaluated, warns on the rule and leaves every count the verdict produced as it was.
/// </summary>
public sealed class ExemplarResolutionTests
{
    private const string NameCure =
        "A name in a `Named` list is the whole simple name, matched exactly, without its namespace, generic "
        + "arity or containing type; the verdict never depends on it, so point it at a type the solution "
        + "declares or remove it.";

    private const string NamespaceCure =
        "A trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses a "
        + "dot; the verdict never depends on it, so point it at a type the solution declares or remove it.";

    // A stand-in for the sentence a partial model composes; its wording is pinned where it is minted.
    private const string PartialModelCure = "THE-PARTIAL-MODEL-CURE";

    private const string FaultPrefix = "This rule's exemplar could not be resolved: ";

    // One repository split over two partial declarations, and a service beside it: the shapes a pointer at
    // "the type to copy" lands on in practice.
    private const string PartialRepository = """
                                             namespace App.Data
                                             {
                                                 public partial class OrdersRepository {}
                                                 public partial class OrdersRepository { public void Save() {} }
                                                 public class OrderService {}
                                             }
                                             """;

    [Fact]
    public void ALiveExemplar_ResolvesItsTypesWithTheirSites()
    {
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Types.Named("Model"));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassedClean();
        result.ShouldHaveResolvedExemplar("App.Domain.Model");
        result.Exemplar
            .Single()
            .DeclarationSites
            .ShouldNotBeEmpty();
    }

    [Fact]
    public void ARuleWithNoExemplar_ResolvesNothing()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/web")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
        result.ShouldHaveResolvedExemplar();
    }

    [Fact]
    public void ADeadExemplar_WarnsWhileTheRuleStaysGreenAndPromotable()
    {
        // A captured baseline with nothing left in it is burned to zero: the rule is offered for promotion.
        // The dead pointer must not take that away, because the verdict it sits beside was measured in full.
        RuleResult result = Checker.Run(Sources.CleanWebBesideDataModel, Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b")
                    .Exemplar(arch.Types.Named("OrdersRepository")))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedDeadExemplars("types named `OrdersRepository`");
        result.ShouldSatisfyAllConditions(
            () => result.SelectionMatchedNothing.ShouldBeFalse(),
            () => result.MeasuredInFull.ShouldBeTrue(),
            () => result.Promotable.ShouldBeTrue());
    }

    [Fact]
    public void ADeadExemplar_LeavesAStaleEntryCountedAsFixed()
    {
        // The baseline verb refuses a rule that was not measured in full and status calls its unmatched
        // entries untested; a dead exemplar makes it neither, so the entry is debt fixed and awaiting acceptance.
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, index, arch =>
            arch.Rule("data/x")
                .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("b")
                .Exemplar(arch.Types.Named("OrdersRepository")));
        RuleResult result = report.Single();

        result.ShouldHaveWarnedDeadExemplars("types named `OrdersRepository`");
        result.ShouldHaveStale(1);
        report.ShouldSatisfyAllConditions(
            () => report.StaleBaselineEntryCount.ShouldBe(1),
            () => report.UnmeasuredBaselineEntryCount.ShouldBe(0),
            () => report.WarningCount.ShouldBe(1));
    }

    [Fact]
    public void ADeadExemplarOnARedRule_WarnsBesideTheViolations()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/domain")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b")
                    .Exemplar(arch.Types.Named("Ghost")))
            .Single();

        result.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "App.Domain.Apple -> App.Web.Controller", "App.Domain.Service -> App.Web.Controller",
                "App.Domain.Service -> App.Web.Helper", "App.Domain.Zebra -> App.Web.Controller"
            ]);
        result.ShouldHaveWarnedDeadExemplars("types named `Ghost`");
        result.Warnings
            .Single()
            .Hint
            .ShouldBe(NameCure);
    }

    [Fact]
    public void EachDeadNameOfANamedList_WarnsOnItsOwn()
    {
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Types.Named("Ghost", "Model", "Phantom"));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveResolvedExemplar("App.Domain.Model");
        result.ShouldHaveWarnedDeadExemplars("types named `Ghost`", "types named `Phantom`");
        result.Warnings.ShouldAllBe(warning => warning.Hint == NameCure);
    }

    [Fact]
    public void EachDeadUnionOperand_WarnsOnItsOwnTerms()
    {
        Action<Arch> define = WebRuleWithExemplar(arch =>
            arch.AnyOf(arch.Types.Named("Model"), arch.Namespace("App.Ghost.*")));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveResolvedExemplar("App.Domain.Model");
        result.ShouldHaveWarnedDeadExemplars("types in `App.Ghost.*`");
        result.Warnings
            .Single()
            .Hint
            .ShouldBe(NamespaceCure);
    }

    [Fact]
    public void AUnionItsOwnAdjectiveEmpties_WarnsOnceOnTheWholeExemplar()
    {
        // Every operand matched, so no operand is the dead part: the union's own adjective emptied it, and the
        // only thing to quote is the whole exemplar.
        Action<Arch> define = WebRuleWithExemplar(arch =>
            arch.AnyOf(arch.Types.Named("Model"), arch.Namespace("App.Web.*")).InNamespace("App.Ghost.*"));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveResolvedExemplar();
        result.ShouldHaveWarnedDeadExemplars("types named `Model` or types in `App.Web.*` in `App.Ghost.*`");
    }

    [Fact]
    public void ADeadPartSpelledTwice_WarnsOnce()
    {
        // The dead name of the first operand and the whole second operand read alike, and a host keys on that
        // reading: two warnings would be one finding counted twice.
        Action<Arch> define = WebRuleWithExemplar(arch =>
            arch.AnyOf(arch.Types.Named("Model", "Ghost"), arch.Types.Named("Ghost")));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHaveWarnedDeadExemplars("types named `Ghost`");
    }

    [Fact]
    public void ATypeofExemplarTheSolutionDoesNotDeclare_Warns()
    {
        // Silent as a forbidden target, where absence is the rule working; here absence is a pointer at
        // nothing, so it warns like any other dead part.
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Type(typeof(Uri)));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveResolvedExemplar();
        result.ShouldHaveWarnedDeadExemplars("`Uri`");
        result.Warnings
            .Single()
            .Hint
            .ShouldBe(
                "A typeof anchor reaches only a type this solution declares, never one supplied by a package or by "
                + "the spec project itself; the verdict never depends on it, so point it at a type the solution "
                + "declares or remove it.");
    }

    [Fact]
    public void AClosedGenericExemplar_WarnsInTheFaultFormAndRaisesNoRuleError()
    {
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Type(typeof(List<int>)));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveResolvedExemplar();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(CheckWarningKind.DeadExemplar),
            () => warning.Message.ShouldStartWith(FaultPrefix + "`List<Int32>` is a closed generic construction; "),
            () => warning.Hint.ShouldBeNull(),
            () => warning.Key.ShouldBe(result.Rule.Exemplar));
    }

    [Fact]
    public void AThrowingWhereInTheExemplar_WarnsInTheFaultForm()
    {
        Action<Arch> define = WebRuleWithExemplar(arch =>
            arch.Types.Named("Model").Where(_ => throw new InvalidOperationException("boom"), "that explode"));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem();
        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(CheckWarningKind.DeadExemplar),
            () => warning.Message.ShouldBe(
                FaultPrefix + "the `Where` predicate threw InvalidOperationException on `App.Domain.Model`: boom"),
            () => warning.Hint.ShouldBeNull());
    }

    [Fact]
    public void AFaultOnARedRule_KeepsTheRedVerdict()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/domain")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b")
                    .Exemplar(arch.Type(typeof(List<int>))))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldAllBe(violation => violation.Kind == ViolationKind.Reference);
        result.Warnings
            .ShouldHaveSingleItem()
            .Message
            .ShouldStartWith(FaultPrefix);
    }

    [Fact]
    public void UnderOneProject_TheExemplarIsNeverResolved()
    {
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Types.Named("Model", "Ghost"));

        RuleResult result = Checker.Run(Sources.LayeredModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, define)
            .Single();

        result.ShouldHaveResolvedExemplar();
        result.Warnings.ShouldNotContain(warning => warning.Kind == CheckWarningKind.DeadExemplar);
    }

    [Fact]
    public void UnderASolutionFilter_TheTypesResolveAndADeadPartIsSilent()
    {
        // A project the filter left out may declare the type, so a part that matched nothing here is no
        // evidence the pointer went stale.
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Types.Named("Model", "Ghost"));

        RuleResult result = Checker.Run(Sources.LayeredModel, BaselineIndex.Empty, Checker.FilterNarrowing, define)
            .Single();

        result.ShouldHavePassedClean();
        result.ShouldHaveResolvedExemplar("App.Domain.Model");
    }

    [Fact]
    public void ARuleTheFilterSkipped_StillCarriesItsExemplar()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, BaselineIndex.Empty, Checker.FilterNarrowing, arch =>
                arch.Rule("layering/nowhere")
                    .Enforce(arch.Namespace("App.Nowhere.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b")
                    .Exemplar(arch.Types.Named("Model")))
            .Single();

        result.ShouldHaveSkipped(Checker.FilterSkipReason);
        result.ShouldHaveResolvedExemplar("App.Domain.Model");
    }

    [Fact]
    public void UnderAPartialModel_TheHintIsThePartialModelsCure()
    {
        Action<Arch> define = WebRuleWithExemplar(arch => arch.Types.Named("Ghost"));

        RuleResult result = Checker.Run(Sources.LayeredModel, new IncompleteModel(PartialModelCure), define)
            .Single();

        result.ShouldHaveWarnedDeadExemplars("types named `Ghost`");
        result.Warnings
            .Single()
            .Hint
            .ShouldBe(PartialModelCure);
    }

    [Fact]
    public void APartialType_ResolvesOnceWithEveryDeclarationSite()
    {
        RuleResult result = Checker.Run(PartialRepository, arch =>
                arch.Rule("data/public-services")
                    .Enforce(arch.Types.Named("OrderService").MustBePublic())
                    .Because("b")
                    .Exemplar(arch.Types.Named("OrdersRepository")))
            .Single();

        result.ShouldHavePassedClean();
        result.ShouldHaveResolvedExemplar("App.Data.OrdersRepository");
        result.Exemplar
            .Single()
            .DeclarationSites
            .Select(site => site.Line)
            .ShouldBe([3, 4]);
    }

    [Fact]
    public void AUnion_ResolvesEveryOperandsTypesInFullNameOrder()
    {
        Action<Arch> define = WebRuleWithExemplar(arch =>
            arch.AnyOf(arch.Types.Named("Service"), arch.Types.Named("Model")));

        RuleResult result = Checker.Run(Sources.LayeredModel, define)
            .Single();

        result.ShouldHavePassedClean();
        result.ShouldHaveResolvedExemplar("App.Domain.Model", "App.Domain.Service");
    }

    [Fact]
    public void ARuleAtItsBaseline_WhoseExemplarWasRenamedAway_KeepsItsVerdictAndWarns()
    {
        // The case the trailer exists for: the type the fix points at is renamed, the spec is not. The debt the
        // baseline records is still held, the rule still passes, and the next check names the stranded pointer
        // with its cure.
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "Controllers open the data layer directly.",
                        arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("Repository pattern for testability.")
                    .Fix("Move the data access into a repository.")
                    .Exemplar(arch.Types.Named("BookingRepository")))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveWarnedDeadExemplars("types named `BookingRepository`");
        result.Warnings
            .Single()
            .ShouldSatisfyAllConditions(
                warning => warning.Message.ShouldBe(
                    "This rule's exemplar \"types named `BookingRepository`\" matched no solution-declared types."),
                warning => warning.Hint.ShouldBe(NameCure));
    }

    [Fact]
    public void TheRuleCarriesItsExemplarAsTheSpecNamesIt()
    {
        Checker.Model(arch => arch.Rule("data/x")
                .Enforce(arch.Types.MustHavePrefix("I"))
                .Because("b")
                .Exemplar(arch.Types.Named("BookingRepository")))
            .Rule("data/x")
            .Exemplar
            .ShouldBe("types named `BookingRepository`");
    }

    // The rule most rows here hang their exemplar on, a web ban the layered model keeps: the exemplar a row
    // passes is then the whole of what it varies.
    private static Action<Arch> WebRuleWithExemplar(Func<Arch, Selection> exemplar)
    {
        return arch =>
            arch.Rule("layering/web")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Domain.*")))
                .Because("b")
                .Exemplar(exemplar(arch));
    }
}
