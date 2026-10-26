using System.CommandLine;
using System.Globalization;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The holder of the command-line reference's help fences. Each fence must be what its command prints with
///     <c>--help</c>, rendered in process at the width the page states, and the page must carry a fence for every
///     command the tool has.
/// </summary>
/// <remarks>
///     A fence that drifts reds with the text to paste, as the other captured fences do, and with a page to copy
///     from. One change to the parser's help layout moves every fence at once, so the holder writes the whole page
///     with every help fence re-rendered to <c>CLI.md.actual</c> beside the test assembly, names both files, and
///     never writes the page itself. Every case writes the same file, so the cases cannot disagree about it.
/// </remarks>
public sealed class CliHelpFenceTests
{
    public static TheoryData<string> CommandLines => [.. CliHelpFences.Rows.Select(static row => row.CommandLine)];

    [Theory]
    [MemberData(nameof(CommandLines))]
    public void HelpFence_IsWhatTheVerbPrints(string commandLine)
    {
        // Arrange
        CliHelpFence row = CliHelpFences.Row(commandLine);
        QuotedFence fence = CliHelpFences.Captured
            .ShouldHaveSingle(captured => captured.Key == row.Key, captured => captured.Key)
            .ShouldFindFence();

        // Act
        string live = CliHelpFences.Render(commandLine);

        // Assert
        if (fence.Lines.SequenceEqual(live.Split('\n'), StringComparer.Ordinal)) return;

        string page = RepoRoot.Absolute(DocPaths.Cli);
        string actual = WriteLivePage();
        throw new ShouldAssertException(
            $"{DocPaths.Cli}:{fence.Line} is not what `{commandLine} --help` prints. The page with every help fence "
            + $"re-rendered is at {actual}. Compare it with {page}, and copy the fences you accept.\n"
            + $"`{commandLine} --help` prints:\n{live}");
    }

    [Fact]
    public void HelpFences_NameEveryCommandTheToolHas()
    {
        // Arrange
        RootCommand root = CommandTree.Root();
        List<string> commands = ["sliderule", .. CommandTree.Below(root).Select(static entry => $"sliderule {entry.Path}")];

        // Act
        List<string> fenced = CliHelpFences.Rows.Select(static row => row.CommandLine)
            .ToList();

        // Assert
        fenced.ShouldBe(
            commands,
            ignoreOrder: true,
            $"The page's help fences must name the root and every command below it, subcommands included. The tree "
            + $"has {string.Join(", ", commands)}; the fences name {string.Join(", ", fenced)}.");
    }

    [Fact]
    public void Render_IgnoresTheUICulture()
    {
        // Arrange: System.CommandLine translates its headings, and German is one of its satellites.
        static string RenderUnder(string culture)
        {
            using ScopedUICulture scope = new(CultureInfo.GetCultureInfo(culture));
            return CliHelpFences.Render("sliderule check");
        }

        // Act
        string german = RenderUnder("de-DE");
        string english = RenderUnder("en-US");

        // Assert
        german.ShouldBe(english);
    }

    [Fact]
    public void Page_StatesTheWidthTheHelpIsRenderedAt()
    {
        // Arrange
        string page = RepoRoot.ReadText(DocPaths.Cli);

        // Act
        List<string> overlong = CliHelpFences.Rows
            .SelectMany(static row => CliHelpFences.Render(row.CommandLine)
                .Split('\n')
                .Where(static line => line.Length > CliHelpFences.Width)
                .Select(line => $"{row.CommandLine}: {line}"))
            .ToList();

        // Assert
        page.ShouldContain($"{CliHelpFences.Width} columns");
        overlong.ShouldReportNothing($"These help lines run past the {CliHelpFences.Width} columns the page states");
    }

    [Fact]
    public void PageWithLiveHelp_ReplacesOnlyTheHelpFenceBodies()
    {
        // Arrange
        const string page =
            """
            # Page

            ```text
            sliderule mcp [<solution>] [options]
            ```

            ```bash
            sliderule mcp App.sln
            ```

            """;

        // Act
        string rewritten = CliHelpFences.PageWithLiveHelp(page.NormalizedLines());

        // Assert
        string help = CliHelpFences.Render("sliderule mcp");
        rewritten.ShouldBe(
            $"# Page\n\n```text\n{help}\n```\n\n```bash\nsliderule mcp App.sln\n```\n",
            "Only the help fence's body should change, and to the live render.");
    }

    // The page with every help fence re-rendered, beside the test assembly rather than over the page.
    private static string WriteLivePage()
    {
        string rewritten = CliHelpFences.PageWithLiveHelp(RepoRoot.ReadText(DocPaths.Cli));

        return RegeneratedFiles.Write(DocPaths.Cli, rewritten);
    }
}
