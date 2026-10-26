using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The rethrow-aware catch verb <c>MustNotSwallow</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a catch
///     edge trips only where a subject <c>catch</c>es a forbidden type at a clause that spells no <c>when</c>
///     filter <em>and</em> does not end in a <c>throw</c>.
/// </summary>
/// <remarks>
///     The pin neither sibling can carry is the differentiator: the two rethrowing handlers are red under
///     <c>MustNotCatchUnfiltered</c> and <b>green</b> here. Beside it the syntactic honesty boundary,
///     both ways: a terminal <c>throw</c> that is not on every path still reads as throwing, and a throw on some
///     path that is not the last statement still reads as swallowing. The rows the catch verbs share
///     (FQN matching, a hierarchy operand, the absent typeof target, the ratchet, the JSON wire form) live in
///     <c>MustNotCatchVerbTests</c>: the catch verbs run one walk and differ only in the sites they read. The kind is
///     <see cref="ViolationKind.Catch" />, which names the fact family rather than the verb, so identity is the
///     (source, caught) type pair keyed by <see cref="BaselineEntry.ForEdge" /> and the swallowing sites are
///     evidence, never identity.
/// </remarks>
public sealed class MustNotSwallowVerbTests
{
    // Errors.DbError is the domain exception nobody may hold and continue on. Four handlers catch exactly that
    // type: Swallower holds it (red under both catch verbs); Rethrower cleans up and rethrows and Translator
    // wraps and throws (both red under MustNotCatchUnfiltered, both GREEN here — they suppress nothing);
    // FilteredHandler names its expectation in a `when` filter (green under both).
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DbError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class Swallower
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { }
                                         }
                                     }
                                     public class Rethrower
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { Cleanup(); throw; }
                                         }
                                         private void Cleanup() {}
                                     }
                                     public class Translator
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError e) { throw new System.InvalidOperationException("x", e); }
                                         }
                                     }
                                     public class FilteredHandler
                                     {
                                         public void Run(bool flag)
                                         {
                                             try { }
                                             catch (Errors.DbError) when (flag) { }
                                         }
                                     }
                                 }
                                 """;

    // One (source, caught) pair caught twice in one type — rethrowing at line 9, swallowing at line 11. Extraction
    // records both as unfiltered sites and only the second as swallowing, which is what the evidence-subset pin
    // reads.
    private const string MixedScene = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class MixedHandler
                                          {
                                              public void Run()
                                              {
                                                  try { }
                                                  catch (Errors.DbError) { throw; }
                                                  try { }
                                                  catch (Errors.DbError) { }
                                              }
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel MixedModel = CompilationFactory.Extract(MixedScene);

    [Fact]
    public void MustNotSwallow_SubjectSwallowsBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        // The subject covers all four handlers, and the single-item claim below is the verb's whole point:
        // only the handler that holds the failure is in the report.
        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Catch, "App.Swallower", "Errors.DbError");

        string block = result.HumanBlock();
        block.ShouldContain("App.Swallower catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotSwallow_TheDifferentiator_RethrowingHandlersAreGreenHereAndRedUnderMustNotCatchUnfiltered()
    {
        // The verb's differentiator, both ways round on one scene. Under MustNotCatchUnfiltered all three
        // unfiltered handlers red, including the two that suppress nothing, which is the cost this verb removes.
        // Under MustNotSwallow only the handler that holds the failure and continues reds, and the filtered
        // handler is green under both.
        RuleResult unfiltered = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotCatchUnfiltered(arch.Namespace("Errors.*")));

        unfiltered.ShouldHaveFailedWithEdges(
            ViolationKind.Catch,
            [
                "App.Rethrower -> Errors.DbError",
                "App.Swallower -> Errors.DbError",
                "App.Translator -> Errors.DbError"
            ]);

        RuleResult swallow = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        swallow.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Swallower -> Errors.DbError"]);
    }

    [Fact]
    public void MustNotSwallow_EveryCatchSiteRethrows_PassesCleanWithNoWarnings()
    {
        // The all-rethrowing-is-green pin. Rethrower catches the banned Errors.DbError with no filter — the edge
        // exists, and MustNotCatchUnfiltered would red it — but its clause ends in a throw, so the swallowing
        // subset is empty and the rule passes. The forbidden target resolves (Errors.DbError exists), so this is
        // a real pass, not an inert one.
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*").WithSuffix("Rethrower")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotSwallow_EveryCatchSiteFiltered_PassesCleanWithNoWarnings()
    {
        // A filtered swallow stays lawful: the filter is where the handler named its expectations, so the house
        // form the sibling verb rewards keeps passing under the refinement.
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*").WithSuffix("FilteredHandler")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotSwallow_EdgeMixesRethrowingAndSwallowingSites_EvidenceIsTheSwallowingSitesOnly()
    {
        // The evidence-subset pin. One (source, caught) edge, two unfiltered catch sites: the rethrowing one at
        // line 9 and the swallowing one at line 11. The edge violates because a site swallows, and the violation
        // carries the swallowing site ALONE — so every printed file:line is a site the rule actually objects to,
        // which is what lets the verb reuse the Catch kind and its "{source} catches {target}" line without
        // printing a falsehood.
        MixedModel.CatchEdge("App.MixedHandler", "Errors.DbError")
            .UnfilteredSites.Lines()
            .ShouldBe([9, 11]);

        RuleResult result = Checker.Verdict(MixedModel, arch => arch.Namespace("App.*")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        result.ShouldHaveFailedWithEdgeAtSites(
            ViolationKind.Catch, "App.MixedHandler", "Errors.DbError", ["Test.cs:11"]);

        string block = result.HumanBlock();
        block.ShouldContain("Test.cs:11 — App.MixedHandler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void MustNotSwallow_ReturnBeforeATerminalThrow_IsGreenAndAThrowThatIsNotLastIsRed()
    {
        // The documented honesty boundary, pinned from both sides. EarlyReturn can leave its handler having
        // suppressed the failure, and is GREEN, because the fact is the block's last STATEMENT and never an
        // all-paths flow analysis. NotLast throws on one path and is RED, for the same syntactic reason. Stating
        // the boundary in a test is what keeps it a documented limit rather than a discovered surprise.
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App
                              {
                                  public class EarlyReturn
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { } catch (Errors.DbError) { if (flag) return; throw; }
                                      }
                                  }
                                  public class NotLast
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { } catch (Errors.DbError) { if (flag) throw; Cleanup(); }
                                      }
                                      private void Cleanup() {}
                                  }
                              }
                              """;

        RuleResult result = Checker.Verdict(source, arch => arch.Namespace("App.*")
            .MustNotSwallow(arch.Namespace("Errors.*")));

        result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.NotLast -> Errors.DbError"]);
    }

    [Fact]
    public void MustNotSwallow_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate: the verb joins the forbidden-set inert-warn family (§4.1) exactly
        // as its siblings do.
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotSwallow(arch.Namespace("Nonexistent.*")));

        result.ShouldHaveWarnedInertTarget();
    }
}
