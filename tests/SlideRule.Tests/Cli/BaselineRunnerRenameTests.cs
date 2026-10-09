using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Cli.Verbs;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The <c>baseline rename</c> runner path (<see cref="BaselineRunner.Rename" />) over a hand-built model and
///     composed baseline files — no workspace, since the mode reads only the spec and the baseline files.
/// </summary>
/// <remarks>
///     The section moves verbatim, site counts and attribution included, and the write re-seals every entry under
///     the new ID, so the moved file equals the canonical composition of the same entries under that ID. The old
///     section is taken from whichever baseline file the orphan scan lists it in. Every refusal lands before any
///     write.
/// </remarks>
public sealed class BaselineRunnerRenameTests : IDisposable
{
    private const string From = "data-access/inline-sql";
    private const string To = "data-access/no-inline-sql";

    private static readonly BaselineEntry Counted = BaselineEntry.ForEdge("T:App.Web.OrderController", "T:App.Data.SqlText")
        .WithSiteCount(2)
        .WithBecause("INC-1234");

    private static readonly BaselineEntry Bare = BaselineEntry.ForEdge("T:App.Web.ReportController", "T:App.Data.SqlText");

    private readonly TempDirectory _temp = TestTempRoot.Fresh("baseline-rename");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Rename_ConventionalSection_MovesEveryEntryVerbatimAndDeletesTheEmptiedFile()
    {
        // Arrange — the old ID's conventional file holds its one section, and the new rule has none.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string source = WriteBaseline("arch/baselines/data-access/inline-sql.json", BaselineComposer.Compose(From, Counted, Bare));
        string destination = _temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json");

        // Act
        (int exit, string output) = Run(model);

        // Assert — both entries under the new ID, re-sealed; the emptied source is gone.
        exit.ShouldBe(0);
        output.ShouldBe(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 2 grandfathered entries.\n"
            + "wrote arch/baselines/data-access/no-inline-sql.json\n"
            + "deleted arch/baselines/data-access/inline-sql.json\n");
        ReadText(destination).ShouldBe(BaselineComposer.Compose(To, Counted, Bare));
        File.Exists(source).ShouldBeFalse();
    }

    [Fact]
    public void Rename_SharedFileInPlace_RenamesTheSectionAndKeepsTheOthers()
    {
        // Arrange — the new rule names a shared file, which holds the old section beside an unrelated one.
        ArchitectureModel model = Checker.Model(arch => SharedRatchet(arch, "arch/shared.json"));
        string shared = WriteBaseline(
            "arch/shared.json", BaselineComposer.Compose((From, [Counted]), ("other/rule", [Bare])));

        // Act
        (int exit, string output) = Run(model);

        // Assert — one file, renamed in place, with the unrelated section kept.
        exit.ShouldBe(0);
        output.ShouldBe(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 1 grandfathered entry.\n"
            + "wrote arch/shared.json\n");
        ReadText(shared).ShouldBe(BaselineComposer.Compose((To, [Counted]), ("other/rule", [Bare])));
    }

    [Fact]
    public void Rename_SourceFileWithOtherSections_RewritesTheSourceAndKeepsIt()
    {
        // Arrange — the old ID's conventional file also holds a section some other rule wrote there.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string source = WriteBaseline(
            "arch/baselines/data-access/inline-sql.json",
            BaselineComposer.Compose((From, [Counted]), ("legacy/kept", [Bare])));
        string destination = _temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json");

        // Act
        (int exit, string output) = Run(model);

        // Assert — the moved section in the new file, and the source rewritten with only what it kept.
        exit.ShouldBe(0);
        output.ShouldBe(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 1 grandfathered entry.\n"
            + "wrote arch/baselines/data-access/no-inline-sql.json\n"
            + "wrote arch/baselines/data-access/inline-sql.json\n");
        ReadText(destination).ShouldBe(BaselineComposer.Compose(To, Counted));
        ReadText(source).ShouldBe(BaselineComposer.Compose("legacy/kept", Bare));
    }

    [Fact]
    public void Rename_OrphanInAThirdConventionalFile_MovesItAndDeletesTheEmptiedFile()
    {
        // Arrange — the old rule named a file of its own, so its section is neither in the new rule's file nor on
        // the old ID's conventional path; the orphan scan still lists it.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string source = WriteBaseline("arch/baselines/legacy.json", BaselineComposer.Compose(From, Counted, Bare));
        string destination = _temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json");

        // Act
        (int exit, string output) = Run(model);

        // Assert — moved from where the scan found it, and that emptied file deleted.
        exit.ShouldBe(0);
        output.ShouldBe(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 2 grandfathered entries.\n"
            + "wrote arch/baselines/data-access/no-inline-sql.json\n"
            + "deleted arch/baselines/legacy.json\n");
        ReadText(destination).ShouldBe(BaselineComposer.Compose(To, Counted, Bare));
        File.Exists(source).ShouldBeFalse();
    }

    [Fact]
    public void Rename_OrphanInAnotherRulesFile_MovesItAndKeepsThatRulesSection()
    {
        // Arrange — the old rule shared a file with a rule that is still declared and still names it.
        ArchitectureModel model = Checker.Model(arch =>
        {
            ConventionalRatchet(arch);
            arch.Rule("other/rule")
                .Migrate("legacy references", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Legacy.*")))
                .Baseline("arch/shared.json")
                .Because("b");
        });
        string shared = WriteBaseline(
            "arch/shared.json", BaselineComposer.Compose((From, [Counted]), ("other/rule", [Bare])));
        string destination = _temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json");

        // Act
        (int exit, string output) = Run(model);

        // Assert — the section moved, and the shared file rewritten with only the other rule's section.
        exit.ShouldBe(0);
        output.ShouldBe(
            "data-access/no-inline-sql: renamed from data-access/inline-sql, moving 1 grandfathered entry.\n"
            + "wrote arch/baselines/data-access/no-inline-sql.json\n"
            + "wrote arch/shared.json\n");
        ReadText(destination).ShouldBe(BaselineComposer.Compose(To, Counted));
        ReadText(shared).ShouldBe(BaselineComposer.Compose("other/rule", Bare));
    }

    [Fact]
    public void Rename_OrphanInSeveralFiles_RefusesNamingEveryFileAndWritesNothing()
    {
        // Arrange — the old section filed twice; moving either would leave the other orphaned for good.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string conventionalText = BaselineComposer.Compose(From, Counted);
        string legacyText = BaselineComposer.Compose(From, Bare);
        string conventional = WriteBaseline("arch/baselines/data-access/inline-sql.json", conventionalText);
        string legacy = WriteBaseline("arch/baselines/legacy.json", legacyText);

        // Act / Assert — every file named, in the scan's order, and nothing touched.
        ShouldRefuse(model).ShouldBe(
            "baseline sections for 'data-access/inline-sql' are filed in arch/baselines/data-access/inline-sql.json "
            + "and arch/baselines/legacy.json; nothing was renamed. Delete every copy but the one to keep, then run "
            + "the rename again.");
        ReadText(conventional).ShouldBe(conventionalText);
        ReadText(legacy).ShouldBe(legacyText);
        File.Exists(_temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBeFalse();
    }

    [Fact]
    public void Rename_DestinationAlreadyCaptured_RefusesAndWritesNothing()
    {
        // Arrange — a capture under the new ID already exists, beside the old section.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string sourceText = BaselineComposer.Compose(From, Counted);
        string destinationText = BaselineComposer.Compose(To, Bare);
        string source = WriteBaseline("arch/baselines/data-access/inline-sql.json", sourceText);
        string destination = WriteBaseline("arch/baselines/data-access/no-inline-sql.json", destinationText);

        // Act / Assert
        ShouldRefuse(model).ShouldBe(
            "rule 'data-access/no-inline-sql' already has a baseline section in "
            + "arch/baselines/data-access/no-inline-sql.json; nothing was renamed.");
        ReadText(source).ShouldBe(sourceText);
        ReadText(destination).ShouldBe(destinationText);
    }

    [Fact]
    public void Rename_NoSourceSection_RefusesNamingInit()
    {
        // Two files looked in, then one: the new rule names the old ID's conventional file as its own.
        string twoPaths = ShouldRefuse(Checker.Model(ConventionalRatchet));
        string onePath = ShouldRefuse(Checker.Model(arch => SharedRatchet(arch, "arch/baselines/data-access/inline-sql.json")));

        twoPaths.ShouldBe(
            "nothing to rename: no baseline section for 'data-access/inline-sql' in "
            + "arch/baselines/data-access/no-inline-sql.json or arch/baselines/data-access/inline-sql.json; "
            + "if the rule is new, run 'sliderule baseline init'.");
        onePath.ShouldBe(
            "nothing to rename: no baseline section for 'data-access/inline-sql' in "
            + "arch/baselines/data-access/inline-sql.json; if the rule is new, run 'sliderule baseline init'.");
        Directory.Exists(_temp.PathOf("arch")).ShouldBeFalse();
    }

    [Fact]
    public void Rename_SourceFileFailsItsSeal_RefusesWithTheIntegrityRecovery()
    {
        // Arrange — one entry's line edited by hand, so its seal no longer matches.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string tampered = BaselineComposer.Compose(From, Counted, Bare)
            .Replace("\"T:App.Web.ReportController\"", "\"T:App.Web.InvoiceController\"", StringComparison.Ordinal);
        string source = WriteBaseline("arch/baselines/data-access/inline-sql.json", tampered);

        // Act / Assert — the refusal carries the recovery for a per-entry seal.
        string refusal = ShouldRefuse(model);
        refusal.ShouldContain("failed its integrity check");
        refusal.ShouldContain("If that entry was edited by hand, restore its line from version control.");
        ReadText(source).ShouldBe(tampered);
        File.Exists(_temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBeFalse();
    }

    [Fact]
    public void Rename_DestinationFileFailsItsSeal_RefusesWithTheIntegrityRecovery()
    {
        // The same refusal on the destination: a shared file the new rule names, broken before the old section is
        // even looked for.
        ArchitectureModel model = Checker.Model(arch => SharedRatchet(arch, "arch/shared.json"));
        string tampered = BaselineComposer.Compose((From, [Counted]), ("other/rule", [Bare]))
            .Replace("\"T:App.Web.ReportController\"", "\"T:App.Web.InvoiceController\"", StringComparison.Ordinal);
        string shared = WriteBaseline("arch/shared.json", tampered);

        string refusal = ShouldRefuse(model);

        refusal.ShouldContain("failed its integrity check");
        refusal.ShouldContain("If that entry was edited by hand, restore its line from version control.");
        ReadText(shared).ShouldBe(tampered);
    }

    [Fact]
    public void Rename_SourceFileMalformed_RefusesAsNotValid()
    {
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        WriteBaseline("arch/baselines/data-access/inline-sql.json", "{ not json");

        ShouldRefuse(model).ShouldContain("is not valid");
        File.Exists(_temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBeFalse();
    }

    [Fact]
    public void Rename_ToNotInSpec_Refuses()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule("data-access/other")
            .Migrate("inline SQL", WebMustNotReferenceData(arch))
            .Because("b"));

        ShouldRefuse(model).ShouldBe("rule 'data-access/no-inline-sql' is not in the spec.");
    }

    [Fact]
    public void Rename_ToNotRatcheted_Refuses()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule(To)
            .Enforce(WebMustNotReferenceData(arch))
            .Because("b"));

        ShouldRefuse(model).ShouldBe(
            "rule 'data-access/no-inline-sql' is not ratcheted — only Migrate and Quarantine containment rules carry baselines.");
    }

    [Fact]
    public void Rename_FromStillDeclared_Refuses()
    {
        ArchitectureModel model = Checker.Model(arch =>
        {
            ConventionalRatchet(arch);
            arch.Rule(From)
                .Migrate("inline SQL, the old rule", WebMustNotReferenceData(arch))
                .Because("b");
        });
        WriteBaseline("arch/baselines/data-access/inline-sql.json", BaselineComposer.Compose(From, Counted));

        ShouldRefuse(model).ShouldBe(
            "rule 'data-access/inline-sql' is still in the spec, so its baseline section is not orphaned; "
            + "rename the rule in the spec first.");
    }

    [Fact]
    public void Rename_MalformedFromReachingTheFileWorkDirectly_RefusesAndDeletesNothing()
    {
        // The old ID becomes a conventional path the rename may delete, so the file work refuses an ID outside the
        // grammar itself rather than trusting the command's earlier check: `../escape` would resolve to
        // arch/escape.json, outside arch/baselines/.
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        string outside = WriteBaseline("arch/escape.json", BaselineComposer.Compose("../escape", Counted));

        string refusal = ShouldRefuse(model, from: "../escape");

        refusal.ShouldBe(
            "'../escape' is not a well-formed rule ID; expected area/rule-name matching ^[a-z0-9-]+(/[a-z0-9-]+)*$.");
        File.Exists(outside).ShouldBeTrue();
        File.Exists(_temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")).ShouldBeFalse();
    }

    [Fact]
    public void Rename_NeverWritesAFileTheModelDoesNotName()
    {
        ArchitectureModel model = Checker.Model(ConventionalRatchet);
        WriteBaseline("arch/baselines/data-access/inline-sql.json", BaselineComposer.Compose(From, Counted));

        Run(model).Exit.ShouldBe(0);

        string[] files = Directory.GetFiles(_temp.Path, "*", SearchOption.AllDirectories);
        files.ShouldBe([_temp.PathOf("arch", "baselines", "data-access", "no-inline-sql.json")]);
    }

    private static void ConventionalRatchet(Arch arch)
    {
        arch.Rule(To)
            .Migrate("inline SQL in controllers", WebMustNotReferenceData(arch))
            .Because("b");
    }

    private static void SharedRatchet(Arch arch, string baselinePath)
    {
        arch.Rule(To)
            .Migrate("inline SQL in controllers", WebMustNotReferenceData(arch))
            .Baseline(baselinePath)
            .Because("b");
    }

    private static Constraint WebMustNotReferenceData(Arch arch)
    {
        return arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*"));
    }

    private (int Exit, string Output) Run(ArchitectureModel model)
    {
        var output = new StringWriter();
        var runner = new BaselineRunner(output, TextWriter.Null);
        int exit = runner.Rename(BaselineRequests.Rename(From, To, _temp.Path), model, _temp.Path);
        return (exit, output.ToString().NormalizedLines());
    }

    private string ShouldRefuse(ArchitectureModel model, string from = From)
    {
        var runner = new BaselineRunner(TextWriter.Null, TextWriter.Null);
        BaselineRequest request = BaselineRequests.Rename(from, To, _temp.Path);
        return Should.Throw<UserErrorException>(() => runner.Rename(request, model, _temp.Path)).Message;
    }

    private string WriteBaseline(string relativePath, string text)
    {
        return _temp.WriteFile(relativePath.Split('/'), text);
    }

    private static string ReadText(string path)
    {
        return File.ReadAllText(path)
            .NormalizedLines();
    }
}
