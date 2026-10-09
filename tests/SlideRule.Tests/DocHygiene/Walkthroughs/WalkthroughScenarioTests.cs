using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     The half of the walkthrough gate that needs no build and no workspace, so it runs wherever the suite does.
/// </summary>
/// <remarks>
///     The replay itself runs only where the examples are built. The facts here are what keep a scenario from
///     rotting in between: an edit to a committed example file that moves an anchor reds here on every machine,
///     not only on the one job that builds the examples.
/// </remarks>
public sealed class WalkthroughScenarioTests
{
    // A line of an edit fence that names the file the lines after it come from, as `// OutboxDispatcher.cs`
    // or `// OutboxDispatcher.cs, in the poll loop`.
    private static readonly Regex FileLabel = new(@"^// (?<file>[A-Za-z0-9_.]+\.cs)\b", RegexOptions.CultureInvariant);

    /// <summary>Every scenario, one row each.</summary>
    public static TheoryData<string> Scenarios => new(WalkthroughScenarios.All.Select(static scenario => scenario.Key));

    /// <summary>Every edit fence that quotes a scenario, one row each.</summary>
    public static TheoryData<string> ScenarioEditFences =>
        new(WalkthroughFences.Edits.Where(static fence => fence.Scenario is not null)
            .Select(static fence => fence.Key));

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Scenario_AppliesToTheCommittedTree(string key)
    {
        // Arrange
        WalkthroughScenario scenario = WalkthroughScenarios.Named(key);

        // Act: every anchor occurs once in the file it edits and every imagined file is still imagined, or this
        // throws naming the scenario and the anchor.
        IReadOnlyDictionary<string, string> edited = scenario.Apply(path => ReadCommitted(scenario.Example, path));

        // Assert: an edit that changed nothing would replay the committed tree under a scenario's name.
        List<string> unchanged = edited.Where(file => file.Value == ReadCommitted(scenario.Example, file.Key))
            .Select(static file => file.Key)
            .ToList();
        unchanged.ShouldReportNothing($"Scenario '{key}' leaves these files as committed");
    }

    /// <summary>
    ///     A fence showing the code a scenario writes must be that code: every non-blank line, in order, a
    ///     verbatim substring of a line of the scenario's edited file, so a fence may leave lines out and
    ///     dedent the ones it keeps but may not show code the scenario does not write. A label line such as
    ///     <c>// OutboxDispatcher.cs</c> moves the fence on to that file.
    /// </summary>
    [Theory]
    [MemberData(nameof(ScenarioEditFences))]
    public void EditFence_IsAnInOrderSliceOfItsScenario(string key)
    {
        // Arrange
        EditFence fence = WalkthroughFences.EditNamed(key);
        WalkthroughScenario scenario = WalkthroughScenarios.Named(fence.Scenario!);
        IReadOnlyDictionary<string, string> edited = scenario.Apply(path => ReadCommitted(scenario.Example, path));
        QuotedFence quoted = fence.ShouldFindFence();

        // Act
        List<string> strays = [];
        foreach ((string file, IReadOnlyList<string> lines) in Segments(quoted.Lines, fence.File!))
        {
            IReadOnlyList<string> source = EditedFile(edited, file, scenario.Key)
                .Split('\n');
            string? stray = QuoteSyncTests.FirstLineNotInSource(lines, source);
            if (stray is not null) strays.Add($"'{stray}' is not a line of {file} after the one before it");
        }

        // Assert
        strays.ShouldReportNothing($"{fence.Doc}:{quoted.Line} shows code scenario '{scenario.Key}' does not write");
    }

    [Fact]
    public void DraftFence_ParsesAsCSharp()
    {
        // Arrange: the replay compiles this fence as the page prints it, which only a machine that built the
        // examples can do; a fence that no longer parses is a fence every other machine can already refuse.
        QuotedFence draft = WalkthroughFences.AdoptingDraft.ShouldFindFence();

        // Act
        List<string> errors = CSharpSyntaxTree.ParseText(string.Join("\n", draft.Lines))
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToList();

        // Assert
        errors.ShouldReportNothing($"{DocPaths.MeridianAdopting}:{draft.Line} no longer parses");
    }

    [Fact]
    public void EveryCut_IsDeclaredOnItsPage()
    {
        // Arrange
        List<string> undeclared = [];

        // Act: a fence compared with a cut of its run is honest only while the page says what it cut; the
        // sentence saying so must still be in the page's prose, however the paragraph is wrapped.
        foreach (ReplayedFence fence in WalkthroughFences.Replayed.Where(static fence => fence.DeclaredCut is not null))
            if (!Prose(fence.Fence.Doc)
                    .Contains(Spaced(fence.DeclaredCut!), StringComparison.Ordinal))
                undeclared.Add($"{fence.Fence.Doc} {fence.Fence.Key}: the page no longer says \"{fence.DeclaredCut}\"");

        // Assert
        undeclared.ShouldReportNothing("These fences show part of a run their page no longer says it cut");
    }

    [Fact]
    public void EveryCapture_IsDeclaredOnItsPage()
    {
        // Arrange
        List<string> undeclared = [];

        // Act: a fence quoting another tool's output is nothing the replay can make again, so its page has to
        // say it is a capture, and the sentence saying so must still be there.
        foreach (IllustrativeFence fence in WalkthroughFences.Illustrative)
            if (!Prose(fence.Doc)
                    .Contains(Spaced(fence.Declaration), StringComparison.Ordinal))
                undeclared.Add($"{fence.Doc} {fence.Key}: the page no longer says \"{fence.Declaration}\"");

        // Assert
        undeclared.ShouldReportNothing("These fences quote another tool's output that their page no longer declares a capture");
    }

    [Fact]
    public void EveryFenceNoRunHolds_RecordsWhy()
    {
        // Act: a fence no run re-makes is a decision, and its registration is where the reason for it is kept.
        List<string> unexplained = WalkthroughFences.Illustrative.Select(static fence => (fence.Key, fence.Reason))
            .Concat(WalkthroughFences.Reading.Select(static fence => (fence.Key, fence.Reason)))
            .Where(static row => string.IsNullOrWhiteSpace(row.Reason))
            .Select(static row => row.Key)
            .ToList();

        // Assert
        unexplained.ShouldReportNothing("These walkthrough fences are held by no run and record no reason why");
    }

    [Fact]
    public void Registry_NamesEveryScenario_AndOnlyScenariosThatExist()
    {
        // Arrange
        HashSet<string> scenarios = WalkthroughScenarios.All.Select(static scenario => scenario.Key)
            .ToHashSet(StringComparer.Ordinal);
        List<(string Key, string? Scenario, string? Example)> rows =
        [
            .. WalkthroughFences.Replayed.Select(static fence => (fence.Fence.Key, fence.Scenario, (string?)fence.Example)),
            .. WalkthroughFences.Edits.Select(static fence => (fence.Key, fence.Scenario, (string?)null))
        ];

        // Act
        List<string> findings = [];
        findings.AddRange(rows.Where(row => row.Scenario is not null && !scenarios.Contains(row.Scenario))
            .Select(static row => $"{row.Key} names scenario '{row.Scenario}', which does not exist."));
        findings.AddRange(rows.Where(static row => row is { Scenario: not null, Example: not null })
            .Where(row => scenarios.Contains(row.Scenario!) && WalkthroughScenarios.Named(row.Scenario!).Example != row.Example)
            .Select(static row => $"{row.Key} replays scenario '{row.Scenario}' over {row.Example}, which is not its example."));
        findings.AddRange(scenarios.Where(key => rows.All(row => row.Scenario != key))
            .Select(static key => $"Scenario '{key}' is quoted by no fence."));
        findings.AddRange(WalkthroughFences.Markers()
            .GroupBy(static marked => marked.Key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => $"{group.Key} keys {group.Count()} fences."));

        // Assert
        findings.ShouldReportNothing("The walkthrough registry and its scenarios disagree");
    }

    // The fence's lines grouped by the file they quote: the fence's own file until a label line names another.
    private static IEnumerable<(string File, IReadOnlyList<string> Lines)> Segments(IReadOnlyList<string> fence, string file)
    {
        List<string> lines = [];
        foreach (string line in fence)
        {
            Match label = FileLabel.Match(line);
            if (!label.Success)
            {
                lines.Add(line);
                continue;
            }

            if (lines.Count > 0) yield return (file, lines);

            file = label.Groups["file"].Value;
            lines = [];
        }

        if (lines.Count > 0) yield return (file, lines);
    }

    // The scenario's edited text of the one file it changes under this name.
    private static string EditedFile(IReadOnlyDictionary<string, string> edited, string fileName, string scenario)
    {
        List<string> named = edited.Keys.Where(candidate => Path.GetFileName(candidate) == fileName)
            .ToList();

        return named is [var path]
            ? edited[path]
            : throw new ShouldAssertException($"Scenario '{scenario}' changes {named.Count} files named {fileName}, where its fence quotes one.");
    }

    private static string? ReadCommitted(string example, string path)
    {
        string absolute = RepoRoot.Absolute($"examples/{example}/{path}");
        return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
    }

    // The page's prose as one run of single-spaced words, so a sentence reads the same however it is wrapped.
    private static string Prose(string doc)
    {
        return Spaced(string.Join(" ", SourceAnchors.ProseLines(RepoRoot.ReadText(doc))
            .Select(static line => line.Text)));
    }

    private static string Spaced(string text)
    {
        return string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
