using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     A dead name in a member subject's <c>Named</c> list (GRAMMAR §4.6), the member twin of
///     <see cref="DeadSubjectNameTests" />: a name no member carries fails the rule as an empty operand of its
///     own, quoted as the subject would read with that name alone, and the verb never runs.
/// </summary>
public sealed class DeadSubjectMemberNameTests
{
    private const string Source = """
                                  namespace App
                                  {
                                      public class Program
                                      {
                                          public static void Main() {}
                                          public static void Run() {}
                                          public void Helper() {}
                                      }
                                      public class Worker
                                      {
                                          public void Run() {}
                                      }
                                  }
                                  """;

    private const string GhostOperand = "`Ghost` methods of types named `Program`";

    private const string MemberNameCure =
        "A name in a member `Named` list is the member's own name, matched exactly, without its declaring type "
        + "or signature; check the selection against what the solution declares.";

    // A stand-in for the sentence a partial model composes; its wording is pinned where it is minted.
    private const string PartialModelCure = "THE-PARTIAL-MODEL-CURE";

    private static readonly CodebaseModel Model = CompilationFactory.Extract(Source);

    [Fact]
    public void ADeadNameBesideALiveOne_FailsTheRuleNamingIt()
    {
        // Helper is not static, so a rule that reached the verb would fail on it; this one fails on the name.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Helper", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single();

        Violation violation = result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberOperandMessage(GhostOperand));
        violation.Hint.ShouldBe(MemberNameCure);
    }

    [Fact]
    public void EveryDeadName_FailsInListOrder()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Phantom", "Main", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.Detail)
            .ShouldBe(
            [
                ConstraintEvaluator.EmptyMemberOperandMessage("`Phantom` methods of types named `Program`"),
                ConstraintEvaluator.EmptyMemberOperandMessage(GhostOperand)
            ]);
    }

    [Fact]
    public void AListNoneOfWhoseNamesMatched_FailsWholeWithNoPerNameDuplicates()
    {
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Phantom", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberSubjectMessage);
    }

    [Fact]
    public void ADeadNameInAnExceptPayload_IsNotRead()
    {
        // An exemption's dead name exempts nothing, which leaves the rule stricter, not unmeasured.
        Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Namespace("App.*").Methods
                        .Except(arch.Types.Named("Program").Methods.Named("Main", "Ghost"))
                        .MustBeStatic())
                    .Because("b"))
            .Single()
            .MemberShapeSubjects()
            .ShouldBe(["M:App.Program.Helper", "M:App.Worker.Run"]);
    }

    [Fact]
    public void AListWhoseEveryNameMatched_ReachesTheVerb()
    {
        // A name reaches every member carrying it across the subject's types, overloads and all.
        Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Namespace("App.*").Methods.Named("Run", "Helper").MustBeStatic())
                    .Because("b"))
            .Single()
            .MemberShapeSubjects()
            .ShouldBe(["M:App.Program.Helper", "M:App.Worker.Run"]);
    }

    [Fact]
    public void ADeadNameUnderAPartialModel_CarriesThePartialModelCure()
    {
        var partial = new IncompleteModel(PartialModelCure);

        RuleResult result = Checker.Run(Model, partial, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Main", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
                ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberOperandMessage(GhostOperand))
            .Hint.ShouldBe(PartialModelCure);
    }

    [Fact]
    public void ARunOverOneProject_SkipsTheGate_AndJudgesTheLiveNames()
    {
        // A name only another project's members carry is not one compilation's to judge, so the live name is
        // measured as written: Main is static and the rule passes rather than failing on Ghost.
        Checker.Run(Model, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Main", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }
}
