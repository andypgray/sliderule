using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Cancellation through a check: the token is observed once per rule, so an abandoned run stops at a
///     rule boundary and throws rather than returning a report missing its tail — and a cancellation
///     raised by the one piece of user code a check runs, a <c>Where</c> or <c>Must</c> delegate, leaves
///     the same way instead of being reported as a defect in the rule that held it.
/// </summary>
public sealed class CheckCancellationTests
{
    [Fact]
    public void Check_TokenAlreadyCancelled_ThrowsBeforeEvaluatingAnyRule()
    {
        var evaluations = 0;
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/counted")
            .Enforce(arch.Types.Must(_ =>
            {
                evaluations++;
                return true;
            }, description: "be counted"))
            .Because("b"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // The guard on the claim below: an unchanged count has to mean "the rule was not evaluated", and
        // over a bed whose types this subject did not select it would mean "there was nothing to evaluate".
        Check(model, CancellationToken.None);
        int evaluatedByAWholeRun = evaluations;
        evaluatedByAWholeRun.ShouldBeGreaterThan(0);

        Should.Throw<OperationCanceledException>(() => Check(model, cancelled.Token));

        evaluations.ShouldBe(evaluatedByAWholeRun);
    }

    [Fact]
    public void Check_DelegateCancelsTheRunAsItEvaluates_StopsBeforeTheNextRule()
    {
        using var source = new CancellationTokenSource();
        var first = 0;
        var second = 0;
        ArchitectureModel model = Checker.Model(arch =>
        {
            arch.Rule("area/cancels")
                .Enforce(arch.Types.Must(_ =>
                {
                    first++;
                    // The predicate runs only inside the Check call below, which returns before the source
                    // leaves scope, so the captured source is never used after it is disposed.
                    // ReSharper disable once AccessToDisposedClosure
                    source.Cancel();
                    return true;
                }, description: "cancel the run"))
                .Because("b");

            arch.Rule("area/never-reached")
                .Enforce(arch.Types.Must(_ =>
                {
                    second++;
                    return true;
                }, description: "be counted"))
                .Because("b");
        });

        Should.Throw<OperationCanceledException>(() => Check(model, source.Token));

        // The per-rule grain, pinned without a race: the first rule ran to its end and the second was never
        // entered, which is what "the token is read once, before each rule" means.
        first.ShouldBeGreaterThan(0);
        second.ShouldBe(0);
    }

    [Fact]
    public void Check_DelegateThrowsOperationCanceled_PropagatesInsteadOfReportingARuleError()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/throws-cancellation")
            .Enforce(arch.Types.Must(_ => throw new OperationCanceledException(), description: "abandon the run"))
            .Because("b"));

        // Throwing is the whole claim: a rule error would have come back as a report, so no assertion on a
        // result can state this one.
        Should.Throw<OperationCanceledException>(() => ArchChecker.Check(model, Sources.LayeredModel));
    }

    [Fact]
    public void Check_DelegateThrowsAnythingElse_IsStillContainedAsARuleError()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/throws")
            .Enforce(arch.Types.Must(_ => throw new InvalidOperationException("boom"), description: "fault"))
            .Because("b"));

        ArchChecker.Check(model, Sources.LayeredModel)
            .Single()
            .ShouldHaveFailedWithDetailContaining(
                ViolationKind.RuleError, "the `Must` predicate threw InvalidOperationException", "boom");
    }

    [Fact]
    public void Check_FaultThatIsNotAnEvaluationRefusal_IsContainedUnderTheBlanketWording()
    {
        // The blanket arm's own wording, which no other row holds. A throwing delegate cannot reach it — the
        // selection evaluator translates one into an evaluation refusal first — so the route is an unhandled
        // vocabulary member, whose fail-closed throw arrives as the unexpected fault the arm is for.
        var selection = new RefinedSelection(new Arch(), TypesNoun.Instance, [new UnhandledAdjective()]);
        ArchitectureModel model = Checker.HandBuilt("area/unhandled", selection.MustHavePrefix("I"));

        ArchChecker.Check(model, Sources.LayeredModel)
            .Single()
            .ShouldHaveFailedWithDetailContaining(
                ViolationKind.RuleError, "Rule evaluation failed: Unhandled selection adjective 'UnhandledAdjective'.");
    }

    // The eight-argument seam, spelled once: every row above is about the token, and the five arguments
    // between the codebase and it are what a whole-solution run over no baselines and no path globs passes.
    private static CheckReport Check(ArchitectureModel model, CancellationToken cancellationToken)
    {
        return ArchChecker.Check(
            model.Rules, Sources.LayeredModel, BaselineIndex.Empty, null, null, null, null, cancellationToken);
    }

    // An adjective no evaluator switch handles, so resolving the selection throws where the vocabulary runs
    // out. Nested here rather than shared, because the fail-closed rows own the same shape for their own
    // claim and a shared one would tie two unrelated pins together.
    private sealed class UnhandledAdjective : SelectionAdjective
    {
        internal override AdjectivePlacement Placement => AdjectivePlacement.Inline;

        internal override string Fragment => string.Empty;
    }
}
