using System.Text.Json;
using System.Text.Json.Nodes;
using Shouldly;
using SlideRule.Cli.Rendering;
using SlideRule.Tests.Cli;
using SlideRule.Tests.DocHygiene.Walkthroughs;
using SlideRule.Tests.ModelFile;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The two fences the self-check page cuts from a check over Meridian, re-run: one SARIF result object,
///     and the same rule's entry in the check document. One fresh check writes both documents, and each fence
///     must be the node it quotes, serialized as the writers serialize it. That holds the keys, their order,
///     the indentation and the escaping at once, and makes the live text the replacement a red prints.
/// </summary>
/// <remarks>
///     <para>
///         Nothing in this suite builds an example, so both fences skip unless Meridian's spec assembly is on
///         disk. CI's examples job builds it and then runs this class, which
///         <see cref="ExampleGatedTests_AreRunByTheExamplesWorkflow" /> holds the workflow to.
///     </para>
/// </remarks>
public sealed class SelfCheckMeridianFenceTests
{
    private const string Workflow = ".github/workflows/ci.yml";

    private const string MeridianSolution = "examples/Meridian/Meridian.slnx";

    private const string MeridianSpecProject = "examples/Meridian/arch/Meridian.Architecture";

    /// <summary>
    ///     The filter the examples job runs the suite's example-gated classes through: each skips unless the
    ///     examples are built, and that job is the only place they are.
    /// </summary>
    private const string ExamplesWorkflowFilter =
        $"FullyQualifiedName~{nameof(ExampleModelRoundTripTests)}"
        + $"|FullyQualifiedName~{nameof(SelfCheckMeridianFenceTests)}"
        + $"|FullyQualifiedName~{nameof(WalkthroughReplayTests)}";

    private static readonly Lazy<Task<MeridianRun>> Run = new(CheckAsync);

    [Fact]
    public async Task SarifResultFence_IsTheResultAFreshCheckWrites()
    {
        // Arrange
        SkipUnlessMeridianIsBuilt();
        MeridianRun run = await Run.Value;
        JsonObject quoted = ShouldParse(CapturedFences.SarifResult);

        // Act
        JsonNode result = ShouldFindResult(run.Sarif, quoted);

        // Assert
        CapturedFences.SarifResult.ShouldMatchTheRun(result.ToJsonString(SlideRuleJson.Options));
    }

    [Fact]
    public async Task CheckDocumentFence_IsTheRuleEntryAFreshCheckWrites()
    {
        // Arrange
        SkipUnlessMeridianIsBuilt();
        MeridianRun run = await Run.Value;
        JsonObject quoted = ShouldParse(CapturedFences.CheckDocument);

        // Act
        JsonNode entry = ShouldFindRule(run.Check, quoted);

        // Assert
        CapturedFences.CheckDocument.ShouldMatchTheRun(entry.ToJsonString(SlideRuleJson.Options));
    }

    [Fact]
    public void ExampleGatedTests_AreRunByTheExamplesWorkflow()
    {
        // Act
        string workflow = RepoRoot.ReadText(Workflow);

        // Assert: the only place the examples are built before the suite runs, so the only place the two facts
        // above, the model-file round trips and the walkthrough replays run at all. This line going missing
        // turns them into skips everywhere, in silence.
        workflow.ShouldContain(
            $"--filter \"{ExamplesWorkflowFilter}\"",
            customMessage: $"{Workflow} no longer runs the example-gated classes after building the examples, so "
                           + "nothing re-runs the self-check page's two Meridian fences or the walkthroughs' output "
                           + "fences.");
    }

    // One check writes both documents: the check document on stdout and the SARIF file beside it, the cache
    // left alone so the run is a fresh one.
    private static async Task<MeridianRun> CheckAsync()
    {
        using TempDirectory temp = TestTempRoot.Fresh("self-check-meridian");
        string sarifPath = temp.PathOf("meridian.sarif");

        CliResult result = await CliRunner.InvokeAsync(
            "check", RepoRoot.Absolute(MeridianSolution), "--json", "--sarif", sarifPath, "--no-cache");
        result.ShouldSucceed();

        JsonNode sarif = JsonNode.Parse(await File.ReadAllTextAsync(sarifPath))!;
        JsonNode check = JsonNode.Parse(result.Out)!;

        return new MeridianRun(sarif, check);
    }

    private static void SkipUnlessMeridianIsBuilt()
    {
        string specProject = RepoRoot.Absolute(MeridianSpecProject);
        string searched = ExampleBuilds.SpecOutputDirectory(specProject);

        Assert.SkipUnless(
            ExampleBuilds.IsBuilt(searched, specProject),
            "Nothing in this suite builds the example solutions, and these fences quote a check over Meridian, "
            + $"which reads the spec assembly a build of it produces. Build it and run this again. Looked in: {searched}");
    }

    // The fence as the JSON object it quotes, or a red naming the line where it stopped being one.
    private static JsonObject ShouldParse(CapturedFence captured)
    {
        QuotedFence fence = captured.ShouldFindFence();
        try
        {
            return JsonNode.Parse(string.Join("\n", fence.Lines))!
                .AsObject();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new ShouldAssertException(
                $"{captured.Doc}:{fence.Line} should quote one JSON object, but it does not parse as one: "
                + exception.Message);
        }
    }

    // The result the fence quotes, found by the fingerprints it carries: the violation identity names the
    // site, which is what makes one of the twelve grandfathered results the one on the page.
    private static JsonNode ShouldFindResult(JsonNode sarif, JsonObject quoted)
    {
        JsonNode? fingerprints = quoted["partialFingerprints"];
        List<JsonNode> results = sarif["runs"]![0]!["results"]!
            .AsArray()
            .OfType<JsonNode>()
            .ToList();

        JsonNode? found = results.FirstOrDefault(result => JsonNode.DeepEquals(result["partialFingerprints"], fingerprints));
        if (found is not null) return found;

        IEnumerable<string> written = results.Select(static result => result["partialFingerprints"]?.ToJsonString() ?? "(none)");
        throw new ShouldAssertException(
            $"No result of a fresh check carries the fence's fingerprints {fingerprints?.ToJsonString() ?? "(none)"}. "
            + $"It wrote:\n{string.Join("\n", written)}");
    }

    // The rule entry the fence quotes, found by its id.
    private static JsonNode ShouldFindRule(JsonNode check, JsonObject quoted)
    {
        var id = quoted["id"]
            ?.GetValue<string>();
        List<JsonNode> rules = check["rules"]!
            .AsArray()
            .OfType<JsonNode>()
            .ToList();

        JsonNode? found = rules.FirstOrDefault(rule => rule["id"]
            ?.GetValue<string>() == id);
        if (found is not null) return found;

        IEnumerable<string?> written = rules.Select(static rule => rule["id"]
            ?.GetValue<string>());
        throw new ShouldAssertException(
            $"A fresh check reports no rule '{id}'. It reported: {string.Join(", ", written)}");
    }

    /// <summary>The two documents one check over Meridian wrote.</summary>
    /// <param name="Sarif">The SARIF log it wrote beside stdout.</param>
    /// <param name="Check">The check document it wrote on stdout.</param>
    private sealed record MeridianRun(JsonNode Sarif, JsonNode Check);
}
