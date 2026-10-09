using System.CommandLine;
using System.Text.Json;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the MCP launch lines the docs tell a reader to paste. Every launch in a tracked doc's fences
///     must start the server the CLI project ships. A client config must run the project's tool command, a
///     <c>claude mcp add</c> registration must name that command after its <c>--</c>, and a <c>dnx</c> launch must
///     fetch the project's package. Whatever each hands the tool must parse on the shipped command tree to the
///     <c>mcp</c> verb.
/// </summary>
/// <remarks>
///     <para>
///         A client config is a fence tagged <c>json</c> or <c>jsonc</c> whose body parses as JSON with a top-level
///         <c>mcpServers</c> object, and every entry in it is held. A registration is a line that starts
///         <c>claude mcp add</c>, and a <c>dnx</c> launch is a line whose first tokens are <c>dotnet dnx</c> or
///         <c>dnx</c>. Both are found by what the line holds, whatever its fence is tagged, the way the command gate
///         finds a <c>sliderule</c> line. A config or a registration that launches through <c>dnx</c> is held as
///         a <c>dnx</c> launch. A fence tagged <c>json</c> or <c>jsonc</c> in any instruction doc must parse,
///         comments and trailing commas allowed, whether or not it is a client config, so a broken one reds rather
///         than drops out of the scan.
///         Fenced lines are read through <see cref="FencedCommands.Lines" />, the reader the command gate uses.
///         <see cref="FencedCommandTests" /> reads none of these, because none starts with the tool's own name.
///     </para>
///     <para>
///         A parse checks spelling, not effect: a solution argument naming a file nobody has parses clean.
///     </para>
/// </remarks>
public sealed class McpLaunchLineTests
{
    private const string McpVerb = "mcp";
    private const string Separator = "--";

    private static readonly JsonDocumentOptions ConfigJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    // The info strings a client config's fence is tagged with: JSON, and JSON with comments.
    private static readonly string[] JsonInfoStrings = ["json", "jsonc"];

    // The dnx options that take the next token as their value; the rest are switches.
    private static readonly HashSet<string> DnxValueOptions =
        new(["--version", "--source", "--add-source", "--configfile", "--verbosity", "-v"], StringComparer.Ordinal);

    // Besides the changelog, the hooks page is left out: its source-checkout registration runs the launcher script
    // through a shell, not the installed tool, and McpRegistrationTests holds it to the files it names.
    private static readonly Lazy<IReadOnlyList<LaunchLine>> DocLaunchLines =
        new(() => FencedCommands.InstructionDocs
            .Where(static doc => doc != DocPaths.HooksReadme)
            .SelectMany(static doc => Find(doc, RepoRoot.ReadText(doc)))
            .ToList());

    [Fact]
    public void EveryLaunchLine_StartsTheServerTheCliProjectShips()
    {
        // Arrange
        IReadOnlyDictionary<string, string> properties = SolutionInventory.Properties(DocPaths.CliProject);
        string toolCommand = properties.GetValueOrDefault("ToolCommandName") ?? string.Empty;
        string packageId = properties.GetValueOrDefault("PackageId") ?? string.Empty;
        List<string> findings = new();
        if (toolCommand.Length == 0)
            findings.Add($"{DocPaths.CliProject} declares no ToolCommandName to hold the launch lines to.");
        if (packageId.Length == 0)
            findings.Add($"{DocPaths.CliProject} declares no PackageId to hold the launch lines to.");

        // Act
        findings.AddRange(DocLaunchLines.Value.SelectMany(line => Findings(line, toolCommand, packageId)));

        // Assert
        findings.ShouldReportNothing(
            "These MCP launch lines would not start the server the CLI project ships; each should run its tool "
            + "command or fetch its package, and hand the tool arguments that parse to the mcp verb");
    }

    [Fact]
    public void CodingAgentsPage_CarriesEachLaunchForm()
    {
        // Arrange: the gate above passes over nothing once the scan stops recognising a form, so the page that
        // teaches all three must yield all three.
        HashSet<LaunchForm> found = DocLaunchLines.Value
            .Where(static line => line.Doc == DocPaths.CodingAgents)
            .Select(static line => line.Form)
            .ToHashSet();

        // Act
        List<string> missing = Enum.GetValues<LaunchForm>()
            .Where(form => !found.Contains(form))
            .Select(static form => $"{DocPaths.CodingAgents} yields no {Describe(form)} launch line.")
            .ToList();

        // Assert
        missing.ShouldReportNothing("The launch-line scan no longer finds every form on the page that shows them all");
    }

    [Fact]
    public void EveryJsonFence_Parses()
    {
        // Arrange: every instruction doc, the hooks page included, since its fences are pasted too.
        IEnumerable<string> docs = FencedCommands.InstructionDocs;

        // Act
        List<string> unparsed = docs
            .SelectMany(static doc => UnparsedJsonFences(doc, RepoRoot.ReadText(doc)))
            .ToList();

        // Assert
        unparsed.ShouldReportNothing(
            "These json fences do not parse, so a reader who pastes one gets an error and the launch-line scan reads "
            + "nothing from it");
    }

    [Fact]
    public void UnparsedJsonFences_ReportsABrokenJsonFence()
    {
        // Arrange
        string text = string.Join("\n", "```json", "{ \"mcpServers\": }", "```");

        // Act
        List<string> findings = UnparsedJsonFences("PAGE.md", text)
            .ToList();

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldStartWith("PAGE.md:2: a json fence that does not parse:");
    }

    [Fact]
    public void UnparsedJsonFences_PassesCommentsTrailingCommasAndOtherTags()
    {
        // Arrange
        string text = string.Join(
            "\n",
            "```jsonc",
            "{",
            "  // a client config often carries a comment",
            "  \"mcpServers\": {},",
            "}",
            "```",
            "```sh",
            "{ not json",
            "```");

        // Act
        List<string> findings = UnparsedJsonFences("PAGE.md", text)
            .ToList();

        // Assert
        findings.ShouldBeEmpty();
    }

    [Fact]
    public void Find_ReadsClientConfigsFromJsonFencesOnly()
    {
        // Arrange: one config in a text fence and the same config in a json fence.
        const string config = "{ \"mcpServers\": { \"sliderule\": { \"command\": \"sliderule\", \"args\": [\"mcp\"] } } }";
        string text = string.Join("\n", "```text", config, "```", "```json", config, "```");

        // Act
        List<LaunchLine> found = Find("PAGE.md", text)
            .ToList();

        // Assert
        LaunchLine only = found.ShouldHaveSingleItem("only a json-tagged fence is read as a client config.");
        only.Form.ShouldBe(LaunchForm.ClientConfig);
        only.Line.ShouldBe(5);
    }

    [Fact]
    public void Find_SkipsTheValueOfADnxOptionBeforeThePackage()
    {
        // Arrange
        string text = string.Join("\n", "```bash", "dotnet dnx --source ./feed --yes SlideRule.Cli -- mcp", "```");

        // Act
        List<LaunchLine> found = Find("PAGE.md", text)
            .ToList();

        // Assert
        found.ShouldHaveSingleItem()
            .Package.ShouldBe("SlideRule.Cli");
    }

    [Fact]
    public void UnparsedJsonFences_NamesAnEmptyFenceByItsOpeningLine()
    {
        // Arrange: a json fence with no content line, so only its opening delimiter can name it.
        string text = string.Join("\n", "intro", "```json", "```");

        // Act
        List<string> findings = UnparsedJsonFences("PAGE.md", text)
            .ToList();

        // Assert
        findings.ShouldHaveSingleItem()
            .ShouldStartWith("PAGE.md:2: a json fence that does not parse:");
    }

    private static IEnumerable<string> Findings(LaunchLine line, string toolCommand, string packageId)
    {
        var where = $"{line.Doc}:{line.Line} ({Describe(line.Form)})";

        if (line.Command is null && line.Package is null)
            yield return $"{where}: names neither a command nor a package to launch.";

        if (line.Command is not null && line.Command != toolCommand)
            yield return $"{where}: runs '{line.Command}', where the tool command is '{toolCommand}'.";

        if (line.Package is not null && line.Package != packageId)
            yield return $"{where}: fetches '{line.Package}', where the package is '{packageId}'.";

        if (line.Arguments is null)
        {
            yield return $"{where}: hands the tool nothing after {Separator}.";
            yield break;
        }

        string arguments = string.Join(" ", line.Arguments);
        ParseResult parsed = FencedCommands.Parse(line.Arguments);
        string verb = parsed.CommandResult.Command.Name;
        if (parsed.Errors.Count > 0)
            yield return $"{where}: {arguments} -> {string.Join(" ", parsed.Errors.Select(static error => error.Message))}";
        else if (verb != McpVerb)
            yield return $"{where}: {arguments} runs '{verb}', where the server's verb is '{McpVerb}'.";
    }

    // Every launch line in the fences of one doc.
    private static IEnumerable<LaunchLine> Find(string doc, string text)
    {
        foreach (SourceAnchors.TaggedBlock block in SourceAnchors.TaggedBlocks(text))
        {
            if (JsonInfoStrings.Contains(block.Info, StringComparer.Ordinal))
                foreach (LaunchLine config in ClientConfigs(doc, block.Lines))
                    yield return config;

            foreach (FencedCommands.FencedCommand line in FencedCommands.Lines(block.Lines))
            {
                string[] tokens = FencedCommands.Tokens(line.Text);
                if (tokens is ["claude", "mcp", "add", ..])
                    yield return Registration(doc, line.Line, tokens);
                else if (DnxArguments(tokens) is { } afterDnx)
                    yield return Dnx(doc, line.Line, LaunchForm.Dnx, afterDnx);
            }
        }
    }

    // Every server entry of a json fence that is a client config; nothing for one that does not parse or carries no
    // mcpServers object.
    private static List<LaunchLine> ClientConfigs(string doc, IReadOnlyList<(string Text, int Number)> block)
    {
        List<LaunchLine> configs = new();
        string body = string.Join("\n", block.Select(static line => line.Text));

        try
        {
            using JsonDocument document = JsonDocument.Parse(body, ConfigJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("mcpServers", out JsonElement servers)
                || servers.ValueKind != JsonValueKind.Object) return configs;

            foreach (JsonProperty server in servers.EnumerateObject())
                configs.Add(ConfigEntry(doc, LineNaming(block, server.Name), server.Value));
        }
        catch (JsonException)
        {
            // Not JSON, so not a client config; a json-tagged fence that does not parse is EveryJsonFence_Parses's
            // finding.
        }

        return configs;
    }

    // Every json or jsonc fence of one doc whose body does not parse, read with the options a client config is read
    // with, named by its first content line, or by its opening delimiter when it has none.
    private static IEnumerable<string> UnparsedJsonFences(string doc, string text)
    {
        foreach (SourceAnchors.TaggedBlock block in SourceAnchors.TaggedBlocks(text))
        {
            if (!JsonInfoStrings.Contains(block.Info, StringComparer.Ordinal)) continue;

            string body = string.Join("\n", block.Lines.Select(static line => line.Text));
            int line = block.Lines.Count > 0 ? block.Lines[0].Number : block.OpeningLine;
            string? failure = null;
            try
            {
                JsonDocument.Parse(body, ConfigJson)
                    .Dispose();
            }
            catch (JsonException exception)
            {
                failure = exception.Message;
            }

            if (failure is not null) yield return $"{doc}:{line}: a {block.Info} fence that does not parse: {failure}";
        }
    }

    private static LaunchLine ConfigEntry(string doc, int line, JsonElement entry)
    {
        string? command = entry.TryGetProperty("command", out JsonElement commandElement)
                          && commandElement.ValueKind == JsonValueKind.String
            ? commandElement.GetString()
            : null;
        string[] arguments = entry.TryGetProperty("args", out JsonElement argumentsElement)
                             && argumentsElement.ValueKind == JsonValueKind.Array
            ? argumentsElement.EnumerateArray()
                .Select(static argument => argument.ValueKind == JsonValueKind.String
                    ? argument.GetString()!
                    : argument.GetRawText())
                .ToArray()
            : [];

        string[] launch = command is null ? arguments : [command, .. arguments];
        if (DnxArguments(launch) is { } afterDnx) return Dnx(doc, line, LaunchForm.ClientConfig, afterDnx);

        return new LaunchLine(doc, line, LaunchForm.ClientConfig, command, null, arguments);
    }

    // A claude mcp add line: everything after its -- is the launch it registers.
    private static LaunchLine Registration(string doc, int line, string[] tokens)
    {
        int separator = Array.IndexOf(tokens, Separator);
        if (separator < 0 || separator == tokens.Length - 1)
            return new LaunchLine(doc, line, LaunchForm.ClaudeMcpAdd, null, null, null);

        string[] launch = tokens[(separator + 1)..];
        if (DnxArguments(launch) is { } afterDnx) return Dnx(doc, line, LaunchForm.ClaudeMcpAdd, afterDnx);

        return new LaunchLine(doc, line, LaunchForm.ClaudeMcpAdd, launch[0], null, launch[1..]);
    }

    // What follows dnx: its own options and the package, then after -- the arguments the tool is handed. A package
    // may carry a version after an @, which the id comparison leaves out.
    private static LaunchLine Dnx(string doc, int line, LaunchForm form, string[] afterDnx)
    {
        int separator = Array.IndexOf(afterDnx, Separator);
        string[] dnxArguments = separator < 0 ? afterDnx : afterDnx[..separator];
        string? package = PackageToken(dnxArguments)
            ?.Split('@')[0];
        string[]? toolArguments = separator < 0 ? null : afterDnx[(separator + 1)..];

        return new LaunchLine(doc, line, form, null, package, toolArguments);
    }

    // The first dnx argument that is neither an option nor the value a value-taking option was handed, so a
    // --source <feed> ahead of the package is not read as the package.
    private static string? PackageToken(string[] dnxArguments)
    {
        for (var i = 0; i < dnxArguments.Length; i++)
        {
            string token = dnxArguments[i];
            if (!token.StartsWith('-')) return token;
            if (DnxValueOptions.Contains(token)) i++;
        }

        return null;
    }

    private static string[]? DnxArguments(string[] tokens)
    {
        return tokens switch
        {
            ["dotnet", "dnx", .. var rest] => rest,
            ["dnx", .. var rest] => rest,
            _ => null
        };
    }

    // The doc line a config entry's name sits on, so a finding points at the entry rather than at its fence.
    private static int LineNaming(IReadOnlyList<(string Text, int Number)> block, string name)
    {
        var quoted = $"\"{name}\"";
        foreach ((string text, int number) in block)
            if (text.Contains(quoted, StringComparison.Ordinal))
                return number;

        return block[0].Number;
    }

    private static string Describe(LaunchForm form)
    {
        return form switch
        {
            LaunchForm.ClientConfig => "client config",
            LaunchForm.ClaudeMcpAdd => "claude mcp add",
            _ => "dnx"
        };
    }

    /// <summary>How a launch line reaches the tool.</summary>
    private enum LaunchForm
    {
        /// <summary>An entry under <c>mcpServers</c> in a client's JSON config.</summary>
        ClientConfig,

        /// <summary>Claude Code's own registration command, <c>claude mcp add</c>.</summary>
        ClaudeMcpAdd,

        /// <summary>A <c>dnx</c> launch, which fetches the package and runs it without an install.</summary>
        Dnx
    }

    /// <summary>
    ///     One launch found in a fence: the <paramref name="Doc" /> and 1-based <paramref name="Line" /> it sits on,
    ///     its <paramref name="Form" />, the <paramref name="Command" /> it runs or the <paramref name="Package" /> it
    ///     fetches, and the <paramref name="Arguments" /> it hands the tool, which are null when it hands none.
    /// </summary>
    private sealed record LaunchLine(
        string Doc,
        int Line,
        LaunchForm Form,
        string? Command,
        string? Package,
        string[]? Arguments);
}
