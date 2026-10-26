using Shouldly;
using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The gate that decides whether a keystroke is worth a completion request at all: one truth table over
///     the character typed and where the caret is.
/// </summary>
/// <remarks>
///     <para>
///         The gate runs before anything is parsed, because a host asks it on every keystroke in every C#
///         file open in the editor. So it reads one line of raw text and is allowed to be the wider of the
///         two readers: a keystroke it lets through costs one request that finds nothing, while a
///         keystroke it turns away is a list that never appears.
///     </para>
///     <para>
///         Asked of the provider rather than of the completion service, which says yes whenever any
///         provider says yes — Roslyn's own symbol completion triggers on a dot everywhere, so a table over
///         the service would be a table about Roslyn. That the service reaches this gate at all is the last
///         fact here, driven through a real insertion.
///     </para>
/// </remarks>
public sealed class TriggerTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Theory]
    // The two characters that open and continue a name, inside a literal. The dot is the case the whole
    // segment-by-segment walk depends on.
    [InlineData("""arch.Namespace("$$");""", '"', true)]
    [InlineData("""arch.Namespace("MyApp.$$");""", '.', true)]
    [InlineData("""arch.Namespace(@"MyApp.$$");""", '.', true)]
    // A letter never opens a list: an author typing ordinary prose in any string in the solution would
    // otherwise pay for a request on every keystroke.
    [InlineData("""arch.Namespace("MyAppD$$");""", 'D', false)]
    // The quote that closes a literal leaves the caret outside it.
    [InlineData("""arch.Namespace("MyApp"$$);""", '"', false)]
    // A dot outside a literal is somebody else's completion.
    [InlineData("""arch.Types.$$""", '.', false)]
    // A quote inside a comment opens no literal, so the dot after it is still outside one.
    [InlineData("""// see "MyApp.$$""", '.', false)]
    // An escaped character does not close the literal it sits in, in either spelling. The scan has to
    // step over both, or a name written after one would open no list.
    [InlineData("""arch.Namespace("MyApp\\.$$");""", '.', true)]
    [InlineData(""""arch.Namespace(@"a""b.$$");"""", '.', true)]
    public void TheTrigger_OpensAListOnlyInsideALiteralAndOnlyForAQuoteOrADot(
        string body, char typed, bool expected)
    {
        // Act
        bool triggers = SpecCompletionFixture.Triggers(body, typed);

        // Assert
        triggers.ShouldBe(expected,
            $"Typing '{typed}' here:\n{SpecCompletionFixture.Compose(body)}");
    }

    [Fact]
    public async Task OnARealInsertion_TheServiceReachesTheProvider()
    {
        // Act: a host filters providers by this gate before asking any of them, so a list that comes back
        // for an insertion trigger is the gate and the provider agreeing end to end.
        OfferedList offered =
            await fixture.OfferedOnInsertionAsync("""arch.Namespace("MyApp.$$");""", '.', Ct);

        // Assert
        offered.ShouldOffer("Domain", "Domain.*");
    }
}
