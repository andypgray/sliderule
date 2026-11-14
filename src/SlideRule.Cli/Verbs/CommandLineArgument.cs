using SlideRule.Roslyn;

namespace SlideRule.Cli.Verbs;

/// <summary>How a path is written into a command line that the CLI prints or a settings file carries.</summary>
internal static class CommandLineArgument
{
    // The characters a shell would read as something other than part of the word, which double quotes make literal.
    // Inside the quotes a shell still reads '"', '$' and '`', and a line break still ends the line System.CommandLine's
    // splitter and a settings reader see, so HookPath refuses those rather than quoting them.
    private static readonly char[] QuotedCharacters =
        [' ', '\t', ';', '&', '|', '(', ')', '<', '>', '\'', '*', '?', '[', ']', '#', '~', '{', '}', '!'];

    private static readonly char[] UnquotableCharacters = ['"', '$', '`', '\n', '\r'];

    /// <summary>
    ///     <paramref name="path" /> as one word to a shell and to System.CommandLine's splitter alike: double-quoted
    ///     when it holds a space or another character a shell would split or expand on.
    /// </summary>
    public static string Quoted(string path)
    {
        return path.IndexOfAny(QuotedCharacters) >= 0 ? $"\"{path}\"" : path;
    }

    /// <summary>
    ///     <paramref name="path" />, relative and with <c>/</c> separators, as the <c>hook</c> verb's argument or
    ///     <c>--spec</c> value in a command Claude Code runs through a shell: <c>./</c> in front of a path the parser
    ///     would read as the <c>install</c> subcommand or as an option, and then <see cref="Quoted" />.
    /// </summary>
    /// <exception cref="UserErrorException">The path holds a double quote, a <c>$</c>, a backtick or a line break.</exception>
    public static string HookPath(string path)
    {
        if (path.IndexOfAny(UnquotableCharacters) >= 0)
            throw new UserErrorException(
                $"Cannot write '{path}' into a hook command: a shell reads a double quote, a $, a backtick or a line "
                + "break in it even inside quotes. Rename the path, or pass one that leads to the same file without "
                + "them.");

        bool misread = string.Equals(path, CommandFactory.HookInstallCommandName, StringComparison.Ordinal)
                       || path.StartsWith('-');
        return Quoted(misread ? "./" + path : path);
    }
}
