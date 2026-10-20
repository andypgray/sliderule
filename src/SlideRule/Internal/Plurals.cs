namespace SlideRule.Internal;

/// <summary>Count inflection for rendered lines, wherever they are composed.</summary>
/// <remarks>
///     Every string inflected here is pinned output, so the English rule has one owner rather than a copy
///     per renderer. It sits in Core's internal namespace because the Roslyn host and the checker inflect
///     counts too (the orphaned-baseline warning), and Checking may not reference Rendering. Not to be
///     confused with <c>ProseFormat.KindPlural</c>, which looks up a grammar term's plural rather than
///     inflecting by count.
/// </remarks>
internal static class Plurals
{
    /// <summary>
    ///     <paramref name="noun" /> inflected for <paramref name="count" />: bare for exactly one,
    ///     pluralized for anything else, zero included. A final <c>y</c> after a consonant becomes
    ///     <c>ies</c> (<c>entry</c> → <c>entries</c>); everything else takes <c>s</c>.
    /// </summary>
    internal static string Noun(int count, string noun)
    {
        if (count == 1) return noun;

        int last = noun.Length - 1;
        bool consonantY = last > 0 && noun[last] == 'y' && "aeiouAEIOU".IndexOf(noun[last - 1]) < 0;
        return consonantY ? noun.Substring(0, last) + "ies" : noun + "s";
    }

    /// <summary>The counted phrase: <c>1 project</c>, <c>3 projects</c>.</summary>
    internal static string Counted(int count, string noun)
    {
        return count + " " + Noun(count, noun);
    }

    /// <summary>The copula agreeing with <paramref name="count" />.</summary>
    internal static string Verb(int count)
    {
        return count == 1 ? "is" : "are";
    }

    /// <summary>The past copula agreeing with <paramref name="count" />.</summary>
    internal static string PastVerb(int count)
    {
        return count == 1 ? "was" : "were";
    }
}
