using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The fast <c>baseline</c> paths that need no workspace. The command is a group of four subcommands, so an
///     <c>add</c> companion on another subcommand is an unknown option refused by name, and <c>rename</c>'s two
///     options are required by the tree itself. What the tree cannot say, <c>add</c>'s own preconditions and a
///     well-formed <c>--from</c>, is validated before solution discovery, so each malformed invocation exits 2
///     with its pinned message even when the solution argument is nonsense.
/// </summary>
public sealed class BaselineCommandTests
{
    [Fact]
    public async Task Baseline_SolutionWithoutASubcommand_ExitsTwoBeforeSolutionDiscovery()
    {
        // The solution argument belongs to a subcommand, so here it is an unmatched token: refused at parse,
        // and never looked for on disk.
        CliResult result = await CliRunner.InvokeAsync("baseline", "does-not-exist.sln");

        result.ShouldRefuseWith("init", "accept-reductions", "add", "rename");
        result.Err.ShouldNotContain("not found", result.Transcript());
    }

    [Theory]
    [InlineData("init")]
    [InlineData("accept-reductions")]
    [InlineData("rename")]
    public async Task Baseline_AddCompanionOnAnotherSubcommand_IsRefusedByName(string subcommand)
    {
        // With the solution argument filled, the parser's own wording refuses the token; empty, the argument's
        // guard does. Either way the option is named, and nothing looks for the solution.
        CliResult result = await CliRunner.InvokeAsync("baseline", subcommand, "does-not-exist.sln", "--rule", "some/rule");

        result.ShouldRefuseWith("'--rule'");
        result.Err.ShouldNotContain("not found", result.Transcript());
    }

    [Fact]
    public async Task BaselineAdd_WithoutRule_ExitsTwoBeforeSolutionDiscovery()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--because", "b", "--subject", "S");

        result.ShouldRefuseWith("baseline add requires --rule <id>.");
        result.Err.ShouldNotContain("does-not-exist", result.Transcript());
    }

    [Fact]
    public async Task BaselineAdd_WithBlankOrMultilineBecause_ExitsTwo()
    {
        // Missing, whitespace-only, and multi-line --because all refuse with the one pinned message.
        CliResult missing = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--subject", "S");
        CliResult blank = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--subject", "S", "--because", "   ");
        CliResult multiline = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--subject", "S", "--because", "a\nb");

        const string expected = "baseline add requires a non-blank, single-line --because.";
        missing.ShouldRefuseWith(expected);
        blank.ShouldRefuseWith(expected);
        multiline.ShouldRefuseWith(expected);
    }

    [Fact]
    public async Task BaselineAdd_WithoutExactlyOneEntryForm_ExitsTwo()
    {
        // A half-edge (--source, no --target), both forms mixed (--source + --subject), and neither form each refuse.
        CliResult halfEdge = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--because", "b", "--source", "X");
        CliResult mixed = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--because", "b", "--subject", "S", "--source", "X");
        CliResult neither = await CliRunner.InvokeAsync(
            "baseline", "add", "does-not-exist.sln", "--rule", "r", "--because", "b");

        const string expected =
            "baseline add requires exactly one entry form: --source with --target (an edge), or --subject (a shape).";
        halfEdge.ShouldRefuseWith(expected);
        mixed.ShouldRefuseWith(expected);
        neither.ShouldRefuseWith(expected);
    }

    [Fact]
    public async Task BaselineRename_WithoutFromOrTo_ExitsTwoBeforeSolutionDiscovery()
    {
        CliResult withoutFrom = await CliRunner.InvokeAsync(
            "baseline", "rename", "does-not-exist.sln", "--to", "data-access/no-inline-sql");
        CliResult withoutTo = await CliRunner.InvokeAsync(
            "baseline", "rename", "does-not-exist.sln", "--from", "data-access/inline-sql");

        withoutFrom.ShouldRefuseWith("'--from'");
        withoutFrom.Err.ShouldNotContain("does-not-exist", withoutFrom.Transcript());
        withoutTo.ShouldRefuseWith("'--to'");
        withoutTo.Err.ShouldNotContain("does-not-exist", withoutTo.Transcript());
    }

    [Fact]
    public async Task BaselineRename_MalformedFrom_ExitsTwoBeforeSolutionDiscovery()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "baseline", "rename", "does-not-exist.sln", "--from", "Data Access/Inline SQL", "--to", "data-access/no-inline-sql");

        result.ShouldRefuseWith(
            "'Data Access/Inline SQL' is not a well-formed rule ID; expected area/rule-name matching ^[a-z0-9-]+(/[a-z0-9-]+)*$.");
        result.Err.ShouldNotContain("does-not-exist", result.Transcript());
    }
}
