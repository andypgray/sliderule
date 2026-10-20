using Shouldly;
using SlideRule.Baselines;
using Xunit;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     <see cref="RuleBaseline" /> matching over a section whose file holds more than one line for an identity:
///     the allowance in force is the one a write would record, whichever line comes first.
/// </summary>
public sealed class RuleBaselineTests
{
    [Theory]
    [InlineData(5, 3)]
    [InlineData(3, 5)]
    public void TryMatch_TwoLinesForOneEdge_ReturnsTheSmallerCount(int firstCount, int secondCount)
    {
        BaselineEntry identity = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        var section = new RuleBaseline([identity.WithSiteCount(firstCount), identity.WithSiteCount(secondCount)]);

        section.TryMatch(identity, out BaselineEntry? stored).ShouldBeTrue();

        stored.ShouldNotBeNull().SiteCount.ShouldBe(3);
        section.Count.ShouldBe(1);
    }
}
