using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

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

    private const string DeadNamedExceptionHint =
        "A name in a member `Named` list is the member's own name, matched exactly, without its declaring type "
        + "or signature; an exception that removes nothing spares nothing, so what it was meant to spare is "
        + "reported as a violation.";

    private const string DeadMemberExceptionHint =
        "A member exception removes only members the selection it is written on holds, and a package's type "
        + "declares none here; an exception that removes nothing spares nothing, so what it was meant to spare is "
        + "reported as a violation.";

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
    public void AnExceptionOfOverridingMethodsWhereNoneExist_StaysSilent()
    {
        // The exemption states a policy, so it removes nothing here and the sentence still holds.
        RuleResult result = Checker.Run(Model, arch =>
                arch.Rule("members/static")
                    .Enforce(arch.Namespace("App.*").Methods
                        .Except(arch.Types.Methods.ThatAreOverrides())
                        .MustBeStatic())
                    .Because("b"))
            .Single();

        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void ADeadNameInAnExceptPayload_WarnsWithoutFailingTheSubject()
    {
        // An exemption's dead name exempts nothing, which leaves the rule stricter, not unmeasured: a warning, never
        // the empty-subject failure a dead name of the subject's own list is.
        Checker.Run(Model, arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Namespace("App.*").Methods
                        .Except(arch.Types.Named("Program").Methods.Named("Main", "Ghost"))
                        .MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithMemberSubjects(["M:App.Program.Helper", "M:App.Worker.Run"])
            .ShouldHaveWarnedStricterException("methods of types in `App.*`", "`Ghost` methods of types named `Program`")
            .Hint.ShouldBe(DeadNamedExceptionHint);
    }

    [Fact]
    public void AnExceptionOfAnotherTypesMethods_WarnsWithTheMemberExceptionCure()
    {
        // The exception names no member, so its cure is about where members are held rather than how they are named:
        // Worker's methods are not among Program's, so excepting them removes nothing.
        Checker.Run(Model, arch =>
                arch.Rule("members/static")
                    .Enforce(arch.Types.Named("Program").Methods
                        .Except(arch.Types.Named("Worker").Methods)
                        .MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithMemberSubjects(["M:App.Program.Helper"])
            .ShouldHaveWarnedStricterException("methods of types named `Program`", "methods of types named `Worker`")
            .Hint.ShouldBe(DeadMemberExceptionHint);
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
            .ShouldHaveFailedWithMemberSubjects(["M:App.Program.Helper", "M:App.Worker.Run"]);
    }

    [Fact]
    public void ADeadNameUnderAPartialModel_CarriesThePartialModelCure()
    {
        IncompleteModel partial = Checker.PartialModel;

        RuleResult result = Checker.Run(Model, incompleteModel: partial, define: arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Main", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
                ViolationKind.EmptySubject, ConstraintEvaluator.EmptyMemberOperandMessage(GhostOperand))
            .Hint.ShouldBe(Checker.PartialModelCure);
    }

    [Fact]
    public void ARunOverOneProject_CutsTheDeadName_AndJudgesTheLiveNames()
    {
        // A name only another project's members carry is not one compilation's to judge, so the checker cuts it
        // before the gate reads the list and the live name is measured as written: Main is static and the rule
        // passes rather than failing on Ghost.
        Checker.Run(Model, narrowing: NarrowedUniverse.OneProject, define: arch =>
                arch.Rule("members/named")
                    .Enforce(arch.Types.Named("Program").Methods.Named("Main", "Ghost").MustBeStatic())
                    .Because("b"))
            .Single()
            .ShouldHavePassed();
    }
}
