using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that holds every call of an action this repository declares, the published one in the operations
///     guide and in this repository's CI and the workflows' local actions in CI, to inputs that action declares.
/// </summary>
/// <remarks>
///     GitHub runs a step that passes an input its action does not declare, and only warns, so a renamed input
///     leaves the guide's example running with that setting dropped, and a CI job running with its default.
/// </remarks>
public sealed class ActionInputTests
{
    private const string PublishedReference = "andypgray/sliderule@";

    // An input's key in the metadata: two spaces in, under the top-level inputs map.
    private static readonly Regex InputKey = new(@"^  (?<name>[a-z][a-z0-9-]*):\s*$");

    // A key of a step's with map.
    private static readonly Regex WithKey = new(@"^\s*(?<name>[a-z][a-z0-9-]*):");

    [Fact]
    public void ActionCalls_InTheGuideAndInCi_PassOnlyDeclaredInputs()
    {
        // Arrange
        QuotedFence guide = QuotedFence.ShouldFindOne(
            DocPaths.Operating, "uses: " + PublishedReference, "the guide's GitHub workflow");
        IReadOnlyList<Call> calls =
        [
            .. CallsIn(ActionUses.In(DocPaths.Operating, guide.Lines.ToArray(), guide.Line)),
            .. CallsIn(ActionUses.In(TrackedFiles.CiWorkflow))
        ];
        Dictionary<string, IReadOnlyCollection<string>> declared = calls
            .Select(static call => call.Metadata)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(static metadata => metadata, ShouldReadDeclaredInputs, StringComparer.Ordinal);

        // Act
        List<string> undeclared = calls
            .SelectMany(static call => call.Inputs.Select(input => (call, input)))
            .Where(pair => !declared[pair.call.Metadata].Contains(pair.input))
            .Select(pair => $"  {pair.call.File}:{pair.call.Line}: {pair.input} ({pair.call.Metadata} declares "
                            + $"{string.Join(", ", declared[pair.call.Metadata])})")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        calls.Where(static call => call.Inputs.Length > 0)
            .Select(static call => $"{call.File} -> {call.Metadata}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(
                [
                    $"{TrackedFiles.CiWorkflow} -> {TrackedFiles.SdkSetupAction}",
                    $"{TrackedFiles.CiWorkflow} -> {TrackedFiles.ActionMetadata}",
                    $"{DocPaths.Operating} -> {TrackedFiles.ActionMetadata}"
                ],
                ignoreOrder: true,
                customMessage: "each file should call each of these actions with inputs this scan can read");
        undeclared.ShouldReportNothing("Call(s) of an action pass an input its metadata does not declare");
    }

    private static IReadOnlyCollection<string> ShouldReadDeclaredInputs(string metadata)
    {
        string[] lines = RepoRoot.ReadLines(metadata);
        string[] inputs = lines
            .SkipWhile(static line => line != "inputs:")
            .Skip(1)
            .TakeWhile(static line => line.Length == 0 || line.StartsWith(' '))
            .Select(static line => InputKey.Match(line))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["name"].Value)
            .ToArray();
        inputs.ShouldNotBeEmpty($"No inputs were read from {metadata}.");

        return inputs;
    }

    // Each step that calls an action declared here, with the metadata file that declares it and the keys of its with
    // map. A local reference names the directory its metadata sits in, the checkout root for the published action.
    private static IEnumerable<Call> CallsIn(IEnumerable<ActionUse> uses)
    {
        return uses
            .Select(static use => (use, metadata: MetadataOf(use.Reference)))
            .Where(static pair => pair.metadata is not null)
            .Select(static pair => new Call(pair.use.File, pair.use.Line, pair.metadata!, WithKeysOf(pair.use.Body)));
    }

    private static string? MetadataOf(string reference)
    {
        if (reference.StartsWith(PublishedReference, StringComparison.Ordinal)) return TrackedFiles.ActionMetadata;

        if (!reference.StartsWith("./", StringComparison.Ordinal)) return null;

        string directory = reference[2..].TrimEnd('/');

        return directory.Length == 0 ? TrackedFiles.ActionMetadata : $"{directory}/{TrackedFiles.ActionMetadata}";
    }

    private static string[] WithKeysOf(string[] body)
    {
        int with = Array.FindIndex(body, static line => line.Trim() == "with:");
        if (with < 0) return [];

        int indent = ActionUses.IndentOf(body[with]);

        return body
            .Skip(with + 1)
            .TakeWhile(line => line.Trim().Length == 0 || ActionUses.IndentOf(line) > indent)
            .Select(static line => WithKey.Match(line))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["name"].Value)
            .ToArray();
    }

    private sealed record Call(string File, int Line, string Metadata, string[] Inputs);
}
