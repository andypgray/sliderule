using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The wording of the five narrowing stamps, the adapter skip, the two refusals and the two per-rule
///     sentences, pinned pure — no workspace, no filter, no disk. The bytes are the whole point of the type: a
///     filtered run's green is only auditable if the sentence above it says which projects the verdict never
///     covered, so each verb's tail, the singular/plural subject, the writer's newline discipline and the
///     relative-path spelling are all spec here. <see cref="SlideRule.Tests.Cli.FilteredSolutionE2ETests" />
///     pins the same bytes coming out of a real <c>.slnf</c> load.
/// </summary>
public sealed class NarrowedUniverseNoticeTests
{
    // The evidence lines every stamp shares, so each test below reads as its tail and nothing else.
    private static readonly string[] TwoProjects = ["MyApp.Domain/MyApp.Domain.csproj", "MyApp.Web/MyApp.Web.csproj"];

    [Fact]
    public void CheckStamp_TwoUncheckedProjects_LedeThenIndentedEvidenceThenTheVerdictTail()
    {
        string stamp = NarrowedUniverseNotice.CheckStamp("BillingOnly.slnf", TwoProjects);

        stamp.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "The verdict below covers only the projects that loaded, so a clean result here is not a clean "
            + "solution.");
    }

    [Fact]
    public void StatusStamp_TwoUncheckedProjects_SaysTheCountsReadLow()
    {
        string stamp = NarrowedUniverseNotice.StatusStamp("BillingOnly.slnf", TwoProjects);

        stamp.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "The burndown below counts only the projects that loaded: an unchecked project contributes no "
            + "violations, so every count reads low.");
    }

    [Fact]
    public void GraphStamp_TwoUncheckedProjects_SaysAMissingProjectMayBeOutOfView()
    {
        string stamp = NarrowedUniverseNotice.GraphStamp("BillingOnly.slnf", TwoProjects);

        stamp.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "The survey below describes only the projects that loaded, so a project or reference edge absent "
            + "from it may simply be out of view.");
    }

    [Fact]
    public void ContextStamp_TwoUncheckedProjects_SaysNoCoverageCannotBeTrustedUnderThem()
    {
        // Context's whole failure mode is the pointer line reading as a clean "not dragon territory", and an
        // unchecked project's cards place nowhere just as an unloaded project's do.
        string stamp = NarrowedUniverseNotice.ContextStamp("BillingOnly.slnf", TwoProjects);

        stamp.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "The answer below covers only the projects that loaded: a scope card from an unchecked project "
            + "places nowhere, so \"no architecture scope covers\" cannot be trusted for paths under them.");
    }

    [Fact]
    public void ExplainStamp_TwoUncheckedProjects_SaysTheirTypesReadAsUnknown()
    {
        // A type declared only by an unchecked project is absent from the model, so explain --type would refuse
        // it as unknown and explain --path would answer that the path declares nothing.
        string stamp = NarrowedUniverseNotice.ExplainStamp("BillingOnly.slnf", TwoProjects);

        stamp.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "The answer below covers only the projects that loaded: a type declared only by an unchecked "
            + "project is not in the model, so it reads as unknown, and a path under them as declaring no type.");
    }

    [Fact]
    public void BaselineRefusal_TwoUncheckedProjects_WritesItsOwnLedeAndNamesTheFix()
    {
        // The refusals do not reuse the stamps' fixed lede: a stamp scopes an answer that still follows, and
        // what a refusal refuses is the point of its first sentence. The fix line is the whole opt-out —
        // there is no --allow flag here, because running the solution the filter references is the answer.
        string refusal = NarrowedUniverseNotice.BaselineRefusal("BillingOnly.slnf", TwoProjects);

        refusal.ShouldBe(
            "error: 'BillingOnly.slnf' narrowed this run — 2 projects the solution declares were not "
            + "checked, so no baseline was written: a baseline captured through a filter signs off debt in "
            + "projects it never measured, and baseline accept-reductions deletes real entries as \"no longer "
            + "occurring\" when the only thing that changed is that a project stopped being checked:\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "Run baseline against the solution the filter references rather than through the filter.");
    }

    [Fact]
    public void RenderRefusal_TwoUncheckedProjects_NamesTheCommittedFilesAndTheDiagram()
    {
        string refusal = NarrowedUniverseNotice.RenderRefusal("BillingOnly.slnf", TwoProjects);

        refusal.ShouldBe(
            "error: 'BillingOnly.slnf' narrowed this run — 2 projects the solution declares were not "
            + "checked, so nothing was rendered: rendered files are committed context, a card from an "
            + "unchecked project places nowhere and would be dropped from the committed files, and --diagram "
            + "would draw a survey missing whole projects:\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "Run render against the solution the filter references rather than through the filter.");
    }

    [Fact]
    public void AdapterSkip_TwoUncheckedProjects_KeepsTheVerdictsAndRefusesTheClaim()
    {
        // The one message that is neither a stamp nor a refusal: a test report has no stream to scope, so it
        // says what the verdicts are worth and what the name would have claimed.
        string skip = NarrowedUniverseNotice.AdapterSkip("BillingOnly.slnf", TwoProjects);

        skip.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked.\n"
            + "  MyApp.Domain/MyApp.Domain.csproj\n"
            + "  MyApp.Web/MyApp.Web.csproj\n"
            + "Rule verdicts come from the projects that loaded, but a test by this name cannot pass while "
            + "declared projects went unchecked; run the solution the filter references for the whole "
            + "answer.");
    }

    [Fact]
    public void RuleSkipReason_TwoUncheckedProjects_CountsThemWithoutListingThemAndDeniesACleanRead()
    {
        // The one message that goes out per rule rather than per run, which is why it carries no evidence
        // block: the projects ride the stamp above the report, and repeating them under every skipped rule
        // would bury the verdict. Its last clause is the whole job — a skip prints beside passes and gates
        // nothing, so it has to say outright that this is not the rule holding.
        string reason = NarrowedUniverseNotice.RuleSkipReason("BillingOnly.slnf", 2);

        reason.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked, and "
            + "this rule's subject matched no type in the projects that loaded — so it was not measured, and "
            + "this is not a clean result for it.");
        reason.ShouldNotContain("\n");
    }

    [Fact]
    public void RuleSkipReason_OneUncheckedProject_TakesTheSingularSubjectToo()
    {
        NarrowedUniverseNotice.RuleSkipReason("Leaf.slnf", 1)
            .ShouldStartWith("'Leaf.slnf' narrowed this run: 1 project the solution declares was not checked,");
    }

    [Fact]
    public void InertTargetHint_TwoUncheckedProjects_NamesTheFilterAndDefersTheDecisionToTheWholeSolution()
    {
        // The cure an inert target carries under a narrowing, in place of advice about how its target was
        // spelled, which may be right. It goes out per rule like the skip reason, so it counts the projects
        // without listing them, and it keeps the authoring hints' shape: one line, a semicolon parting
        // diagnosis from cure, a full stop. The keep-or-delete question the shape advice closes on survives,
        // moved behind the run that can answer it.
        string hint = NarrowedUniverseNotice.InertTargetHint("BillingOnly.slnf", 2);

        hint.ShouldBe(
            "'BillingOnly.slnf' narrowed this run: 2 projects the solution declares were not checked, so this "
            + "rule's target may match types outside the projects that loaded; check the rule against the "
            + "solution the filter references before deciding whether to keep it.");
        hint.ShouldNotContain("\n");
    }

    [Fact]
    public void InertTargetHint_OneUncheckedProject_TakesTheSingularSubjectToo()
    {
        NarrowedUniverseNotice.InertTargetHint("Leaf.slnf", 1)
            .ShouldStartWith("'Leaf.slnf' narrowed this run: 1 project the solution declares was not checked, so ");
    }

    [Fact]
    public void RenderRefusal_OneUncheckedProject_TakesTheSameSingularSubjectAsTheStamps()
    {
        // The refusals write their own lede but share the subject clause, which is why the count logic sits
        // in one place rather than once per message.
        string refusal = NarrowedUniverseNotice.RenderRefusal("Leaf.slnf", ["MyApp.Web/MyApp.Web.csproj"]);

        refusal.ShouldStartWith(
            "error: 'Leaf.slnf' narrowed this run — 1 project the solution declares was not checked, so "
            + "nothing was rendered:");
    }

    [Fact]
    public void CheckStamp_OneUncheckedProject_SwitchesTheSubjectToTheSingular()
    {
        // A stamp that read "1 projects ... were not checked" is the sentence a reader stops trusting, and
        // one unchecked project is the commonest narrowing there is.
        string stamp = NarrowedUniverseNotice.CheckStamp("Leaf.slnf", ["MyApp.Web/MyApp.Web.csproj"]);

        stamp.ShouldStartWith("'Leaf.slnf' narrowed this run: 1 project the solution declares was not checked.\n");
    }

    [Fact]
    public void WriteStamp_AStamp_SplitsOnLineFeedAndAdoptsTheWritersNewlineThenLeavesABlankLine()
    {
        // The stamps are assembled with LFs; a console writer's newline is CRLF. Writing the block whole
        // would put lone LFs into a CRLF stream, so it goes out one WriteLine per line, and the blank line
        // below it is what separates the stamp from the answer it scopes.
        var output = new StringWriter { NewLine = "\r\n" };

        LineBlocks.WriteStamp(output, "lede:\n  evidence\ntail");

        output.ToString()
            .ShouldBe("lede:\r\n  evidence\r\ntail\r\n\r\n");
    }

    [Fact]
    public void Relative_ProjectsInsideAndOutsideTheSolutionDirectory_AreForwardSlashedAndRelative()
    {
        // A machine path in a stamp is a path no golden can pin and no reader can copy, so every project is
        // shown from the solution directory — including one above it, which a solution declaring a project
        // outside its own directory produces (a shared library beside the solution rather than under it).
        string solutionDirectory = Path.Combine(Path.GetTempPath(), "narrowed-universe-tests", "solution");
        string[] projects =
        [
            Path.Combine(solutionDirectory, "MyApp.Web", "MyApp.Web.csproj"),
            Path.Combine(solutionDirectory, "..", "Shared", "Shared.csproj")
        ];

        IReadOnlyList<string> shown = NarrowedUniverseNotice.Relative(projects, solutionDirectory);

        shown.ShouldBe(["MyApp.Web/MyApp.Web.csproj", "../Shared/Shared.csproj"]);
    }
}
