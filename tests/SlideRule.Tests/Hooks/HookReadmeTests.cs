using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SlideRule.Cli;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Holds <c>hooks/README.md</c> and the settings snippets to the verb they document: the exit codes, options and
///     environment variables they name, the commands a snippet wires and the baseline spellings it denies are each
///     checked against the product.
/// </summary>
/// <remarks>
///     The exit-code table's two code columns are what is held. Its Meaning column and the prose under it stay held by
///     review, all but the config-error line the prose quotes, which is held to the product's constant as the codes
///     are. Both code columns are the product's own constants, so a code either side renumbers reds here before the
///     page can go on stating one the tool no longer returns.
/// </remarks>
public sealed class HookReadmeTests
{
    private const string HooksDirectory = "hooks/";

    private const string Verb = "sliderule hook";

    private const string BaselineCommandName = "baseline";

    // The root managed block, which carries the line a machine without the command is told to run a verb by.
    private const string RootAgents = "AGENTS.md";

    // The events a snippet wires, each of which the verb has a branch for: the prompt that opens a turn, and the two
    // stops that close one.
    private static readonly string[] WiredEvents = ["UserPromptSubmit", "Stop", "SubagentStop"];

    private static readonly string[] Snippets = ["hooks/settings.snippet.json", "examples/Meridian/hooks/settings.snippet.json"];

    // A row of the exit table: a backticked code, a cell of prose, then a cell that opens with a backticked code.
    private static readonly Regex ExitRow = new(@"^\|\s*`(\d+)`\s*\|[^|]*\|\s*`(\d+)`", RegexOptions.CultureInvariant);

    private static readonly Regex Variable = new(@"\bSLIDERULE_[A-Z_]+\b", RegexOptions.CultureInvariant);

    // A backticked span that runs the verb, whose options are the ones the README tells a reader to pass.
    private static readonly Regex VerbSpan = new(@"`sliderule hook\b[^`]*`", RegexOptions.CultureInvariant);

    // A long option such as --max-rounds standing on its own, not the tail of a longer token.
    private static readonly Regex OptionToken = new(@"(?<![\w-])--[a-z][a-z-]*", RegexOptions.CultureInvariant);

    // A permission entry that governs one of the two tools Claude Code runs a shell command through, whose body is
    // the command pattern.
    private static readonly Regex ShellRule = new(@"^(Bash|PowerShell)\((.*)\)$", RegexOptions.CultureInvariant);

    private static readonly string[] ShellTools = ["Bash", "PowerShell"];

    [Fact]
    public void ExitTable_StatesTheContractRows()
    {
        // Arrange
        List<(int Check, int Hook)> stated = RepoRoot.ReadLines(DocPaths.HooksReadme)
            .Select(static line => ExitRow.Match(line))
            .Where(static match => match.Success)
            .Select(static match => (Code(match.Groups[1].Value), Code(match.Groups[2].Value)))
            .ToList();
        List<(int Check, int Hook)> contract = HookExitContract.Rows
            .Select(static row => (row.CheckExit, row.HookExit))
            .ToList();

        // Act
        IEnumerable<string> unstated = contract
            .Except(stated)
            .Select(static row => $"check {row.Check} -> hook {row.Hook} is a contract row the table does not state");
        IEnumerable<string> stale = stated
            .Except(contract)
            .Select(static row => $"check {row.Check} -> hook {row.Hook} is stated but is not a contract row");
        List<string> findings = [.. unstated, .. stale];
        if (stated.Count == 0) findings.Add($"{DocPaths.HooksReadme} has no exit-table row the scan can read.");

        // Assert
        findings.ShouldReportNothing(
            $"{DocPaths.HooksReadme}'s exit-code table has drifted from the rows HookRunnerTests drives the verb through");
    }

    [Fact]
    public void ConfigErrorLine_IsTheOneTheReadmeQuotes()
    {
        // Arrange
        var quoted = $"`{HookRunner.ConfigErrorLine}`";
        string readme = RepoRoot.ReadText(DocPaths.HooksReadme);

        // Act
        List<string> findings = [];
        if (!readme.Contains(quoted, StringComparison.Ordinal))
            findings.Add(
                $"{DocPaths.HooksReadme} never quotes {quoted}, "
                + "the line the hook reports SlideRule's own errors under");

        // Assert
        findings.ShouldReportNothing(
            $"{DocPaths.HooksReadme} and the hook disagree about the line its own errors are reported under");
    }

    [Fact]
    public void VerbOptions_AreTheOnesTheReadmeNames()
    {
        // Arrange
        HashSet<string> taken = RootSubcommand(CommandFactory.HookCommandName)
            .Options
            .Select(static option => option.Name)
            .Where(static name => name.StartsWith("--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        string readme = RepoRoot.ReadText(DocPaths.HooksReadme);
        HashSet<string> passed = VerbSpan.Matches(readme)
            .SelectMany(static span => OptionToken.Matches(span.Value))
            .Select(static token => token.Value)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        IEnumerable<string> unnamed = taken
            .Where(option => !readme.Contains($"`{option}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(static option => $"{option} is an option of {Verb}, and {DocPaths.HooksReadme} never names it");
        IEnumerable<string> unknown = passed
            .Except(taken, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static option =>
                $"{option} is passed to {Verb} in {DocPaths.HooksReadme}, and the verb takes no such option");
        List<string> findings = [.. unnamed, .. unknown];
        if (taken.Count == 0) findings.Add($"{Verb} yielded no option, so there was nothing to compare.");

        // Assert
        findings.ShouldReportNothing($"{DocPaths.HooksReadme} and {Verb} disagree about which options the verb takes");
    }

    [Fact]
    public void SnippetHooks_RunTheVerbOnACommandLineThatParses()
    {
        // Arrange
        List<string> findings = [];

        // Act: every hook entry of every snippet, event by event. A snippet is pasted as it stands, so an entry that
        // runs anything but the verb, or passes it something it refuses, fails on the reader's machine and nowhere
        // else.
        foreach (string snippet in Snippets)
        {
            using JsonDocument document = JsonDocument.Parse(RepoRoot.ReadText(snippet));
            JsonElement hooks = document.RootElement.GetProperty("hooks");
            string[] wired = hooks.EnumerateObject()
                .Select(static entry => entry.Name)
                .ToArray();
            if (!wired.Order(StringComparer.Ordinal)
                    .SequenceEqual(WiredEvents.Order(StringComparer.Ordinal)))
                findings.Add($"{snippet} wires {string.Join(", ", wired)}; the verb's contract is {string.Join(", ", WiredEvents)}.");

            foreach (JsonProperty entry in hooks.EnumerateObject())
            foreach (string command in Commands(entry.Value))
                if (!command.StartsWith(Verb, StringComparison.Ordinal))
                    findings.Add($"{snippet} {entry.Name}: '{command}' does not run {Verb}.");
                else if (FencedCommands.ParseErrors(command) is { Count: > 0 } errors)
                    findings.Add($"{snippet} {entry.Name}: '{command}' does not parse: {string.Join(" ", errors)}");
        }

        // Assert
        findings.ShouldReportNothing("A settings snippet wires something other than the hook verb it documents");
    }

    [Fact]
    public void EverySnippet_DeniesEveryBaselineSubcommandInEachTaughtSpelling()
    {
        // Arrange: the subcommands come from the command tree, so a new one is held with no edit here. The
        // prefixes are the spellings an agent is taught: the installed command, the unbound server's reply, and the
        // managed block's line for a machine without the command.
        string[] subcommands = RootSubcommand(BaselineCommandName)
            .Subcommands
            .Select(static command => command.Name)
            .ToArray();
        var unboundReplyPrefix = $"dotnet dnx SlideRule.Cli@{ServerVersion.SemVer} --yes --";
        const string managedBlockPrefix = "dotnet dnx SlideRule.Cli --yes --";
        string[] prefixes = ["sliderule", unboundReplyPrefix, managedBlockPrefix];
        List<string> findings = [];
        if (subcommands.Length == 0) findings.Add($"The {BaselineCommandName} command yielded no subcommand, so there was nothing to deny.");

        // Each dnx prefix is held to the text that teaches it, so a change to that text reds here before the deny
        // rules can stop covering what an agent is told.
        if (!ServerInstructions.UnboundCallCoda.Contains(unboundReplyPrefix, StringComparison.Ordinal))
            findings.Add($"The unbound server's reply no longer teaches '{unboundReplyPrefix}', so this fact checks a spelling no agent is given.");
        if (!RepoRoot.ReadText(RootAgents).Contains(managedBlockPrefix, StringComparison.Ordinal))
            findings.Add($"{RootAgents} no longer teaches '{managedBlockPrefix}', so this fact checks a spelling no agent is given.");

        // Act
        foreach (string snippet in Snippets)
        {
            using JsonDocument document = JsonDocument.Parse(RepoRoot.ReadText(snippet));
            (string Tool, Regex Rule)[] rules = document.RootElement
                .GetProperty("permissions")
                .Strings("deny")
                .Select(static entry => ShellRule.Match(entry))
                .Where(static match => match.Success)
                .Select(static match => (
                    Tool: match.Groups[1].Value,
                    Rule: RuleRegex(match.Groups[2].Value, ignoreCase: match.Groups[1].Value == "PowerShell")))
                .ToArray();

            // Each tool is held on its own rules alone: a Bash rule never stops a command the PowerShell tool runs,
            // nor the reverse.
            foreach (string tool in ShellTools)
            {
                Regex[] toolRules = rules
                    .Where(rule => rule.Tool == tool)
                    .Select(static rule => rule.Rule)
                    .ToArray();

                IEnumerable<string> missed = prefixes
                    .SelectMany(prefix => subcommands.Select(subcommand => $"{prefix} {BaselineCommandName} {subcommand}"))
                    .Where(command => !toolRules.Any(rule => rule.IsMatch(command)))
                    .Select(command => $"{snippet}: no {tool} deny rule stops '{command}'.");
                IEnumerable<string> overreached = prefixes
                    .SelectMany(static prefix => new[] { $"{prefix} check MyApp.sln", $"{prefix} graph MyApp.sln" })
                    .Where(command => toolRules.Any(rule => rule.IsMatch(command)))
                    .Select(command => $"{snippet}: a {tool} deny rule stops '{command}', which the agent needs.");
                findings.AddRange(missed);
                findings.AddRange(overreached);
            }
        }

        // Assert
        findings.ShouldReportNothing(
            "A settings snippet's deny rules miss a spelling of baseline an agent is taught, through one of the shell tools, or deny a verb the agent needs");
    }

    [Fact]
    public void Variables_AreTheOnesTheVerbAndTheLauncherRead()
    {
        // Arrange: the verb's own knobs are the product's SLIDERULE_HOOK_ variables, and the launcher's are the
        // ones its script reads.
        HashSet<string> read = TrackedFiles.All
            .Where(IsHookScript)
            .SelectMany(static script => Variables(RepoRoot.ReadText(script)))
            .Concat(HookVariables())
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> named = Variables(RepoRoot.ReadText(DocPaths.HooksReadme))
            .ToHashSet(StringComparer.Ordinal);

        // Act
        IEnumerable<string> unnamed = read
            .Except(named, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static variable =>
                $"{variable} is read by the verb or the launcher, and {DocPaths.HooksReadme} never names it");
        IEnumerable<string> stale = named
            .Except(read, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(static variable =>
                $"{variable} is named in {DocPaths.HooksReadme}, and neither the verb nor the launcher reads it");
        List<string> findings = [.. unnamed, .. stale];
        if (read.Count == 0) findings.Add("Neither the verb nor the launcher yielded a SLIDERULE_ variable, so there was nothing to compare.");

        // Assert
        findings.ShouldReportNothing(
            $"{DocPaths.HooksReadme} disagrees with the verb and the launcher about which environment variables configure them");
    }

    private static Command RootSubcommand(string name)
    {
        return CommandTree.Root()
            .Subcommands
            .Single(command => command.Name == name);
    }

    // Every command an event's matcher groups run: hooks.<event>[].hooks[].command.
    private static IEnumerable<string> Commands(JsonElement groups)
    {
        return groups.EnumerateArray()
            .SelectMany(static group => group.GetProperty("hooks")
                .EnumerateArray())
            .Select(static hook => hook.GetProperty("command")
                .GetString() ?? "");
    }

    // The product's environment variables that configure the hook verb, read off the one inventory that names them.
    private static IEnumerable<string> HookVariables()
    {
        return DeclaredConstants.Of<string>(typeof(SlideRuleEnvVars))
            .Where(static name => name.StartsWith("SLIDERULE_HOOK_", StringComparison.Ordinal));
    }

    // A script directly under hooks/, such as the MCP launcher.
    private static bool IsHookScript(string path)
    {
        return path.StartsWith(HooksDirectory, StringComparison.Ordinal)
               && path.IndexOf('/', HooksDirectory.Length) < 0
               && (path.EndsWith(".sh", StringComparison.Ordinal) || path.EndsWith(".ps1", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Variables(string text)
    {
        return Variable.Matches(text)
            .Select(static match => match.Value);
    }

    // A `*` stands for any text, as Claude Code matches a Bash or a PowerShell rule, and a PowerShell rule matches
    // without regard to case. No rule ends in a space and a `*`, the one shape that also matches the bare command, so
    // this conversion is exact for them.
    private static Regex RuleRegex(string body, bool ignoreCase)
    {
        string pattern = Regex.Escape(body)
            .Replace(@"\*", ".*", StringComparison.Ordinal);
        RegexOptions options = ignoreCase
            ? RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
            : RegexOptions.CultureInvariant;

        return new Regex($"^{pattern}$", options);
    }

    private static int Code(string digits)
    {
        return int.Parse(digits, CultureInfo.InvariantCulture);
    }
}
