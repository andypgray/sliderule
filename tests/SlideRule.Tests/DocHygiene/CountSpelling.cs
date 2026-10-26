using System.Globalization;

namespace SlideRule.Tests.DocHygiene;

/// <summary>How a count is spelled where it is quoted.</summary>
internal enum CountStyle
{
    /// <summary>Digits, as the tool prints them: <c>12</c>.</summary>
    Numeral,

    /// <summary>The number word in running prose: <c>twelve</c>.</summary>
    Word,

    /// <summary>The number word starting a sentence: <c>Thirty-three</c>.</summary>
    TitleWord,

    /// <summary>The number word up to ten and digits above: <c>seven</c>, <c>12</c>.</summary>
    WordToTen
}

/// <summary>
///     The count-spelling grammar the count gates share: how each <see cref="CountStyle" /> writes a count,
///     the fragment a registered template composes, and what a count reads as where a gate scans prose for one.
/// </summary>
internal static class CountSpelling
{
    internal static readonly string[] Units =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen",
        "nineteen"
    ];

    internal static readonly string[] Tens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    // Declared after Units and Tens because the initializer calls Spell, which reads them: a static field
    // initializer runs in declaration order, so hoisting this above them would leave the set built from
    // nulls.
    private static readonly HashSet<string> CountWords = Enumerable.Range(0, 100)
        .SelectMany(static count => Enum.GetValues<CountStyle>()
            .Select(style => Spell(count, style)))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    ///     Spells <paramref name="count" /> the way <paramref name="style" /> writes it. Only 0 to 99 are
    ///     spellable as words; anything else throws, because a silently unspellable number would compose
    ///     a fragment that matches nothing and read as a passing gate. <see cref="CountStyle.WordToTen" /> spells
    ///     0 to 10 as words and anything above in digits.
    /// </summary>
    public static string Spell(int count, CountStyle style)
    {
        bool digits = style == CountStyle.Numeral || (style == CountStyle.WordToTen && count > 10);
        if (digits) return count.ToString(CultureInfo.InvariantCulture);

        if (count is < 0 or > 99)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Only 0 to 99 are spelled as number words.");

        string word = count < Units.Length
            ? Units[count]
            : count % 10 == 0
                ? Tens[count / 10]
                : $"{Tens[count / 10]}-{Units[count % 10]}";

        return style == CountStyle.TitleWord ? char.ToUpperInvariant(word[0]) + word[1..] : word;
    }

    /// <summary>
    ///     Fills <paramref name="template" />'s <c>{0}</c>-style placeholders with
    ///     <paramref name="counts" /> spelled in <paramref name="style" /> — the fragment a registered
    ///     prose sentence must contain.
    /// </summary>
    public static string Compose(string template, IReadOnlyList<int> counts, CountStyle style)
    {
        object[] spelled = counts
            .Select(count => (object)Spell(count, style))
            .ToArray();
        return string.Format(CultureInfo.InvariantCulture, template, spelled);
    }

    /// <summary>
    ///     How a count reads where a gate scans prose for one, captured as <c>count</c>: a number word,
    ///     hyphenated above twenty, or a numeral. It sits beside <see cref="IsCountWord" /> because the two
    ///     must agree about what a count token looks like, and a copy of the fragment that stopped agreeing
    ///     would make its gate go quiet rather than red.
    /// </summary>
    internal const string CountToken = @"(?<count>[A-Za-z][a-z]*(-[a-z]+)?|\d+)";

    /// <summary>
    ///     Whether <paramref name="written" /> is a count as this repository's prose spells one: a number
    ///     word in running or sentence-initial case, or a numeral.
    /// </summary>
    /// <remarks>
    ///     The set is derived from <see cref="Spell" /> rather than written out a second time, which is
    ///     what keeps a claim a scanner accepts and a claim a gate can check from ever diverging. Every
    ///     gate that scans prose for a claim needs the test: a sentence like "Four new rules over this
    ///     repository's real code" puts a qualifier where the number goes, so requiring a spelled count
    ///     there is what tells a total from a delta.
    /// </remarks>
    public static bool IsCountWord(string written)
    {
        return CountWords.Contains(written);
    }
}
