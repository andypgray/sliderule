using SlideRule.Internal;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     Parses the semicolon-separated glob list every filtering option on this CLI takes — the MSBuild
///     list idiom. Blanks are dropped, so a trailing separator is not a pattern that matches nothing, and
///     an absent option (null) parses to the empty list every filter reads as "narrow nothing". One parser
///     so two options cannot disagree about what a list is.
/// </summary>
internal static class GlobList
{
    /// <summary>The globs in <paramref name="value" />, trimmed, in the order they were written.</summary>
    public static IReadOnlyList<string> Parse(string? value)
    {
        return value is null
            ? []
            : MsBuildList.Items(value).ToList();
    }

    /// <summary>
    ///     The option value that <see cref="Parse" /> reads back as <paramref name="globs" />, or null for none:
    ///     how a list a settings file holds as JSON reaches an option that takes this form.
    /// </summary>
    public static string? Join(IReadOnlyList<string>? globs)
    {
        return globs is null || globs.Count == 0 ? null : string.Join(";", globs);
    }
}
