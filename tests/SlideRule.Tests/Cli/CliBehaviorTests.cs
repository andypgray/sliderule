using System.CommandLine;
using Shouldly;
using SlideRule.Cli;
using SlideRule.Cli.Settings;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Hooks;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The fast CLI paths that need no workspace: a command line the tool cannot run exits 2 rather than
///     System.CommandLine's default 1, before any work starts, and under the hook verb it exits the hook's own
///     non-blocking 1. The <c>mcp</c> row is a regression guard: a solution path that does not exist parses, so
///     the mcp action runs and must fail fast on the unresolvable bind. Without that fail-fast, a
///     redirected-but-never-closed stdin under the test host hangs the whole suite.
/// </summary>
/// <remarks>
///     That fail-fast is the <em>argument</em> half only, and the difference is the point. An argument
///     that does not resolve means the operator named something wrong, so <c>mcp</c> still exits 2 without
///     starting anything. Omitting the argument is the documented-optional path, so a walk-up that finds
///     nothing starts the server unbound and announces the reason instead of dying during <c>initialize</c>
///     with nowhere to say it. Nothing in this class may exercise that second half — an in-process
///     invocation of it starts a server on a stdin this host never closes, which is the very hang above.
///     <c>McpUnboundServerTests</c> covers it over a real child process.
/// </remarks>
public sealed class CliBehaviorTests
{
    [Fact]
    public async Task UnknownCommand_ExitsTwo()
    {
        CliResult result = await CliRunner.InvokeAsync("frobnicate");

        result.ShouldRefuseWith();
    }

    /// <summary>
    ///     The root and every command group, as the words that name them, read from the tree the way
    ///     <see cref="CliEntry" /> decides which command's help follows a parse error: a group is a command with
    ///     subcommands and no action of its own.
    /// </summary>
    public static TheoryData<string> Groups =>
    [
        "",
        .. CommandTree.Below(CommandTree.Root())
            .Where(static entry => entry.Command is { Subcommands.Count: > 0, Action: null })
            .Select(static entry => entry.Path)
    ];

    [Theory]
    [MemberData(nameof(Groups))]
    public async Task BareGroup_PrintsTheErrorThenItsHelpOnStderr_AndExitsTwo(string group)
    {
        // Both halves come from the tool itself, so the comparison holds under any UI culture and any
        // console width.
        string[] words = group.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string parseError = CommandTree.Root()
            .Parse(words)
            .Errors
            .ShouldHaveSingleItem()
            .Message;
        CliResult help = await CliRunner.InvokeAsync([.. words, "--help"]);

        CliResult result = await CliRunner.InvokeAsync(words);

        result.ShouldRefuseWith();
        result.Out.ShouldBeEmpty(result.Transcript());
        result.Err.ShouldBe(parseError + Environment.NewLine + Environment.NewLine + help.Out, result.Transcript());
    }

    /// <summary>
    ///     Every runnable command the tool ships, as the words that name it (a subcommand after its group), read
    ///     from the tree so that a command added later is covered too. The hook verb is the one left out: it answers
    ///     in Claude Code's exit codes, so its own row is below.
    /// </summary>
    public static TheoryData<string> Commands =>
    [
        .. CommandTree.Below(CommandTree.Root())
            .Where(static entry => entry.Command.Action is not null && entry.Path != CommandFactory.HookCommandName)
            .Select(static entry => entry.Path)
    ];

    [Theory]
    [MemberData(nameof(Commands))]
    public async Task UnknownOptionWhereAPositionalGoes_IsRefusedByName(string command)
    {
        // Each command's first positional argument is where an unmatched token lands: the solution, or
        // explain's rule ID. Refused at parse, the run never gets as far as looking for a file named after the
        // option.
        CliResult result = await CliRunner.InvokeAsync([.. command.Split(' '), "--bogus"]);

        result.ShouldRefuseWith("Unrecognized option '--bogus'.");
        result.Err.ShouldNotContain("not found", result.Transcript());
    }

    [Fact]
    public async Task UnknownOptionWhereTheHookVerbsSolutionGoes_IsReportedWithoutBlocking()
    {
        // Act: a settings entry with a typo in it.
        CliResult result = await CliRunner.InvokeAsync(CommandFactory.HookCommandName, "--bogus");

        // Assert: refused by name like every other verb's, but in the hook's own terms. The CLI's 2 would tell the
        // agent to keep working over a line only the user can fix, so the hook exits 1 under its config-error line.
        result.ShouldReportConfigError("Unrecognized option '--bogus'.");
        result.Err.ShouldNotContain("not found", result.Transcript());
    }

    [Fact]
    public async Task UnknownOptionWhereExplainsSolutionGoes_IsRefusedByName()
    {
        CliResult result = await CliRunner.InvokeAsync("explain", "some/rule", "--bogus");

        result.ShouldRefuseWith("Unrecognized option '--bogus'.");
    }

    [Fact]
    public async Task MistypedCommand_IsRefusedWithTheNearestCommandSuggestedOnStderr()
    {
        CliResult result = await CliRunner.InvokeAsync("chek");

        // The suggestion's wording is the library's and localized, so the row asserts the token and the line
        // naming the command, which no culture changes.
        result.ShouldRefuseWith("'chek'");
        result.Out.ShouldBeEmpty(result.Transcript());
        result.Err.Split(Environment.NewLine).ShouldContain("check", result.Transcript());
    }

    [Fact]
    public async Task MistypedOptionAfterTheSolution_IsRefusedWithTheOptionSuggestedOnStderr()
    {
        CliResult result = await CliRunner.InvokeAsync("check", "App.slnx", "--jsno");

        result.ShouldRefuseWith("'--jsno'");
        result.Out.ShouldBeEmpty(result.Transcript());
        result.Err.Split(Environment.NewLine).ShouldContain("--json", result.Transcript());
    }

    [Fact]
    public async Task FailureOutsideEveryCommandsHandler_ExitsTwoThroughTheErrorMapper()
    {
        var root = new RootCommand();
        root.SetAction(int (_) => throw new InvalidOperationException("outside every handler"));

        CliResult result = await InvokeEntryAsync(root);

        // System.CommandLine's own handler would print its "Unhandled exception" header and exit 1, the CLI's code
        // for a red rule. An unexpected exception keeps its stack trace: it is a bug.
        result.ShouldRefuseWith("System.InvalidOperationException: outside every handler");
        result.Err.ShouldNotContain("Unhandled exception", result.Transcript());
    }

    [Fact]
    public async Task FailureOutsideTheHookVerbsHandler_IsReportedWithoutBlocking()
    {
        Command hook = new(CommandFactory.HookCommandName);
        hook.SetAction(int (_) => throw new InvalidOperationException("outside the hook's handler"));
        var root = new RootCommand { hook };

        CliResult result = await InvokeEntryAsync(root, CommandFactory.HookCommandName);

        result.ShouldReportConfigError("System.InvalidOperationException: outside the hook's handler");
    }

    [Fact]
    public void SolutionPathBeginningWithADash_ParsesWhenWrittenFromTheCurrentDirectory()
    {
        ParseResult parsed = CommandTree.Root()
            .Parse(["check", "./-odd.sln"]);

        parsed.Errors.ShouldBeEmpty();
        parsed.GetValue<string?>("solution")
            .ShouldBe("./-odd.sln");
    }

    [Fact]
    public async Task RequestBindingFailure_ExitsTwoThroughTheErrorMapper()
    {
        // System.CommandLine's own handler would print its "Unhandled exception" header and exit 1, which
        // a Claude Code hook reads as a red rule. An unexpected exception keeps its stack trace: it is a bug.
        CliResult result = await InvokeWithFailingBindingAsync(new InvalidOperationException("binding failed"));

        result.ShouldRefuseWith("System.InvalidOperationException: binding failed");
        result.Err.ShouldNotContain("Unhandled exception", result.Transcript());
    }

    [Fact]
    public async Task RequestBindingUserError_FromTheSettingsFile_ExitsTwoWithTheMessageAlone()
    {
        using TempDirectory temp = TestTempRoot.Fresh("cli-settings-binding");
        string settingsFile = temp.WriteFile([ProjectSettings.FileName], """{ "solution": "Gone.sln" }""");

        CliResult result = await CliRunner.InvokeAsync("check", temp.Path);

        result.ShouldRefuseWith();
        result.Err
            .Trim()
            .ShouldBe(
                $"{settingsFile} names a solution that does not exist: {temp.PathOf("Gone.sln")}. Fix its "
                + "'solution', or pass the solution as the argument.",
                result.Transcript());
    }

    [Fact]
    public async Task Mcp_UnresolvableSolution_ExitsTwoWithoutStartingServer()
    {
        // A path that does not exist parses, so the mcp action runs; McpServerCommand must resolve the
        // binding and exit 2 BEFORE starting the stdio server. If it ever starts the server here, stdin never
        // closes under the test host and the whole suite hangs. The refusal naming the file is what shows the
        // run reached the binding rather than stopping at the interactive-launch check before it.
        CliResult result = await CliRunner.InvokeAsync("mcp", Path.Combine("no-such-directory", "NoSuch.sln"));

        result.ShouldRefuseWith("NoSuch.sln");
    }

    // A bare root command wired the way every verb is, with a request that cannot be built. No real verb's
    // builder throws anything but a UserErrorException on a command line that parsed, so an unexpected
    // exception is reachable only this way.
    private static Task<CliResult> InvokeWithFailingBindingAsync(Exception bindingFailure)
    {
        var root = new RootCommand();
        CommandFactory.SetRequestAction<string>(
            root,
            _ => throw bindingFailure,
            (_, _, _, _) => Task.FromResult(ExitCodes.Clean));

        return CliResult.CapturedAsync((output, error) => root.Parse([])
            .InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, Ct));
    }

    // The entry point over a hand-built tree. Every real command's action runs under its own handler, so a failure
    // that escapes one is reachable only this way.
    private static Task<CliResult> InvokeEntryAsync(RootCommand root, params string[] args)
    {
        return CliResult.CapturedAsync((output, error) =>
            CliEntry.InvokeAsync(root, args, new InvocationConfiguration { Output = output, Error = error }));
    }
}
