using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Cli.Diff;
using SlideRule.Cli.Verbs;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <see cref="BaselineHistory" /> over real repositories whose commits carry chosen dates and authors. An entry
///     keeps the date of the commit that added it through every rewrite that leaves its rule and identity alone: a
///     lower site count, a rename to another file, a move to another path, a reformat. It is dated anew when it
///     leaves and comes back, when it reaches the branch through a merge, and when another live section gains it.
///     Without the history an entry has no date and the read says why.
/// </summary>
/// <remarks>
///     Baseline files are composed by the format's writer, and the rename row runs <see cref="BaselineRunner.Rename" />,
///     so each row's files are the ones the verbs write.
/// </remarks>
public sealed class BaselineHistoryTests : IDisposable
{
    private const string Rule = "data-access/no-inline-sql";
    private const string OtherRule = "data-access/no-raw-ado";
    private const string ConventionalPath = "arch/baselines/data-access/no-inline-sql.json";

    private static readonly BaselineEntry Invoice = BaselineEntry
        .ForEdge("T:App.Web.InvoiceController", "T:App.Data.SqlText")
        .WithSiteCount(3);

    private static readonly BaselineEntry Report = BaselineEntry.ForEdge("T:App.Web.ReportController", "T:App.Data.SqlText");

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-history");

    public BaselineHistoryTests()
    {
        GitCommand.Run(_temp.Path, "init", "-b", "main");
    }

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public async Task ReadAsync_FirstCapture_DatesTheEntryByItsCommit()
    {
        // Arrange — a JSON file that is not a baseline comes first, and the author's name is not ASCII.
        Write("config/settings.json", "{ \"rules\": [] }\n");
        Commit(1, "Ada");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice.WithBecause("INC-1234 — façade")));
        Commit(2, "Zoë");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(2), "Zoë");
    }

    [Fact]
    public async Task ReadAsync_LowerSiteCount_KeepsTheDate()
    {
        // Arrange — the reduction re-seals the line, as accept-reductions writes it.
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice));
        Commit(1, "Ada");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice.WithSiteCount(2)));
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_RenamedIntoAnotherFile_KeepsTheDate()
    {
        // Arrange — the old ID's conventional file holds the section; rename writes the new ID's and deletes it.
        const string from = "data-access/inline-sql";
        string source = Write("arch/baselines/data-access/inline-sql.json", BaselineComposer.Compose(from, Invoice, Report));
        Commit(1, "Ada");
        ArchitectureModel model = Checker.Model(arch => arch.Rule(Rule)
            .Migrate("inline SQL in controllers", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b"));
        BaselineRunner.Rename(TextWriter.Null, BaselineRequests.Rename(from, Rule, _temp.Path), model, _temp.Path);
        File.Exists(source).ShouldBeFalse("the rename moves the section out of the old ID's file");
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
        ages.ShouldDate(Rule, Report, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_FixedAndAddedAgain_DatesFromTheReAdd()
    {
        // Arrange
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice, Report));
        Commit(1, "Ada");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Report));
        Commit(2, "Bob");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice, Report));
        Commit(3, "Cy");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(3), "Cy");
        ages.ShouldDate(Rule, Report, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_OneIdentityUnderTwoRulesOfAFile_DatesEachRuleItsOwn()
    {
        // Arrange
        Write("arch/shared.json", BaselineComposer.Compose((Rule, [Invoice])));
        Commit(1, "Ada");
        Write("arch/shared.json", BaselineComposer.Compose((Rule, [Invoice]), (OtherRule, [Invoice])));
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
        ages.ShouldDate(OtherRule, Invoice, Day(2), "Bob");
    }

    [Fact]
    public async Task ReadAsync_IdentityMovedToALiveSection_DatesFromTheMove()
    {
        // Arrange — the first rule keeps a section, so nothing was renamed and the moved entry is new to its rule.
        Write("arch/shared.json", BaselineComposer.Compose((Rule, [Invoice, Report]), (OtherRule, [Report])));
        Commit(1, "Ada");
        Write("arch/shared.json", BaselineComposer.Compose((Rule, [Report]), (OtherRule, [Invoice, Report])));
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(OtherRule, Invoice, Day(2), "Bob");
    }

    [Fact]
    public async Task ReadAsync_IdentityMovedOutOfAnEmptiedSection_DatesFromTheMove()
    {
        // Arrange — the first rule's section stays in the file with nothing in it, so nothing was renamed.
        Write("arch/shared.json", BaselineComposer.Compose((Rule, [Invoice]), (OtherRule, [Report])));
        Commit(1, "Ada");
        Write("arch/shared.json", BaselineComposer.Compose((Rule, []), (OtherRule, [Invoice, Report])));
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(OtherRule, Invoice, Day(2), "Bob");
    }

    [Fact]
    public async Task ReadAsync_DeclaredFilesTheGlobMisses_AreDatedAtTheirPaths()
    {
        // Arrange — the solution sits below the repository root; one declared file is above it and one has another
        // suffix, and a third is declared outside the repository altogether.
        string solution = _temp.PathOf("app");
        string above = Write("arch/shared.json", BaselineComposer.Compose(Rule, Invoice));
        string suffixed = Write("app/arch/debt.baseline", BaselineComposer.Compose(OtherRule, Report));
        Commit(1, "Ada");
        using TempDirectory outside = TestTempRoot.Fresh("baseline-history-outside");
        string elsewhere = outside.PathOf("arch", "elsewhere.json");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(solution, [above, suffixed, elsewhere], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
        ages.ShouldDate(OtherRule, Report, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_VersionsThatFailTheirIntegrityChecks_StillDateTheirEntries()
    {
        // Arrange — a legacy file whose digest no longer matches, then a current one whose seal no longer matches,
        // then the entry as a write leaves it. Each damaged version is one the ordinary read refuses.
        string legacy = Regex.Replace(
            BaselineComposer.ComposeLegacy((Rule, [Report])), "\"digest\": \"[0-9a-f]{64}\"",
            $"\"digest\": \"{new string('0', 64)}\"");
        string tampered = BaselineComposer.Compose(Rule, Report.WithSiteCount(2))
            .Replace("\"siteCount\": 2", "\"siteCount\": 5", StringComparison.Ordinal);
        BaselineDocumentReader.Read(legacy)
            .Refusal.ShouldNotBeNull("the legacy version's digest is stale");
        BaselineDocumentReader.Read(tampered)
            .Refusal.ShouldNotBeNull("the current version's seal is stale");
        Write(ConventionalPath, legacy);
        Commit(1, "Ada");
        Write(ConventionalPath, tampered);
        Commit(2, "Bob");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Report.WithSiteCount(2)));
        Commit(3, "Cy");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Report, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_EntryResolvedInAMerge_DatesItByTheMerge()
    {
        // Arrange — a side branch adds the entry, and HEAD's first-parent line first holds it at the merge.
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Report));
        Commit(1, "Ada");
        GitCommand.Run(_temp.Path, "checkout", "-b", "side");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice, Report));
        Commit(2, "Bob");
        GitCommand.Run(_temp.Path, "checkout", "main");
        Write("config/settings.json", "{}\n");
        Commit(3, "Cy");
        GitCommand.RunAs(_temp.Path, "Dee", Day(4), "merge", "--no-ff", "side", "-m", "merge side");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(4), "Dee");
        ages.ShouldDate(Rule, Report, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_EntryReformattedAcrossLines_KeepsTheDate()
    {
        // Arrange
        string composed = BaselineComposer.Compose(Rule, Invoice);
        Write(ConventionalPath, composed);
        Commit(1, "Ada");
        string reformatted = composed.Replace("{ \"source\"", "{\n          \"source\"", StringComparison.Ordinal);
        reformatted.ShouldNotBe(composed, "the reformat splits the entry's line");
        BaselineDocumentReader.Read(reformatted)
            .Document.ShouldNotBeNull("the reformatted file is still a baseline");
        Write(ConventionalPath, reformatted);
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_FileMovedToTheConventionalPath_KeepsTheDate()
    {
        // Arrange — the spec drops a custom .Baseline(path), and the file moves with it.
        // The old path sorts ahead of the new one, so the commit lists the deletion first.
        string custom = Write("arch/archive.json", BaselineComposer.Compose(Rule, Invoice));
        Commit(1, "Ada");
        File.Delete(custom);
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice));
        Commit(2, "Bob");

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
    }

    [Fact]
    public async Task ReadAsync_EntryInTheWorkingTreeOnly_IsNotCommitted()
    {
        // Arrange
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice));
        Commit(1, "Ada");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice, Report));

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(_temp.Path, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(1), "Ada");
        ages.ShouldNotDate(Rule, Report, AgeUnknownReason.NotCommitted);
    }

    [Fact]
    public async Task ReadAsync_ShallowClone_LeavesTheOldestFetchedCommitsEntriesUndated()
    {
        // Arrange — the clone fetches the last two commits, so the entry the first one added is in the boundary's tree.
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Report));
        Commit(1, "Ada");
        Write("config/settings.json", "{}\n");
        Commit(2, "Bob");
        Write(ConventionalPath, BaselineComposer.Compose(Rule, Invoice, Report));
        Commit(3, "Cy");
        string clone = _temp.PathOf("clone");
        GitCommand.Run(_temp.Path, "clone", "--depth", "2", new Uri(_temp.Path).AbsoluteUri, clone);

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(clone, [], Ct);

        // Assert
        ages.ShouldDate(Rule, Invoice, Day(3), "Cy");
        ages.ShouldNotDate(Rule, Report, AgeUnknownReason.ShallowClone);
    }

    [Fact]
    public async Task ReadAsync_DirectoryOutsideAnyRepository_HasNoHistory()
    {
        // Arrange
        using TempDirectory outside = TestTempRoot.Fresh("baseline-history-no-git");
        outside.WriteFile(ConventionalPath.Split('/'), BaselineComposer.Compose(Rule, Invoice));

        // Act
        BaselineAges ages = await BaselineHistory.ReadAsync(outside.Path, [], Ct);

        // Assert
        ages.ShouldNotDate(Rule, Invoice, AgeUnknownReason.NoHistory);
    }

    private static DateTimeOffset Day(int day)
    {
        return new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.FromHours(1)).AddDays(day);
    }

    private string Write(string relativePath, string text)
    {
        return _temp.WriteFile(relativePath.Split('/'), text);
    }

    private void Commit(int day, string author)
    {
        GitCommand.Run(_temp.Path, "add", "-A");
        GitCommand.RunAs(_temp.Path, author, Day(day), "commit", "-m", $"day {day}");
    }
}
