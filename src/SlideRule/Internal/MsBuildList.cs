namespace SlideRule.Internal;

/// <summary>
///     The MSBuild list idiom parsed in one place, so no two readers disagree about what a list is: a
///     semicolon-separated value whose parts are trimmed and whose blanks are dropped, so a trailing separator
///     is not an empty item. Items come back in the order they were written.
/// </summary>
internal static class MsBuildList
{
    // Core is netstandard2.0, where Split takes a char[] rather than a char and StringSplitOptions has no
    // TrimEntries; the array is held rather than built per call because these parse on the load path.
    private static readonly char[] Separator = [';'];

    internal static IEnumerable<string> Items(string value)
    {
        return value
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0);
    }
}
