using System.Text.Json;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The <c>status --json</c> document's optional slots (<see cref="StatusJsonRenderer" />), rendered over
///     reports checked in memory: the dead patterns listed under a ratcheted rule and where its unmatched entries
///     are counted, and the orphan scan's list. Each slot is absent where it has nothing to say. The whole-document
///     shape is pinned by the <c>violated-status.json</c> golden, which reaches none of these slots.
/// </summary>
public sealed class StatusJsonRendererTests
{
    // One captured entry whose pair no longer exists: fixed, or untested, depending on what the rule measured.
    private static readonly BaselineIndex GhostPairCaptured =
        Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.GhostController", "T:App.Data.Db"));

    [Fact]
    public void Render_PartlyInertRule_NamesTheDeadPatternAndCountsItsEntryAsUnmeasured()
    {
        // The rule's selection matched something, so matchedNothing would be false and stays absent; the
        // untestedOperands key is what tells a client that `stale` here is not progress.
        CheckReport report = Checker.Run(
            Sources.CleanWebBesideDataModel, GhostPairCaptured, Sources.PartlyInertDataAccess);

        using JsonDocument document = Render(report);
        JsonElement ratchet = Ratchet(document);
        JsonElement summary = document.RootElement.GetProperty("summary");

        ratchet.GetProperty("untestedOperands")
            .EnumerateArray()
            .Select(operand => operand.GetString())
            .ShouldBe(["types in `App.Ghost.*`"]);
        ratchet.ShouldSatisfyAllConditions(
            () => ratchet.GetProperty("stale")
                .GetInt32()
                .ShouldBe(1),
            () => ratchet.TryGetProperty("matchedNothing", out _)
                .ShouldBeFalse(),
            () => ratchet.GetProperty("promotable")
                .GetBoolean()
                .ShouldBeFalse());
        summary.ShouldSatisfyAllConditions(
            () => summary.GetProperty("unmeasured")
                .GetInt32()
                .ShouldBe(1),
            () => summary.GetProperty("fixedAwaitingAcceptance")
                .GetInt32()
                .ShouldBe(0));
    }

    [Fact]
    public void Render_RuleMeasuredInFull_CarriesNeitherDeadOperandKey()
    {
        // Absent, never an empty array, which is what keeps the keys additive under the same schema version.
        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, GhostPairCaptured, Sources.LiveDataAccess);

        using JsonDocument document = Render(report);
        JsonElement ratchet = Ratchet(document);

        ratchet.ShouldSatisfyAllConditions(
            () => ratchet.TryGetProperty("untestedOperands", out _)
                .ShouldBeFalse(),
            () => ratchet.TryGetProperty("stricterOperands", out _)
                .ShouldBeFalse());
        document.RootElement.GetProperty("summary")
            .GetProperty("fixedAwaitingAcceptance")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public void Render_StricterRule_NamesTheDeadAllowedPatternAndCountsItsEntryAsFixed()
    {
        // A dead allowed pattern only adds violations, so the unmatched entry is fixed debt: the pattern rides
        // its own key, the untested key stays absent, and the summary counts the entry as progress.
        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, GhostPairCaptured, Sources.StricterDataAccess);

        using JsonDocument document = Render(report);
        JsonElement ratchet = Ratchet(document);
        JsonElement summary = document.RootElement.GetProperty("summary");

        ratchet.GetProperty("stricterOperands")
            .EnumerateArray()
            .Select(operand => operand.GetString())
            .ShouldBe(["types in `App.Ghost.*`"]);
        ratchet.ShouldSatisfyAllConditions(
            () => ratchet.TryGetProperty("untestedOperands", out _)
                .ShouldBeFalse(),
            () => ratchet.GetProperty("stale")
                .GetInt32()
                .ShouldBe(1),
            () => ratchet.GetProperty("promotable")
                .GetBoolean()
                .ShouldBeFalse());
        summary.ShouldSatisfyAllConditions(
            () => summary.GetProperty("fixedAwaitingAcceptance")
                .GetInt32()
                .ShouldBe(1),
            () => summary.TryGetProperty("unmeasured", out JsonElement unmeasured)
                .ShouldBe(false, $"unmeasured is {unmeasured}"));
    }

    [Fact]
    public void Render_EmptyOrphanScan_CarriesNoOrphansKey()
    {
        CheckReport report = Checker.Run(Sources.CleanWebBesideDataModel, GhostPairCaptured, Sources.LiveDataAccess);

        using JsonDocument document = Render(report);

        document.RootElement.TryGetProperty("orphans", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Render_OrphanScan_ListsOrphansThenRefusedFilesUnderTheSameSchemaVersion()
    {
        // Through the checker, so the scan the index carries is the one the report hands the renderer.
        var scan = new BaselineOrphanScan(
            [new BaselineOrphan("data/old-x", "arch/baselines/data/old-x.json", 2)],
            [new UnreadableBaseline("arch/baselines/broken.json", "line 1, column 3: bad token.")]);
        CheckReport report = Checker.Run(
            Sources.CleanWebBesideDataModel, GhostPairCaptured.WithOrphanScan(scan), Sources.LiveDataAccess);

        using JsonDocument document = Render(report);

        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(2);
        JsonElement[] orphans = document.RootElement.GetProperty("orphans")
            .EnumerateArray()
            .ToArray();
        // Each item's keys in wire order, with nulls omitted: an orphan never says refused, a refused file never
        // says ruleId or entries.
        orphans.Select(orphan => string.Join(
                "; ",
                orphan.EnumerateObject()
                    .Select(property => $"{property.Name}={property.Value}")))
            .ShouldBe(
            [
                "ruleId=data/old-x; path=arch/baselines/data/old-x.json; entries=2",
                "path=arch/baselines/broken.json; refused=line 1, column 3: bad token."
            ]);
    }

    // The document as the verb writes it, over a clean load: nothing about the workspace is what these rows
    // are about, so the trust stamp is the empty one.
    private static JsonDocument Render(CheckReport report)
    {
        var output = new StringWriter();
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(WorkspaceDiagnostics.None, "/repo");

        StatusJsonRenderer.Render(output, report, "S.sln", "Spec.dll", [], trust);
        return JsonDocument.Parse(output.ToString());
    }

    // The one rule's ratchet block — every row here checks a single ratcheted rule.
    private static JsonElement Ratchet(JsonDocument document)
    {
        return document.RootElement.GetProperty("rules")
            .EnumerateArray()
            .Single()
            .GetProperty("ratchet");
    }
}
