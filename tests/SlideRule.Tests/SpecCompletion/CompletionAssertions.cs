using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Tests.SpecAnalyzers;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The Shouldly surface over one completion request: what was offered, in what order, with what tags,
///     what a commit writes, and which characters commit or filter.
/// </summary>
/// <remarks>
///     Every failure message carries the whole subject — the half-written source with its caret, and every
///     name that was offered — because the first question a red raises is what an author would have seen,
///     and a count answers none of it. Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason
///     <c>RuleResultAssertions</c> records: leaving it off makes an inner failure name the read under test
///     rather than the caller's local.
/// </remarks>
internal static class CompletionAssertions
{
    /// <summary>
    ///     Asserts the spec compiles and returns what was offered at the caret.
    /// </summary>
    /// <remarks>
    ///     A call that does not bind produces no invocation operation, so a typo in a body would come back
    ///     as the provider offering nothing — and every fact here whose whole claim is silence would pass on
    ///     the strength of the typo. Checking the compilation first is what makes those mean what they say.
    /// </remarks>
    /// <param name="fixture">The workspace the request is made in.</param>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="cancellationToken">The running test's token.</param>
    internal static async Task<OfferedList> ShouldCompileAndOfferAsync(
        this SpecCompletionFixture fixture, string body, CancellationToken cancellationToken)
    {
        OfferedList offered = await fixture.OfferedAsync(body, cancellationToken);
        offered.Compilation.ShouldHaveNothingToCompileAbout(cancellationToken);

        return offered;
    }

    /// <summary>Asserts nothing at all was offered.</summary>
    internal static void ShouldOfferNothing(this OfferedList offered)
    {
        offered.Texts.ShouldBeEmpty(offered.Describe("Expected no names here"));
    }

    /// <summary>Asserts exactly these names were offered, in any order.</summary>
    /// <param name="offered">What came back.</param>
    /// <param name="names">The names, which are the whole list.</param>
    internal static void ShouldOfferExactly(this OfferedList offered, params string[] names)
    {
        offered.Texts
            .Order(StringComparer.Ordinal)
            .ShouldBe(names.Order(StringComparer.Ordinal), offered.Describe("Expected exactly these names"));
    }

    /// <summary>Asserts each of these names is among what was offered.</summary>
    /// <param name="offered">What came back.</param>
    /// <param name="names">The names that have to be there.</param>
    internal static void ShouldOffer(this OfferedList offered, params string[] names)
    {
        List<string> missing = names.Where(name => !offered.Texts.Contains(name, StringComparer.Ordinal))
            .ToList();

        missing.ShouldBeEmpty(offered.Describe($"Expected these to be offered: {string.Join(", ", names)}"));
    }

    /// <summary>Asserts none of these names was offered.</summary>
    /// <param name="offered">What came back.</param>
    /// <param name="names">The names that must not be there.</param>
    internal static void ShouldNotOffer(this OfferedList offered, params string[] names)
    {
        List<string> present = names.Where(name => offered.Texts.Contains(name, StringComparer.Ordinal))
            .ToList();

        present.ShouldBeEmpty(offered.Describe($"Expected none of these: {string.Join(", ", names)}"));
    }

    /// <summary>
    ///     Asserts these names were offered and that they sit in this order relative to each other.
    /// </summary>
    /// <remarks>
    ///     Relative rather than absolute, because the list is the whole product and the whole framework and
    ///     a fact about ranking is a fact about two rows, not about the hundreds between them.
    /// </remarks>
    /// <param name="offered">What came back.</param>
    /// <param name="names">The names, in the order they have to appear in.</param>
    internal static void ShouldOfferInThisOrder(this OfferedList offered, params string[] names)
    {
        offered.ShouldOffer(names);

        List<int> positions = names.Select(name => IndexOf(offered, name))
            .ToList();

        string ranks = string.Join(", ", positions);
        ranks.ShouldBe(
            string.Join(", ", positions.Order()),
            offered.Describe($"Expected {string.Join(" before ", names)}, which came back at {ranks}"));
    }

    /// <summary>Asserts the row for this name carries this tag, which is the glyph a host draws.</summary>
    /// <param name="offered">What came back.</param>
    /// <param name="name">The offered name.</param>
    /// <param name="tag">The tag, from the host's own well-known set.</param>
    internal static void ShouldOfferWithTag(this OfferedList offered, string name, string tag)
    {
        CompletionItem item = offered.Pick(name);

        item.Tags.ShouldContain(tag, offered.Describe($"Expected '{name}' to be tagged {tag}"));
    }

    /// <summary>
    ///     Asserts that committing this name leaves the literal reading exactly this.
    /// </summary>
    /// <remarks>
    ///     Driven through the service's own commit path rather than by reading the item, because the item's
    ///     display text is deliberately not the text a commit writes and the gap between them is the whole
    ///     reason the provider computes the edit itself.
    /// </remarks>
    /// <param name="offered">What came back.</param>
    /// <param name="name">The name to commit.</param>
    /// <param name="literal">The literal's content afterwards, quotes excluded.</param>
    /// <param name="cancellationToken">The running test's token.</param>
    internal static async Task ShouldInsertAsync(
        this OfferedList offered, string name, string literal, CancellationToken cancellationToken)
    {
        CompletionItem item = offered.Pick(name);
        CompletionChange change =
            await offered.Service.GetChangeAsync(offered.Document, item, commitCharacter: null, cancellationToken);

        SourceText before = await offered.Document.GetTextAsync(cancellationToken);
        SourceText after = before.WithChanges(change.TextChange);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            after, SpecSnippet.Language, cancellationToken: cancellationToken);
        SyntaxToken token = tree.GetRoot(cancellationToken)
            .FindToken(change.TextChange.Span.Start);

        ContentOf(token)
            .ShouldBe(literal, offered.Describe($"Committing '{name}' left \"{ContentOf(token)}\""));
    }

    /// <summary>
    ///     Asserts these are exactly the characters that commit this row, as a host computes them.
    /// </summary>
    /// <remarks>
    ///     The host's default set is the starting point and the item's rules are folded over it, which is
    ///     what makes an empty expectation mean "nothing but enter and tab" rather than "no rules declared".
    ///     The default set carries a quote, a dot, a comma, a star and an angle bracket among many others,
    ///     so a row that declared nothing would commit on every one of them.
    /// </remarks>
    /// <param name="offered">What came back.</param>
    /// <param name="name">The offered name.</param>
    /// <param name="characters">The characters, and no others.</param>
    internal static void ShouldCommitOnly(this OfferedList offered, string name, params char[] characters)
    {
        CompletionItem item = offered.Pick(name);
        string commits = Folded(
            offered.Service.GetRules().DefaultCommitCharacters, item.Rules.CommitCharacterRules);
        string expected = Folded([], [CharacterSetModificationRule.Create(CharacterSetModificationKind.Add, characters)]);

        commits.ShouldBe(
            expected,
            offered.Describe($"Expected '{name}' to commit only on [{expected}] and it commits on [{commits}]"));
    }

    /// <summary>
    ///     Asserts these are exactly the characters the row adds to the host's filter set — the characters
    ///     an author can type without the list closing.
    /// </summary>
    /// <param name="offered">What came back.</param>
    /// <param name="name">The offered name.</param>
    /// <param name="characters">The characters, and no others.</param>
    internal static void ShouldFilterOn(this OfferedList offered, string name, params char[] characters)
    {
        CompletionItem item = offered.Pick(name);
        string filters = Folded([], item.Rules.FilterCharacterRules);
        string expected = Folded([], [CharacterSetModificationRule.Create(CharacterSetModificationKind.Add, characters)]);

        filters.ShouldBe(
            expected,
            offered.Describe($"Expected '{name}' to filter on [{expected}] and it filters on [{filters}]"));
    }

    /// <summary>Asserts typing this character at the caret opens a list.</summary>
    /// <param name="fixture">The workspace the request is made in.</param>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="typed">The character the author typed.</param>
    internal static void ShouldTriggerOn(this SpecCompletionFixture fixture, string body, char typed)
    {
        SpecCompletionFixture.Triggers(body, typed)
            .ShouldBeTrue($"Typing '{typed}' here has to open a list:\n{SpecCompletionFixture.Compose(body)}");
    }

    /// <summary>Asserts typing this character at the caret opens nothing.</summary>
    /// <param name="fixture">The workspace the request is made in.</param>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="typed">The character the author typed.</param>
    internal static void ShouldNotTriggerOn(this SpecCompletionFixture fixture, string body, char typed)
    {
        SpecCompletionFixture.Triggers(body, typed)
            .ShouldBeFalse($"Typing '{typed}' here must open nothing:\n{SpecCompletionFixture.Compose(body)}");
    }

    // The one row carrying this name, or a failure naming every row there was.
    private static CompletionItem Pick(this OfferedList offered, string name)
    {
        List<CompletionItem> matching = offered.Items
            .Where(item => string.Equals(OfferedList.TextOf(item), name, StringComparison.Ordinal))
            .ToList();

        return matching.ShouldHaveSingleItem(offered.Describe($"Looking for exactly one row named '{name}'"));
    }

    private static int IndexOf(OfferedList offered, string name)
    {
        for (var index = 0; index < offered.Texts.Count; index++)
            if (string.Equals(offered.Texts[index], name, StringComparison.Ordinal))
                return index;

        return -1;
    }

    // A host applies each of an item's rules to its own default set in order, and the last word belongs to
    // whichever rule came last. Reproduced here rather than asserted over the rules themselves, because
    // what matters is the set an author ends up typing against.
    private static string Folded(
        IEnumerable<char> defaults, IEnumerable<CharacterSetModificationRule> rules)
    {
        SortedSet<char> characters = new(defaults);
        foreach (CharacterSetModificationRule rule in rules)
            switch (rule.Kind)
            {
                case CharacterSetModificationKind.Add:
                    characters.UnionWith(rule.Characters);
                    break;
                case CharacterSetModificationKind.Remove:
                    characters.ExceptWith(rule.Characters);
                    break;
                default:
                    characters.Clear();
                    characters.UnionWith(rule.Characters);
                    break;
            }

        return new string(characters.ToArray());
    }

    // What a string-literal token says, quotes and verbatim marker excluded.
    private static string ContentOf(SyntaxToken token)
    {
        string text = token.Text;
        int opening = text.Length > 0 && text[0] == '@' ? 2 : 1;
        if (text.Length < opening) return text;

        bool closed = text.Length > opening && text[text.Length - 1] == '"';

        return text.Substring(opening, (closed ? text.Length - 1 : text.Length) - opening);
    }
}
