using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The showcase page's quotes of the pinned eShopOnWeb fork, each held to its file in a checkout of the pinned
///     commit as <see cref="QuoteSyncTests" /> holds this repository's quotes to theirs.
/// </summary>
/// <remarks>
///     Every case skips unless <see cref="ShowcaseCheckout.RootVariable" /> names a checkout, for the reason
///     <see cref="ShowcaseCheckout" /> gives.
/// </remarks>
public sealed class ShowcaseExcerptTests
{
    /// <summary>Every showcase excerpt, one row each.</summary>
    public static TheoryData<string> Excerpts => new(QuoteSyncTests.ShowcaseExcerpts.Select(static excerpt => excerpt.Key));

    [Theory]
    [MemberData(nameof(Excerpts))]
    public void Excerpt_QuotesThePinnedFork(string key)
    {
        // Arrange
        ShowcaseCheckout.ShouldBeAtThePin();
        QuoteSyncTests.Excerpt excerpt = QuoteSyncTests.ShowcaseExcerpts.Single(candidate => candidate.Key == key);
        QuotedFence fence = QuotedFence.ShouldFindOne(excerpt.Doc, excerpt.Marker, $"where the {key} excerpt should be exactly one");

        // Act
        string? drift = QuoteSyncTests.DriftOf(excerpt, fence.Lines, ShowcaseCheckout.LineReader());

        // Assert
        drift.ShouldBeNull($"{excerpt.Doc}:{fence.Line} no longer quotes the pinned fork");
    }
}
