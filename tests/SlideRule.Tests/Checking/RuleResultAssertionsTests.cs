using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The negative case for <see cref="RuleResultAssertions" />: an assertion helper that never throws
///     passes silently at every site that calls it, so the one fact worth pinning is that it reds.
/// </summary>
/// <remarks>
///     A documented tolerance needs the same protection as a documented refusal, so the unordered comparison
///     is pinned here too, positively: a later tightening to ordered would otherwise red in whichever swept
///     row happened to spell its set in reading order rather than here, where the decision is recorded.
/// </remarks>
public sealed class RuleResultAssertionsTests
{
    private const string OneCatch = """
                                    namespace Errors { public class DbError : System.Exception {} }
                                    namespace App
                                    {
                                        public class Handler
                                        {
                                            public void Run() { try { } catch (Errors.DbError) { } }
                                        }
                                    }
                                    """;

    private const string TwoCatches = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class Alpha
                                          {
                                              public void Run() { try { } catch (Errors.DbError) { } }
                                          }
                                          public class Beta
                                          {
                                              public void Run() { try { } catch (Errors.DbError) { } }
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel OneCatchModel = CompilationFactory.Extract(OneCatch);

    private static readonly CodebaseModel TwoCatchesModel = CompilationFactory.Extract(TwoCatches);

    [Fact]
    public void ShouldHaveFailedWithSingleEdge_KindMismatch_Reds()
    {
        // A Catch violation asserted as a Reference: the batched property conditions must surface it rather
        // than swallow it, which is what makes the helper safe to use at a hundred sites.
        RuleResult result = Caught(OneCatchModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Handler", "Errors.DbError");
        }

        Should.Throw<ShouldAssertException>(Assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdges_KindMismatch_Reds()
    {
        // The same mismatch one grain up: filtering to the wrong kind yields an empty projection, and the
        // set comparison has to red on it rather than reading the edge off some other kind's violation.
        RuleResult result = Caught(OneCatchModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Handler -> Errors.DbError"]);
        }

        Should.Throw<ShouldAssertException>(Assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdgesIncluding_OneEdgePresentOneAbsent_Reds()
    {
        // The batched-fragment case: a present edge must not carry an absent one past the assertion, which is
        // the failure mode ShouldSatisfyAllConditions exists to prevent and nothing else here covers.
        RuleResult result = Caught(TwoCatchesModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithEdgesIncluding(
                ViolationKind.Catch, "App.Alpha -> Errors.DbError", "App.Zeta -> Errors.DbError");
        }

        Should.Throw<ShouldAssertException>(Assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdges_ExpectationInReverseOfReportOrder_Passes()
    {
        // The tolerance, stated positively. Report order is ordinal by source then target, so Alpha precedes
        // Beta; naming them the other way round is still the same set, and this verb says so.
        RuleResult result = Caught(TwoCatchesModel);

        result.ShouldHaveFailedWithEdges(
            ViolationKind.Catch, ["App.Beta -> Errors.DbError", "App.Alpha -> Errors.DbError"]);
    }

    [Theory]
    [InlineData("edges")]
    [InlineData("subjects")]
    [InlineData("including")]
    [InlineData("edgesAtSites")]
    public void EveryCollectionVerb_EmptyExpectation_Reds(string verb)
    {
        // An empty expectation is a silent total pass — a rule red on some other kind satisfies both the
        // status claim and empty-equals-empty — so every verb refuses it rather than reporting green.
        RuleResult result = Caught(OneCatchModel);

        Action assertion = verb switch
        {
            "edges" => () => result.ShouldHaveFailedWithEdges(ViolationKind.Catch, []),
            "subjects" => () => result.ShouldHaveFailedWithSubjects([]),
            "edgesAtSites" => () => result.ShouldHaveFailedWithEdgesAtSites(ViolationKind.Catch, []),
            _ => () => result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Catch)
        };

        Should.Throw<ShouldAssertException>(assertion);
    }

    [Fact]
    public void AnyAssertion_WhenItReds_CarriesTheWholeResultIntoTheMessage()
    {
        // The reason this vocabulary exists, pinned once: the reader of a red gets the rule, its status and
        // every violation, not two bare collections. Nothing else asserts that Describe reaches the message.
        RuleResult result = Caught(OneCatchModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Handler -> Errors.Missing"]);
        }

        var red = Should.Throw<ShouldAssertException>(Assertion);

        red.Message.ShouldContain("violations (");
        red.Message.ShouldContain("Catch App.Handler -> Errors.DbError");
    }

    [Fact]
    public void ShouldHaveFailedWithEdgeAtSites_ExactSitesInOrder_Passes()
    {
        // The evidence verb, stated positively: one catch, one line, and naming that line exactly is the whole
        // claim the rows over a multiply-declared name make, so a wider list may not satisfy it.
        RuleResult result = Caught(OneCatchModel);

        result.ShouldHaveFailedWithEdgeAtSites(ViolationKind.Catch, "App.Handler", "Errors.DbError", ["Test.cs:6"]);
    }

    [Fact]
    public void ShouldHaveFailedWithEdgeAtSites_ASiteTheViolationDoesNotCite_Reds()
    {
        // The refusal beside it, in the shape the defect takes: the edge is right and the evidence carries a
        // line from somewhere else, which is what a violation charged with another copy's sites looks like.
        RuleResult result = Caught(OneCatchModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Catch, "App.Handler", "Errors.DbError", ["Test.cs:6", "Other.cs:6"]);
        }

        Should.Throw<ShouldAssertException>(Assertion);
    }

    [Fact]
    public void ShouldHaveFailedWithEdgesAtSites_ExpectationInReverseOfReportOrder_Passes()
    {
        // The plural verb inherits the unordered edge comparison, so the set may be named in reading order or
        // any other; what it does not inherit is tolerance inside one edge's site list.
        RuleResult result = Caught(TwoCatchesModel);

        result.ShouldHaveFailedWithEdgesAtSites(ViolationKind.Catch,
        [
            ("App.Beta -> Errors.DbError", ["Test.cs:10"]),
            ("App.Alpha -> Errors.DbError", ["Test.cs:6"])
        ]);
    }

    [Fact]
    public void ShouldHaveFailedWithEdgesAtSites_RightEdgesCitingEachOthersSites_Reds()
    {
        // Edge and sites are asserted together, so both edges present with their evidence swapped is a red
        // rather than a green over two matching edge names.
        RuleResult result = Caught(TwoCatchesModel);

        void Assertion()
        {
            result.ShouldHaveFailedWithEdgesAtSites(ViolationKind.Catch,
            [
                ("App.Alpha -> Errors.DbError", ["Test.cs:10"]),
                ("App.Beta -> Errors.DbError", ["Test.cs:6"])
            ]);
        }

        Should.Throw<ShouldAssertException>(Assertion);
    }

    private static RuleResult Caught(CodebaseModel codebase)
    {
        return Checker.Run(codebase, arch => arch.Rule("ex/no-catch")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("b"))
            .Single();
    }
}
