using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Verbs;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The site measure through the three <c>baseline</c> modes that record it, over an in-memory fast-tier check —
///     no workspace, like <see cref="BaselineRunnerAddTests" /> beside it. What each mode may do to a count
///     is the whole subject: every write records what it observed, <c>accept-reductions</c> may only
///     tighten (record where there was nothing, lower where the observation came in under), and
///     <c>add</c> is the one route by which a count goes up — attributed, one entry at a time.
/// </summary>
/// <remarks>
///     <para>
///         Beside the counting, rows pin what else a write must not do, each because nothing else would
///         notice it moving: emit one identity twice (a write once emitted one line per violation sharing
///         it), upgrade a legacy file it reports unchanged, or create a file it put nothing into.
///     </para>
///     <para>
///         The runner is driven through <see cref="BaselineRunner.AddEntry" /> and
///         <see cref="BaselineRunner.ApplyFiles" />, the two seams that take a report rather than load a
///         workspace, so a row costs a compilation instead of an MSBuild solution load.
///     </para>
/// </remarks>
public sealed class BaselineRunnerCountTests : IDisposable
{
    private const string RuleId = "data-access/ledger-behind-repository";
    private const string BaselineFile = "ledger.json";
    private const string ControllerId = "T:App.Web.ReportController";
    private const string ExportControllerId = "T:App.Web.ExportController";
    private const string LedgerId = "T:App.Data.Ledger";

    // The refusal a rule with a dead target pattern earns in both modes: it names the pattern and the remedy,
    // and not the whole-selection sentence, which would be false of a rule that measured its other pattern.
    private const string PartlyInertRefusal =
        "data-access/ledger-behind-repository: cannot capture — the target operand \"types in `App.Ghost.*`\" "
        + "matched no types, so this run measured only part of the rule; skipped. Fix each dead operand or move "
        + "it to a rule of its own, then re-run.";

    // Two sites under the one identity: the ledger is named on the signature line and again on the line
    // that constructs it.
    private const string TwoSiteSource = """
                                         namespace App.Data { public class Ledger {} }
                                         namespace App.Web
                                         {
                                             public class ReportController
                                             {
                                                 public App.Data.Ledger Load()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }
                                         }
                                         """;

    // The same controller after a third mention lands in it — one more line naming the same pair, which is
    // growth inside an identity the baseline already blesses.
    private const string ThreeSiteSource = """
                                           namespace App.Data { public class Ledger {} }
                                           namespace App.Web
                                           {
                                               public class ReportController
                                               {
                                                   public App.Data.Ledger Load()
                                                   {
                                                       return new App.Data.Ledger();
                                                   }

                                                   public App.Data.Ledger Reload()
                                                   {
                                                       return Load();
                                                   }
                                               }
                                           }
                                           """;

    // Two controllers reaching the same ledger over two lines each — two identities under one rule, for the
    // row whose subject is how many entries a run moved rather than how many sites sit under one of them.
    private const string TwoPairSource = """
                                         namespace App.Data { public class Ledger {} }
                                         namespace App.Web
                                         {
                                             public class ReportController
                                             {
                                                 public App.Data.Ledger Load()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }

                                             public class ExportController
                                             {
                                                 public App.Data.Ledger Fetch()
                                                 {
                                                     return new App.Data.Ledger();
                                                 }
                                             }
                                         }
                                         """;

    // No web layer at all, so the rule's subject is empty: the one state a write cannot capture.
    private const string NoWebSource = """
                                       namespace App.Data { public class Ledger {} }
                                       """;

    // The other half of the same rot, and the dangerous one: the subject is intact and the forbidden TARGET
    // matched nothing, so the rule warns itself inert and produces no violations at all.
    private const string NoDataSource = """
                                        namespace App.Web { public class ReportController {} }
                                        """;

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-runner-count");

    private static BaselineEntry Pair => BaselineEntry.ForEdge(ControllerId, LedgerId);

    private static BaselineEntry ExportPair => BaselineEntry.ForEdge(ExportControllerId, LedgerId);

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void AddEntry_NewEntry_RecordsTheSitesObservedUnderItsIdentity()
    {
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId));

        var output = new StringWriter();
        int exit = Runner(output)
            .AddEntry(AddRequest("INC-1"), report, _temp.Path);

        exit.ShouldBe(0);
        // The valve grandfathers a pair AND says how much of it: both of the controller's mentions ride the
        // one entry, and the entry records that it is two rather than any.
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-1")));
    }

    [Fact]
    public void AddEntry_PresentUncountedEntry_RecordsTheCountAndNamesTheMoveFromUncounted()
    {
        // The entry an older write left behind: attributed, but with no measure on it at all.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithBecause("INC-1")));

        AddEntry("INC-2", report)
            .ShouldContain(
                "data-access/ledger-behind-repository: entry already baselined — attribution updated and site count re-recorded (uncounted → 2).");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-2")));
    }

    [Fact]
    public void AddEntry_PresentEntryThatGrew_ReRecordsTheCountAndNamesBothEndsOfTheMove()
    {
        // The valve's whole reason for touching the measure: the pair has gained a site, which the ratchet
        // reds, and add is the only verb that may raise the allowance to cover it.
        CheckReport report = Check(ThreeSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(
            RuleId,
            Pair.WithSiteCount(2)
                .WithBecause("INC-1")));

        AddEntry("INC-2", report)
            .ShouldContain(
                "data-access/ledger-behind-repository: entry already baselined — attribution updated and site count re-recorded (2 → 3).");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(3)
                    .WithBecause("INC-2")));
    }

    [Fact]
    public void AcceptReductions_UncountedEntry_RecordsTheCountAndUpgradesTheFile()
    {
        // The designed upgrade path off the legacy format: the entry is unchanged in identity and
        // attribution, gains the measure it never carried, and the envelope comes forward with it.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, Pair.WithBecause("INC-1")));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: recorded the site count on 1 entry.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                Pair.WithSiteCount(2)
                    .WithBecause("INC-1")));
    }

    [Fact]
    public void AcceptReductions_TwoUncountedEntries_InflectTheEntryNounForTheCount()
    {
        // The sentence counts entries, so the count has to reach the noun — one entry, two entries. Pinned
        // through the verb that renders it, because that is where a regression would be read.
        CheckReport report = Check(TwoPairSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, ExportPair, Pair));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: recorded the site count on 2 entries.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(
                RuleId,
                ExportPair.WithSiteCount(2),
                Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_EntryObservedUnderItsCount_LowersItToWhatTheRunSaw()
    {
        // A real reduction inside a pair that still exists: the identity is not stale, so only the measure
        // can carry the news that a site was fixed.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(3)));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: lowered the site count on 1 entry.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_EntryObservedOverItsCount_RefusesTheGrowthAndLeavesTheEntryAlone()
    {
        // The mode only ever tightens, so growth is refused here exactly as a new identity is: it accepted
        // nothing, and it says so beside the refusal rather than quietly widening the allowance.
        CheckReport report = Check(ThreeSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: nothing to accept.");
        echo.ShouldContain(
            "data-access/ledger-behind-repository: refused site growth on 1 entry — a grandfathered pair grows only via 'sliderule baseline add', one attributed entry at a time.");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
    }

    [Fact]
    public void AcceptReductions_EntryObservedWithNoSites_LeavesItAloneRatherThanLoweringToZero()
    {
        // An edge the run saw with no file:line evidence has nothing to measure, exactly as an entry written
        // before counts existed has: it is grandfathered at pair grain and the mode leaves it be. Read as a
        // reduction instead, this would record a count of zero — which an entry may not carry, so the mode
        // would throw on the very file it was pointed at.
        string content = BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2));
        string path = WriteBaseline(content);

        string echo = AcceptReductions([ObservedResult(observed: [0])]);

        echo.ShouldContain("data-access/ledger-behind-repository: nothing to accept.");
        Read(path)
            .ShouldBe(content);
    }

    [Fact]
    public void AcceptReductions_NothingMoved_KeepsTheNothingToAcceptVerdictAlone()
    {
        // The verdict every kind of acceptance has to clear, on a run where none of them fired: no entry
        // removed, no count lowered, none first recorded.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
        byte[] before = File.ReadAllBytes(path);

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: nothing to accept.");
        echo.ShouldNotContain("site count");
        echo.ShouldNotContain("refused");
        File.ReadAllBytes(path)
            .ShouldBe(before);
    }

    [Fact]
    public void Init_TwoViolationsSharingOneIdentity_WriteOneEntryCoveringTheLargerCount()
    {
        // A symbol ID names a name, not a node (GRAMMAR §4.3), so one pair can arrive as two violations —
        // here the same controller compiled into two projects, reaching a ledger of the same full name in
        // each. Synthetic nodes, because the shape is about identity rather than about any real codebase.
        RuleResult result = ObservedResult(observed: [2, 3]);

        string echo = Init([result]);

        // One line, not two — and the allowance covers the larger of the two, because anything less would
        // red the very state the capture was taken from.
        echo.ShouldContain("data-access/ledger-behind-repository: captured 1 grandfathered violation.");
        Read(_temp.PathOf(BaselineFile))
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(3)));
    }

    [Fact]
    public void Init_FullyCapturedLegacyFile_WritesNothingAndLeavesTheVersionWhereItWas()
    {
        // init reports "unchanged" here, so it must also BE unchanged: a write would compose the current
        // schema version and silently upgrade a file the operator was told nobody touched.
        CheckReport report = Check(TwoSiteSource);
        string path = WriteBaseline(BaselineComposer.ComposeLegacy(RuleId, Pair));
        byte[] before = File.ReadAllBytes(path);

        string echo = Init(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: already captured (1 entries) — unchanged.");
        echo.ShouldContain($"unchanged {BaselineFile}");
        File.ReadAllBytes(path)
            .ShouldBe(before);
        Read(path)
            .ShouldContain($"\"schemaVersion\": {BaselineFormat.LegacySchemaVersion},");
    }

    [Fact]
    public void AcceptReductions_UncapturedRule_WritesNoFile()
    {
        // The mode tells the operator to run init first. It must not create, in the same breath, the file
        // that sentence is about: nothing was tightened, so there is nothing to write, and a section-less
        // file would sit in the tree under a "wrote" line contradicting it.
        CheckReport report = Check(TwoSiteSource);

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: no baseline section — run 'sliderule baseline init' first.");
        echo.ShouldNotContain("wrote");
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Init_RuleWithNothingCapturable_WritesNoFile()
    {
        // An empty subject is a selection that matched nothing, so it is refused as rot rather than as an
        // unbaselinable identity; either way the file it would have gone into is not created around that
        // absence.
        CheckReport report = Check(NoWebSource);

        string echo = Init(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: cannot capture — the rule's selection matched nothing, so "
            + "this run measured no debt for it; skipped. Fix the rule's selection, then re-run.");
        echo.ShouldNotContain("wrote");
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Init_RuleThatErrored_SaysSoInItsOwnTerms()
    {
        // The only state that reaches this sentence: a rule that threw has no stable identity either, but
        // nothing about its selection was wrong, and telling the author to fix one would send them after the
        // wrong defect.
        var errored = new RuleResult(Model().Rule(RuleId), RuleStatus.Failed, [Violation.RuleError("boom")]);

        string echo = Init([errored]);

        echo.ShouldContain("data-access/ledger-behind-repository: cannot capture — the rule had an evaluation error; skipped.");
        echo.ShouldNotContain("wrote");
    }

    [Fact]
    public void AcceptReductions_RuleWhoseTargetRotted_RefusesRatherThanDeletingTheWholeSection()
    {
        // The harm that made the status wording matter. The target selection matched nothing, so the
        // rule warns itself inert and reports no violations — which is indistinguishable, to an intersection,
        // from every violation having been fixed. Before the guard this echoed "accepted 2 reductions" and
        // wrote an empty section over two live entries, on the command status itself told the reader to run.
        CheckReport report = Check(NoDataSource);
        string before = BaselineComposer.Compose(RuleId, ExportPair.WithSiteCount(2), Pair.WithSiteCount(2));
        string path = WriteBaseline(before);

        string echo = AcceptReductions(report.Results);

        echo.ShouldSatisfyAllConditions(
            () => echo.ShouldContain(
                "data-access/ledger-behind-repository: cannot capture — the rule's selection matched nothing, so "
                + "this run measured no debt for it; skipped. Fix the rule's selection, then re-run."),
            () => echo.ShouldNotContain("reduction"),
            () => echo.ShouldNotContain("wrote"));
        Read(path)
            .ShouldBe(before);
    }

    [Fact]
    public void Init_RuleWithADeadTargetPattern_RefusesNamingThePatternAndWritesNoFile()
    {
        // The live pattern found a real pair, which a capture would record, beside a pattern that matched
        // nothing at all: the section would sign off debt the rule never measured.
        CheckReport report = Check(TwoSiteSource, PartlyInertModel());

        string echo = Init(report.Results);

        echo.ShouldSatisfyAllConditions(
            () => echo.ShouldContain(PartlyInertRefusal),
            () => echo.ShouldNotContain("wrote"));
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void AcceptReductions_RuleWithADeadTargetPattern_RefusesAndLeavesTheFileByteIdentical()
    {
        // ExportController is gone, so its entry reads as a reduction — but an entry only the dead pattern could
        // have matched would read the same, and this run cannot tell the two apart. The rule is refused whole,
        // as one whose selection matched nothing is, and the section is left exactly as it was.
        CheckReport report = Check(TwoSiteSource, PartlyInertModel());
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, ExportPair.WithSiteCount(2), Pair.WithSiteCount(2)));
        byte[] before = File.ReadAllBytes(path);

        string echo = AcceptReductions(report.Results);

        echo.ShouldSatisfyAllConditions(
            () => echo.ShouldContain(PartlyInertRefusal),
            () => echo.ShouldNotContain("reduction"),
            () => echo.ShouldNotContain("wrote"));
        File.ReadAllBytes(path)
            .ShouldBe(before);
    }

    [Fact]
    public void Init_RuleWithADeadAllowedPattern_RefusesWithTheAllowListText()
    {
        // The pair reds because the one pattern meant to allow it matches nothing: a capture would record a red
        // the dead pattern caused as debt, so init refuses, naming the pattern and why.
        CheckReport report = Check(TwoSiteSource, StricterModel());

        string echo = Init(report.Results);

        echo.ShouldSatisfyAllConditions(
            () => echo.ShouldContain(
                "data-access/ledger-behind-repository: cannot capture — the allow-list operand \"types in "
                + "`App.Ghost.*`\" matched no types, so what it was meant to allow is reported as violations and "
                + "would be recorded as debt; skipped. Fix or remove each dead operand, then re-run."),
            () => echo.ShouldNotContain("wrote"));
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void AcceptReductions_RuleWithADeadAllowedPattern_RetiresTheFixedEntry()
    {
        // A dead allowed pattern only adds violations, so it cannot be why ExportController's entry went
        // unmatched: the controller is gone, the entry is fixed debt, and accept-reductions retires it.
        CheckReport report = Check(TwoSiteSource, StricterModel());
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, ExportPair.WithSiteCount(2), Pair.WithSiteCount(2)));

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain("data-access/ledger-behind-repository: accepted 1 reduction");
        Read(path)
            .ShouldBe(BaselineComposer.Compose(RuleId, Pair.WithSiteCount(2)));
    }

    [Fact]
    public void Init_RuleWithADeadMembership_RefusesWithTheMembershipText()
    {
        // The controller reds because the one home it may live in names nothing: a capture would record a red
        // the dead membership caused as debt.
        CheckReport report = Check(
            TwoSiteSource,
            ModelOf(arch => arch.Namespace("App.Web.*").MustBelongTo(arch.Namespace("App.Ghost.*"))));

        string echo = Init(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: cannot capture — the membership \"types in `App.Ghost.*`\" "
            + "matched no types, so the types it was meant to hold are reported as violations and would be "
            + "recorded as debt; skipped. Fix or remove each dead operand, then re-run.");
        Directory.EnumerateFiles(_temp.Path, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public void AcceptReductions_RuleWithADeadAmongPart_RefusesAndLeavesTheFileByteIdentical()
    {
        // A dead among part can pass a subject it would have made ambiguous, so an unmatched entry may be one
        // only it would have kept red: the rule is refused whole, as a dead target pattern's is.
        CheckReport report = Check(
            TwoSiteSource,
            ModelOf(arch => arch.Namespace("App.Web.*")
                .MustHaveExactlyOneCounterpart(among: arch.Namespace("App.Ghost.*"), named: "I{Name}")));
        string path = WriteBaseline(BaselineComposer.Compose(RuleId, BaselineEntry.ForSubject(ExportControllerId)));
        byte[] before = File.ReadAllBytes(path);

        string echo = AcceptReductions(report.Results);

        echo.ShouldContain(
            "data-access/ledger-behind-repository: cannot capture — the among operand \"types in `App.Ghost.*`\" "
            + "matched no types, so this run measured only part of the rule; skipped. Fix or remove each dead "
            + "operand, then re-run.");
        File.ReadAllBytes(path)
            .ShouldBe(before);
    }

    // One ratcheted reference rule over the two synthetic namespaces, checked against an empty baseline so
    // every current violation surfaces as the state a write would capture — the report shape every mode takes.
    private static CheckReport Check(string source)
    {
        return Check(source, Model());
    }

    private static CheckReport Check(string source, ArchitectureModel model)
    {
        return ArchChecker.Check(model, CompilationFactory.Extract(source), BaselineIndex.Empty);
    }

    private static ArchitectureModel Model()
    {
        return Checker.Model(arch => arch.Rule(RuleId)
            .Migrate(
                "the web layer reaches the ledger directly",
                arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Baseline(BaselineFile)
            .Because("b"));
    }

    // The same rule with a second target pattern that names nothing: App.Data.* still bites and App.Ghost.* has
    // gone dead, so the rule measures only part of what it names.
    private static ArchitectureModel PartlyInertModel()
    {
        return Checker.Model(arch => arch.Rule(RuleId)
            .Migrate(
                "the web layer reaches the ledger directly",
                arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*"), arch.Namespace("App.Ghost.*")))
            .Baseline(BaselineFile)
            .Because("b"));
    }

    // The same pair through an allow-list whose one pattern names nothing, so the web layer may reference
    // nothing and the ledger pair reds: the rule is stricter than written.
    private static ArchitectureModel StricterModel()
    {
        return Checker.Model(arch => arch.Rule(RuleId)
            .Migrate(
                "the web layer reaches the ledger directly",
                arch.Namespace("App.Web.*").MustOnlyReference(arch.Namespace("App.Ghost.*")))
            .Baseline(BaselineFile)
            .Because("b"));
    }

    // The rule under any constraint, for the rows that vary the verb the dead operand sits in.
    private static ArchitectureModel ModelOf(Func<Arch, Constraint> constraint)
    {
        return Checker.Model(arch => arch.Rule(RuleId)
            .Migrate("the web layer reaches the ledger directly", constraint(arch))
            .Baseline(BaselineFile)
            .Because("b"));
    }

    // Reference violations of the rule under one baseline identity, carrying the site counts named. Their
    // endpoints wear the same symbol IDs under different project attributions, so two of them are distinct
    // nodes folding to one entry — and one of them is an observation real source cannot make, which is the
    // only way to put a sightless edge in front of a mode that would otherwise never see one.
    private static RuleResult ObservedResult(int[] observed)
    {
        List<Violation> violations = observed
            .Select((sites, index) => Violation.Reference(
                SyntheticNodes.Type("App.Web.ReportController", ControllerId, $"Web{index}"),
                SyntheticNodes.Type("App.Data.Ledger", LedgerId, $"Data{index}"),
                SyntheticNodes.Sites("ReportController.cs", sites)))
            .ToList();

        return new RuleResult(Model().Rule(RuleId), RuleStatus.Failed, violations);
    }

    private BaselineRunner Runner(TextWriter output)
    {
        return new BaselineRunner(output, TextWriter.Null);
    }

    private BaselineRequest AddRequest(string because)
    {
        return BaselineRequests.Add(RuleId, because, "App.Web.ReportController", "App.Data.Ledger", _temp.Path);
    }

    // init and accept-reductions through the one seam they share, handing back what the runner said.
    private string Init(IEnumerable<RuleResult> results)
    {
        return Apply(BaselineRequests.Init(_temp.Path), results);
    }

    private string AcceptReductions(IEnumerable<RuleResult> results)
    {
        return Apply(BaselineRequests.AcceptReductions(_temp.Path), results);
    }

    private string Apply(BaselineRequest request, IEnumerable<RuleResult> results)
    {
        var output = new StringWriter();
        Runner(output)
            .ApplyFiles(request, results, _temp.Path);
        return output.ToString();
    }

    // add through its own seam, handing back what the runner said — the exit code is dropped here as it
    // is in Apply; the one row that claims it keeps its own writer.
    private string AddEntry(string because, CheckReport report)
    {
        var output = new StringWriter();
        Runner(output)
            .AddEntry(AddRequest(because), report, _temp.Path);
        return output.ToString();
    }

    private string WriteBaseline(string content)
    {
        return _temp.WriteFile([BaselineFile], content);
    }

    private static string Read(string path)
    {
        return File.ReadAllText(path)
            .NormalizedLines();
    }
}
