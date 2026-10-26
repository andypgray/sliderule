using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     Which literals the caret can be inside of, and where inside one it has to be.
/// </summary>
/// <remarks>
///     Every span this provider hands a host is in the document's own coordinates, so the shapes where the
///     content text and the source text stop agreeing character for character are refused rather than
///     approximated: a segment span computed from one would land somewhere else in the other, and a commit
///     would overwrite the wrong characters. None of the refused shapes can spell a namespace or a type
///     name, so refusing them costs an author nothing.
/// </remarks>
public sealed class LiteralSurfaceTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Fact]
    public async Task InAVerbatimLiteral_OffersTheSameNames()
    {
        // Act: the content opens two characters in rather than one, and everything after that is the same
        // text at the same positions.
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace(@"MyApp.$$");""", Ct);

        // Assert
        offered.ShouldOffer("Domain", "Domain.*");
    }

    [Fact]
    public async Task InAnUnterminatedLiteral_OffersTheSameNames()
    {
        // Act: deliberately not held to compiling — this is the shape the caret is in the moment the
        // opening quote is typed, and there is no closing quote to subtract from the content.
        OfferedList offered = await fixture.OfferedAsync("""arch.Namespace("MyApp.$$""", Ct);

        // Assert
        offered.ShouldOffer("Domain", "Domain.*");
    }

    [Theory]
    // An interpolated literal: one with no holes is a constant and reaches the slot, but its token is not a
    // string literal and its parts are not at the offsets a segment walk assumes.
    [InlineData("""arch.Namespace($"MyApp.$$");""")]
    // A raw literal.
    [InlineData(""""arch.Namespace("""MyApp.$$""");"""")]
    // One half of a concatenation: what reaches the slot there is a computed value, and completing one half
    // would offer names for a string the author is building rather than for the whole one the slot receives.
    [InlineData("""arch.Namespace("MyApp." + "$$");""")]
    // Past the closing quote.
    [InlineData("""arch.Namespace("MyApp.Domain"$$);""")]
    // A literal carrying a backslash: an escape is where the content stops agreeing character for character
    // with the source, so every span after it would be computed one place off.
    [InlineData("""arch.Namespace("MyApp\\.$$");""")]
    public async Task ARefusedShape_OffersNothing(string body)
    {
        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(body, Ct);

        // Assert
        offered.ShouldOfferNothing();
    }
}
