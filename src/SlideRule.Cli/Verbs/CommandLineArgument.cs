using SlideRule.Roslyn;

namespace SlideRule.Cli.Verbs;

/// <summary>How a path is written into a command line that the CLI prints or a hook entry carries.</summary>
internal static class CommandLineArgument
{
    // The characters a shell would read as something other than part of the word, which double quotes make literal.
    private static readonly char[] QuotedCharacters =
        [' ', '\t', ';', '&', '|', '(', ')', '<', '>', '\'', '*', '?', '[', ']', '#', '~', '{', '}', '!'];

    /// <summary>
    ///     <paramref name="path" /> as one word to a shell and to System.CommandLine's splitter alike: double-quoted
    ///     when it holds a space or another character a shell would split or expand on.
    /// </summary>
    public static string Quoted(string path)
    {
        return path.IndexOfAny(QuotedCharacters) >= 0 ? $"\"{path}\"" : path;
    }

    /// <summary>
    ///     <paramref name="path" />, relative and with <c>/</c> separators, as one <c>args</c> element of a hook entry
    ///     Claude Code spawns with no shell: the <c>hook</c> verb's argument or <c>--spec</c> value, with <c>./</c> in
    ///     front of a path the parser would read as an option.
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     The path holds <c>${</c>, which Claude Code would read as a path placeholder such as
    ///     <c>${CLAUDE_PROJECT_DIR}</c> and replace.
    /// </exception>
    public static string HookPath(string path)
    {
        if (path.Contains("${", StringComparison.Ordinal))
            throw new UserErrorException(
                $"Cannot write '{path}' into a hook entry: Claude Code replaces a ${{...}} placeholder in a hook's "
                + "arguments with a path of its own. Rename the path, or pass one that leads to the same file without it.");

        return path.StartsWith('-') ? "./" + path : path;
    }
}
