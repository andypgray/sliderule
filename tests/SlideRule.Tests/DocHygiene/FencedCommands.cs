using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using SlideRule.Cli;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The reader of the command lines in a doc's fenced code blocks, and the parse that holds each
///     <c>sliderule</c> command to the command tree the tool ships. Both command gates read lines through it, and
///     all of the logic lives here so the gates and the unit tests exercise the same code path.
/// </summary>
internal static class FencedCommands
{
    private const string Tool = "sliderule";

    // An optional indent and "$ " prompt ahead of a line's first token.
    private static readonly Regex Prompt = new(@"^\s*(?:\$\s+)?", RegexOptions.CultureInvariant);

    // The tool's name as a line's first word. Matched on the text rather than on the split tokens, because the
    // split reads a quoted JSON key such as "sliderule": as the name followed by a colon.
    private static readonly Regex CommandStart = new(@"^sliderule(?:\s|$)", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Every tracked markdown doc whose fences the command gates read: all of them but the changelog, which
    ///     records each release's commands and launch lines as they shipped, so a rename since is history there
    ///     rather than an instruction.
    /// </summary>
    public static IEnumerable<string> InstructionDocs =>
        TrackedFiles.Markdown.Where(static doc => doc != DocPaths.Changelog);

    /// <summary>
    ///     Every <c>sliderule</c> command in the fenced code blocks of <paramref name="docText" />, each with the
    ///     1-based line it starts on. A line is a command when its first token is <c>sliderule</c>;
    ///     <see cref="Lines" /> says how a line is read.
    /// </summary>
    public static IReadOnlyList<FencedCommand> Find(string docText)
    {
        return SourceAnchors.FencedBlocks(docText)
            .SelectMany(Lines)
            .Where(static line => CommandStart.IsMatch(line.Text))
            .ToList();
    }

    /// <summary>
    ///     Every line of one fenced <paramref name="block" />, read as a shell command line, each with the 1-based
    ///     line it starts on. An optional <c>$ </c> prompt is not part of the line, a line ending in a backslash
    ///     continues onto the next line of the same fence, and a trailing <c>#</c> comment, one with whitespace
    ///     before it and outside double quotes, is not part of the command.
    /// </summary>
    public static IReadOnlyList<FencedCommand> Lines(IReadOnlyList<(string Text, int Number)> block)
    {
        List<FencedCommand> lines = new();

        for (var index = 0; index < block.Count; index++)
        {
            int line = block[index].Number;
            List<string> pieces = new();
            string current = Prompt.Replace(block[index].Text, string.Empty)
                .TrimEnd();

            while (current.EndsWith('\\') && index + 1 < block.Count)
            {
                pieces.Add(current[..^1]
                    .Trim());
                index++;
                current = block[index]
                    .Text.Trim();
            }

            pieces.Add(WithoutComment(current));
            IEnumerable<string> joined = pieces.Where(static piece => piece.Length > 0);
            lines.Add(new FencedCommand(line, string.Join(" ", joined), pieces.Count > 1));
        }

        return lines;
    }

    /// <summary>
    ///     Splits <paramref name="text" /> into arguments with System.CommandLine's own splitter, the one the tool's
    ///     command line goes through. It is not a POSIX shell: a closing double quote ends the word.
    /// </summary>
    public static string[] Tokens(string text)
    {
        return CommandLineParser.SplitCommandLine(text)
            .ToArray();
    }

    /// <summary>
    ///     The shipped command tree's parse of <paramref name="arguments" />: what the shell hands the tool, its
    ///     own name already dropped.
    /// </summary>
    public static ParseResult Parse(IReadOnlyList<string> arguments)
    {
        RootCommand root = CommandFactory.BuildRootCommand();

        return root.Parse(arguments);
    }

    /// <summary>
    ///     The errors the shipped command tree reports for <paramref name="command" />, split by
    ///     <see cref="Tokens" />; empty when it parses.
    /// </summary>
    public static IReadOnlyList<string> ParseErrors(string command)
    {
        string[] tokens = Tokens(command);

        // The shell hands the tool its arguments without its own name, and in this process the root command is
        // named after the test host, so the name is dropped here as the shell drops it.
        string[] arguments = tokens is [Tool, .. var rest] ? rest : tokens;
        ParseResult parsed = Parse(arguments);

        return parsed.Errors.Select(static error => error.Message)
            .ToList();
    }

    // Cuts a trailing shell comment: a '#' outside double quotes with whitespace before it.
    private static string WithoutComment(string line)
    {
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"')
                quoted = !quoted;
            else if (character == '#' && !quoted && index > 0 && char.IsWhiteSpace(line[index - 1]))
                return line[..index]
                    .TrimEnd();
        }

        return line;
    }

    /// <summary>
    ///     One logical line of a fence, read as a shell command line: the 1-based <paramref name="Line" /> it starts
    ///     on, its <paramref name="Text" /> with the prompt, the continuations and any trailing comment resolved, and
    ///     whether it was <paramref name="Continued" /> across lines.
    /// </summary>
    internal sealed record FencedCommand(int Line, string Text, bool Continued);
}
