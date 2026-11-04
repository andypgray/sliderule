using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that holds every call of the published action, in the operations guide and in this repository's
///     CI, to inputs the action declares.
/// </summary>
/// <remarks>
///     GitHub runs a step that passes an input its action does not declare, and only warns, so a renamed input
///     leaves the guide's example running with that setting dropped.
/// </remarks>
public sealed class ActionInputTests
{
    // An input's key in the metadata: two spaces in, under the top-level inputs map.
    private static readonly Regex InputKey = new(@"^  (?<name>[a-z][a-z0-9-]*):\s*$");

    // A key of a step's with map.
    private static readonly Regex WithKey = new(@"^\s*(?<name>[a-z][a-z0-9-]*):");

    [Fact]
    public void ActionCalls_InTheGuideAndInCi_PassOnlyDeclaredInputs()
    {
        // Arrange
        IReadOnlyCollection<string> declared = ShouldReadDeclaredInputs();
        QuotedFence guide = QuotedFence.ShouldFindOne(
            DocPaths.Operating, "uses: andypgray/sliderule@", "the guide's GitHub workflow");
        IReadOnlyList<Call> calls =
        [
            .. CallsIn(ActionUses.In(DocPaths.Operating, guide.Lines.ToArray(), guide.Line), "andypgray/sliderule@"),
            .. CallsIn(ActionUses.In(TrackedFiles.CiWorkflow), "./")
        ];

        // Act
        List<string> undeclared = calls
            .SelectMany(static call => call.Inputs.Select(input => (call, input)))
            .Where(pair => !declared.Contains(pair.input))
            .Select(static pair => $"  {pair.call.File}:{pair.call.Line}: {pair.input}")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        calls.Where(static call => call.Inputs.Length > 0)
            .Select(static call => call.File)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(
                [TrackedFiles.CiWorkflow, DocPaths.Operating],
                ignoreOrder: false,
                customMessage: "each file should call the action with inputs this scan can read");
        undeclared.ShouldReportNothing(
            $"Call(s) of the action pass an input {TrackedFiles.ActionMetadata} does not declare (it declares "
            + $"{string.Join(", ", declared)})");
    }

    private static IReadOnlyCollection<string> ShouldReadDeclaredInputs()
    {
        string[] lines = RepoRoot.ReadLines(TrackedFiles.ActionMetadata);
        string[] inputs = lines
            .SkipWhile(static line => line != "inputs:")
            .Skip(1)
            .TakeWhile(static line => line.Length == 0 || line.StartsWith(' '))
            .Select(static line => InputKey.Match(line))
            .Where(static match => match.Success)
            .Select(static match => match.Groups["name"].Value)
            .ToArray();
        inputs.ShouldNotBeEmpty($"No inputs were read from {TrackedFiles.ActionMetadata}.");

        return inputs;
    }

    // Each step whose action reference starts with the prefix, with the keys of the with map in its body.
    private static IEnumerable<Call> CallsIn(IEnumerable<ActionUse> uses, string reference)
    {
        return uses
            .Where(use => use.Reference.StartsWith(reference, StringComparison.Ordinal))
            .Select(static use => new Call(use.File, use.Line, WithKeysOf(use.Body)));
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

    private sealed record Call(string File, int Line, string[] Inputs);
}
