using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Tests.TestSupport;
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
        Should.Throw<OperationCanceledException>(() => Harness.Check(model, Sources.LayeredModel));
    }

    [Fact]
    public void Check_DelegateThrowsAnythingElse_IsStillContainedAsARuleError()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/throws")
            .Enforce(arch.Types.Must(_ => throw new InvalidOperationException("boom"), description: "fault"))
            .Because("b"));

        Harness.Check(model, Sources.LayeredModel)
            .Single()
            .ShouldHaveFailedWithDetailContaining(
                ViolationKind.RuleError, "the `Must` predicate threw InvalidOperationException", "boom");
    }

    // The eight-argument seam, spelled once: every row above is about the token, and the five arguments
    // between the codebase and it are what a whole-solution run over no baselines and no path globs passes.
    private static CheckReport Check(ArchitectureModel model, CancellationToken cancellationToken)
    {
        return ArchChecker.Check(
            model.Rules, Sources.LayeredModel, BaselineIndex.Empty, null, null, null, null, cancellationToken);
    }
}
