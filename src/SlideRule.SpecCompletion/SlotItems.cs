using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.SpecCompletion;

/// <summary>
///     Turns an offered name into the item a host shows, and turns a committed item back into the edit that
///     writes it.
/// </summary>
/// <remarks>
///     <para>
///         Visual Studio reads an item's commit and filter rules both; the language server behind the other
///         editors reads commit rules and has no filter-character concept at all. So the rules are written to
///         be right when only the commit half is read: a dot commits a namespace step or a non-generic type
///         and re-triggers the next segment, and no other typed character commits anything. A <c>*</c>, a
///         <c>&lt;</c>, a comma, a space and a quote are each a character an author types next to a name, and
///         any one of them committing the highlighted row would write a name nobody chose. That failure is
///         silent, because a glob that names something else still checks green.
///     </para>
///     <para>
///         The edit is computed rather than taken from the display text. A generic type shows its
///         name and its type parameters as two pieces, so that the list reads as a list of names; the
///         default commit writes only the display text and would drop the parameters, leaving a name that
///         matches nothing. The span and the text to write are both carried on the item, because that is
///         what survives a host that serialises an item and hands it back.
///     </para>
/// </remarks>
internal static class SlotItems
{
    /// <summary>The text a commit writes, carried on the item because the display text is not it.</summary>
    /// <remarks>
    ///     Internal rather than private because it is also what tells this provider's items apart from the
    ///     host's own in a list: the name a host stamps on an item is not public, and every item from here
    ///     carries this property and nothing else does.
    /// </remarks>
    internal const string InsertProperty = "insert";

    /// <summary>Where the segment this item replaces starts.</summary>
    private const string StartProperty = "start";

    /// <summary>How long that segment is.</summary>
    private const string LengthProperty = "length";

    /// <summary>What a row shows instead of an assembly where it names a subtree rather than one name.</summary>
    private const string SubtreeDescription = "subtree";

    private static readonly CompletionItemRules NamespaceStepRules =
        Rules(commit: ['.'], filter: ['*', '<', ',', ' ']);

    private static readonly CompletionItemRules SubtreeRules =
        Rules(commit: [], filter: ['.', '*', '<', ',', ' ']);

    private static readonly CompletionItemRules LoneStarRules =
        Rules(commit: [], filter: ['.', '*']);

    private static readonly CompletionItemRules TypeRules =
        Rules(commit: ['.'], filter: ['*', '<', ',', ' ']);

    // A generic row's text goes on past its name: an author may type the angle brackets and what goes in them,
    // and a constructed spelling puts dots inside them (IHandler<MyApp.Order>). So a dot neither commits this
    // row, which would write the declared parameters over the brackets being typed, nor closes the list, and
    // '>' is a filter character on this row alone because only a generic's text holds one. Enter and Tab
    // still commit it.
    private static readonly CompletionItemRules GenericTypeRules =
        Rules(commit: [], filter: ['.', '*', '<', '>', ',', ' ']);

    private static readonly CompletionItemRules SimpleNameRules =
        Rules(commit: [], filter: ['.', '*', '<', ',', ' ']);

    /// <summary>The item a host shows for one offered name.</summary>
    /// <param name="offer">The name, its shape and where it came from.</param>
    /// <param name="span">The segment of the literal a commit replaces.</param>
    internal static CompletionItem For(Offer offer, TextSpan span)
    {
        string insert = offer.Insert;
        int parameters = offer.Shape == OfferShape.Type ? insert.IndexOf('<') : -1;
        bool generic = parameters >= 0;
        string display = generic ? insert.Substring(0, parameters) : insert;
        string? suffix = generic ? insert.Substring(parameters) : null;

        ImmutableDictionary<string, string> carried = ImmutableDictionary<string, string>.Empty
            .Add(InsertProperty, insert)
            .Add(StartProperty, span.Start.ToString(CultureInfo.InvariantCulture))
            .Add(LengthProperty, span.Length.ToString(CultureInfo.InvariantCulture));

        return CompletionItem.Create(
            display,
            display,
            SortTextFor(offer),
            carried,
            ImmutableArray.Create(offer.Tag),
            RulesFor(offer.Shape, generic),
            displayTextPrefix: null,
            displayTextSuffix: suffix,
            inlineDescription: DescriptionFor(offer));
    }

    /// <summary>
    ///     The edit that commits <paramref name="item" />, or the host's own default where the item did not
    ///     come from here.
    /// </summary>
    /// <remarks>
    ///     The fall-back is not dead code: the same override answers for every item the host hands it, and
    ///     an item this provider did not make carries none of these properties. Writing the display text
    ///     over the item's own span is what the base implementation does.
    /// </remarks>
    /// <param name="item">The item the host is committing.</param>
    internal static CompletionChange ChangeFor(CompletionItem item)
    {
        if (item.Properties.TryGetValue(InsertProperty, out string? insert)
            && insert is not null
            && TryReadPosition(item, StartProperty, out int start)
            && TryReadPosition(item, LengthProperty, out int length))
            return CompletionChange.Create(new TextChange(new TextSpan(start, length), insert));

        return CompletionChange.Create(new TextChange(item.Span, item.DisplayText));
    }

    private static bool TryReadPosition(CompletionItem item, string property, out int value)
    {
        value = 0;

        return item.Properties.TryGetValue(property, out string? text)
               && text is not null
               && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    // The sort text is the rank and then the text itself, which is what puts every name the referenced
    // projects declare above every name they do not. A subtree form lands immediately after the step it
    // belongs to, because a dot precedes every letter ordinally.
    private static string SortTextFor(Offer offer)
    {
        return (offer.Rank == 0 ? "0" : "1") + offer.Insert;
    }

    private static string? DescriptionFor(Offer offer)
    {
        return offer.Shape switch
        {
            OfferShape.Subtree => SubtreeDescription,
            OfferShape.LoneStar => SubtreeDescription,
            _ => offer.Assembly
        };
    }

    private static CompletionItemRules RulesFor(OfferShape shape, bool generic)
    {
        return shape switch
        {
            OfferShape.NamespaceStep => NamespaceStepRules,
            OfferShape.Subtree => SubtreeRules,
            OfferShape.LoneStar => LoneStarRules,
            OfferShape.SimpleName => SimpleNameRules,
            _ => generic ? GenericTypeRules : TypeRules
        };
    }

    // Commit characters REPLACE the host's default set, so a row with an empty one commits on nothing but
    // the enter key and the tab key. Filter characters are ADDED to the default set, which is what keeps
    // a list open while an author types the punctuation that surrounds a name rather than dismissing it.
    private static CompletionItemRules Rules(char[] commit, char[] filter)
    {
        return CompletionItemRules.Default
            .WithCommitCharacterRules(
                ImmutableArray.Create(
                    CharacterSetModificationRule.Create(CharacterSetModificationKind.Replace, commit)))
            .WithFilterCharacterRules(
                ImmutableArray.Create(
                    CharacterSetModificationRule.Create(CharacterSetModificationKind.Add, filter)));
    }
}
