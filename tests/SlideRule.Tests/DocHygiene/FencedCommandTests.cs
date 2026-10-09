using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the <c>sliderule</c> commands the docs tell a reader to run. Every such line in a fenced
///     code block of a tracked doc must parse on the command tree the tool ships, so a renamed or removed flag
///     fails the suite at every doc that still spells it, instead of failing in the reader's shell. The unit
///     tests below pin what the scan reads as a command. The Usage line in each of the command-line reference's
///     help fences is skipped, because it is a synopsis rather than a command.
/// </summary>
/// <remarks>
///     A parse checks spelling, not effect: an argument that names a file nobody has, or a flag used where
///     another was meant, parses clean. What the commands do is held elsewhere, by the fences that quote their
///     output.
/// </remarks>
public sealed class FencedCommandTests
{
    // A help Usage line such as "sliderule check [<solution>] [options]" is a synopsis the parser refuses, and
    // the help holder already holds it exactly, so a registered Usage line is not a command here.
    private static readonly Lazy<IReadOnlyList<(string Doc, FencedCommands.FencedCommand Command)>> DocCommands =
        new(() => FencedCommands.InstructionDocs
            .SelectMany(static doc => FencedCommands.Find(RepoRoot.ReadText(doc))
                .Where(command => !CliHelpFences.IsUsageLine(doc, command.Text))
                .Select(command => (doc, command)))
            .ToList());

    [Fact]
    public void EverySlideRuleCommandInADocFence_ParsesOnTheCommandTree()
    {
        // Act
        List<string> unparsed = DocCommands.Value
            .Select(static found => (found.Doc, found.Command, Errors: FencedCommands.ParseErrors(found.Command.Text)))
            .Where(static parsed => parsed.Errors.Count > 0)
            .Select(static parsed =>
                $"{parsed.Doc}:{parsed.Command.Line}: {parsed.Command.Text} -> {string.Join(" ", parsed.Errors)}")
            .ToList();

        // Assert
        unparsed.ShouldReportNothing(
            "These commands in doc fences do not parse on the command tree the tool ships; correct each to the "
            + "verb and flags the tool has now");
    }

    [Fact]
    public void DocScan_FindsCommands()
    {
        // Assert: a scan that stopped recognising commands would turn the gate green over nothing.
        DocCommands.Value.ShouldNotBeEmpty("The scan found no sliderule command in any tracked doc's fences.");
    }

    [Fact]
    public void DocScan_FindsAContinuedCommand()
    {
        // Assert: the redraw commands are written across lines, and a join that broke would hand the parse the
        // first line alone.
        DocCommands.Value.ShouldContain(
            found => found.Command.Continued,
            "The scan found no command continued across lines, which the redraw commands are.");
    }

    [Fact]
    public void DocScan_ReadsNoHelpCaptureAsACommand()
    {
        // Arrange: the scan itself still finds the reference's Usage lines, so the skip is what keeps them out.
        IReadOnlyList<FencedCommands.FencedCommand> found = FencedCommands.Find(RepoRoot.ReadText(DocPaths.Cli));

        // Act
        List<int> read = DocCommands.Value
            .Where(static entry => entry.Doc == DocPaths.Cli)
            .Select(static entry => entry.Command.Line)
            .ToList();

        // Assert
        found.ShouldNotBeEmpty($"The scan found no sliderule line in {DocPaths.Cli}, so the skip does nothing.");
        read.ShouldBeEmpty($"These lines of {DocPaths.Cli} were read as commands, though each sits in a help fence.");
    }

    [Fact]
    public void Find_PromptedCommand_DropsThePrompt()
    {
        // Arrange
        const string doc = "```bash\n$ sliderule check App.sln\n```\n";

        // Act
        IReadOnlyList<FencedCommands.FencedCommand> commands = FencedCommands.Find(doc);

        // Assert
        commands.ShouldBe([new FencedCommands.FencedCommand(2, "sliderule check App.sln", false)]);
    }

    [Fact]
    public void Find_TrailingComment_IsCutButAHashInQuotesIsKept()
    {
        // Arrange
        const string doc = "```bash\nsliderule check App.sln --spec \"Arch#1.csproj\"   # exit 0: clean\n```\n";

        // Act
        IReadOnlyList<FencedCommands.FencedCommand> commands = FencedCommands.Find(doc);

        // Assert
        commands.ShouldHaveSingleItem()
            .Text.ShouldBe("sliderule check App.sln --spec \"Arch#1.csproj\"");
    }

    [Fact]
    public void Find_ContinuedCommand_JoinsItsLines()
    {
        // Arrange
        const string doc =
            """
            To redraw:

            ```bash
            sliderule render App.sln \
              --spec Arch.csproj \
              --diagram-only "A;B"
            ```
            """;

        // Act
        IReadOnlyList<FencedCommands.FencedCommand> commands = FencedCommands.Find(doc);

        // Assert
        commands.ShouldBe(
            [new FencedCommands.FencedCommand(4, "sliderule render App.sln --spec Arch.csproj --diagram-only \"A;B\"", true)]);
    }

    [Fact]
    public void Lines_ContinuedLineOfAnyCommand_JoinsItsLines()
    {
        // Arrange: the launch-line gate reads its registrations through the same reader, so a continued line
        // joins whatever command it belongs to.
        IReadOnlyList<(string Text, int Number)> block = SourceAnchors.FencedBlocks(
                "```bash\nclaude mcp add sliderule \\\n  -- sliderule mcp App.sln   # register\n```\n")
            .ShouldHaveSingleItem();

        // Act
        IReadOnlyList<FencedCommands.FencedCommand> lines = FencedCommands.Lines(block);

        // Assert
        lines.ShouldBe(
            [new FencedCommands.FencedCommand(2, "claude mcp add sliderule -- sliderule mcp App.sln", true)]);
    }

    [Theory]
    [InlineData("dotnet tool install -g sliderule")]
    [InlineData("claude mcp add --scope project sliderule -- sliderule mcp MyApp.sln")]
    [InlineData("echo sliderule check App.sln")]
    [InlineData("sliderulex check App.sln")]
    public void Find_ToolNotTheFirstToken_IsNotACommand(string line)
    {
        // Arrange
        var doc = $"```bash\n{line}\n```\n";

        // Act
        IReadOnlyList<FencedCommands.FencedCommand> commands = FencedCommands.Find(doc);

        // Assert
        commands.ShouldBeEmpty();
    }

    [Fact]
    public void Find_CommandInProse_IsNotRead()
    {
        // Act
        IReadOnlyList<FencedCommands.FencedCommand> commands = FencedCommands.Find("sliderule check App.sln\n");

        // Assert
        commands.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("sliderule check App.sln --diff-base HEAD --json")]
    [InlineData("sliderule render App.sln --diagram ARCHITECTURE.md --diagram-only \"A;B C\"")]
    [InlineData("sliderule mcp App.sln")]
    public void ParseErrors_CommandTheToolHas_ReportsNone(string command)
    {
        // Act
        IReadOnlyList<string> errors = FencedCommands.ParseErrors(command);

        // Assert
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void ParseErrors_UnknownFlag_NamesIt()
    {
        // Act
        IReadOnlyList<string> errors = FencedCommands.ParseErrors("sliderule check App.sln --diffbase HEAD");

        // Assert
        errors.ShouldContain(error => error.Contains("--diffbase"));
    }

    [Fact]
    public void ParseErrors_UnknownFlagWhereTheSolutionGoes_NamesIt()
    {
        // Act: a flag that takes no value, in a command that names no solution, lands where the solution goes.
        IReadOnlyList<string> errors = FencedCommands.ParseErrors("sliderule check --jsn");

        // Assert
        errors.ShouldContain(error => error.Contains("--jsn"));
    }
}
