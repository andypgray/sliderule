namespace SlideRule.Internal;

/// <summary>
///     The MSBuild list idiom parsed in one place: a semicolon-separated value whose parts are trimmed and
///     whose blanks are dropped, so a trailing separator is not an empty item and a value of nothing but
///     separators and spaces is no items at all.
/// </summary>
/// <remarks>
///     Every list this product reads is written the way MSBuild writes one — a <c>PrivateAssets</c> metadata
///     value, a <c>TargetFrameworks</c> property, the glob list a filtering option takes — and one parser is
///     what stops two of them disagreeing about what a list is.
/// </remarks>
internal static class MsBuildList
{
    // Core is netstandard2.0, where Split takes a char[] rather than a char and StringSplitOptions has no
    // TrimEntries; the array is held rather than built per call because these parse on the load path.
    private static readonly char[] Separator = [';'];

    /// <summary>
    ///     The items of <paramref name="value" />, trimmed, blanks dropped, in the order they were written.
    /// </summary>
    internal static IEnumerable<string> Items(string value)
    {
        return value
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0);
    }
}
