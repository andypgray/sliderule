using Xunit;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The throw verb <c>MustOnlyThrow</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3): a STRICT allow-list
///     — every throw edge whose thrown type is not in the allowed set is a violation,
///     <b>external thrown types included</b> (the point of departure from <c>MustOnlyReference</c>, which
///     exempts external targets).
/// </summary>
/// <remarks>
///     The verb never raises the whole-target inert warning: an empty allow-set is loud on its own, since every
///     throw then reds. Each pattern in the allow-set that matched nothing is named by the stricter-than-written
///     warning instead.
///     Violation identity is the (source, thrown) type pair, keyed by <see cref="BaselineEntry.ForEdge" />.
/// </remarks>
public sealed class MustOnlyThrowVerbTests
{
    // Service throws a permitted DomainError and a non-permitted InfraError; only DomainError is in the allow-set.
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DomainError : System.Exception {}
                                     public class InfraError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class Service
                                     {
                                         public void Run(bool b)
                                         {
                                             if (b) throw new Errors.DomainError();
                                             throw new Errors.InfraError();
                                         }
                                     }
                                 }
                                 """;

    // A single solution throw, reused by the absent-allowed-type and empty-allow-set pins.
    private const string OneThrow = """
                                    namespace Errors { public class MyError : System.Exception {} }
                                    namespace App
                                    {
                                        public class Service { public void Run() => throw new Errors.MyError(); }
                                    }
                                    """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel OneThrowModel = CompilationFactory.Extract(OneThrow);

    [Fact]
    public void MustOnlyThrow_SubjectThrowsDisallowedType_FailsWithSourceTargetSitesAndHumanLine()
    {
        // Service throws DomainError (allowed → silent) and InfraError (not allowed → red): the strict allow-list
        // flags only the unlisted throw, and the allowed one passing proves the allow-set is live.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/only-domain")
                    .Enforce(arch.Namespace("App.*")
                        .MustOnlyThrow(arch.Namespace("Errors.*").WithSuffix("DomainError")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Throw, "App.Service", "Errors.InfraError");

        string block = result.HumanBlock();
        block.ShouldContain("App.Service throws Errors.InfraError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustOnlyThrow_DisallowedExternalThrow_IsRed()
    {
        // The strictness pin: Service throws an EXTERNAL System.InvalidOperationException that is not in the
        // allow-set. MustOnlyReference would exempt an external target; MustOnlyThrow drops that clause, so the
        // external throw is red. The allow-set holds a solution DomainError, so the rule is live, not vacuous.
        const string source = """
                              namespace App
                              {
                                  public class DomainError : System.Exception {}
                                  public class Service
                                  {
                                      public void Run() => throw new System.InvalidOperationException();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/only-domain")
                    .Enforce(arch.Namespace("App.*").WithSuffix("Service")
                        .MustOnlyThrow(arch.Types.WithSuffix("Error")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Throw, ["App.Service -> System.InvalidOperationException"]);
    }

    [Fact]
    public void MustOnlyThrow_TypeSugarAllowsExternalThrow_Passes()
    {
        // A Type-sugar allow-set member resolves an external type by FQN (the target universe includes
        // externals): MustOnlyThrow(typeof(TimeoutException)) allows Service's external TimeoutException throw,
        // so the rule passes with no violation.
        const string source = """
                              namespace App
                              {
                                  public class Service
                                  {
                                      public void Run() => throw new System.TimeoutException();
                                  }
                              }
                              """;

        RuleResult result = Checker.Run(source, arch =>
                arch.Rule("ex/only-timeout")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(typeof(TimeoutException)))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyThrow_AllowedTypeAbsentFromModel_AllowsNothingAndIsRed()
    {
        // An allowed type nobody references (System.DivideByZeroException) resolves to an empty allow-set —
        // harmlessly, with no crash — so it allows nothing: Service's Errors.MyError throw is red.
        RuleResult result = Checker.Run(OneThrowModel, arch =>
                arch.Rule("ex/only-absent")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(typeof(DivideByZeroException)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Throw, ["App.Service -> Errors.MyError"]);
    }

    [Fact]
    public void MustOnlyThrow_EmptyAllowSet_WarnsStricterNeverInert()
    {
        // The allow-set (a namespace glob) resolves empty, which for MustNotCatch raises the whole-target inert
        // warning. MustOnlyThrow never does — an empty allow-set is loud on its own (every throw is red) — so the
        // result carries the violations and names the dead pattern as the stricter-than-written warning alone.
        RuleResult result = Checker.Run(OneThrowModel, arch =>
                arch.Rule("ex/only-nowhere")
                    .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Throw, ["App.Service -> Errors.MyError"]);
        result.ShouldHaveWarnedStricterOnOperand("types in `Nonexistent.*`");
    }

    [Fact]
    public void MustOnlyThrow_EmptySubject_FailsWithDefaultMessage()
    {
        // An empty subject fails the rule by default with the shared message (GRAMMAR §4.1), exactly as every
        // other verb — the throw verb takes the same subject gate.
        RuleResult result = Checker.Run(
                "namespace App { public class Foo {} }",
                arch => arch.Rule("ex/empty")
                    .Enforce(arch.Namespace("Nowhere.*").MustOnlyThrow(arch.Namespace("App.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
    }

    [Fact]
    public void MustOnlyThrow_GrandfatheredThrowPasses_NewThrowStaysRed()
    {
        // The throw ratchet keys the (source, thrown) type pair (GRAMMAR §4.3): Worker is grandfathered for
        // throwing Alpha, but its still-unlisted throw of Beta is a distinct identity → red. The allow-set holds
        // an Allowed.Good so the rule is live rather than an empty-allow-set catch-all.
        const string source = """
                              namespace N { public class Alpha : System.Exception {} public class Beta : System.Exception {} }
                              namespace Allowed { public class Good : System.Exception {} }
                              namespace N
                              {
                                  public class Worker
                                  {
                                      public void Run(int x)
                                      {
                                          if (x == 0) throw new N.Alpha();
                                          throw new N.Beta();
                                      }
                                  }
                              }
                              """;
        BaselineIndex index = Checker.Baselines("ex/only-throw", BaselineEntry.ForEdge("T:N.Worker", "T:N.Alpha"));

        RuleResult result = Checker.Run(source, index, arch =>
                arch.Rule("ex/only-throw")
                    .Migrate("legacy throws", arch.Namespace("N.*").MustOnlyThrow(arch.Namespace("Allowed.*")))
                    .Because("throw only the sanctioned exceptions"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Throw, ["N.Worker -> N.Beta"]);
        result.ShouldHaveGrandfathered(1);
    }

    [Fact]
    public void JsonReportRenderer_ThrowViolation_EmitsThrowKindAndTargetAndOmitsMemberSlots()
    {
        // The JSON kind string is "throw", and the thrown type rides the `target` field a reference uses, so
        // the report is schemaVersion 3 with the member and subject slots omitted.
        CheckReport report = Checker.Run(SceneModel, arch =>
            arch.Rule("ex/only-domain")
                .Enforce(arch.Namespace("App.*").MustOnlyThrow(arch.Namespace("Errors.*").WithSuffix("DomainError")))
                .Because("b"));

        report.ShouldRenderEdgeViolation("throw", "App.Service", "Errors.InfraError");
    }
}
