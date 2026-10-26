using Shouldly;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The families <see cref="ViolationKinds" /> sorts <see cref="ViolationKind" /> into: every kind is an edge,
///     a shape or one of the two kinds about the rule rather than the code, and no kind is two of those.
/// </summary>
public sealed class ViolationKindsTests
{
    [Fact]
    public void KindsThatAreNeitherEdgeNorShape_AreTheTwoThatCarryNoSite()
    {
        ViolationKind[] neither = Enum.GetValues<ViolationKind>()
            .Where(kind => !kind.IsEdge() && !kind.IsShape())
            .ToArray();

        neither.ShouldBe([ViolationKind.EmptySubject, ViolationKind.RuleError], ignoreOrder: true);
    }

    [Fact]
    public void NoKind_IsBothAnEdgeAndAShape()
    {
        ViolationKind[] both = Enum.GetValues<ViolationKind>()
            .Where(kind => kind.IsEdge() && kind.IsShape())
            .ToArray();

        both.ShouldBeEmpty();
    }

    [Fact]
    public void TheOnlyEdgeThatIsNotATypeEdge_IsMemberUse()
    {
        ViolationKind[] memberEdges = Enum.GetValues<ViolationKind>()
            .Where(kind => kind.IsEdge() && !kind.IsTypeEdge())
            .ToArray();

        memberEdges.ShouldBe([ViolationKind.MemberUse]);
    }
}
