using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The baseline orphan scan (<see cref="BaselineOrphanScanner" />) over composed files in a temp directory and
///     a hand-built model. It reads every file under <c>arch/baselines/</c> and every file a ratcheted rule names,
///     and lists each section filed under an ID the spec does not declare. A section under a declared ID is never
///     an orphan wherever it sits, a file the reader refuses is listed and passed over, and files and sections
///     come out in ordinal order.
/// </summary>
public sealed class BaselineOrphanScannerTests : IDisposable
{
    private const string Declared = "data-access/no-inline-sql";

    private static readonly BaselineEntry Edge = BaselineEntry.ForEdge("T:App.Web.OrderController", "T:App.Data.SqlText");
    private static readonly BaselineEntry OtherEdge = BaselineEntry.ForEdge("T:App.Web.ReportController", "T:App.Data.SqlText");

    private static readonly ArchitectureModel ConventionalModel = Checker.Model(arch => arch.Rule(Declared)
        .Migrate("inline SQL", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
        .Because("b"));

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-orphans");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Scan_ConventionalFileUnderAnUndeclaredId_IsAnOrphanWithItsEntryCount()
    {
        Write("arch/baselines/data-access/inline-sql.json", BaselineComposer.Compose("data-access/inline-sql", Edge, OtherEdge));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);

        Describe(scan).ShouldBe(["data-access/inline-sql in arch/baselines/data-access/inline-sql.json (2)"]);
        scan.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void Scan_DeclaredSharedFile_ListsOnlyTheSectionNoRuleNames()
    {
        // arch/shared.json sits outside arch/baselines, and is read because the rule names it.
        ArchitectureModel model = SharedModel("arch/shared.json");
        Write("arch/shared.json", BaselineComposer.Compose((Declared, [Edge]), ("legacy/old", [OtherEdge])));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(model, _temp.Path);

        Describe(scan).ShouldBe(["legacy/old in arch/shared.json (1)"]);
    }

    [Fact]
    public void Scan_DeclaredFileUnderArchBaselines_IsReadOnceSoEachOrphanIsListedOnce()
    {
        // Reached twice, as a file under arch/baselines/ and as the file a rule names.
        ArchitectureModel model = SharedModel("arch/baselines/shared.json");
        Write("arch/baselines/shared.json", BaselineComposer.Compose((Declared, [Edge]), ("legacy/old", [OtherEdge])));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(model, _temp.Path);

        Describe(scan).ShouldBe(["legacy/old in arch/baselines/shared.json (1)"]);
    }

    [Fact]
    public void Scan_AFileThatCannotBeOpened_IsUnreadableWithAReasonNamingNoPath()
    {
        // The exception's own message names the machine's absolute path, which status must not print.
        string path = _temp.WriteFile(["arch", "baselines", "locked.json"], BaselineComposer.Compose("legacy/old", Edge));

        BaselineOrphanScan scan;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            scan = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);
        }

        Describe(scan).ShouldBeEmpty();
        scan.Unreadable.Select(file => $"{file.Path}: {file.Reason}")
            .ShouldBe(["arch/baselines/locked.json: the file could not be opened."]);
    }

    [Fact]
    public void Scan_DeclaredIdAtAPathItsRuleDoesNotName_IsNotAnOrphan()
    {
        // The definition is "names no declared rule", not "sits where no rule looks".
        Write("arch/baselines/misc.json", BaselineComposer.Compose(Declared, Edge));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);

        Describe(scan).ShouldBeEmpty();
        scan.Unreadable.ShouldBeEmpty();
    }

    [Fact]
    public void Scan_MalformedAndTamperedFiles_AreListedAsUnreadableAndTheScanCarriesOn()
    {
        Write("arch/baselines/a-broken.json", "{ not json");
        string tampered = BaselineComposer.Compose("legacy/tampered", Edge)
            .Replace("\"T:App.Web.OrderController\"", "\"T:App.Web.InvoiceController\"", StringComparison.Ordinal);
        Write("arch/baselines/b-tampered.json", tampered);
        Write("arch/baselines/c-orphan.json", BaselineComposer.Compose("legacy/old", Edge));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);

        Describe(scan).ShouldBe(["legacy/old in arch/baselines/c-orphan.json (1)"]);
        scan.Unreadable.Select(file => file.Path)
            .ShouldBe(["arch/baselines/a-broken.json", "arch/baselines/b-tampered.json"]);
        // The syntax refusal carries its position; the seal refusal is a sentence about the entry alone.
        scan.Unreadable[0].Reason.ShouldStartWith("line 1, column ");
        scan.Unreadable[1].Reason.ShouldNotStartWith("line ");
    }

    [Fact]
    public void Scan_FileOutsideArchBaselines_IsReadOnlyWhenARuleNamesIt()
    {
        Write("arch/other.json", BaselineComposer.Compose("legacy/old", Edge));

        BaselineOrphanScan undeclared = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);
        BaselineOrphanScan declared = BaselineOrphanScanner.Scan(SharedModel("arch/other.json"), _temp.Path);

        Describe(undeclared).ShouldBeEmpty();
        undeclared.Unreadable.ShouldBeEmpty();
        Describe(declared).ShouldBe(["legacy/old in arch/other.json (1)"]);
    }

    [Fact]
    public void Scan_NoBaselinesDirectoryAtAll_IsTheEmptyScan()
    {
        BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path)
            .ShouldBeSameAs(BaselineOrphanScan.Empty);
    }

    [Fact]
    public void Scan_OrdersFilesByPathAndEachFilesSectionsByRuleId()
    {
        Write("arch/baselines/b.json", BaselineComposer.Compose(("z/old", [Edge]), ("a/old", [Edge, OtherEdge])));
        Write("arch/baselines/a/x.json", BaselineComposer.Compose("m/old", Edge));

        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(ConventionalModel, _temp.Path);

        Describe(scan)
            .ShouldBe(
            [
                "m/old in arch/baselines/a/x.json (1)",
                "a/old in arch/baselines/b.json (2)",
                "z/old in arch/baselines/b.json (1)"
            ]);
    }

    private static ArchitectureModel SharedModel(string baselinePath)
    {
        return Checker.Model(arch => arch.Rule(Declared)
            .Migrate("inline SQL", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Baseline(baselinePath)
            .Because("b"));
    }

    private static IEnumerable<string> Describe(BaselineOrphanScan scan)
    {
        return scan.Orphans.Select(orphan => $"{orphan.RuleId} in {orphan.Path} ({orphan.Entries})");
    }

    private void Write(string relativePath, string text)
    {
        _temp.WriteFile(relativePath.Split('/'), text);
    }
}
