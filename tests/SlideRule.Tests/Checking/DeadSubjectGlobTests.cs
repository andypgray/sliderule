using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Internal;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     A dead glob in an <c>Under</c> list (GRAMMAR §4.1), read the way a dead name in a <c>Named</c> list is:
///     <c>Under(a, b)</c> names exactly what <c>AnyOf(Under(a), Under(b))</c> names, so on a subject a glob no type
///     is under fails the rule as an empty operand of its own, a run over one project drops it, and one inside an
///     <c>Except</c> payload exempts nothing and is not read. The target and allow-list positions are rows of
///     <see cref="DeadTargetPartTests" /> and <see cref="DeadAllowOperandTests" />.
/// </summary>
public sealed class DeadSubjectGlobTests
{
    private const string GlobCure =
        "A path glob is relative to the solution directory with `/` between segments, and reaches a type when any "
        + "file declaring it is under the glob; check the selection against what the solution declares.";

    private static readonly SolutionRoot Root = new(Sources.FolderRoot);

    [Fact]
    public void EveryDeadGlobBesideALiveOne_FailsTheRule_InListOrder()
    {
        RuleResult result = Checker.Run(Sources.FolderModel, Root, arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Gone", "src/Widgets", "src/Lost").MustBeSealed())
                    .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.Detail)
            .ShouldBe(
            [
                ConstraintEvaluator.EmptyOperandMessage("types under `src/Gone`"),
                ConstraintEvaluator.EmptyOperandMessage("types under `src/Lost`")
            ]);
        result.Violations.ShouldAllBe(violation => violation.Hint == GlobCure);
    }

    [Fact]
    public void AListNoneOfWhoseGlobsMatched_FailsWholeWithTheGlobCure()
    {
        RuleResult result = Checker.Run(Sources.FolderModel, Root, arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Gone", "src/Lost").MustBeSealed())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage)
            .Hint.ShouldBe(GlobCure);
    }

    [Fact]
    public void ARunOverOneProject_DropsTheDeadGlob_AndJudgesTheLiveOne()
    {
        // Another project's types may be declared under the glob, so it is not this compilation's to fail.
        Checker.Run(Sources.FolderModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, Root, arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Widgets", "src/Gone").MustBeSealed())
                    .Because("b"))
            .Single()
            .ShapeSubjects()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["App.Button", "App.Panel"]);
    }

    [Fact]
    public void ADeadGlobInAnExceptPayload_IsNotRead()
    {
        // An exemption's dead glob exempts nothing, which leaves the rule stricter, not unmeasured.
        Checker.Run(Sources.FolderModel, Root, arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Widgets")
                        .Except(arch.Types.Under("src/Widgets/Deep", "src/Gone"))
                        .MustBeSealed())
                    .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe(["App.Button"]);
    }
}
