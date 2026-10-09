using Shouldly;
using Xunit;
using SlideRule.Checking;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     The example walkthroughs' output fences, re-run. Each scenario a page imagines is made in memory over the
///     example's workspace, checked and rendered by the product's own code, and every fence quoting that run must
///     be what it prints, cut as the page says it is cut. A stale fence reds printing the text to paste.
/// </summary>
/// <remarks>
///     <para>
///         Nothing in this suite builds an example, so every case skips unless its example's spec assembly is on
///         disk. CI's examples job builds all four and then runs this class, which
///         <see cref="SelfCheckMeridianFenceTests.ExampleGatedTests_AreRunByTheExamplesWorkflow" /> holds the
///         workflow to.
///     </para>
/// </remarks>
public sealed class WalkthroughReplayTests
{
    /// <summary>Every replayed fence, one row each.</summary>
    public static TheoryData<string> ReplayedFences => new(WalkthroughFences.Replayed.Select(static fence => fence.Fence.Key));

    /// <summary>Every scenario, one row each.</summary>
    public static TheoryData<string> Scenarios => new(WalkthroughScenarios.All.Select(static scenario => scenario.Key));

    /// <summary>Every example, one row each.</summary>
    public static TheoryData<string> Examples => new(WalkthroughScenarios.Examples);

    [Theory]
    [MemberData(nameof(ReplayedFences))]
    public async Task Fence_IsWhatItsScenarioPrints(string key)
    {
        // Arrange
        ReplayedFence fence = WalkthroughFences.ReplayedNamed(key);
        SkipUnlessBuilt(fence.Example);

        // Act
        ExampleRun run = await ExampleReplay.RunOf(fence.Example);

        // Assert
        fence.ShouldMatchItsRun(run);
    }

    /// <summary>
    ///     Each page says which rules its edit breaks and quotes those alone, so the edit must move exactly those:
    ///     judged without the diff for the verdicts, because that is where a scenario's own findings show, and
    ///     under it for the warnings, because a tripwire says nothing anywhere else.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Scenario_MovesExactlyTheRulesItsPageNames(string key)
    {
        // Arrange
        WalkthroughScenario scenario = WalkthroughScenarios.Named(key);
        SkipUnlessBuilt(scenario.Example);
        ExampleRun run = await ExampleReplay.RunOf(scenario.Example);
        ScenarioRun edited = run.Scenario(key);

        // Act
        List<string> moved = Changed(run.Tree.Plain, edited.Plain, RuleVerdict.Of);
        List<string> warned = Changed(run.Tree.Diff, edited.Diff, static result => string.Join("\n", result.Warnings.Select(static warning => warning.Message)));

        // Assert
        moved.ShouldBe(scenario.Moves, $"the rules scenario '{key}' breaks are the ones its page quotes");
        warned.ShouldBe(scenario.Warns, $"the rules that warn under scenario '{key}''s diff are the ones its page quotes");
    }

    /// <summary>
    ///     The replay composes what the verbs print rather than running them, so over the committed tree, where
    ///     the read-only verbs can run, the two must agree: the same model, the same report and the same board.
    /// </summary>
    [Theory]
    [MemberData(nameof(Examples))]
    public async Task Composition_PrintsWhatTheVerbPrints(string example)
    {
        // Arrange
        SkipUnlessBuilt(example);
        ExampleRun run = await ExampleReplay.RunOf(example);
        string solution = ExampleSolutions.Named(example)
            .SolutionPath;

        // Act
        CliResult check = await CliRunner.InvokeAsync("check", solution, "--no-cache");
        CliResult status = await CliRunner.InvokeAsync("status", solution, "--no-cache");

        // Assert
        ModelDump.Render(run.Committed)
            .ShouldBe(ModelDump.Render(run.Governed), "the replay extracts the committed tree as the verbs do");
        check.ShouldSucceed();
        check.Out.NormalizedLines()
            .ShouldBe($"{run.Tree.Report}\n");
        status.ShouldSucceed();
        status.Out.NormalizedLines()
            .ShouldBe($"{string.Join("\n", run.Tree.StatusLines)}\n");
    }

    /// <summary>
    ///     The adoption walkthrough promises its reader that the walk lands on the committed baselines byte for
    ///     byte, content seals included. The capture its baseline fence quotes is that walk's, so what it writes
    ///     must be the committed files exactly.
    /// </summary>
    [Fact]
    public async Task AdoptingBaselineInit_WritesTheCommittedBaselinesByteForByte()
    {
        // Arrange
        SkipUnlessBuilt(WalkthroughScenarios.Meridian);
        ExampleRun run = await ExampleReplay.RunOf(WalkthroughScenarios.Meridian);
        string baselines = Path.Combine(run.SolutionDirectory, ExampleSolutions.ArchDirectory, "baselines");

        // Act
        Dictionary<string, byte[]> committed = Directory.EnumerateFiles(baselines, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(run.SolutionDirectory, path)
                    .Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

        // Assert
        IReadOnlyDictionary<string, byte[]> written = run.AdoptionSteps.BaselineFiles;
        written.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(committed.Keys.Order(StringComparer.Ordinal));
        List<string> differing = committed.Where(file => !file.Value.AsSpan()
                .SequenceEqual(written[file.Key]))
            .Select(static file => file.Key)
            .ToList();
        differing.ShouldReportNothing("These baselines differ from the ones a day-zero capture writes over the committed spec");
    }

    // The rules whose reading differs between the committed tree and the edited one, in report order.
    private static List<string> Changed(CheckReport committed, CheckReport edited, Func<RuleResult, string> reading)
    {
        return committed.Results.Zip(edited.Results)
            .Where(pair => reading(pair.First) != reading(pair.Second))
            .Select(static pair => pair.First.Rule.Id)
            .ToList();
    }

    private static void SkipUnlessBuilt(string example)
    {
        string specProject = ExampleSolutions.Named(example)
                                 .SpecProjectDirectory
                             ?? throw new InvalidOperationException($"{example} has no spec project.");
        string searched = ExampleBuilds.SpecOutputDirectory(specProject);

        Assert.SkipUnless(
            ExampleBuilds.IsBuilt(searched, specProject),
            $"Nothing in this suite builds the example solutions, and this replay reads the spec assembly a build of "
            + $"{example} produces. Build it and run this again. Looked in: {searched}");
    }
}
