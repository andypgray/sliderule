using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The catch verb <c>MustNotCatch</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a catch edge trips
///     only where a subject <c>catch</c>es a forbidden type, matched by exact definition-level FQN, so banning
///     <c>typeof(Exception)</c> never flags a narrower <c>catch (IOException)</c>. Violation identity is the
///     (source, caught) type pair, keyed by <see cref="BaselineEntry.ForEdge" /> as a reference is.
/// </summary>
public sealed class MustNotCatchVerbTests
{
    // Errors.DbError is the domain exception nobody may swallow. DataHandler catches it (the red edge);
    // CleanHandler catches only an external OperationCanceledException outside the banned namespace — a catch,
    // but not a forbidden one, so the verb is silent on it.
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DbError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class DataHandler
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { }
                                         }
                                     }
                                     public class CleanHandler
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (System.OperationCanceledException) { }
                                         }
                                     }
                                 }
                                 """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    [Fact]
    public void MustNotCatch_SubjectCatchesBannedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotCatch(arch.Namespace("Errors.*")));

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Catch, "App.DataHandler", "Errors.DbError");

        string block = result.HumanBlock();
        block.ShouldContain("App.DataHandler catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotCatch_SubjectCatchesUnbannedType_PassesCleanWithNoWarnings()
    {
        // CleanHandler catches an external OperationCanceledException — a catch, but outside the banned
        // Errors.* namespace, so the ban is silent. The forbidden target resolves (Errors.DbError exists), so
        // this is a real pass, not an inert one.
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*").WithSuffix("CleanHandler")
            .MustNotCatch(arch.Namespace("Errors.*")));

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotCatch_BansExceptionButCatchesNarrowerType_NarrowCatchNotFlagged()
    {
        // Matching is exact definition-level FQN, not hierarchy: MustNotCatch(typeof(Exception)) flags the broad
        // `catch (System.Exception)` but NEVER the narrower `catch (IOException)` — the narrow catch is the good
        // state the verb rewards. Broad's presence proves the ban is live, not vacuously empty.
        const string source = """
                              namespace App
                              {
                                  public class Broad
                                  {
                                      public void Run() { try { } catch (System.Exception) { } }
                                  }
                                  public class Narrow
                                  {
                                      public void Run() { try { } catch (System.IO.IOException) { } }
                                  }
                              }
                              """;

        RuleResult result = Checker.Verdict(source, arch => arch.Types.MustNotCatch(typeof(Exception)));

        result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Broad -> System.Exception"]);
    }

    [Fact]
    public void MustNotCatch_DerivedFromOperand_MatchesSolutionExceptionNotExternal()
    {
        // A hierarchy-adjective operand (arch.Types.DerivedFrom) ranges over solution-declared types only —
        // external types carry a shallow hierarchy. Worker catches its own N.AppError (solution, derives from
        // Exception → matched → red) and an external System.InvalidOperationException (never matched by a
        // DerivedFrom operand, so not flagged), pinning that the adjective matches solution types but not externals.
        const string source = """
                              namespace N
                              {
                                  public class AppError : System.Exception {}
                                  public class Worker
                                  {
                                      public void Run()
                                      {
                                          try { } catch (N.AppError) { }
                                          try { } catch (System.InvalidOperationException) { }
                                      }
                                  }
                              }
                              """;

        RuleResult result = Checker.Verdict(source, arch => arch.Namespace("N.*")
            .MustNotCatch(arch.Types.DerivedFrom(typeof(Exception))));

        result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["N.Worker -> N.AppError"]);
    }

    [Fact]
    public void MustNotCatch_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate, exactly as MustNotConstruct's inert-target semantics (§4.1).
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotCatch(arch.Namespace("Nonexistent.*")));

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void MustNotCatch_AbsentTypeofTarget_IsSilentWinNoWarning()
    {
        // A bare typeof target absent from the codebase is the WIN condition, not an inert warning: nobody
        // catches System.FormatException, so the ban resolves empty but — being a concrete typeof anchor, not a
        // pattern — stays silent (the departure from the pattern-operand inert warning above).
        RuleResult result = Checker.Verdict(SceneModel, arch => arch.Namespace("App.*")
            .MustNotCatch(typeof(FormatException)));

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotCatch_GrandfatheredEdgePasses_NewCatchStaysRed()
    {
        // The catch ratchet keys the (source, caught) type pair (GRAMMAR §4.3): Handler is grandfathered for
        // catching AErr, but its NEW `catch (BErr)` is a distinct identity → red.
        const string source = """
                              namespace Errors { public class AErr : System.Exception {} public class BErr : System.Exception {} }
                              namespace App
                              {
                                  public class Handler
                                  {
                                      public void Run()
                                      {
                                          try { } catch (Errors.AErr) { }
                                          try { } catch (Errors.BErr) { }
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Checker.Baselines("ex/no-catch", BaselineEntry.ForEdge("T:App.Handler", "T:Errors.AErr"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/no-catch")
                    .Migrate("legacy broad catches", arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                    .Because("catch specific exceptions"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Catch, ["App.Handler -> Errors.BErr"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void JsonReportRenderer_CatchViolation_EmitsCatchKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "catch", and the caught type rides the `target` field a reference uses, so
        // the report is schemaVersion 3 with the member and subject slots omitted.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/no-catch-domain")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("b"));

        report.ShouldRenderEdgeViolation("catch", "App.DataHandler", "Errors.DbError");
    }
}
