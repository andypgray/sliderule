using Microsoft.CodeAnalysis.Completion;
using Shouldly;
using Xunit;
using SlideRule.SpecCompletion;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     What a commit writes, and which characters commit at all.
/// </summary>
/// <remarks>
///     <para>
///         <b>The host's default commit set is the danger.</b> It carries a quote, a dot, a comma, a star
///         and an angle bracket among many others, and every one of those is a character an author types
///         next to a name. A row that declared nothing would commit the highlighted item on any of them
///         and write a name nobody chose — and a glob that names something else still loads clean and
///         still checks green, so nothing downstream would ever say so.
///     </para>
///     <para>
///         <b>A generic shows its name and its parameters as two pieces.</b> That is what makes the list
///         read as a list of names rather than of signatures, and it is why the edit is computed rather
///         than taken from the display text: the default commit would write the name alone and leave an
///         anchor that matches nothing.
///     </para>
/// </remarks>
public sealed class CommittedTextTests(SpecCompletionFixture fixture) : IClassFixture<SpecCompletionFixture>
{
    [Fact]
    public async Task ANamespaceStep_CommitsOnADotAndWritesItsOwnName()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);

        // Act, Assert: the dot commits the step and re-triggers the next segment, which is the whole of
        // how a name is written one piece at a time.
        await offered.ShouldInsertAsync("Domain", "MyApp.Domain", Ct);
        offered.ShouldCommitOnly("Domain", '.');
        offered.ShouldFilterOn("Domain", '*', '<', ',', ' ');
    }

    [Fact]
    public async Task ASubtreeForm_CommitsOnNothingAndWritesItsTrailingStar()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);

        // Act, Assert: a dot after a subtree form would mean nothing, so it neither commits it nor closes
        // the list.
        await offered.ShouldInsertAsync("Domain.*", "MyApp.Domain.*", Ct);
        offered.ShouldCommitOnly("Domain.*");
        offered.ShouldFilterOn("Domain.*", '.', '*', '<', ',', ' ');
    }

    [Fact]
    public async Task TheLoneStar_CommitsOnNothingAndWritesJustTheStar()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Namespace("MyApp.$$");""", Ct);

        // Act, Assert
        await offered.ShouldInsertAsync("*", "MyApp.*", Ct);
        offered.ShouldCommitOnly("*");
        offered.ShouldFilterOn("*", '.', '*');
    }

    [Fact]
    public async Task ANonGenericType_CommitsOnADotSoANestedTypeCanFollow()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Methods.Returning("MyApp.Domain.$$");""", Ct);

        // Act, Assert
        await offered.ShouldInsertAsync("EntityBase", "MyApp.Domain.EntityBase", Ct);
        offered.ShouldCommitOnly("EntityBase", '.');
        offered.ShouldFilterOn("EntityBase", '*', '<', ',', ' ');
    }

    [Fact]
    public async Task AGenericType_WritesItsTypeParametersAndNotJustItsName()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Implementing("MyApp.Domain.$$");""", Ct);

        // Act, Assert: the row reads IHandler with <T> beside it, and what it writes is the whole name an
        // anchor is compared against.
        await offered.ShouldInsertAsync("IHandler<T>", "MyApp.Domain.IHandler<T>", Ct);
        await offered.ShouldInsertAsync("IMap<TKey, TValue>", "MyApp.Domain.IMap<TKey, TValue>", Ct);
        offered.ShouldCommitOnly("IHandler<T>");
        offered.ShouldFilterOn("IHandler<T>", '.', '*', '<', '>', ',', ' ');
    }

    [Fact]
    public async Task ASimpleName_CommitsOnNothingBecauseADotWouldMakeItMatchNothing()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Types.Named("$$");""", Ct);

        // Act, Assert
        await offered.ShouldInsertAsync("Order", "Order", Ct);
        offered.ShouldCommitOnly("Order");
        offered.ShouldFilterOn("Order", '.', '*', '<', ',', ' ');
    }

    [Fact]
    public void AnItemThisProviderDidNotMake_CommitsItsOwnDisplayText()
    {
        // Arrange: the same override answers for every item the host hands it, so the fall-back is not
        // dead code. An item from anywhere else carries none of the properties the edit is computed
        // from, and writing its display text over its own span is what the base implementation does.
        var foreign = CompletionItem.Create("Whatever");

        // Act
        CompletionChange change = SlotItems.ChangeFor(foreign);

        // Assert
        change.TextChange.NewText.ShouldBe("Whatever");
    }

    [Fact]
    public async Task CommittingMidSegment_LeavesNoneOfWhatWasTyped()
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(
            """arch.Types.Implementing("MyApp.Domain.IHan$$dler<T>");""", Ct);

        // Act, Assert
        await offered.ShouldInsertAsync("IMap<TKey, TValue>", "MyApp.Domain.IMap<TKey, TValue>", Ct);
    }
}
