using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Holds the tool table on the coding-agents page to the MCP server's live surface: a row for every
///     <c>[McpServerTool]</c> and every <c>[McpServerPrompt]</c> and no other, and each row's Params cell naming
///     exactly the parameters that tool or prompt binds from a call's argument object, with a trailing <c>?</c> on
///     exactly the optional ones.
/// </summary>
/// <remarks>
///     <para>
///         The Params column drifted twice while nothing held it: <c>skeleton</c> was missing from the
///         <c>arch_graph</c> row for three days, and <c>index</c> from both laddered rows for twenty days after it
///         shipped.
///     </para>
///     <para>
///         The live side is the enumeration <see cref="Mcp.CoercerCoverageTests" /> audits the coercers with: the
///         tool methods gated on the attribute's name, then their JSON-bound parameters, a parameter being optional
///         when it declares a default. The prompts are read the same way, from the prompt types the server
///         registers, non-public and static methods included, because the one prompt is both. The documented side
///         is the backticked names in each Params cell, read outside parentheses, because a parenthetical glosses a
///         type and never names a parameter; a cell of "none" names none. The Returns column stays prose, held by
///         review.
///     </para>
/// </remarks>
public sealed class McpToolTableTests
{
    private const BindingFlags PromptMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                                               | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    // A row's first cell is a tool or prompt name and nothing else: snake_case in backticks.
    private static readonly Regex SurfaceRow = new(@"^\|\s*`([a-z]+(?:_[a-z]+)+)`\s*\|", RegexOptions.CultureInvariant);

    private static readonly Regex Parenthetical = new(@"\([^()]*\)", RegexOptions.CultureInvariant);
    private static readonly Regex BacktickedName = new(@"`([A-Za-z][A-Za-z0-9]*\??)`", RegexOptions.CultureInvariant);

    [Fact]
    public void ToolTable_HasARowForEachToolAndPromptAndNoOther()
    {
        // Arrange
        Dictionary<string, IReadOnlySet<string>> documented = DocumentedRows();
        Dictionary<string, IReadOnlySet<string>> live = LiveSurface();

        // Act
        IEnumerable<string> undocumented = live.Keys
            .Except(documented.Keys, StringComparer.Ordinal)
            .Select(static name => $"{name} is on the server's surface but has no row");
        IEnumerable<string> stale = documented.Keys
            .Except(live.Keys, StringComparer.Ordinal)
            .Select(static name => $"{name} has a row but is not on the server's surface");
        List<string> drift = undocumented
            .Concat(stale)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (live.Count == 0)
            drift.Add("Reflection found no tool and no prompt, so there was no surface to compare the table with.");

        // Assert
        drift.ShouldReportNothing(
            $"{DocPaths.CodingAgents}'s tool table has drifted from the [McpServerTool] and [McpServerPrompt] methods");
    }

    [Fact]
    public void ParamsCells_SpellEachToolsJsonBoundParameters()
    {
        // Arrange
        Dictionary<string, IReadOnlySet<string>> documented = DocumentedRows();
        Dictionary<string, IReadOnlySet<string>> live = LiveSurface();

        // Act
        List<string> drift = live
            .Where(entry => documented.ContainsKey(entry.Key))
            .SelectMany(entry => ParameterDrift(entry.Key, entry.Value, documented[entry.Key]))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert: an optionality mismatch reads as two lines, the surface's spelling and the cell's.
        drift.ShouldReportNothing(
            $"{DocPaths.CodingAgents}'s Params column has drifted from the JSON-bound parameters, spelled name? "
            + "where a call may leave one out");
    }

    private static IEnumerable<string> ParameterDrift(string name, IReadOnlySet<string> live, IReadOnlySet<string> documented)
    {
        IEnumerable<string> undocumented = live
            .Except(documented, StringComparer.Ordinal)
            .Select(parameter => $"{name}.{parameter} is on the server's surface but not in its Params cell");
        IEnumerable<string> stale = documented
            .Except(live, StringComparer.Ordinal)
            .Select(parameter => $"{name}.{parameter} is in its Params cell but not on the server's surface");

        return undocumented.Concat(stale);
    }

    private static Dictionary<string, IReadOnlySet<string>> DocumentedRows()
    {
        return RepoRoot.ReadLines(DocPaths.CodingAgents)
            .Where(static line => SurfaceRow.IsMatch(line))
            .Select(static line => line.Split('|'))
            .ToDictionary(
                static cells => cells[1].Trim()
                    .Trim('`'),
                static cells => CellParameters(cells[2]),
                StringComparer.Ordinal);
    }

    // The backticked names in a Params cell, read outside its parentheses, spelled as the cell spells them.
    private static IReadOnlySet<string> CellParameters(string cell)
    {
        string unglossed = cell;
        string previous;
        do
        {
            previous = unglossed;
            unglossed = Parenthetical.Replace(previous, "");
        } while (unglossed != previous);

        return BacktickedName.Matches(unglossed)
            .Select(static match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    // Every tool and prompt the server registers, each with its JSON-bound parameters as a Params cell spells them.
    private static Dictionary<string, IReadOnlySet<string>> LiveSurface()
    {
        Dictionary<string, IReadOnlySet<string>> surface = new(StringComparer.Ordinal);
        foreach ((string toolName, MethodInfo method) in ToolAttributeDiscovery.GetNamedToolMethods())
            surface.Add(toolName, CellSpellings(method));

        IEnumerable<MethodInfo> promptMethods = typeof(ToolAttributeDiscovery).Assembly
            .GetTypes()
            .Where(static type => type.GetCustomAttribute<McpServerPromptTypeAttribute>() is not null)
            .SelectMany(static type => type.GetMethods(PromptMethods));
        foreach (MethodInfo method in promptMethods)
        {
            if (method.GetCustomAttribute<McpServerPromptAttribute>()
                    ?.Name is not { } promptName) continue;

            surface.Add(promptName, CellSpellings(method));
        }

        return surface;
    }

    private static HashSet<string> CellSpellings(MethodInfo method)
    {
        return method.GetParameters()
            .Where(ToolAttributeDiscovery.IsJsonBoundParameter)
            .Select(CellSpelling)
            .ToHashSet(StringComparer.Ordinal);
    }

    // How a Params cell spells a parameter: its name, with a trailing ? when a call may leave it out.
    private static string CellSpelling(ParameterInfo parameter)
    {
        return parameter.HasDefaultValue ? $"{parameter.Name}?" : parameter.Name!;
    }
}
