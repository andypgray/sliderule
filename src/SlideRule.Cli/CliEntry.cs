using System.CommandLine;
using System.CommandLine.Parsing;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;

namespace SlideRule.Cli;

/// <summary>
///     The invocation core, factored out of <c>Program</c> so the in-process e2e tests exercise the
///     real command tree, parse-error remap, and action dispatch through a redirected
///     <see cref="InvocationConfiguration" />. Building and parsing touch no Roslyn type.
/// </summary>
internal static class CliEntry
{
    /// <summary>
    ///     Runs <paramref name="args" /> against the command tree <see cref="CommandFactory.BuildRootCommand" /> builds
    ///     over the given seams.
    /// </summary>
    public static async Task<int> InvokeAsync(
        string[] args,
        InvocationConfiguration configuration,
        ISolutionSource hostSource,
        IEnvironment environment,
        TextReader? input = null)
    {
        RootCommand root = CommandFactory.BuildRootCommand(hostSource, environment, input);
        ParseResult parseResult = root.Parse(args);

        // Remap System.CommandLine's default parse-error exit code (1) to ExitCodes.Error, because 1 is
        // ExitCodes.Violations.
        if (parseResult.Errors.Count > 0)
        {
            // Except under the hook verb, which answers in Claude Code's codes: there the CLI's 2 would tell the
            // agent to keep working, over a settings entry only the user can fix. So a command line that does not
            // parse is the hook's own error, reported the way the verb reports every other.
            if (IsHookVerb(parseResult))
            {
                IEnumerable<string> messages = parseResult.Errors.Select(static parseError => parseError.Message);
                string report = string.Join(configuration.Error.NewLine, messages);
                return await HookRunner.ReportConfigErrorAsync(configuration.Error, report);
            }

            foreach (ParseError error in parseResult.Errors) await configuration.Error.WriteLineAsync(error.Message);

            // No verb was named (a bare call, a mistyped verb, an unknown option at the root), or a command
            // group was named without one of its subcommands (a bare `baseline`), so the error says what is
            // wrong and nothing says what is right. That command's help follows it on stderr, which keeps
            // stdout empty for a caller that parses it.
            Command named = parseResult.CommandResult.Command;
            bool namedNoRunnableCommand = named == parseResult.RootCommandResult.Command
                                          || named is { Subcommands.Count: > 0, Action: null };
            if (namedNoRunnableCommand)
                await WriteHelpAsync(root, PathOf(parseResult.CommandResult), configuration.Error);

            return ExitCodes.Error;
        }

        return await parseResult.InvokeAsync(configuration);
    }

    // The hook verb itself, not a command that merely shares its name further down the tree.
    private static bool IsHookVerb(ParseResult parseResult)
    {
        CommandResult named = parseResult.CommandResult;
        return named.Command.Name == CommandFactory.HookCommandName
               && named.Parent is CommandResult { Parent: null };
    }

    // The command names from the root down to this one, the root itself excluded: what to parse in front of
    // --help to reach the same command's help.
    private static IReadOnlyList<string> PathOf(CommandResult result)
    {
        var names = new List<string>();
        for (CommandResult at = result; at.Parent is CommandResult parent; at = parent)
            names.Insert(0, at.Command.Name);

        return names;
    }

    private static async Task WriteHelpAsync(RootCommand root, IReadOnlyList<string> path, TextWriter error)
    {
        await error.WriteLineAsync();

        ParseResult help = root.Parse([.. path, "--help"]);
        await help.InvokeAsync(new InvocationConfiguration { Output = error, Error = error });
    }
}
