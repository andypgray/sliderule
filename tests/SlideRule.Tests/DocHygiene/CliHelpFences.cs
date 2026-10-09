using System.CommandLine;
using System.CommandLine.Help;
using System.Globalization;
using SlideRule.Cli;
using SlideRule.Cli.Rendering;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The help fences on the command-line reference: one per command the tool has, each picked out of the page by
///     the Usage line its help prints, and the render every one of them is held to.
/// </summary>
/// <remarks>
///     <para>
///         The render fixes the three things that vary with the run, so the fence varies only with the command tree.
///         The width follows the console window and is unlimited when stdout is redirected, so it is set to
///         <see cref="Width" />, which the page states. The headings follow the UI culture through
///         System.CommandLine's satellite assemblies, so the render runs under the invariant culture. The Usage line
///         names the host process, which in the suite is the test assembly, so that one name is replaced by the CLI
///         project's tool command.
///     </para>
///     <para>
///         Nothing here reads a <see cref="CapturedFences" /> static, because <see cref="CapturedFences.All" />
///         reads <see cref="Captured" /> and the two static initialisers would otherwise run into each other.
///     </para>
/// </remarks>
internal static class CliHelpFences
{
    /// <summary>The column width every help fence is rendered at, as the page says.</summary>
    internal const int Width = 100;

    private const string UsageHeading = "Usage:";

    private static readonly string Holder =
        $"{nameof(CliHelpFenceTests)}.{nameof(CliHelpFenceTests.HelpFence_IsWhatTheVerbPrints)}";

    private static readonly Lazy<string> ToolCommandName = new(static () =>
        SolutionInventory.Properties(DocPaths.CliProject)
            .GetValueOrDefault("ToolCommandName")
        ?? throw new InvalidOperationException($"{DocPaths.CliProject} declares no ToolCommandName."));

    /// <summary>
    ///     Every command the page carries help for, in the order the page shows them: the root, then each verb in
    ///     the order the root help lists them, with a group's subcommands after it.
    /// </summary>
    internal static readonly IReadOnlyList<CliHelpFence> Rows =
    [
        new("sliderule", "sliderule [command] [options]"),
        new("sliderule check", "sliderule check [<solution>] [options]"),
        new("sliderule explain", "sliderule explain [<rule-id> [<solution>]] [options]"),
        new("sliderule render", "sliderule render [<solution>] [options]"),
        new("sliderule baseline", "sliderule baseline [command] [options]"),
        new("sliderule baseline init", "sliderule baseline init [<solution>] [options]"),
        new("sliderule baseline accept-reductions", "sliderule baseline accept-reductions [<solution>] [options]"),
        new("sliderule baseline add", "sliderule baseline add [<solution>] [options]"),
        new("sliderule baseline rename", "sliderule baseline rename [<solution>] [options]"),
        new("sliderule status", "sliderule status [<solution>] [options]"),
        new("sliderule graph", "sliderule graph [<solution>] [options]"),
        new("sliderule mcp", "sliderule mcp [<solution>] [options]"),
        new("sliderule hook", "sliderule hook [<solution>] [options]")
    ];

    /// <summary>The help fences as captured fences, so the quote gates hold each marker to exactly one fence.</summary>
    internal static readonly IReadOnlyList<CapturedFence> Captured =
    [
        .. Rows.Select(static row => new CapturedFence(DocPaths.Cli, row.Key, row.Marker, Holder))
    ];

    internal static CliHelpFence Row(string commandLine)
    {
        return Rows.Single(row => row.CommandLine == commandLine);
    }

    /// <summary>
    ///     The help <paramref name="commandLine" /> prints with <c>--help</c> after it, as the installed tool prints
    ///     it at <see cref="Width" /> columns: LF line endings, no trailing whitespace on any line, and no trailing
    ///     blank line.
    /// </summary>
    internal static string Render(string commandLine)
    {
        RootCommand root = CommandFactory.BuildRootCommand();
        HelpOption help = root.Options.OfType<HelpOption>()
            .Single();
        ((ToolNamedHelpAction)help.Action!).MaxWidth = Width;

        string[] words = commandLine.Split(' ')[1..];
        StringWriter writer = new();
        CultureInfo culture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        try
        {
            root.Parse([.. words, "--help"])
                .Invoke(new InvocationConfiguration { Output = writer, Error = writer });
        }
        finally
        {
            CultureInfo.CurrentUICulture = culture;
        }

        // Nothing else is normalised: the line endings are the platform's, trailing padding is invisible on the
        // page, and the blank line after the last section is the writer's.
        List<string> lines = writer.ToString()
            .NormalizedLines()
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .ToList();
        while (lines is [.., ""]) lines.RemoveAt(lines.Count - 1);

        ShouldNameTheTool(lines);
        return string.Join("\n", lines);
    }

    /// <summary>
    ///     <paramref name="page" /> with the body of each registered help fence replaced by its live render, and
    ///     every other line left as it was.
    /// </summary>
    internal static string PageWithLiveHelp(string page)
    {
        string newline = page.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<string> lines = page.NormalizedLines()
            .Split('\n')
            .ToList();

        // Last fence first, so the line numbers of the fences above stay where the scan found them.
        foreach ((IReadOnlyList<(string Text, int Number)> block, CliHelpFence row) in HelpBlocks(page)
                     .Reverse())
        {
            int start = block[0].Number - 1;
            lines.RemoveRange(start, block.Count);
            lines.InsertRange(start, Render(row.CommandLine)
                .Split('\n'));
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    ///     Whether <paramref name="text" />, a command line read from <paramref name="doc" />, is one of the page's
    ///     Usage lines: the synopsis a help fence shows under its Usage heading, which no shell runs.
    /// </summary>
    internal static bool IsUsageLine(string doc, string text)
    {
        return doc == DocPaths.Cli && Rows.Any(row => row.Marker == text);
    }

    // Each fence of the page that carries exactly one registered marker, with the row it carries.
    private static IEnumerable<(IReadOnlyList<(string Text, int Number)> Block, CliHelpFence Row)> HelpBlocks(
        string page)
    {
        foreach (IReadOnlyList<(string Text, int Number)> block in SourceAnchors.FencedBlocks(page))
        {
            List<CliHelpFence> carried = Rows
                .Where(fence => block.Any(line => line.Text.Contains(fence.Marker, StringComparison.Ordinal)))
                .ToList();
            if (carried is [var row]) yield return (block, row);
        }
    }

    // The tool prints its command on the Usage line whatever assembly runs it, and the in-process root here is named
    // after the test host, so a line naming the project's ToolCommandName shows both that the CLI renamed its root
    // and that the name it chose is the one the package installs.
    private static void ShouldNameTheTool(List<string> lines)
    {
        int named = lines
            .Where((_, index) => index > 0 && lines[index - 1] == UsageHeading)
            .Count(static line => line.TrimStart().StartsWith(ToolCommandName.Value + " ", StringComparison.Ordinal));

        if (named != 1)
            throw new InvalidOperationException(
                $"Expected exactly one Usage line naming the tool '{ToolCommandName.Value}', and found {named}:\n"
                + string.Join("\n", lines));
    }
}

/// <summary>
///     One help fence: the <paramref name="CommandLine" /> whose help it shows, and the <paramref name="Marker" />,
///     the Usage line that help prints, that picks the fence out of the page.
/// </summary>
internal sealed record CliHelpFence(string CommandLine, string Marker)
{
    /// <summary>The fence's key among the captured fences.</summary>
    internal string Key => "help-" + CommandLine.Replace(' ', '-');
}
