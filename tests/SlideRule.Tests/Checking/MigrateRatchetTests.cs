using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Tests.Checking.Targets;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The Migrate ratchet: a Migrate rule evaluates like Enforce, then its
///     violations are partitioned against a baseline keyed by stable symbol IDs (GRAMMAR §4.3). In the
///     baseline = grandfathered (pass); not in it — including new code in the old pattern and a new
///     forbidden target from a grandfathered source — = red. An edge entry may also record how many
///     sites it grandfathers, and then the count decides too: more is growth and red, fewer is a
///     reduction that passes, and an entry recording nothing holds its pair at any size. A stale entry is
///     counted and never reds. <c>EmptySubject</c>/<c>RuleError</c> are never baselinable; an inert target
///     still warns-and-passes. A rule whose selection matched nothing, or matched only in part, reads as not
///     measured in full, so its unmatched entries are not counted as fixed and it is not promotable; a
///     stricter-than-written pattern still counts them as fixed. An uncaptured failing rule beside orphaned
///     baseline sections gets one rename hint per orphan. The two-arg overload sees no baselines; an Enforce
///     rule ignores the index entirely.
/// </summary>
public sealed class MigrateRatchetTests
{
    // One (source, caught) edge caught three times in one type: unfiltered once and behind a `when` filter
    // twice. MustNotCatch reports all three sites as evidence; MustNotCatchUnfiltered reports the one.
    private const string ThreeCatchHandler = """
                                             namespace Errors { public class DbError : System.Exception {} }
                                             namespace App
                                             {
                                                 public class Handler
                                                 {
                                                     public void Run(bool flag)
                                                     {
                                                         try { }
                                                         catch (Errors.DbError) { }
                                                         try { }
                                                         catch (Errors.DbError) when (flag) { }
                                                         try { }
                                                         catch (Errors.DbError) when (!flag) { }
                                                     }
                                                 }
                                             }
                                             """;

    // One misnamed type beside one conforming one — the shape the subject-keyed ratchet rows read.
    private const string MixedNames = "namespace App { public class GoodHandler {} public class BadThing {} }";

    // The one forbidden edge every controller fixture carries, as the baseline keys it.
    private static readonly BaselineEntry OldControllerToDb = BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db");

    private static readonly CodebaseModel ThreeCatchHandlerModel = CompilationFactory.Extract(ThreeCatchHandler);

    // The one forbidden edge again, this time as a model carrying it with no site at all — the state a
    // legacy file's uncounted entry describes, arrived at from the run's end instead of the file's.
    private static readonly CodebaseModel UnsitedControllerEdge = UnsitedEdge();

    // The same source's rule: every type in App must carry the Handler suffix.
    private static void HandlerNaming(Arch arch)
    {
        arch.Rule("naming/x")
            .Migrate("Types are inconsistently named.", arch.Namespace("App.*").MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based.");
    }

    // The same forbidden reference the controller fixtures carry, selected by suffix rather than by
    // namespace: a hand-built node declares no namespace, so only a name-keyed selection reaches one.
    private static void NoDbFromControllers(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate("old", arch.Types.WithSuffix("Controller").MustNotReference(arch.Types.WithSuffix("Db")))
            .Because("b");
    }

    private static CodebaseModel UnsitedEdge()
    {
        TypeNode source = SyntheticNodes.Type("App.Web.OldController");
        TypeNode target = SyntheticNodes.Type("App.Data.Db");

        return SyntheticNodes.Referencing([source, target], new ReferenceEdge(source, target, new EdgeEvidence([])));
    }

    // The empty-subject spec the no-match rows run: a controller cone no type occupies.
    private static void NoDataAccessFromNowhere(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "old",
                arch.Namespace("App.Nowhere.*").WithSuffix("Controller")
                    .MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b");
    }

    [Fact]
    public void Check_MigrateViolationInBaseline_PassesWithGrandfathered()
    {
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveGrandfathered(1);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateViolationNotInBaseline_FailsRed()
    {
        // Captured section, but this edge is not in it — new code in the old pattern is red.
        RuleResult result = Checker.Run(Sources.OneControllerModel, Checker.Baselines("data/x"), Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem();
        result.Grandfathered.ShouldBeEmpty();
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_GrandfatheredSourceWithNewForbiddenTarget_FailsRed()
    {
        // Pair identity (GRAMMAR §4.3): OldController is grandfathered for Db, but its NEW edge to Cache is red.
        const string source = """
                              namespace App.Web
                              {
                                  public class OldController
                                  {
                                      public App.Data.Db A() => new App.Data.Db();
                                      public App.Data.Cache B() => new App.Data.Cache();
                                  }
                              }
                              namespace App.Data { public class Db {} public class Cache {} }
                              """;
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(source, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Web.OldController -> App.Data.Cache"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void Check_MigrateConstructionViolation_GrandfathersByEdgePair()
    {
        // A construction violation ratchets exactly like a reference: its identity is the (source, constructed)
        // type pair (GRAMMAR §4.3), so the same ForEdge entry grandfathers OldController `new`ing Db.
        // (OneController's Load() does `new App.Data.Db()`.)
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "Controllers `new` the data layer directly (legacy Active Record style).",
                        arch.Namespace("App.Web.*").WithSuffix("Controller")
                            .MustNotConstruct(arch.Namespace("App.Data.*")))
                    .Because("Resolve via DI for testability."))
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveGrandfathered(1);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MoreSitesThanTheEntryRecords_IsRedAsGrowth()
    {
        // The hole the measure closes: without a recorded count, a second old-pattern site inside an
        // already-grandfathered pair rides in free — which is where new code in the old pattern
        // gets written, inside a type whose surrounding code already does it.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb.WithSiteCount(1));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Web.OldController -> App.Data.Db"]);
        result.ShouldHaveGrown(1);
        result.ShouldHaveGrandfathered(0);
        // Red with ALL its sites, not just the new one: which site is new is a diff question, and the count
        // is deliberately lossy — that lossiness is what buys immunity to line churn.
        result.Violations.ShouldHaveSingleItem()
            .Sites.Count.ShouldBe(2);
    }

    [Fact]
    public void Check_SitesWithinTheRecordedCount_PassesGrandfathered()
    {
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb.WithSiteCount(2));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_FewerSitesThanTheEntryRecords_PassesAndCountsShrunk()
    {
        // A reduction is never red — tightening is the direction the ratchet wants — but it is reported, so
        // 'baseline accept-reductions' has something to lower the recorded count to.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb.WithSiteCount(3));

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(1);
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Check_UncountedEntry_GrandfathersAtPairGrainAndCountsUncounted()
    {
        // An entry recording no count holds its pair at any size. That is what keeps a partially upgraded
        // or foreign baseline section valid — and it is reported, because it is the state a write can clear.
        BaselineIndex index = Checker.Baselines(
            "data/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveUncounted(1);
    }

    [Fact]
    public void Check_EdgeSeenWithNoSites_IsGrandfatheredAndCountsNeitherShrunkNorUncounted()
    {
        // An edge the run saw with no file:line evidence is the same state as an entry written before counts
        // existed: grandfathered at pair grain, with nothing to measure and nothing to report. Read as a
        // reduction instead, it would offer 'baseline accept-reductions' a count of zero to record, which
        // an entry may not carry. The model is hand-built because no extraction reaches this state — every
        // edge factory is fed from syntax — and the verdict path has to agree with the write path anyway.
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb.WithSiteCount(2));

        RuleResult result = Checker.Run(UnsitedControllerEdge, index, NoDbFromControllers)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveShrunk(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_SubjectEntry_IsNeitherGrownNorUncounted()
    {
        // A subject entry's sites are declarations, so there is no measure to record and nothing to report:
        // counting it as uncounted would leave a naming rule's whole section nagging forever with nothing
        // an author could do about it.
        BaselineIndex index = Checker.Baselines("naming/x", BaselineEntry.ForSubject("T:App.BadThing"));

        RuleResult result = Checker.Run(MixedNames, index, HandlerNaming)
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveGrown(0);
        result.ShouldHaveUncounted(0);
    }

    [Fact]
    public void Check_GrownPair_IsNotStaleAndCarriesItsStoredEntry()
    {
        // Matching is by identity, and a grown pair matched — so it is live debt that grew, never debt that
        // was fixed. Reading it as stale would offer it to 'baseline accept-reductions', which would then
        // delete the entry recording the very debt that just got worse.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb
                .WithSiteCount(1)
                .WithBecause("INC-1234"));

        RuleResult result = Checker.Run(Sources.TwoSiteControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveStale(0);
        Violation grown = result.Violations.ShouldHaveSingleItem();
        BaselineEntry stored = result.GrownEntries[grown];
        stored.SiteCount.ShouldBe(1);
        stored.Because.ShouldBe("INC-1234");
    }

    [Fact]
    public void Check_SameEdgeUnderANarrowerCatchVerb_MeasuresOnlyThatVerbsSites()
    {
        // All three catch verbs key the identical edge (GRAMMAR §4.3), but each reports its own sites — so
        // the count is as measured by the verb that recorded it. An entry recorded under
        // MustNotCatchUnfiltered holds under that verb and reds under MustNotCatch, whose evidence is wider.
        // Inherent to counting evidence, and the remedy is 'baseline add' re-recording the entry.
        BaselineIndex index = Checker.Baselines(
            "ex/x",
            BaselineEntry.ForEdge("T:App.Handler", "T:Errors.DbError")
                .WithSiteCount(1));

        RuleResult unfiltered = Checker.Run(ThreeCatchHandlerModel, index, arch =>
                arch.Rule("ex/x")
                    .Migrate("Handlers catch the domain error blind.",
                        arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("A blind catch hides a failing dependency."))
            .Single();
        RuleResult broad = Checker.Run(ThreeCatchHandlerModel, index, arch =>
                arch.Rule("ex/x")
                    .Migrate("Handlers catch the domain error at all.",
                        arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("A blind catch hides a failing dependency."))
            .Single();

        unfiltered.ShouldHavePassed();
        unfiltered.ShouldHaveGrandfathered(1);
        unfiltered.ShouldHaveGrown(0);

        broad.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Handler -> Errors.DbError"]);
        broad.ShouldHaveGrown(1);
    }

    [Fact]
    public void Check_StaleBaselineEntry_CountsWithoutFailing()
    {
        // Old is grandfathered and present; the Ghost entry matches no current violation → stale.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveGrandfathered(1);
        result.ShouldHaveStale(1);
    }

    [Fact]
    public void Check_UncapturedMigrateRule_AllViolationsRedAndCapturedFalse()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem();
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_CapturedEmptySection_PassesAndCapturedTrue()
    {
        // A clean controller with an empty (zero-debt) section — the promotable state.
        const string source = """
                              namespace App.Web { public class CleanController { public string M() => ""; } }
                              namespace App.Data { public class Db {} }
                              """;

        RuleResult result = Checker.Run(source, Checker.Baselines("data/x"), Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.BaselineCaptured.ShouldBeTrue();
        result.Grandfathered.ShouldBeEmpty();
        result.ShouldHaveStale(0);
    }

    [Fact]
    public void Check_MigrateEmptySubjectUnderNarrowing_SkipsWithoutStalingTheSection()
    {
        // The ratchet's half of the narrowing skip. Ratchet would match nothing against the captured
        // section and report both entries as fixed awaiting acceptance — for a rule this run never
        // measured — which is exactly the reduction 'baseline accept-reductions' would then delete. So
        // the skip is minted before the ratchet fork and forces the counts to nothing, while
        // BaselineCaptured stays truthful: the section is real, it simply went unmeasured.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run(
                "namespace App { public class X {} }", index, Checker.FilterNarrowing, NoDataAccessFromNowhere)
            .Single();

        result.ShouldHaveSkipped(Checker.FilterSkipReason);
        result.ShouldHaveGrandfathered(0);
        result.ShouldHaveStale(0);
        result.BaselineCaptured.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateRuleError_IsFailedNeverBaselinable()
    {
        RuleResult result = Checker.Run(Sources.HierarchyModel, Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
    }

    [Fact]
    public void Check_MigrateInertTarget_WarnsAndPasses()
    {
        RuleResult result = Checker.Run("namespace App.Web { public class HomeController {} }", Checker.Baselines("data/x"), arch =>
                arch.Rule("data/x")
                    .Migrate(
                        "old",
                        arch.Namespace("App.Web.*").WithSuffix("Controller")
                            .MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void Check_MigrateInertTargetOverACapturedSection_MarksTheRuleAsHavingMeasuredNothing()
    {
        // The arm the warns-and-passes row above cannot reach: its baseline is empty, so the whole captured
        // section going unmatched never happens there. Here two entries are captured and the target has rotted,
        // which leaves the rule passing with nothing tolerated, nothing red, and its entire section stale —
        // the four counts a burned-down migration has, on a rule that measured nothing. The predicate is what
        // parts them, and every surface downstream reads it rather than re-deriving it.
        BaselineIndex index = Checker.Baselines(
            "data/x",
            OldControllerToDb,
            BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        RuleResult result = Checker.Run("namespace App.Web { public class HomeController {} }", index, arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Baseline("ghost.json")
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
        result.ShouldHaveGrandfathered(0);
        result.ShouldHaveStale(2);
        result.SelectionMatchedNothing.ShouldBeTrue();
        result.Promotable.ShouldBeFalse("a rule that measured nothing must never be offered for promotion");
    }

    [Fact]
    public void Check_MigrateEmptySubject_MarksTheRuleAsHavingMeasuredNothingToo()
    {
        // The same fact arriving as a failure rather than a warning: an empty subject is one violation no
        // baseline can hold, so the rule reds and its whole section still goes unmatched. One predicate
        // answers for both, because a reader testing only the violations would miss the inert half and a
        // reader testing only the warnings would miss this one.
        BaselineIndex index = Checker.Baselines("data/x", OldControllerToDb);

        RuleResult result = Checker.Run("namespace App.Data { public class Db {} }", index, arch =>
                arch.Rule("data/x")
                    .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Baseline("ghost.json")
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.EmptySubject);
        result.ShouldHaveStale(1);
        result.SelectionMatchedNothing.ShouldBeTrue();
    }

    [Fact]
    public void Check_MigrateThatMeasuredItsSubject_DoesNotReadAsHavingMeasuredNothing()
    {
        // The other side of the predicate, so it cannot pass by answering true always: a rule with a real
        // subject and a real target reds on a real finding and nothing about its selection came up empty.
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.SelectionMatchedNothing.ShouldBeFalse();
    }

    [Fact]
    public void Check_MigratePartlyInertOverACapturedSection_CountsItsUnmatchedEntryAsNotMeasured()
    {
        // The live pattern measured App.Data and found the ghost pair gone — but an entry only the dead pattern
        // could have matched would go unmatched in exactly the same way, and nothing on the result tells the
        // two apart. So the rule is not measured in full although its selection did match something, and the
        // report moves the unmatched entry out of the fixed total rather than offering it for acceptance.
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, index, Sources.PartlyInertDataAccess);
        RuleResult result = report.Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
        result.ShouldHaveStale(1);
        result.ShouldSatisfyAllConditions(
            () => result.SelectionMatchedNothing.ShouldBeFalse(),
            () => result.MeasuredInFull.ShouldBeFalse(),
            () => result.Promotable.ShouldBeFalse());
        report.ShouldSatisfyAllConditions(
            () => report.UnmeasuredBaselineEntryCount.ShouldBe(1),
            () => report.StaleBaselineEntryCount.ShouldBe(0));
    }

    [Fact]
    public void Check_MigrateStricterOverACapturedSection_CountsItsUnmatchedEntryAsFixed()
    {
        // A dead allowed pattern only adds violations, so it cannot be why the ghost pair's entry went unmatched:
        // the entry is fixed debt and the report counts it so, while the rule as a whole stays unmeasured as
        // written and is never offered for promotion.
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, index, Sources.StricterDataAccess);
        RuleResult result = report.Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
        result.ShouldHaveStale(1);
        result.ShouldSatisfyAllConditions(
            () => result.UnmatchedEntriesTested.ShouldBeTrue(),
            () => result.MeasuredInFull.ShouldBeFalse(),
            () => result.Promotable.ShouldBeFalse());
        report.ShouldSatisfyAllConditions(
            () => report.StaleBaselineEntryCount.ShouldBe(1),
            () => report.UnmeasuredBaselineEntryCount.ShouldBe(0));
    }

    [Fact]
    public void Check_MigratePartlyInertWithAnEmptySection_IsNotPromotable()
    {
        // Burned to zero on every count, which is promotion's exact shape, on a rule that measured only part of
        // what it names: enforcing it would pin a ban missing a clause. The same rule without its dead pattern
        // is the control, and is promotable.
        RuleResult partly = Checker.Run(
                Sources.CleanWebBesideDataModel, Checker.Baselines("data/x"), Sources.PartlyInertDataAccess)
            .Single();
        RuleResult whole = Checker.Run(
                Sources.CleanWebBesideDataModel, Checker.Baselines("data/x"), Sources.LiveDataAccess)
            .Single();

        partly.ShouldHavePassed();
        partly.ShouldHaveStale(0);
        partly.Promotable.ShouldBeFalse("a rule with a dead target pattern must never be offered for promotion");
        whole.Promotable.ShouldBeTrue("the control: the same rule without the dead pattern has burned to zero");
    }

    [Fact]
    public void Check_ModelOverload_TreatsBaselinesAsEmpty()
    {
        // The two-argument entry point takes no baselines, so a Migrate rule checked through it is uncaptured
        // and its violation stays red.
        ArchitectureModel model = Checker.Model(Sources.NoDataAccess);

        RuleResult result = Harness.Check(model, Sources.OneControllerModel)
            .Single();

        result.ShouldHaveFailed();
        result.BaselineCaptured.ShouldBeFalse();
    }

    [Fact]
    public void Check_UncapturedFailingRuleBesideOrphans_GetsOneWarningPerOrphanInScanOrder()
    {
        BaselineIndex index = BaselineIndex.Empty.WithOrphanScan(Sources.TwoOrphans);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveOrphanWarnings("data/old-x", "legacy/old");
        result.Warnings[0]
            .Hint.ShouldBe(
                "If this rule was renamed from 'data/old-x', run 'sliderule baseline rename --from data/old-x "
                + "--to data/x' to keep its entries; if the rule is new, run 'sliderule baseline init'.");
    }

    [Fact]
    public void Check_CapturedFailingRuleBesideOrphans_GetsNone()
    {
        // A captured rule has its section; an orphan beside it is someone else's.
        BaselineIndex index = Checker.Baselines("data/x").WithOrphanScan(Sources.TwoOrphans);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveOrphanWarnings();
    }

    [Fact]
    public void Check_PassingUncapturedRuleBesideOrphans_GetsNone()
    {
        BaselineIndex index = BaselineIndex.Empty.WithOrphanScan(Sources.TwoOrphans);

        RuleResult result = Checker.Run(Sources.CleanWebBesideDataModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveOrphanWarnings();
    }

    [Fact]
    public void Check_UncapturedFailingRuleWithAnEmptyScan_GetsNone()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, BaselineIndex.Empty, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveOrphanWarnings();
    }

    [Fact]
    public void Check_RuleFailingOnlyOnAnEmptySubjectBesideOrphans_GetsNone()
    {
        // An empty subject has no identity to capture, so neither recovery would move anything.
        BaselineIndex index = BaselineIndex.Empty.WithOrphanScan(Sources.TwoOrphans);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, NoDataAccessFromNowhere)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveOrphanWarnings();
    }

    [Fact]
    public void Check_RuleWithADeadTargetPatternAndLiveViolationsBesideAnOrphan_GetsTheRenameOnlyHint()
    {
        // init skips a rule not measured in full, so its hint names only the rename.
        BaselineIndex index = BaselineIndex.Empty.WithOrphanScan(Sources.OneOrphan);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.PartlyInertDataAccess)
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveOrphanWarnings("data/old-x");
        result.Warnings.Single(warning => warning.Kind == CheckWarningKind.OrphanedBaseline)
            .Hint.ShouldBe(
                "If this rule was renamed from 'data/old-x', run 'sliderule baseline rename --from data/old-x "
                + "--to data/x' to keep its entries.");
    }

    [Fact]
    public void Check_EnforceRule_IgnoresBaselineIndex()
    {
        // Even an index that carries the exact edge does not grandfather an Enforce rule.
        BaselineIndex index = Checker.Baselines("layer/x", OldControllerToDb);

        RuleResult result = Checker.Run(Sources.OneControllerModel, index, arch =>
                arch.Rule("layer/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Grandfathered.ShouldBeEmpty();
    }
}
