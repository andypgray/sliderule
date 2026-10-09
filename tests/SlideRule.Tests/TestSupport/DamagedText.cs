namespace SlideRule.Tests.TestSupport;

/// <summary>The damaged copies of a valid text that a reader's robustness sweep feeds it.</summary>
internal static class DamagedText
{
    // Each stands for a class of damage: a stray structural character, a changed kind, a broken string.
    private static readonly char[] Replacements = ['"', '{', '}', '[', ']', ',', ':', '0', 'x', '\\', ' '];

    /// <summary>
    ///     Every copy of <paramref name="valid" /> one character away from it: per offset, the deletion, then
    ///     each replacement that differs from the character there.
    /// </summary>
    internal static IEnumerable<string> OneCharacterAway(string valid)
    {
        for (var offset = 0; offset < valid.Length; offset++)
        {
            yield return valid.Remove(offset, 1);
            foreach (char replacement in Replacements)
            {
                if (valid[offset] == replacement) continue;

                yield return valid.Substring(0, offset) + replacement + valid.Substring(offset + 1);
            }
        }
    }

    /// <summary>Every prefix of <paramref name="valid" /> short of the whole document, shortest first.</summary>
    internal static IEnumerable<string> CutShort(string valid)
    {
        // The whole text less its final newline is still the whole document, so stop one short of that.
        for (var length = 0; length < valid.Length - 1; length++)
            yield return valid.Substring(0, length);
    }
}
