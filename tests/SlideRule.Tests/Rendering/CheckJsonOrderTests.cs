using System.Text.Json;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;
// The suite's reader over a check document, not the CLI renderer of the same name that this file also imports.
using CheckJson = SlideRule.Tests.TestSupport.CheckJson;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The check document's key order (<see cref="JsonReportRenderer.Document" />, and so
///     <c>arch_check</c>'s too): the roll-up and every trust stamp serialize ahead of <c>rules</c> — the
///     bulk a truncating reader's cut lands in — and the stamps serialize ahead of the <c>summary</c> they
///     invalidate. The goldens cannot pin this: a stamp is omitted from every clean run, so six of the seven
///     keys here appear in no golden at all. The one document under test carries all of them at once —
///     including the per-rule subject-coverage pair, which the same reasoning applies to one level down.
/// </summary>
public sealed class CheckJsonOrderTests
{
    // One controller opening the data layer directly, so the rule fails and the roll-up counts are real,
    // plus an attributed type inside the subject namespace so the rule's subject-coverage pair is populated
    // too — this document's job is to carry every optional slot at once, and those two are optional.
    private const string OneController = """
                                         namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                         namespace App.Web { [System.CodeDom.Compiler.GeneratedCode("Test", "1.0")] public class Emitted {} }
                                         namespace App.Data { public class Db {} }
                                         """;

    // A NuGet audit advisory the web project's load raised, so the stamped document carries packageAudit as
    // well as the stream.
    private static readonly string Advisory =
        CapturedAuditLines.Framed("App.Web/App.Web.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

    private static readonly CheckReport WebOpensData = Checker.Run(OneController, arch =>
        arch.Rule("layer/no-data")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("The web layer must not open the data layer directly."));

    // A run whose workspace loaded incompletely, whose packages did not restore, that a solution filter
    // narrowed, whose solution declares a project in another language and a project that compiles twice,
    // whose load raised a NuGet audit advisory, and that a --rules glob narrowed further: every optional slot
    // populated at once, which no real run needs to be for the order to matter and which no golden can be.
    // Must stay declared below WebOpensData: static field initializers run in declaration order, and Render
    // reads it.
    private static readonly string StampedDocument = Render(DocumentGrain.Full);

    // The two authoring signals together: a rule whose subject matched nothing, and a rule inert against a
    // pattern target. Neither can ride WebOpensData, whose rule fails on a real edge and is cured by
    // changing the code — so a `hint` appears on no other document here, and this is what can pin it.
    private static readonly CheckReport TwoAuthoringSignals = Checker.Run(OneController, arch =>
    {
        arch.Rule("empty/subject")
            .Enforce(arch.Namespace("App.Nowhere.*").MustHaveSuffix("X"))
            .Because("A subject that matches nothing fails loudly.");

        arch.Rule("inert/target")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Ghost.*")))
            .Because("Nothing should reference the ghost layer.");
    });

    [Theory]
    [InlineData("summary")]
    [InlineData("modelIncomplete")]
    [InlineData("failedProjects")]
    [InlineData("uncheckedProjects")]
    [InlineData("restoreFailedProjects")]
    [InlineData("unsupportedProjects")]
    [InlineData("multiTargetedProjects")]
    public void Document_VerdictAndTrustStamps_SerializeAheadOfRules(string key)
    {
        // Below the grain ladder's coarsest rung a reader with a response budget cuts at the last newline
        // that fits, and that cut lands inside `rules` — so everything under it is what a red-heavy report
        // loses first, which is the report whose verdict matters most.
        StampedDocument.ShouldSerializeBefore(key, "rules");
    }

    [Theory]
    [InlineData("modelIncomplete")]
    [InlineData("failedProjects")]
    [InlineData("uncheckedProjects")]
    [InlineData("restoreFailedProjects")]
    [InlineData("unsupportedProjects")]
    [InlineData("multiTargetedProjects")]
    public void Document_TrustStamps_SerializeAheadOfTheSummaryTheyInvalidate(string key)
    {
        StampedDocument.ShouldSerializeBefore(key, "summary");
    }

    [Theory]
    [InlineData("subjectTypes")]
    [InlineData("subjectGeneratedTypes")]
    public void Document_SubjectCoverage_SerializesAheadOfTheRulesViolations(string key)
    {
        // Inside a rule rather than at document level: the pair qualifies THIS rule's subject, so it sits
        // with the rule's own framing — id, verdict, prose, baseline — above the violation list that scales
        // with the codebase and is the first thing a truncating reader loses. Read off the rule's own object,
        // because `violations` also names a roll-up count in the summary above it.
        CheckJson.Rule(StampedDocument, "layer/no-data")
            .ShouldSerializeBefore(key, "violations");
    }

    [Fact]
    public void Document_SubjectCoverage_KeepsTheDenominatorAheadOfTheNumerator()
    {
        // "2 types, 1 generated" reads in that order for the same reason it is written in that order: the
        // count comes before the qualifier that narrows it.
        CheckJson.Rule(StampedDocument, "layer/no-data")
            .ShouldSerializeBefore("subjectTypes", "subjectGeneratedTypes");
    }

    [Fact]
    public void Document_ViolationCure_SerializesRightBelowTheDiagnosisItCures()
    {
        // Diagnosis then cure, on the wire exactly as on two consecutive lines of the report — and ahead of
        // the evidence group, whose counts and sites are about the code rather than about the rule.
        JsonElement violation = CheckJson.Violations(Hinted(DocumentGrain.Full), "empty/subject")
            .ShouldHaveSingleItem();

        violation.ShouldSerializeBefore("detail", "hint");
        violation.ShouldSerializeBefore("hint", "siteCount");
    }

    [Fact]
    public void Document_WarningCure_SerializesBelowTheMessage()
    {
        string document = Hinted(DocumentGrain.Full);

        ShouldHaveWarningOfKind(document, "inert/target", "inertTarget")
            .ShouldSerializeBefore("message", "hint");
    }

    [Fact]
    public void Document_PartlyInertWarning_CarriesAKindOfItsOwnAndItsCureBelowTheMessage()
    {
        // A dead pattern beside a live one is its own kind on the wire, so a client that reads `inertTarget` as
        // a rule that measured nothing keeps reading it that way. The rule here fails on its live pattern, and
        // the warning rides beside the violation all the same.
        CheckReport report = Checker.Run(OneController, arch =>
            arch.Rule("inert/operand")
                .Enforce(arch.Namespace("App.Web.*")
                    .MustNotReference(arch.Namespace("App.Data.*"), arch.Namespace("App.Ghost.*")))
                .Because("The web layer must not open the data layer or the ghost layer."));

        string document = report.JsonReport();

        ShouldHaveWarningOfKind(document, "inert/operand", "deadOperand")
            .ShouldSerializeBefore("message", "hint");
    }

    [Fact]
    public void Document_OrphanedBaselineWarning_CarriesItsKindAndItsHintBelowTheMessage()
    {
        // An uncaptured ratcheted rule failing beside a section no rule names: the kind goes out in the enum's
        // camelCase spelling, as every other kind does.
        var scan = new BaselineOrphanScan([new BaselineOrphan("layer/old-no-data", "arch/baselines/layer/old-no-data.json", 1)], []);
        CheckReport report = Checker.Run(OneController, BaselineIndex.Empty.WithOrphanScan(scan), arch =>
            arch.Rule("layer/no-data")
                .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly."));

        string document = report.JsonReport();

        ShouldHaveWarningOfKind(document, "layer/no-data", "orphanedBaseline")
            .ShouldSerializeBefore("message", "hint");
    }

    [Fact]
    public void Document_ARuleWithNothingToCure_CarriesNoHintKeyAtAll()
    {
        // Absent, never null: a spec raising neither authoring signal writes no hint key, which is what
        // keeps the key additive under the same schemaVersion.
        StampedDocument.ShouldNotContain("\"hint\"");
    }

    [Fact]
    public void Document_IndexGrain_DropsTheWarningCureWithTheRestOfTheProse()
    {
        // Warnings are the one thing no grain elides, so an un-elided cure would be the only prose left
        // standing on the floor rung — beside a rule stripped of its own sentence, reason and fix.
        string index = Hinted(DocumentGrain.Index);

        index.ShouldSatisfyAllConditions(
            () => index.ShouldContain("\"inertTarget\""),
            () => index.ShouldNotContain("\"hint\""),
            () => index.ShouldNotContain("\"because\""));
    }

    [Fact]
    public void Document_SkeletonGrain_KeepsTheWarningCureAndLosesTheViolationsWithTheirs()
    {
        // The two cures ride different carriers and so leave at different rungs: the violation's goes with
        // the array it sits in, and the warning's survives to the rung below it.
        // Read from `rules` onward: the roll-up summary above them names a `violations` count of its own,
        // which the rung keeps and which has nothing to do with the array.
        string skeleton = Hinted(DocumentGrain.Skeleton);

        skeleton.ShouldSatisfyAllConditions(
            () => ShouldHaveRulesBlock(skeleton)
                .ShouldNotContain("\"violations\":"),
            () => ShouldHaveRulesBlock(skeleton)
                .ShouldContain("\"violationCount\":"),
            () => skeleton.ShouldContain("passes forever, so decide before keeping it"));
    }

    [Fact]
    public void Document_WorkspaceDiagnostics_StaysBelowTheRules()
    {
        // MSBuild's own words are evidence rather than verdict, they have no ceiling, and the actionable half
        // of them is already hoisted into failedProjects and restoreFailedProjects — so this is the one slot
        // deliberately left where a cut can reach it.
        StampedDocument.ShouldSerializeBefore("rules", "workspaceDiagnostics");
    }

    [Fact]
    public void Document_IndexGrain_ElidesTheDiagnosticStreamToItsCountAndKeepsEveryTrustStamp()
    {
        // The slot's other consequence: "no ceiling" is not a figure of speech. Its lines are one per project
        // per framework per complaint, so a ladder that elided everything else could still not fit a channel
        // while they rode it. It is the last thing the floor rung drops, and it can be dropped because its
        // actionable half is keyed above it.
        string index = Render(DocumentGrain.Index);

        index.ShouldElideDiagnosticStreamTo(1);
        foreach (string stamp in
                 new[]
                 {
                     "modelIncomplete", "failedProjects", "uncheckedProjects", "restoreFailedProjects",
                     "unsupportedProjects", "multiTargetedProjects", "summary"
                 })
            index.ShouldContain($"\"{stamp}\"");
    }

    [Fact]
    public void Document_EveryGrainAboveTheFloor_KeepsTheDiagnosticStreamWhole()
    {
        // The elision is the floor rung's alone. A reader who can hold a skeleton gets MSBuild's words in
        // full, because at that grain they are affordable and they are the only place the load's own account
        // of itself survives.
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
        {
            string document = Render(grain);

            document.ShouldKeepDiagnosticStreamWhole();
        }
    }

    [Fact]
    public void Document_IndexGrainWithACleanLoad_CarriesNeitherTheStreamNorABareZero()
    {
        // A count of nothing is a key that says nothing. The elision reports what it dropped, so with
        // nothing to drop it reports nothing — the rule every other <key>Count on these documents takes.
        string index = WebOpensData.JsonReport(DocumentGrain.Index);

        index.ShouldNotContain("\"workspaceDiagnosticCount\"");
    }

    [Fact]
    public void Document_PackageAudit_SerializesBetweenTheRulesAndTheDiagnosticStream()
    {
        // The advisories are words about the environment rather than the verdict, so they sit below the rules
        // with the stream — and above the stream, whose bulk has no ceiling and so belongs where a cut lands.
        StampedDocument.ShouldSerializeBefore("rules", "packageAudit");
        StampedDocument.ShouldSerializeBefore("packageAudit", "workspaceDiagnostics");
    }

    [Fact]
    public void Document_IndexGrain_ElidesThePackageAuditToItsCount()
    {
        // On a field bed whose package-audit feed was unreachable the audit's lines were 96% of this document
        // at every grain (measured). Grouped per advisory they do not scale with the projects, and the floor
        // still drops the entries to a count, beside the stream's.
        Render(DocumentGrain.Index)
            .ShouldElidePackageAuditTo(1);
    }

    [Fact]
    public void Document_EveryGrainAboveTheFloor_KeepsThePackageAuditWhole()
    {
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
            Render(grain)
                .ShouldCarryPackageAudit(CapturedAuditLines.BuildTasksGitAdvisory, ["App.Web"]);
    }

    [Fact]
    public void Document_NoAdvisories_CarriesNeitherKeyAtAnyGrain()
    {
        // Absent rather than empty, at every rung, so a report on a load that raised no advisory has
        // neither key, which is what keeps the goldens stable.
        foreach (DocumentGrain grain in Enum.GetValues<DocumentGrain>())
            WebOpensData.JsonReport(grain)
                .ShouldCarryNoPackageAudit();
    }

    // The two-signal document at one grain: a clean load, so nothing but the signals themselves varies.
    private static string Hinted(DocumentGrain grain)
    {
        return TwoAuthoringSignals.JsonReport(grain);
    }

    // The stamped document at one grain; StampedDocument is this at Full.
    private static string Render(DocumentGrain grain)
    {
        string solutionDirectory = Directory.GetCurrentDirectory();
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [Advisory],
            FailedProjects = ["App.Web/App.Web.csproj"],
            UncheckedProjects = ["App.Reports/App.Reports.csproj"],
            RestoreFailedProjects = ["App.Data/App.Data.csproj"],
            UnsupportedProjects =
            [
                new UnsupportedProject("App.Signals/App.Signals.fsproj", UnsupportedProjectKind.NotCsharp)
            ],
            MultiTargetedProjects = [new MultiTargetedProject("App.Shared", ["net10.0", "netstandard2.0"], "net10.0")]
        };

        return JsonReportRenderer.Document(
            report: WebOpensData,
            solutionDirectory: solutionDirectory,
            solutionName: "S.sln",
            specAssembly: "Spec.dll",
            diffBase: null,
            workspaceDiagnostics: ["App.Web/App.Web.csproj : error MSB4019: imported project was not found"],
            trust: WorkspaceTrustStamp.From(diagnostics, solutionDirectory),
            rulesFilter: ["layer/*"],
            grain: grain);
    }

    // The document from `rules` onward.
    private static string ShouldHaveRulesBlock(string document)
    {
        int at = document.ShouldHaveKeyAt("rules");
        return document.Substring(at);
    }

    // The one warning of a rule with the given kind.
    private static JsonElement ShouldHaveWarningOfKind(string document, string ruleId, string kind)
    {
        return CheckJson.Rule(document, ruleId)
            .GetProperty("warnings")
            .EnumerateArray()
            .Where(warning => warning.GetProperty("kind")
                .GetString() == kind)
            .ShouldHaveSingleItem(document);
    }
}
