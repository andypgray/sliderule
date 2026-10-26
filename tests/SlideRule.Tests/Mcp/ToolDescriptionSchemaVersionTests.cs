using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Cli.Mcp.Pipeline;
using SlideRule.Cli.Rendering;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Holds the "(schemaVersion N)" each document-shaped tool's description quotes to the constant its
///     renderer writes, so a version bump cannot leave the model-facing text naming the old number. Only the
///     number is read: the wording around it belongs to the description.
/// </summary>
public sealed class ToolDescriptionSchemaVersionTests
{
    // Every live tool by wire name, with the version its description must quote, or null for a tool that
    // answers in text and whose description must quote none.
    private static readonly IReadOnlyDictionary<string, int?> ExpectedVersions = new Dictionary<string, int?>
    {
        [ArchToolNames.Check] = JsonReportRenderer.SchemaVersion,
        [ArchToolNames.Status] = StatusJsonRenderer.SchemaVersion,
        [ArchToolNames.Graph] = GraphJsonRenderer.SchemaVersion,
        [ArchToolNames.Explain] = null,
        [ArchToolNames.Context] = null
    };

    public static TheoryData<string> ToolNames => new(ExpectedVersions.Keys);

    [Fact]
    public void EveryLiveTool_HasAnExpectedVersion()
    {
        LiveToolDescriptions().Keys
            .ShouldBe(ExpectedVersions.Keys, ignoreOrder: true);
    }

    [Theory]
    [MemberData(nameof(ToolNames))]
    public void Description_QuotesTheVersionItsRendererWrites(string toolName)
    {
        string description = LiveToolDescriptions()[toolName];
        int[] expected = ExpectedVersions[toolName] is { } version ? [version] : [];

        QuotedVersions(description)
            .ShouldBe(expected, description);
    }

    private static Dictionary<string, string> LiveToolDescriptions()
    {
        return LiveToolParameters.NamedToolMethods()
            .ToDictionary(
                static tool => tool.Name,
                static tool => tool.Method.GetCustomAttribute<DescriptionAttribute>()!.Description);
    }

    private static int[] QuotedVersions(string description)
    {
        return Regex.Matches(description, @"\(schemaVersion (\d+)\)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();
    }
}
