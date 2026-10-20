using System.Text;

namespace SlideRule.Rendering;

/// <summary>
///     The Mermaid text primitives both diagram renderers share. One escaping rule serves both fences of one
///     artifact, so a character that is safe in the survey cannot be unsafe in the law.
/// </summary>
/// <remarks>
///     Both drawings keep one plain dialect: <c>flowchart LR</c> with quoted labels, <c>accTitle</c> and
///     <c>accDescr</c> for screen readers, and no <c>%%{init}%%</c> block, <c>classDef</c> or colours, so a
///     drawing renders correctly for a reader in a light or a dark theme and structure carries the meaning.
/// </remarks>
internal static class MermaidText
{
    /// <summary>
    ///     ASCII letters and digits only, not <see cref="char.IsLetterOrDigit(char)" />, which is Unicode-aware
    ///     and would leave accented letters in an identifier position where Mermaid's tolerance is unknown.
    /// </summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            bool ascii = character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';
            builder.Append(ascii ? character : '_');
        }

        return builder.ToString();
    }

    /// <summary>
    ///     A label's text with the four characters Mermaid reads as markup sent out as the entities it
    ///     reads back as themselves: a double quote would end a quoted label early, angle brackets open
    ///     markup (a generic type name is full of them), and <c>#</c> opens an entity reference
    ///     (<c>#35;</c> is the documented escape for a literal one).
    /// </summary>
    /// <remarks>
    ///     The <c>#</c> pass must run FIRST — reversed, it would rewrite the <c>#</c> of an emitted
    ///     <c>#quot;</c> into <c>#35;quot;</c>.
    /// </remarks>
    internal static string Label(string text)
    {
        return text
            .Replace("#", "#35;")
            .Replace("\"", "#quot;")
            .Replace("<", "#lt;")
            .Replace(">", "#gt;");
    }

    /// <summary>
    ///     One node ID per name, in order: the prefix plus a deterministic slug, deduped with an ordinal
    ///     suffix so two names that slug alike (<c>MyApp.Web</c> and <c>MyApp-Web</c>) still get distinct
    ///     nodes.
    /// </summary>
    /// <remarks>
    ///     The prefix is the caller's, and every ID carries one. Mermaid's flowchart grammar reserves bare
    ///     words (end, graph, class, subgraph and more), none of them anchored to the start of a statement, so
    ///     a name that is one of them would break the whole diagram rather than just its own node; and the
    ///     link rules read a leading <c>o</c> or <c>x</c> as an arrowhead. A prefix keeps every ID out of reach
    ///     of both, stays safe as the grammar adds words, and makes the grammar's case sensitivity irrelevant
    ///     rather than something to rely on.
    /// </remarks>
    private static IReadOnlyList<string> UniqueIds(string prefix, IReadOnlyList<string> names)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>(names.Count);
        foreach (string name in names)
        {
            string slug = prefix + Slug(name);
            string candidate = slug;
            var suffix = 2;
            while (!taken.Add(candidate)) candidate = $"{slug}_{suffix++}";

            ids.Add(candidate);
        }

        return ids;
    }

    /// <summary>
    ///     One node ID per key, in order: <see cref="UniqueIds" /> over the names
    ///     <paramref name="nameOf" /> reads, zipped back onto the keys that produced them.
    /// </summary>
    /// <remarks>
    ///     Duplicate keys collapse to the last ID minted for them, which is what a caller keying on a name
    ///     expects.
    /// </remarks>
    internal static Dictionary<TKey, string> IdMap<TKey>(
        string prefix, IReadOnlyList<TKey> keys, Func<TKey, string> nameOf, IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
    {
        List<string> names = keys.Select(nameOf).ToList();
        IReadOnlyList<string> ids = UniqueIds(prefix, names);

        var map = new Dictionary<TKey, string>(comparer);
        for (var i = 0; i < keys.Count; i++) map[keys[i]] = ids[i];

        return map;
    }

    /// <summary>
    ///     The fence skeleton both drawings sit in. A blank line precedes each section that has content, so a
    ///     section the drawing has nothing for leaves no gap behind it and the fence's shape stays a function
    ///     of what is in it.
    /// </summary>
    internal static IReadOnlyList<string> Fence(string accTitle, string accDescr, params IReadOnlyList<string>[] sections)
    {
        var lines = new List<string>
        {
            "```mermaid",
            "flowchart LR",
            $"    accTitle: {accTitle}",
            $"    accDescr: {accDescr}"
        };

        foreach (IReadOnlyList<string> section in sections)
        {
            if (section.Count == 0) continue;

            lines.Add("");
            lines.AddRange(section);
        }

        lines.Add("```");

        return lines;
    }
}
