using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The coverage verb <c>MustBelongTo</c> over the fast path (GRAMMAR §5.3, §4.1, §4.3): a subject no
///     membership names is red, so the rule closes the ungoverned remainder a set of positive rules leaves
///     open. Memberships resolve in <em>subject</em> position — they name where a type may live rather than
///     the far end of an edge — and the test is plain membership in their union.
/// </summary>
/// <remarks>
///     The verb reads only the selection membership the model already resolves, so there is no extracted
///     fact for the extraction tests to cover. Violations are
///     <see cref="ViolationKind.Shape" />: identity is the subject symbol ID riding
///     <see cref="BaselineEntry.ForSubject" />, which leaves the membership list part of the rule and never
///     part of the identity — a rule that gains a membership keeps its blessed entries. A membership that
///     matches nothing is not subject emptiness, so it fails nothing of its own; it warns that the rule is
///     stricter than written, per dead pattern, part or name, except on a narrowed run.
/// </remarks>
public sealed class MustBelongToVerbTests
{
    // Two declared layers and one namespace outside both. Legacy.Ledger is the ungoverned remainder every
    // red below is about; Domain carries two types so a narrowed membership has something to drop.
    private const string Scene = """
                                 namespace Domain
                                 {
                                     public class Order {}
                                     public class Invoice {}
                                 }
                                 namespace Web
                                 {
                                     public class HomeController {}
                                 }
                                 namespace Legacy
                                 {
                                     public class Ledger {}
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustBelongTo_SubjectInNoMembership_FailsWithSubjectAndDeclarationSites()
    {
        // A shape violation has no edge to cite, so the file:line an agent jumps to is where the uncovered
        // type is declared — the subject's own declaration sites, carried as evidence.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjectAtSites("Legacy.Ledger", ["Test.cs:12"]);
    }

    [Fact]
    public void MustBelongTo_EverySubjectCoveredByAMembership_PassesClean()
    {
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Enforce(arch.Types.Except(arch.Namespace("Legacy.*"))
                        .MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustBelongTo_SingleMembership_IsPlainMembershipInThatSelection()
    {
        // One membership is the degenerate case and stays a membership test rather than becoming an identity
        // test: the types the selection names pass, and everything else is the remainder.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/domain-only")
                    .Enforce(arch.Namespace("Domain.*").MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/domain-only")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Web.HomeController", "Legacy.Ledger"]);
    }

    [Fact]
    public void MustBelongTo_UnionMembershipAndAnExceptedOne_NarrowTheResolvedSet()
    {
        // A union operand resolves as ONE membership covering both its parts, so this names the same set the
        // two-membership spelling above does.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/union-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.AnyOf(arch.Namespace("Domain.*"), arch.Namespace("Web.*"))))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Legacy.Ledger"]);

        // Except narrows the membership itself: Domain minus Invoice no longer covers Invoice, so the type
        // joins the uncovered remainder rather than staying green on a layer it is still inside.
        Checker.Run(SceneModel, arch =>
                arch.Rule("layering/excepted-membership")
                    .Enforce(arch.Types.MustBelongTo(
                        arch.Namespace("Domain.*").Except(arch.Types.WithNameMatching("Invoice")),
                        arch.Namespace("Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSubjects(["Domain.Invoice", "Legacy.Ledger"]);
    }

    [Fact]
    public void MustBelongTo_MembershipMatchingNothing_WarnsStricterAndTheRedsAreTheUncoveredSubjects()
    {
        // A membership naming nothing covers nothing, so every subject it was meant to hold reds under its own
        // name; the warning is what names the stale membership (§4.1). The per-operand emptiness failure
        // guards a union SUBJECT (§4.1) and does not reach a membership, so the reds are the uncovered subjects
        // and nothing else.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/absent-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Nowhere.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Web.HomeController", "Legacy.Ledger"]);
        result.ShouldHaveWarnedStricterOnMembership("types in `Nowhere.*`");
        result.ShouldSatisfyAllConditions(
            () => result.Violators(ViolationKind.EmptySubject, violation => violation.Detail!)
                .ShouldBeEmpty(),
            () => result.UnmatchedEntriesTested.ShouldBeTrue(),
            () => result.MeasuredInFull.ShouldBeFalse());
    }

    [Fact]
    public void MustResideInNamespace_DeadGlob_WarnsExactlyAsItsMustBelongToForm()
    {
        // The reside pair is MustBelongTo with one membership, so its warning is that form's in every slot a
        // host reads: kind, key, message and cure.
        RuleResult reside = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/reside")
                    .Enforce(arch.Types.MustResideInNamespace("Nowhere.*"))
                    .Because("b"))
            .Single();
        RuleResult belong = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/reside")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Nowhere.*")))
                    .Because("b"))
            .Single();

        reside.ShouldHaveWarnedStricterOnMembership("types in `Nowhere.*`");
        CheckWarning resided = reside.Warnings.Single();
        CheckWarning belonged = belong.Warnings.ShouldHaveSingleItem();
        $"{resided.Kind} {resided.Key}: {resided.Message} | {resided.Hint}"
            .ShouldBe($"{belonged.Kind} {belonged.Key}: {belonged.Message} | {belonged.Hint}");
    }

    [Fact]
    public void MustBelongTo_DeadNameInANamedMembership_WarnsNamingTheOneNameSpelling()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/named-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.Types.Named("Order", "Invoice", "Ghost"), arch.Namespace("Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Legacy.Ledger"]);
        result.ShouldHaveWarnedStricterOnMembership("types named `Ghost`");
    }

    [Fact]
    public void MustBelongTo_DeadPartOfAUnionMembership_Warns()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/union-membership")
                    .Enforce(arch.Types.MustBelongTo(
                        arch.AnyOf(arch.Namespace("Domain.*"), arch.Namespace("Ghost.*")),
                        arch.Namespace("Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Legacy.Ledger"]);
        result.ShouldHaveWarnedStricterOnMembership("types in `Ghost.*`");
    }

    [Fact]
    public void MustBelongTo_DeadPartOfALayerDefinition_Warns()
    {
        // A membership is never cut down to one project the way a rule subject is, so a layer used as one is
        // opened and the dead piece of its definition named.
        RuleResult result = Checker.Run(SceneModel, arch =>
            {
                Layer core = arch.Layer("Core", arch.AnyOf(arch.Namespace("Domain.*"), arch.Namespace("Ghost.*")));
                arch.Rule("layering/layer-membership")
                    .Enforce(arch.Types.MustBelongTo(core, arch.Namespace("Web.*")))
                    .Because("b");
            })
            .Single();

        result.ShouldHaveFailedWithSubjects(["Legacy.Ledger"]);
        result.ShouldHaveWarnedStricterOnMembership("types in `Ghost.*`");
    }

    [Fact]
    public void MustBelongTo_TypeofMembershipAbsentFromTheCode_IsSilent()
    {
        // A typeof the code does not declare holds nothing, and the compiler has already checked its name.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/typeof-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Type(typeof(Guid))))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Web.HomeController", "Legacy.Ledger"]);
        result.Warnings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("one-project")]
    public void MustBelongTo_DeadMembershipOnANarrowedRun_IsSilent(string narrowing)
    {
        // A project the run never loaded may declare what the membership names, so a dead pattern here is no
        // evidence it went stale.
        NarrowedUniverse universe = narrowing == "filter" ? Checker.FilterNarrowing : NarrowedUniverse.OneProject;

        RuleResult result = Checker.Run(SceneModel, BaselineIndex.Empty, universe, arch =>
                arch.Rule("layering/absent-membership")
                    .Enforce(arch.Types.MustBelongTo(arch.Namespace("Domain.*"), arch.Namespace("Nowhere.*")))
                    .Because("b"))
            .Single();

        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustBelongTo_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1) — the coverage
        // verb takes the same subject gate, which is what keeps an empty membership and an empty subject
        // distinguishable outcomes.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("layering/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustBelongTo_GrandfatheredStrayPasses_NewStrayStaysRed()
    {
        // Identity is the subject symbol ID (GRAMMAR §4.3), one entry per uncovered type. Legacy.Ledger is
        // blessed; Web.HomeController — uncovered because the rule declares only the Domain membership — is a
        // distinct identity and stays red.
        BaselineIndex index = Checker.Baselines(
            "layering/no-ungoverned-types", BaselineEntry.ForSubject("T:Legacy.Ledger"));

        RuleResult result = Checker.Run(SceneModel, index, arch =>
                arch.Rule("layering/no-ungoverned-types")
                    .Migrate(
                        "Some types predate the declared layers.",
                        arch.Types.MustBelongTo(arch.Namespace("Domain.*")))
                    .Because("A type in no declared layer is governed by nothing."))
            .Single();

        result.ShouldHaveFailedWithSubjects(["Web.HomeController"]);
        result.ShouldHaveGrandfathered(1);
    }
}
