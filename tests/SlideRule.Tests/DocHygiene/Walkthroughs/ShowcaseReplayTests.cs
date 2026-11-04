using SlideRule.Tests.Cli;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     The showcase page's output fences, re-run over the pinned eShopOnWeb fork by this commit's code. Each fence
///     must be what the run prints, cut as the page says it is cut, and a stale fence reds printing the text to paste.
/// </summary>
/// <remarks>
///     Every case skips unless <see cref="ShowcaseCheckout.RootVariable" /> names a checkout of the fork, for the
///     reason <see cref="ShowcaseCheckout" /> gives; <see cref="ShowcasePinTests" /> holds CI's showcase job to
///     naming one and running this class.
/// </remarks>
public sealed class ShowcaseReplayTests
{
    /// <summary>Every showcase fence, one row each.</summary>
    public static TheoryData<string> Fences => new(WalkthroughFences.Showcase.Select(static fence => fence.Fence.Key));

    [Theory]
    [MemberData(nameof(Fences))]
    public async Task Fence_IsWhatTheShowcaseRunPrints(string key)
    {
        // Arrange
        ShowcaseCheckout.ShouldBeAtThePin();
        ReplayedFence fence = WalkthroughFences.ShowcaseNamed(key);

        // Act
        ExampleRun run = await ExampleReplay.ShowcaseRun();

        // Assert
        fence.ShouldMatchItsRun(run);
    }

    /// <summary>
    ///     The page quotes <c>check</c> and <c>status</c> as the verbs print them and the rest as the replay composes
    ///     it, so over the pinned tree the two must agree: the same model, and the composed report under the composed
    ///     unsupported-projects stamp is what each verb printed.
    /// </summary>
    [Fact]
    public async Task Composition_PrintsWhatTheVerbPrints()
    {
        // Arrange
        ShowcaseCheckout.ShouldBeAtThePin();

        // Act
        ExampleRun run = await ExampleReplay.ShowcaseRun();

        // Assert
        run.Committed.ShouldModelTheSameAs(run.Governed);
        run.CheckVerb.ShouldSucceed()
            .ShouldPrintVerbatim($"{run.CheckPrinted}\n");
        run.StatusVerb.ShouldSucceed()
            .ShouldPrintVerbatim($"{run.StatusPrinted}\n");
    }
}
