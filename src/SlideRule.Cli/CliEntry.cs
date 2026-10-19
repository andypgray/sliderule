using System.CommandLine;
using System.CommandLine.Invocation;
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
    public static Task<int> InvokeAsync(
        string[] args,
        InvocationConfiguration configuration,
        ISolutionSource hostSource,
        IEnvironment environment,
        TextReader? input = null)
    {
        RootCommand root = CommandFactory.BuildRootCommand(hostSource, environment, input);
        return InvokeAsync(root, args, configuration);
    }

    /// <summary>
    ///     Parses <paramref name="args" /> against <paramref name="root" /> and runs the result under the CLI's exit
    ///     codes, or the hook's under the hook verb. Turns off <paramref name="configuration" />'s default exception
    ///     handler, which would exit 1.
    /// </summary>
    internal static async Task<int> InvokeAsync(RootCommand root, string[] args, InvocationConfiguration configuration)
    {
        ParseResult parseResult = root.Parse(args);
        bool hookVerb = IsHookVerb(parseResult);

        // Remap System.CommandLine's default parse-error exit code (1) to ExitCodes.Error, because 1 is
        // ExitCodes.Violations.
        if (parseResult.Errors.Count > 0)
        {
            // Except under the hook verb, which answers in Claude Code's codes: there the CLI's 2 would tell the
            // agent to keep working, over a settings entry only the user can fix. So a command line that does not
            // parse is the hook's own error, reported the way the verb reports every other.
            if (hookVerb)
            {
                IEnumerable<string> messages = parseResult.Errors.Select(static parseError => parseError.Message);
                string report = string.Join(configuration.Error.NewLine, messages);
                return await HookRunner.ReportConfigErrorAsync(configuration.Error, report);
            }

            ReportParseErrors(parseResult, configuration.Error);
            return ExitCodes.Error;
        }

        // Every command's action already runs under CommandEntryPoint. This catches what fails outside one, the help
        // action included, which System.CommandLine's own handler would report with exit 1: a red rule to a script,
        // and a non-blocking error under the hook only by accident.
        configuration.EnableDefaultExceptionHandler = false;
        return hookVerb
            ? await HookRunner.RunReportingConfigErrorAsync(InvokeParsed, configuration.Error, CancellationToken.None)
            : await CommandEntryPoint.RunAsync(InvokeParsed, configuration.Error, CancellationToken.None);

        Task<int> InvokeParsed()
        {
            return parseResult.InvokeAsync(configuration);
        }
    }

    // The stock parse-error action, with its typo suggestions, writes both of its channels to stderr, which keeps
    // stdout empty for a caller that parses it. It is constructed rather than read from the parse result, whose
    // action is a help or version option's own when one was named beside the error. Help follows the errors only
    // when no verb was named (a bare call, a mistyped verb, an unknown option at the root), or a command group was
    // named without one of its subcommands (a bare `baseline`): the error then says what is wrong and nothing says
    // what is right.
    private static void ReportParseErrors(ParseResult parseResult, TextWriter error)
    {
        Command named = parseResult.CommandResult.Command;
        bool namedNoRunnableCommand = named == parseResult.RootCommandResult.Command
                                      || named is { Subcommands.Count: > 0, Action: null };

        // A parse that has not been invoked holds a configuration of its own, which the action writes through.
        InvocationConfiguration report = parseResult.InvocationConfiguration;
        report.Output = error;
        report.Error = error;
        new ParseErrorAction { ShowHelp = namedNoRunnableCommand }.Invoke(parseResult);
    }

    // The hook verb itself, not a command that shares its name further down the tree.
    private static bool IsHookVerb(ParseResult parseResult)
    {
        CommandResult named = parseResult.CommandResult;
        return named.Command.Name == CommandFactory.HookCommandName
               && named.Parent is CommandResult { Parent: null };
    }
}
