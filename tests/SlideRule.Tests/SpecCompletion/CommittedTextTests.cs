using Microsoft.CodeAnalysis.Completion;
using Shouldly;
using SlideRule.SpecCompletion;
using Xunit;

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
    // The commit and filter sets are spelled one character apiece, so a trailing space is the space.
    [Theory]
    // A namespace step commits on a dot and writes its own name: the dot commits the step and re-triggers
    // the next segment, which is the whole of how a name is written one piece at a time.
    [InlineData("""arch.Namespace("MyApp.$$");""", "Domain", "MyApp.Domain", ".", "*<, ")]
    // A subtree form commits on nothing and writes its trailing star: a dot after it would mean nothing, so
    // it neither commits it nor closes the list.
    [InlineData("""arch.Namespace("MyApp.$$");""", "Domain.*", "MyApp.Domain.*", "", ".*<, ")]
    // The lone star commits on nothing and writes just the star.
    [InlineData("""arch.Namespace("MyApp.$$");""", "*", "MyApp.*", "", ".*")]
    // A non-generic type commits on a dot, so a nested type can follow.
    [InlineData("""arch.Types.Methods.Returning("MyApp.Domain.$$");""", "EntityBase", "MyApp.Domain.EntityBase", ".",
        "*<, ")]
    // A simple name commits on nothing, because a dot would make it match nothing.
    [InlineData("""arch.Types.Named("$$");""", "Order", "Order", "", ".*<, ")]
    public async Task ARow_WritesItsNameAndCommitsAndFiltersOnItsOwnCharacters(
        string body, string name, string written, string commits, string filters)
    {
        // Arrange
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync(body, Ct);

        // Act, Assert
        await offered.ShouldInsertAsync(name, written, Ct);
        offered.ShouldCommitOnly(name, commits.ToCharArray());
        offered.ShouldFilterOn(name, filters.ToCharArray());
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
