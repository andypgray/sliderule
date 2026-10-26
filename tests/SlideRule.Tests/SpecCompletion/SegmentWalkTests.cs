using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The walk itself: which segment of a dotted literal the caret is in, what is offered under the part
///     already written, and what a commit replaces.
/// </summary>
/// <remarks>
///     A name is written one segment at a time, so the fact worth holding at each step is that the offer is
///     relative to what is already there and that committing replaces the segment rather than appending to
///     a half-typed one. The two forms a namespace slot offers — the step and the subtree — are one pair
///     deliberately: a bare <c>*</c> committed at <c>"MyApp.Dom"</c> would replace what was typed, and
///     <c>Domain.*</c> is what was meant.
/// </remarks>
public sealed class SegmentWalkTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Fact]
    public async Task AtTheRoot_OffersTheTopLevelNamespacesAndTheLoneStar()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("$$");""", Ct);

        // Assert
        offered.ShouldOffer("MyApp", "MyApp.*", "*");
    }

    [Fact]
    public async Task AfterANamespace_OffersTheChildrenOfThatNamespaceAndNothingElse()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);

        // Assert: the whole list, so the spec project's own MyApp.Architecture, which holds nothing a rule
        // could anchor on, is not in it.
        offered.ShouldOfferExactly("Domain", "Domain.*", "Web", "Web.*", "*");
    }

    [Fact]
    public async Task EveryNamespace_IsOfferedAsAStepAndAsASubtree()
    {
        // Act
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.Domain.$$");""", Ct);

        // Assert
        offered.ShouldOfferExactly("Orders", "Orders.*", "*");
    }

    [Fact]
    public async Task MidSegment_CommitsOverTheWholeSegmentRatherThanAppendingToIt()
    {
        // Arrange
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.Dom$$ain");""", Ct);

        // Act, Assert
        await offered.ShouldInsertAsync("Domain.*", "MyApp.Domain.*", Ct);
    }

    [Fact]
    public async Task WithASegmentAfterTheCaret_CommitsOverThatSegmentAndLeavesTheRest()
    {
        // Arrange: the caret is in the middle of a name that already has more written after it, which
        // is what fixing a typo in a committed spec looks like.
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.Doma$$in.Orders");""", Ct);

        // Act, Assert: the segment ends at the next dot, so what follows it survives the commit.
        await offered.ShouldInsertAsync("Domain", "MyApp.Domain.Orders", Ct);
    }

    [Fact]
    public async Task APathThatResolvesToNothing_OffersNothing()
    {
        // Act: a misspelt segment earlier in the literal leaves nothing to look the next one up under,
        // and a list of the top-level names here would be a list for a path the author is not on.
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("NoSuchRoot.$$");""", Ct);

        // Assert
        offered.ShouldOfferNothing();
    }

    [Fact]
    public async Task ATypeSlot_OffersTheNamespacesButNoSubtreeFormAndNoLoneStar()
    {
        // Act
        OfferedList offered =
            await fixture.ShouldCompileAndOfferAsync("""arch.Types.Implementing("MyApp.$$");""", Ct);

        // Assert
        offered.ShouldOffer("Domain", "Web");
        offered.ShouldNotOffer("Domain.*", "Web.*", "*");
    }

    [Fact]
    public async Task UnderANonGenericType_OffersItsNestedTypes()
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.Returning("MyApp.Domain.InvoiceEntity.$$");""", Ct);

        // Assert
        offered.ShouldOfferExactly("Line");
    }

    [Fact]
    public async Task UnderAGenericType_OffersItsNestedTypes()
    {
        // Act: the path carries the outer type's declared parameters, because that is how the list spelled
        // it one segment ago — resolving what was written is what keeps the two halves one name.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.DerivedFrom("MyApp.Domain.Container<T>.$$");""", Ct);

        // Assert
        offered.ShouldOfferExactly("Entry");
    }

    [Fact]
    public async Task AWildcardInThePath_OffersNothing()
    {
        // Act: everything after a star is a glob rather than a name, so there is no namespace to look the
        // next segment up under.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.*.$$");""", Ct);

        // Assert
        offered.ShouldOfferNothing();
    }
}
