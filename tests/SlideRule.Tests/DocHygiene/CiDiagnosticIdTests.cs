extern alias guidance;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Analyzers;
using SlideRule.Tests.TestSupport;
using Xunit;
using GuidanceDiagnostics = guidance::SlideRule.Guidance.DotNet.GuidanceDiagnostics;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The diagnostic IDs the CI workflow spells out by hand, held to the descriptor sets they copy: the two lists its
///     builds promote to errors, and the IDs the guidance consumer step expects a build of the violating sources to
///     report. A descriptor added, dropped or shipped at another severity leaves a copy stale, and CI says so only
///     after a push, as a build that promotes too little or a consumer step that reds on correct output.
/// </summary>
public sealed class CiDiagnosticIdTests
{
    private const string ConsumerStep = "Build a fresh project on the packed guidance package";

    // One `.editorconfig` severity line as the consumer step's printf writes it.
    private static readonly Regex SeverityEntry = new(
        @"dotnet_diagnostic\.(?<id>SR[0-9]{4})\.severity = (?<severity>[a-z]+)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void BuildTierIds_AreTheBuildAnalyzersSupportedIds()
    {
        // Arrange
        string[] supported = new BuildTierAnalyzer().SupportedDiagnostics
            .Select(static descriptor => descriptor.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Act
        (int line, string[] listed) = ShouldFindEnvList("SR_BUILD_TIER_IDS");

        // Assert
        listed.ShouldBe(
            supported,
            $"{TrackedFiles.CiWorkflow}:{line} SR_BUILD_TIER_IDS should list the build analyzer's supported IDs in order");
    }

    [Fact]
    public void GuidanceIds_AreTheGuidanceAnalyzersDescriptors()
    {
        // Arrange
        string[] descriptors = GuidanceDiagnostics.All
            .Select(static descriptor => descriptor.Id)
            .ToArray();

        // Act
        (int line, string[] listed) = ShouldFindEnvList("SR_GUIDANCE_IDS");

        // Assert
        listed.ShouldBe(
            descriptors,
            $"{TrackedFiles.CiWorkflow}:{line} SR_GUIDANCE_IDS should list the guidance analyzer's descriptors in order");
    }

    [Fact]
    public void ConsumerStep_ExpectsEachRuleThatShipsAtWarning_ThenThatSetTunedByItsEditorConfig()
    {
        // Arrange: the step sorts what the build reported with `sort -u`, which orders these IDs ordinally.
        SortedSet<string> atWarning = new(
            GuidanceDiagnostics.ByRule.Values
                .Where(static descriptor => descriptor.DefaultSeverity == DiagnosticSeverity.Warning)
                .Select(static descriptor => descriptor.Id),
            StringComparer.Ordinal);
        IReadOnlyList<(int Line, string Text)> step = ShouldFindStep(ConsumerStep);

        // Act
        (int Line, string[] Ids)[] expected = step
            .Where(static line => line.Text.TrimStart().StartsWith("expected='", StringComparison.Ordinal))
            .Select(static line => (line.Line, Ids: line.Text.Trim()["expected=".Length..].Trim('\'').Split(' ')))
            .ToArray();
        (int Line, string Text) tuning = step
            .Where(static line => line.Text.Contains(".editorconfig", StringComparison.Ordinal)
                                  && line.Text.TrimStart().StartsWith("printf ", StringComparison.Ordinal))
            .ShouldHaveSingleItem($"The step '{ConsumerStep}' should write one .editorconfig with printf.");
        SortedSet<string> tuned = ShouldApplyTuning(atWarning, tuning);

        // Assert
        expected.Length.ShouldBe(2, $"The step '{ConsumerStep}' should carry two expected= lists, the violating build's and the tuned build's.");
        expected[0].Ids.ShouldBe(
            atWarning.ToArray(),
            $"{TrackedFiles.CiWorkflow}:{expected[0].Line} should expect each guidance rule that ships at Warning, sorted");
        expected[1].Ids.ShouldBe(
            tuned.ToArray(),
            $"{TrackedFiles.CiWorkflow}:{expected[1].Line} should expect that set after the .editorconfig at line {tuning.Line}, sorted");
    }

    // The IDs a top-level env key lists, and its line, or a red when the workflow does not declare the key once.
    private static (int Line, string[] Ids) ShouldFindEnvList(string key)
    {
        string[] lines = RepoRoot.ReadLines(TrackedFiles.CiWorkflow);
        int index = Enumerable.Range(0, lines.Length)
            .Where(index => lines[index].TrimStart().StartsWith($"{key}: ", StringComparison.Ordinal))
            .ShouldHaveSingleItem($"{TrackedFiles.CiWorkflow} should declare {key} once.");
        string value = lines[index].Trim()[(key.Length + 1)..].Trim().Trim('\'');

        return (index + 1, value.Split(','));
    }

    // The body of the one step named name, each line with its number in the workflow. The body begins on the line
    // after the name's, so its first line is the name's line plus 1.
    private static IReadOnlyList<(int Line, string Text)> ShouldFindStep(string name)
    {
        (int nameLine, string body) = QuoteSyncTests.WorkflowStepsWithLines(RepoRoot.ReadText(TrackedFiles.CiWorkflow), name)
            .ShouldHaveSingleItem($"{TrackedFiles.CiWorkflow} should have one step named '{name}'.");

        return body.Split('\n')
            .Select((text, offset) => (nameLine + offset + 1, text))
            .ToArray();
    }

    // The set a build reports once the printf's .editorconfig is applied: a rule raised to warning or error joins it,
    // and one turned off leaves it.
    private static SortedSet<string> ShouldApplyTuning(IEnumerable<string> atWarning, (int Line, string Text) tuning)
    {
        SortedSet<string> tuned = new(atWarning, StringComparer.Ordinal);
        MatchCollection entries = SeverityEntry.Matches(tuning.Text);
        entries.ShouldNotBeEmpty($"{TrackedFiles.CiWorkflow}:{tuning.Line} should set at least one SR severity: {tuning.Text.Trim()}");

        foreach (Match entry in entries)
        {
            string id = entry.Groups["id"].Value;
            string severity = entry.Groups["severity"].Value;
            switch (severity)
            {
                case "warning" or "error":
                    tuned.Add(id);
                    break;
                case "none" or "silent" or "suggestion":
                    tuned.Remove(id);
                    break;
                default:
                    throw new ShouldAssertException($"{TrackedFiles.CiWorkflow}:{tuning.Line} sets {id} to '{severity}', which this gate cannot apply.");
            }
        }

        return tuned;
    }
}
