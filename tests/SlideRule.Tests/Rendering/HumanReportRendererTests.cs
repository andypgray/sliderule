using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Rendering;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The human failure-text renderer (<see cref="HumanReportRenderer" />, the shared CLI + xUnit-adapter
///     surface): each violation arm's line and its ordering, the lines a rule block carries beneath its
///     violations, what a block withholds from a rule that reached no verdict about the code, and the
///     <c>Render</c> summary tail. Pinned strings are the spec.
/// </summary>
public sealed class HumanReportRendererTests
{
    private const string AsyncSource = """
                                       namespace App.Async
                                       {
                                           using System.Threading.Tasks;
                                           public class HomeController
                                           {
                                               public Task<int> Load() => Task.FromResult(0);
                                           }
                                       }
                                       """;

    private static readonly CodebaseModel AsyncModel = CompilationFactory.Extract(AsyncSource);

    // The shared/utilities caution both the fired and the silent row are about — the same scope, so the
    // only difference between them is whether a warning reached the block.
    private static readonly ArchRule SharedUtilitiesTripwire = Checker.Tripwire(arch =>
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("MyApp.Shared.*"))
            .Dragons("Argument order is structural.")
            .Because("Every caller passes them positionally."));

    [Fact]
    public void RuleBlock_RuleError_RendersErrorPrefixedDetailLine()
    {
        // A hand-built model bypasses spec-build's closed-generic refusal (GRAMMAR §8 item 14) to reach the
        // checker's RuleError backstop; the renderer's RuleError arm prefixes the detail with "error: ".
        var arch = new Arch();
        Constraint constraint = arch.Namespace("App.Async.*").Methods.Returning(typeof(Task<int>))
            .MustHaveSuffix("Async");
        ArchitectureModel model = Checker.HandBuilt("naming/x", constraint);
        RuleResult result = Harness.Check(model, AsyncModel)
            .Single();

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.RuleError);
        block.ShouldContain(
            "error: `Task<Int32>` is a closed generic construction; member return-type matching is definition-level. " +
            "Anchor on the open definition instead.");
    }

    [Fact]
    public void RuleBlock_EmptySubject_RendersMatchedNoTypesDetailLine()
    {
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("naming/x")
                    .Enforce(arch.Namespace("Nowhere.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.EmptySubject);
        block.ShouldContain("The subject selection matched no solution-declared types.");
    }

    [Fact]
    public void RuleBlock_EmptySubject_RendersItsCureOnAHintLineUnderTheDiagnosis()
    {
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("naming/x")
                    .Enforce(arch.Namespace("Nowhere.*").MustHavePrefix("I"))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("hint: A trailing `.*` covers the namespace itself");
        block.ShouldContainInOrder("matched no solution-declared types", "hint:");
    }

    [Fact]
    public void RuleBlock_SeveralEmptyPartsSharingOneCure_PrintItOnce()
    {
        // A union with two empty parts names both, then says once what they share: a reader who has met the
        // sentence learns nothing from meeting it again, and the diagnosis lines are what differ.
        var result = new RuleResult(
            Checker.Rule("union/x"), RuleStatus.Failed,
            [
                Violation.EmptySubject("the first part matched nothing", "SHARED-CURE"),
                Violation.EmptySubject("the second part matched nothing", "SHARED-CURE")
            ]);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("the first part matched nothing"),
            () => block.ShouldContain("the second part matched nothing"),
            () => block.Occurrences("hint: SHARED-CURE").ShouldBe(1, block));
    }

    [Fact]
    public void RuleBlock_InertTargetWarning_RendersItsCureUnderTheWarning()
    {
        RuleResult result = Checker.Run(
                "namespace App.Domain { public class Foo {} }",
                arch => arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("warning: This rule is inert");
        block.ShouldContainInOrder("warning:", "hint:");
    }

    [Fact]
    public void RuleBlock_TwoDeadPatternsSharingOneCure_PrintBothWarningsAndTheCureOnce()
    {
        // Two dead namespace patterns get the same cure, and a reader who has met it learns nothing from
        // meeting it again: every warning line stays, and the repeat of the cure goes, as the empty-subject
        // diagnoses dedupe theirs.
        RuleResult result = Checker.Run(
                "namespace App.Domain { public class Foo {} }",
                arch => arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(
                            arch.Namespace("App.Domain.*"),
                            arch.Namespace("App.Ghost.*"),
                            arch.Namespace("App.Phantom.*")))
                    .Because("b"))
            .Single();

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain(
                "warning: This rule is partly inert: its target selection operand \"types in `App.Ghost.*`\" "
                + "matched no types."),
            () => block.ShouldContain(
                "warning: This rule is partly inert: its target selection operand \"types in `App.Phantom.*`\" "
                + "matched no types."),
            () => block.Occurrences("  hint: ").ShouldBe(1, block));
    }

    [Fact]
    public void RuleBlock_UncapturedRuleWithADeadTargetPattern_OmitsTheBaselineHintAndKeepsTheFix()
    {
        // The rule failed on what its live pattern found, so it reached a verdict about the code and keeps its
        // fix. It does not get the init hint: the verb skips a rule with a dead pattern, and the hint would
        // name a command that refuses it right beside the warning saying why.
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b")
                    .Fix("Go through the repository."))
            .Single();

        string block = result.HumanBlock();

        result.ShouldHaveFailed();
        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("warning: This rule is partly inert"),
            () => block.ShouldContain("fix: Go through the repository."),
            () => block.ShouldNotContain("baseline init"));
    }

    [Fact]
    public void RuleBlock_RatchetedRuleFailingOnlyOnAnEmptySubject_OmitsTheBaselineHint()
    {
        // `baseline init` grandfathers observed violations, and an empty subject has no identity to
        // record — so that hint is an instruction which silently does nothing, and printing it beside the
        // cure that works leaves two `hint:` lines disagreeing about what to do.
        var result = new RuleResult(
            MigrateRule(), RuleStatus.Failed,
            [Violation.EmptySubject("matched nothing", "THE-REAL-CURE")]);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldNotContain("baseline init"),
            () => block.ShouldContain("hint: THE-REAL-CURE"));
    }

    [Fact]
    public void RuleBlock_FailingOnlyOnAnEmptySubject_KeepsTheReasonAndWithholdsTheFix()
    {
        // The fix advises correcting a violation of the rule, and an empty subject is not one: the rule never
        // ran, so the advice is for a situation that did not arise, sitting directly above the hint that says
        // what actually went wrong. The reason stays — it is a fact about the rule whatever happened to it,
        // and the xunit adapter's failure message is this same block.
        var result = new RuleResult(
            FixedRule(), RuleStatus.Failed,
            [Violation.EmptySubject("matched nothing", "THE-REAL-CURE")]);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("because: A reader cannot find the type."),
            () => block.ShouldNotContain("fix: Rename it."),
            () => block.ShouldContain("hint: THE-REAL-CURE"));
    }

    [Fact]
    public void RuleBlock_FailingOnlyOnARuleError_WithholdsTheFixToo()
    {
        // The same question, the same answer: a predicate that threw reached no verdict about the code either,
        // so the gate is keyed on that rather than on the one kind the empty-subject case named.
        var result = new RuleResult(
            FixedRule(), RuleStatus.Failed, [Violation.RuleError("boom")]);

        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain("because: A reader cannot find the type."),
            () => block.ShouldNotContain("fix: Rename it."),
            () => block.ShouldContain("error: boom"));
    }

    [Fact]
    public void RuleBlock_FailingOnTheCode_StillPrintsTheFix()
    {
        // The control: a real finding is exactly what the fix is advice for, so the fix prints. Without this
        // row the gate could pass by withholding the fix from everything.
        var result = new RuleResult(
            FixedRule(), RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])]);

        result.HumanBlock()
            .ShouldContain("fix: Rename it.");
    }

    [Fact]
    public void RuleBlock_FailingOnTheCodeWithAnExemplar_PrintsItsPointerDirectlyAfterTheFix()
    {
        // From real source, so the site is the extractor's own: the type's simple name and the file and line
        // that declare it, the place a reader opens to copy the pattern.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/domain")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("The domain must not know the web.")
                    .Fix("Go through an abstraction.")
                    .Exemplar(arch.Types.Named("Model")))
            .Single();

        result.HumanBlock()
            .ShouldContain(
                "  because: The domain must not know the web.\n"
                + "  fix: Go through an abstraction.\n"
                + "  exemplar: Model (Test.cs:12)\n");
    }

    [Fact]
    public void RuleBlock_ExemplarOfSeveralTypes_JoinsEachPointerAtItsFirstDeclarationSite()
    {
        // The types arrive ordered by full name; a partial type is one pointer, at the first of its sites.
        ArchRule rule = FixedRule();
        var result = new RuleResult(
            rule, RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])],
            exemplar:
            [
                SyntheticNodes.Type("OrderService", new SourceLocation("Domain/OrderService.cs", 5)),
                SyntheticNodes.Type(
                    "OrdersRepository",
                    new SourceLocation("Data/OrdersRepository.cs", 3),
                    new SourceLocation("Data/OrdersRepository.Queries.cs", 1))
            ]);

        result.HumanBlock()
            .ShouldContain(
                "  fix: Rename it.\n"
                + "  exemplar: OrderService (Domain/OrderService.cs:5), "
                + "OrdersRepository (Data/OrdersRepository.cs:3)\n");
    }

    [Fact]
    public void RuleBlock_ExemplarWithoutAFix_StillPrintsWhereTheFixWouldStand()
    {
        var result = new RuleResult(
            Checker.Rule("naming/x", because: "A reader cannot find the type."), RuleStatus.Failed,
            [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])],
            exemplar: [SyntheticNodes.Type("OrderService", new SourceLocation("Domain/OrderService.cs", 5))]);

        result.HumanBlock()
            .ShouldContain(
                "  because: A reader cannot find the type.\n"
                + "  exemplar: OrderService (Domain/OrderService.cs:5)\n");
    }

    [Fact]
    public void RuleBlock_FailingOnlyOnAnEmptySubject_WithholdsTheExemplarWithTheFix()
    {
        // The exemplar is the fix's pointer, so it is advice for the same situation and stands under the same
        // gate: a rule that reached no verdict about the code has nothing to copy a pattern for.
        var result = new RuleResult(
            FixedRule(), RuleStatus.Failed,
            [Violation.EmptySubject("matched nothing", "THE-REAL-CURE")],
            exemplar: [SyntheticNodes.Type("OrderService", new SourceLocation("Domain/OrderService.cs", 5))]);

        result.HumanBlock()
            .ShouldNotContain("exemplar:");
    }

    [Fact]
    public void RuleBlock_PassingRuleWithAResolvedExemplar_PrintsNoPointer()
    {
        // A pointer at the type to copy is for whoever is fixing a violation, and a passing rule has none.
        var result = new RuleResult(
            FixedRule(), RuleStatus.Passed, [],
            [CheckWarning.DeadExemplar("types named `Ghost`", "THE-CURE")],
            exemplar: [SyntheticNodes.Type("OrderService", new SourceLocation("Domain/OrderService.cs", 5))]);

        result.HumanBlock()
            .ShouldNotContain("exemplar:");
    }

    [Fact]
    public void RuleBlock_FailingRuleWhoseExemplarResolvedNothing_PrintsNoPointerLine()
    {
        // Only resolved types are listed, and with none there is no line at all rather than an empty one; the
        // dead name is the warning's to report.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/domain")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("The domain must not know the web.")
                    .Fix("Go through an abstraction.")
                    .Exemplar(arch.Types.Named("Ghost")))
            .Single();
        string block = result.HumanBlock();

        block.ShouldSatisfyAllConditions(
            () => block.ShouldNotContain("  exemplar:"),
            () => block.ShouldContain(
                "warning: This rule's exemplar \"types named `Ghost`\" matched no solution-declared types."));
    }

    [Fact]
    public void RuleBlock_RatchetedRuleThatErrored_OmitsTheBaselineHintAsWell()
    {
        // `baseline init` grandfathers by identity, and a rule error has none either (GRAMMAR §4.3), so the
        // hint would tell a ratcheted, errored rule to run a command that silently records nothing. The gate
        // is keyed on the identity rather than on the kinds that lack one, so it covers this kind and the
        // empty subject alike.
        var result = new RuleResult(
            MigrateRule(), RuleStatus.Failed, [Violation.RuleError("boom")]);

        result.HumanBlock()
            .ShouldNotContain("baseline init");
    }

    [Fact]
    public void RuleBlock_ShapeSubjectWithoutDeclarationSites_RendersUnlocatedFullName()
    {
        // A Shape violation carrying no site has no file:line, so the renderer emits its bare FullName as
        // an unlocated line (HumanReportRenderer.ViolationLines) rather than a located `path:line — …` line.
        TypeNode subject = SyntheticNodes.Type("App.Orphan");
        var result = new RuleResult(
            Checker.Rule("shape/x"), RuleStatus.Failed, [Violation.Shape(subject, [])]);

        string block = result.HumanBlock();

        block.ShouldContain("App.Orphan");
        block.ShouldEndWith("App.Orphan"); // unlocated — no trailing `:line`
    }

    [Fact]
    public void RuleBlock_RuleWithCitation_RendersCitationLineBetweenBecauseAndFix()
    {
        // The failing rule's framing, in the order a reader needs it: the reason, the page it rests on, then
        // what to do about it. A rule citing nothing renders no citation line.
        ArchRule cited = Checker.Rule(
            "http/reuse-httpclient", because: "A new client per call exhausts sockets.",
            fix: "Inject IHttpClientFactory.",
            citation: "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
        var result = new RuleResult(
            cited, RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])]);

        string block = result.HumanBlock();

        block.ShouldContain(
            "  because: A new client per call exhausts sockets.\n"
            + "  citation: https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines\n"
            + "  fix: Inject IHttpClientFactory.");

        var uncited = new RuleResult(
            Checker.Rule("shape/x"), RuleStatus.Failed, [Violation.Shape(SyntheticNodes.Type("App.Orphan"), [])]);
        uncited.HumanBlock()
            .ShouldNotContain("citation:");
    }

    [Fact]
    public void RuleBlock_ConstructionViolation_RendersConstructsLineAtNewSite()
    {
        // A Construction kind missing from IsEdge would render nothing and a red
        // report would read green. Pin the located line text: `Source constructs Target` at the `new` site.
        var construction = Violation.Construction(
            SyntheticNodes.Type("App.Factory"), SyntheticNodes.Type("Widgets.Widget"),
            [new SourceLocation("Factory.cs", 12)]);
        var result = new RuleResult(
            Checker.Rule("di/x"), RuleStatus.Failed, [construction]);

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Construction);
        block.ShouldContain("App.Factory constructs Widgets.Widget");
        block.ShouldContain(":12 — App.Factory constructs Widgets.Widget");
    }

    [Fact]
    public void RuleBlock_CatchViolation_RendersCatchesLineAtCatchSite()
    {
        // A Catch kind missing from IsEdge would render nothing and a red report would
        // read green. Pin the located line text: `Source catches Target` at the `catch` site (GRAMMAR §4.8).
        Violation catchViolation = Violation.Catch(
            SyntheticNodes.Type("App.Handler"), SyntheticNodes.Type("Errors.DbError"),
            [new SourceLocation("Handler.cs", 9)]);
        var result = new RuleResult(
            Checker.Rule("ex/x"), RuleStatus.Failed, [catchViolation]);

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Catch);
        block.ShouldContain("App.Handler catches Errors.DbError");
        block.ShouldContain(":9 — App.Handler catches Errors.DbError");
    }

    [Fact]
    public void RuleBlock_ExposeViolation_RendersExposesLineAtExposureSite()
    {
        // An Expose kind missing from IsEdge would render nothing and a red report would
        // read green. Pin the located line text: `Source exposes Target` at the exposing member's site (GRAMMAR §4.9).
        Violation exposeViolation = Violation.Expose(
            SyntheticNodes.Type("App.Facade"), SyntheticNodes.Type("Secrets.Secret"),
            [new SourceLocation("Facade.cs", 5)]);
        var result = new RuleResult(
            Checker.Rule("ex/x"), RuleStatus.Failed, [exposeViolation]);

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Expose);
        block.ShouldContain("App.Facade exposes Secrets.Secret");
        block.ShouldContain(":5 — App.Facade exposes Secrets.Secret");
    }

    [Fact]
    public void RuleBlock_ThrowViolation_RendersThrowsLineAtThrowSite()
    {
        // Likewise the Throw arm: a missing case would silently render nothing. Pin `Source throws Target` at the
        // `throw` site (GRAMMAR §4.8).
        Violation throwViolation = Violation.Throw(
            SyntheticNodes.Type("App.Service"), SyntheticNodes.Type("Errors.InfraError"),
            [new SourceLocation("Service.cs", 14)]);
        var result = new RuleResult(
            Checker.Rule("ex/x"), RuleStatus.Failed, [throwViolation]);

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Throw);
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain(":14 — App.Service throws Errors.InfraError");
    }

    [Fact]
    public void RuleBlock_UnfilteredCatchViolation_RendersCatchesLineAtTheUnfilteredSiteOnly()
    {
        // MustNotCatchUnfiltered reuses the Catch arm, so it renders the same `Source catches Target` line — and
        // because its violation carries the edge's UNFILTERED sites, the filtered clause at line 9 is never
        // printed while the unfiltered one at line 11 is. That is what keeps every rendered line true under a
        // verb whose law the line itself does not state: nobody is pointed at a site the rule sanctions.
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App
                              {
                                  public class Handler
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { }
                                          catch (Errors.DbError) when (flag) { }
                                          try { }
                                          catch (Errors.DbError) { }
                                      }
                                  }
                              }
                              """;
        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/filter-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("A broad catch names what it expects."))
            .Single();

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Catch);
        block.ShouldContain("Test.cs:11 — App.Handler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void RuleBlock_ForbiddenThrowViolation_RendersThrowsLineAtThrowSite()
    {
        // MustNotThrow reuses the Throw arm: the ban polarity renders the same `Source throws Target` line the
        // allow-list does, at the `throw` site — one arm serving the whole fact family.
        const string source = """
                              namespace App
                              {
                                  public class Service
                                  {
                                      public void Run() => throw new System.Exception();
                                  }
                              }
                              """;
        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/no-bare-throws")
                    .Enforce(arch.Namespace("App.*").MustNotThrow(typeof(Exception)))
                    .Because("Throw a type a caller can dispatch on."))
            .Single();

        string block = result.HumanBlock();

        result.ShouldHaveFailedWithSingle(ViolationKind.Throw);
        block.ShouldContain("Test.cs:5 — App.Service throws System.Exception");
    }

    [Fact]
    public void RuleBlock_MixedUnlocatedAndLocated_EmitsUnlocatedBeforeLocated()
    {
        // Every unlocated line (EmptySubject/RuleError/site-less Shape) is emitted before the file-ordered
        // located lines, regardless of input order (HumanReportRenderer.ViolationLines).
        var site = new SourceLocation("Located.cs", 7);
        TypeNode located = SyntheticNodes.Type("App.Located", site);
        var result = new RuleResult(
            Checker.Rule("shape/x"), RuleStatus.Failed,
            [Violation.Shape(located, [site]), Violation.EmptySubject("UNLOCATED-MARKER", "HINT-MARKER")]);

        string block = result.HumanBlock();

        // The unlocated EmptySubject detail precedes the located Shape's `path:line — App.Located`, though the
        // located violation was listed first.
        block.ShouldContainInOrder("UNLOCATED-MARKER", "App.Located");
    }

    [Fact]
    public void RuleBlock_GrownPair_RendersRedSitesAndTheGrownTrailer()
    {
        // A grandfathered pair carrying one site more than its entry records. Every site is red, not just the
        // new one — which site is new is a diff question the count cannot answer — and the trailer is what
        // says why a pair the baseline names is red at all: the allowance beside what the run measured.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(1));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        string block = result.HumanBlock();

        block.ShouldContain("Test.cs:5 — App.Web.OldController references App.Data.Db");
        block.ShouldContain("Test.cs:6 — App.Web.OldController references App.Data.Db");
        block.ShouldContain("  grown: 1 grandfathered pair exceeded the baseline site count (1 baselined, 2 observed)");
        // The pair is red, so nothing passed for the grandfathered count to report — the trailer is the only
        // place the baseline is named.
        block.ShouldNotContain("grandfathered:");
    }

    [Fact]
    public void RuleBlock_SkippedRatchetedRule_IsTheHeaderAndTheReasonAndNothingElse()
    {
        // A ratcheted rule the run reached no verdict for has no burndown to print: the grandfathered count
        // and the baseline init hint are both claims about a check that never happened. Pinned as the
        // whole block rather than as two absences, so any line that ever creeps in below the reason reds.
        const string reason = "'BillingOnly.slnf' narrowed this run.";
        ArchRule rule = MigrateRule();
        var result = new RuleResult(rule, RuleStatus.Skipped, [], [], reason, [], default, true);

        string block = result.HumanBlock();

        block.ShouldBe($"skip {rule.Id} — {rule.Sentence}\n  skipped: {reason}");
    }

    [Fact]
    public void RuleBlock_FiredQuarantineTripwire_PrintsTheDragonsOnceBeneathTheWarnings()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .Dragons("Banker's rounding happens at line-item level.")
                .Because("Replacement scheduled."));

        // Two changed files, one set of dragons: they are a fact about the scope, not about which file was
        // touched, so the reader meets them once however many warnings fired.
        string block = Touched(tripwire, "Calc.cs", "Rounding.cs")
            .HumanBlock();

        block.ShouldEndWith("\n  dragons: Banker's rounding happens at line-item level.");
        block.Occurrences("  dragons: ")
            .ShouldBe(1, block);
        block.ShouldNotContain("dragons-doc:");
    }

    [Fact]
    public void RuleBlock_FiredCautionTripwire_PrintsTheDragonsBeneathTheWarning()
    {
        Touched(SharedUtilitiesTripwire, "Helpers.cs")
            .HumanBlock()
            .ShouldEndWith("\n  dragons: Argument order is structural.");
    }

    [Fact]
    public void RuleBlock_SilentTripwire_PrintsNoDragonsLine()
    {
        // Nothing in the change set touched the scope. The dragons are still true, but stating them under a
        // rule that did not fire puts them on every clean run of every check.
        var quiet = new RuleResult(SharedUtilitiesTripwire, RuleStatus.Passed, []);

        quiet.HumanBlock()
            .ShouldNotContain("dragons");
    }

    [Fact]
    public void RuleBlock_FiredTripwireWithBothDragonsForms_PrintsBothLinesInOrder()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .Dragons("Argument order is structural.")
                .DragonsDoc("arch/utilities-dragons.md")
                .Because("Every caller passes them positionally."));

        Touched(tripwire, "Helpers.cs")
            .HumanBlock()
            .ShouldEndWith("\n  dragons: Argument order is structural.\n  dragons-doc: arch/utilities-dragons.md");
    }

    [Fact]
    public void RuleBlock_FiredTripwireWithDragonsDocOnly_PrintsTheDocLineAlone()
    {
        ArchRule tripwire = Checker.Tripwire(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .DragonsDoc("arch/utilities-dragons.md")
                .Because("Every caller passes them positionally."));

        string block = Touched(tripwire, "Helpers.cs")
            .HumanBlock();

        block.ShouldEndWith("\n  dragons-doc: arch/utilities-dragons.md");
        block.ShouldNotContain("  dragons: ");
    }

    [Fact]
    public void Render_MultiRuleReport_WritesSummaryTail()
    {
        var report = new CheckReport(
        [
            new RuleResult(Checker.Rule("r/pass"), RuleStatus.Passed, []),
            new RuleResult(Checker.Rule("r/fail"), RuleStatus.Failed, [Violation.RuleError("boom")]),
            new RuleResult(Checker.Rule("r/skip"), RuleStatus.Skipped, [], [], "no --diff-base diff context", [])
        ]);

        var writer = new StringWriter { NewLine = "\n" };
        HumanReportRenderer.Render(writer, report, Directory.GetCurrentDirectory());
        var output = writer.ToString();

        output.ShouldContain("Checked 3 rules: 1 passed, 1 failed, 1 skipped (1 violation, 0 warnings).");
        output.ShouldContain("skipped: no --diff-base diff context"); // the Skipped arm renders its reason
    }

    [Fact]
    public void Render_WarnedRule_IsCountedAsPassedAndItsWarningsInTheTail()
    {
        // A warned rule is a passed rule — the posture is the severity, and a warning never moves the exit
        // code — so 'warn' is the per-rule flag, the tail counts the warnings, and the passed figure stays
        // equal to what a check's JSON report calls rulesPassed.
        var report = new CheckReport(
        [
            new RuleResult(Checker.Rule("r/pass"), RuleStatus.Passed, []),
            Touched(SharedUtilitiesTripwire, "Helpers.cs")
        ]);

        var writer = new StringWriter { NewLine = "\n" };
        HumanReportRenderer.Render(writer, report, Directory.GetCurrentDirectory());
        var output = writer.ToString();

        output.ShouldContain("warn shared/utilities/tripwire");
        output.ShouldContain("Checked 2 rules: 2 passed, 0 failed, 0 skipped (0 violations, 1 warning).");
        report.RulesPassed.ShouldBe(2); // the tail's passed figure, and the warned rule is one of the two
    }

    [Fact]
    public void RenderWithoutPasses_LeavesOutOnlyTheBlocksOfRulesThatPassedQuietly()
    {
        // The pass block comes first, so the whole report minus that block is the whole report from the warn
        // header on: pinned as that relation rather than as a transcription, so the row says what the
        // rendering is. The skip block stays, because it qualifies the verdict; the tail stays whole, because
        // it is the verdict, and it still counts the rule the page no longer shows.
        var report = new CheckReport(
        [
            new RuleResult(Checker.Rule("r/pass"), RuleStatus.Passed, []),
            Touched(SharedUtilitiesTripwire, "Helpers.cs"),
            new RuleResult(Checker.Rule("r/skip"), RuleStatus.Skipped, [], [], "no --diff-base diff context", [])
        ]);
        var whole = new StringWriter { NewLine = "\n" };
        var trimmed = new StringWriter { NewLine = "\n" };

        HumanReportRenderer.Render(whole, report, Directory.GetCurrentDirectory());
        HumanReportRenderer.RenderWithoutPasses(trimmed, report, Directory.GetCurrentDirectory());

        const string warnHeader = "warn shared/utilities/tripwire";
        var wholeText = whole.ToString();
        wholeText.ShouldStartWith("pass r/pass");
        wholeText.ShouldContain(warnHeader);
        trimmed.ToString()
            .ShouldBe(wholeText[wholeText.IndexOf(warnHeader, StringComparison.Ordinal)..]);
        trimmed.ToString()
            .ShouldEndWith("Checked 3 rules: 2 passed, 0 failed, 1 skipped (0 violations, 1 warning).\n");
    }

    [Fact]
    public void RuleBlock_UncapturedRuleBesideAnOrphan_PrintsTheWarningAndItsHintInPlaceOfTheInitHint()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty.WithOrphanScan(Sources.OneOrphan), Sources.NoDataAccess)
            .Single();

        string block = result.HumanBlock();
        block.ShouldSatisfyAllConditions(
            () => block.ShouldContain(
                "\n  warning: No baseline is captured for this rule, and baseline section 'data/old-x' in "
                + "arch/baselines/data/old-x.json (1 entry) names no rule in the spec.\n"
                + "  hint: If this rule was renamed from 'data/old-x', run 'sliderule baseline rename --from "
                + "data/old-x --to data/x' to keep its entries; if the rule is new, run 'sliderule baseline init'."),
            () => block.ShouldNotContain("no baseline captured"));
    }

    [Fact]
    public void RuleBlock_UncapturedRuleWithNoOrphan_IsTheBlockItAlwaysWas()
    {
        RuleResult plain = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
            .Single();
        RuleResult emptyScan = Checker.Run(
                Sources.OneControllerModel, BaselineIndex.Empty.WithOrphanScan(BaselineOrphanScan.Empty),
                Sources.NoDataAccess)
            .Single();

        string block = plain.HumanBlock();
        block.ShouldEndWith(
            "\n  hint: no baseline captured for this rule; run 'sliderule baseline init' to grandfather existing violations");
        emptyScan.HumanBlock()
            .ShouldBe(block);
    }

    // A rule carrying both halves of the failing rule's framing, spelled out rather than placeholders: the
    // rows below assert which of them reached the page, so a reader of a red has to be able to tell the two
    // lines apart.
    private static ArchRule FixedRule()
    {
        return Checker.Rule("naming/x", because: "A reader cannot find the type.", fix: "Rename it.");
    }

    // A tripwire that fired: one warning per changed file, of the kind its posture raises, which is what the
    // checker produces and what lets a row ask whether two of them still print one set of dragons.
    private static RuleResult Touched(ArchRule tripwire, params string[] files)
    {
        bool caution = tripwire.Posture == Posture.Caution;
        List<CheckWarning> warnings = files
            .Select(file => CheckWarning.ScopeTouched(caution, file, tripwire.Scope!.ScopeId, tripwire.Id))
            .ToList();

        return new RuleResult(tripwire, RuleStatus.Passed, [], warnings);
    }

    // A ratcheted rule, built through the spec so it carries a real BaselinePath — the one thing that makes
    // the renderer's ratchet lines reachable at all.
    private static ArchRule MigrateRule()
    {
        return Checker.Model(arch => arch.Rule("data/x")
                .Migrate("old", arch.Namespace("App.*").MustHaveSuffix("X"))
                .Because("b"))
            .Rule("data/x");
    }
}
