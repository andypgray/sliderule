using SlideRule.Prose;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The match and the refusal for a closed set of wire words a surface takes, such as a grain or a format: a word
///     matches ignoring case and trimmed, and an unknown one is refused in one shape whichever set it missed.
/// </summary>
internal static class WireWords
{
    /// <summary>
    ///     The position of <paramref name="name" /> in <paramref name="names" />, or -1 when it is not one.
    /// </summary>
    public static int IndexOf(IReadOnlyList<string> names, string name)
    {
        string wanted = name.Trim();
        for (var index = 0; index < names.Count; index++)
            if (string.Equals(names[index], wanted, StringComparison.OrdinalIgnoreCase))
                return index;

        return -1;
    }

    /// <summary>
    ///     The refusal for <paramref name="name" />, as the caller wrote it, which names no <paramref name="noun" />
    ///     in <paramref name="accepted" />.
    /// </summary>
    public static string Refusal(string noun, string name, IReadOnlyList<string> accepted)
    {
        var lead = $"Unknown {noun} '{name}'";
        return Refusals.SingleValueRefusal(name, lead, $"pass one {noun}")
               ?? $"{lead}; use {ProseFormat.JoinReferences(accepted)}.";
    }
}
