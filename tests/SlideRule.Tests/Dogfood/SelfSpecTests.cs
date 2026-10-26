using System.Text.Json;
using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Architecture;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Packs.DotNet;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Files;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Cli;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Dogfood;

/// <summary>The dogfood gates: SlideRule checked against itself, and its own generated files.</summary>
/// <remarks>One class, so the workspace-heavy runs serialize. Each gate states its own contract below.</remarks>
public sealed class SelfSpecTests
{
    /// <summary>
    ///     The spec name the provenance line carries — the spec assembly's file name without extension,
    ///     which is what <c>render</c> derives from the resolved DLL path.
    /// </summary>
    private const string SpecName = "SlideRule.Architecture";

    private const string ClaimsFenceType = "SlideRule.Cli.CommandEntryPoint";

    /// <summary>
    ///     Where this solution's own render can place a context file. <c>examples/</c> is deliberately
    ///     absent: those <c>AGENTS.md</c>s belong to the example solutions' own specs and are gated by the
    ///     examples job's own zero-diff.
    /// </summary>
    private static readonly string[] ContextFileRoots = ["src", "arch", "tests"];

    /// <summary>
    ///     The projects the committed diagram draws: every project this solution declares outside the
    ///     tests and their fixtures — the shipping packages, the rule pack, and the spec project. An
    ///     allow-list rather than a deny-list, because the fixture projects outnumber these and an
    ///     allow-list keeps the committed artifact stable while they churn. Derived from the solution
    ///     file rather than listed here, so a new one is drawn the day it is declared.
    /// </summary>
    private static readonly string[] ShippingProjects = SolutionInventory.DrawnProjects
        .Select(static project => project.Name)
        .ToArray();

    /// <summary>
    ///     This repository's own codebase, extracted once for every gate here and beyond that reads it: by
    ///     the verbs' own source, over the warm pool (so the workspace load is shared with the rest of the
    ///     suite), with this repository's spec resolved against it. One walk yields both models below.
    /// </summary>
    /// <remarks>
    ///     Only the two models are kept. The source, the snapshot it served and the spec model loaded from the
    ///     spec assembly all go out of scope with the extraction, so no static holds any of them alive.
    /// </remarks>
    private static readonly Lazy<Task<(CodebaseModel Survey, CodebaseModel Governed)>> SelfCodebase = new(async () =>
    {
        SpecfulCodebase extracted = await SpecfulCodebase.ExtractAsync(
            RepoRoot.Solution, RepoRoot.ArchitectureSpecCsproj, RepoRoot.Directory, CancellationToken.None);

        return (extracted.Survey, extracted.Governed);
    });

    /// <summary>
    ///     This repository's own codebase with nothing excluded: the model <c>graph</c> surveys and
    ///     <c>render --diagram</c> draws, extracted the way both of them extract it.
    /// </summary>
    /// <remarks>
    ///     The model is immutable and already shared across the run by <see cref="FixtureCodebase" />, so
    ///     several gates reading one instance is the established shape rather than a new risk — internal so
    ///     that a gate elsewhere that needs this repository as a real codebase shares it too rather than
    ///     paying the Roslyn walk a second time. The two gates that need a <em>different</em> model —
    ///     <see cref="ScopedCards_AreCurrent" /> and <see cref="ModelFile_IsCurrent" />, which must exclude
    ///     what <c>render</c> excludes — share <see cref="RenderCodebase" /> instead.
    /// </remarks>
    internal static readonly Lazy<Task<CodebaseModel>> WholeCodebase = new(async () => (await SelfCodebase.Value).Survey);

    /// <summary>
    ///     This repository's own codebase with what <c>render</c> excludes left out — the spec project and the
    ///     plumbing only it references — for the two gates that compare what <c>render</c> would write with
    ///     what is committed. The same walk as <see cref="WholeCodebase" />'s, merged over the other exclusion.
    /// </summary>
    private static readonly Lazy<Task<CodebaseModel>> RenderCodebase = new(async () => (await SelfCodebase.Value).Governed);

    /// <summary>
    ///     This repository's own spec, built once for the gates that render or read it. The
    ///     model is immutable and each of them only reads it, so one instance answers them all.
    /// </summary>
    internal static readonly ArchitectureModel SelfModel = ArchModelBuilder.Build(new SlideRuleArchitectureSpec());

    /// <summary>
    ///     The CI-equivalent self-spec gate, and — on the same run — the gate on the advisory channel
    ///     beside it. <c>workspaceDiagnostics</c> is what every MCP consumer of this repo's own check
    ///     reads, the arch hook included, so it is asserted <em>empty</em>: a channel that is never empty
    ///     teaches its readers to skim, and the next real diagnostic would arrive inside noise nobody
    ///     looks at. It is kept empty by removing causes rather than filtering reports: the spec fixtures
    ///     reference the MyApp projects they govern, and the anchors that only need to <em>name</em> a
    ///     MyApp type name it by fully-qualified string instead of declaring a stub that impersonates it.
    ///     Pinned as emptiness rather than a count, because the failure worth catching is a line nobody
    ///     meant to add.
    /// </summary>
    [Fact]
    public async Task SelfSpec_Check_ExitsZero()
    {
        // Loads the whole solution through MSBuildWorkspace (several seconds — an accepted cost). --json so
        // the same run answers both halves; the report goes to stdout, the human warnings still to stderr.
        CliResult result = await CliRunner.InvokeAsync(
            "check", RepoRoot.Solution, "--spec", RepoRoot.ArchitectureSpecCsproj, "--json");

        // The assertion surfaces the CLI's own output on failure — otherwise a red self-check shows only
        // "2 != 0" with no clue why.
        result.ShouldSucceed();

        using JsonDocument report = result.ShouldHaveJsonStdout();
        report.RootElement.Strings("workspaceDiagnostics")
            .ShouldBeEmpty(
                "this repo's own check must carry no advisory note at all — a duplicate declaration here is " +
                "a layout mistake, not background noise, and this channel is the product's own front door.");
        // A NuGet advisory published against a package this repository restores reports under packageAudit
        // rather than in the stream above, so the emptiness there does not see one. This keeps the alarm.
        report.ShouldCarryNoPackageAudit();
    }

    [Fact]
    public void AgentsMd_IsCurrent()
    {
        string composed = AgentContextRenderer.RootBlock(SelfModel, SpecName);
        string committed = File.ReadAllText(RepoRoot.AgentsMd);

        committed.ShouldCarryOneManagedBlock("AGENTS.md must carry exactly one managed block.")
            .ShouldBe(composed);
    }

    [Fact]
    public async Task ArchitectureMd_IsCurrent()
    {
        // The shared no-exclusions model: the diagram and the survey are two renderings of one codebase and
        // must never disagree about what is in it.
        CodebaseModel codebase = await WholeCodebase.Value;

        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        string composed = DiagramComposer.Compose(
            summary, Path.GetFileName(RepoRoot.Solution), SelfModel, SpecName, new DiagramScope(ShippingProjects, []));
        string committed = File.ReadAllText(RepoRoot.ArchitectureMd);

        committed.ShouldCarryOneManagedBlock("ARCHITECTURE.md must carry exactly one managed block.")
            .ShouldBe(composed);
    }

    /// <summary>
    ///     The one hand-written count on the self-check page that needs the codebase to answer it: how
    ///     many project lines the survey prints for this solution. The rest of that table is held from
    ///     committed bytes alone by <see cref="HandCountSyncTests" />, and a reader looking for all of it
    ///     needs both halves. This row sits here because the number is the survey's own roster, which only
    ///     an extraction produces — and the roster is wider than what the solution declares, because the
    ///     survey prints a line for every project a reference dragged into the build as well.
    /// </summary>
    [Fact]
    public async Task SelfCheckProjectLineCount_MatchesTheSurvey()
    {
        // The shared no-exclusions model, extracted as `graph` extracts it: a count taken from any other
        // model would be describing a survey the page does not quote.
        CodebaseModel codebase = await WholeCodebase.Value;
        GraphSummary summary = GraphSummarizer.Summarize(codebase);

        IReadOnlyList<string> drift = HandCounts.Drift(
            DocPaths.SelfCheck,
            "It prints {count} project lines",
            "the project lines the survey prints",
            summary.Projects.Count,
            CountStyle.WordToTen);

        drift.ShouldReportNothing("The self-check page states a project-line count the survey contradicts");
    }

    /// <summary>
    ///     The survey fence on the self-check page, re-run: the lines <c>graph</c> prints for the projects the
    ///     committed diagram draws, in the order it prints them. A type added to one of those projects, a
    ///     reference or a package moved, a project drawn or dropped — each changes what the fence must say, and
    ///     the red names the project before ending on the whole fence to paste.
    /// </summary>
    [Fact]
    public async Task SelfCheckGraphFence_IsWhatTheSurveyPrintsForTheDrawnProjects()
    {
        // Arrange: the survey as `graph` prints it, over the model it extracts. The roster follows its
        // heading, one line for each project in the summary, in the summary's order.
        CodebaseModel codebase = await WholeCodebase.Value;
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        List<string> survey = GraphFormatter.Lines(summary, Path.GetFileName(RepoRoot.Solution), DocumentGrain.Full, [])
            .ToList();
        int roster = survey.IndexOf($"Projects ({summary.Projects.Count}):") + 1;

        List<(string Name, string Line)> printed = summary.Projects
            .Select((project, index) => (project.Name, Line: survey[roster + index]))
            .Where(entry => ShippingProjects.Contains(entry.Name, StringComparer.Ordinal))
            .ToList();

        QuotedFence fence = CapturedFences.GraphSurvey.ShouldFindFence();
        List<(string Name, string Line)> quoted = fence.Lines
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => (RosterProject(line), line))
            .ToList();

        // Act
        List<string> drift = GraphFenceDrift(printed, quoted);
        if (drift.Count > 0)
            drift.Add(
                $"Replace the body of the fence at {DocPaths.SelfCheck}:{fence.Line} with:\n"
                + string.Join("\n", printed.Select(static entry => entry.Line)));

        // Assert
        drift.ShouldReportNothing("The self-check page's survey fence is not what `graph` prints for the drawn projects");
    }

    /// <summary>
    ///     The self-check page's <c>explain --type</c> excerpt is a run of this repository's spec over this
    ///     repository: every line it quotes is a line of the answer for <c>CommandEntryPoint</c>, in order.
    /// </summary>
    [Fact]
    public async Task ClaimsFence_IsAnExcerptOfWhatExplainPrintsForTheShell()
    {
        // Arrange: the answer as explain --type composes it, over the model the shared self-spec gates read.
        CodebaseModel codebase = await WholeCodebase.Value;
        TypeNode type = codebase.Types.Single(candidate => candidate.FullName == ClaimsFenceType && !candidate.IsExternal);
        TypeClaims claims = TypeClaims.Of(SelfModel, new ClaimTracer(codebase), type);

        // Act
        string answer = string.Join("\n", ClaimsFormatter.Lines(claims, RepoRoot.Directory));

        // Assert
        CapturedFences.SelfCheckClaims.ShouldBeAnInOrderExcerptOf(answer, $"The answer for {ClaimsFenceType}:\n{answer}");
    }

    /// <summary>
    ///     The membership label's <see langword="false" /> arm, against the only real passenger set on hand:
    ///     this repository. Its <c>.slnx</c> declares the shipping projects; the three MyApp fixture projects
    ///     are dragged into the workspace by the fixture spec projects' own <c>ProjectReference</c>s and are
    ///     not declared anywhere. No fixture reproduces that — a fixture solution that declared its own
    ///     passengers would not have any — so this is where the arm is proven.
    /// </summary>
    /// <remarks>
    ///     Also the gate on the <c>render --diagram</c> default: the survey fence draws declared members, so
    ///     a regression that labelled these three <see langword="true" /> would put them in the committed
    ///     drawing. Asserted as an exact roster rather than a count, because the failure worth catching is a
    ///     project changing sides, which a count cannot see.
    /// </remarks>
    [Fact]
    public async Task WholeCodebase_LabelsFixturePassengersAndShippingProjects()
    {
        CodebaseModel codebase = await WholeCodebase.Value;

        codebase.Projects
            .Where(project => project.SolutionMember == false)
            .Select(project => project.Name)
            .ShouldBe(["MyApp.Domain", "MyApp.Legacy.Billing", "MyApp.Web"]);

        // The true arm on the same model, so a membership read that silently returned nothing (which would
        // label everything null and pass the assertion above) cannot hide here.
        codebase.Projects
            .Where(project => ShippingProjects.Contains(project.Name, StringComparer.Ordinal))
            .Select(project => project.SolutionMember)
            .ShouldAllBe(member => member == true);
    }

    /// <summary>
    ///     The shape guard behind <see cref="ArchitectureMd_IsCurrent" />, and the cheaper half: no
    ///     workspace, just the committed file. The block is supposed to carry two drawings of the same
    ///     system — the codebase survey drawn from what exists, and the architecture law drawn from the
    ///     spec — and the composer is the only thing that puts them side by side. Rewire the render or the
    ///     drift gate back to <see cref="GraphDiagramRenderer" /> alone and the committed artifact loses a
    ///     fence, which the equality gate would catch only after someone re-rendered. This one names the
    ///     missing half directly.
    /// </summary>
    [Fact]
    public void ArchitectureMd_CarriesBothFences()
    {
        string? body = ManagedBlock.ExtractBody(File.ReadAllText(RepoRoot.ArchitectureMd));
        body.ShouldNotBeNull("ARCHITECTURE.md carries no managed block.");

        body.Occurrences("```mermaid")
            .ShouldBe(2, "the managed block must carry both drawings.");
        body.ShouldContain("accTitle: Codebase survey:");
        body.ShouldContain("accTitle: Architecture law:");
    }

    /// <summary>
    ///     The card class, gated as a class. Composes every context file a <c>render</c> of this repo would
    ///     write — through the very composer the command uses, so the gate cannot drift from it — and
    ///     asserts two things: each composed body is byte-equal to what is committed, and no committed card
    ///     exists that the composer did not produce. The second half is the one a per-file assertion can
    ///     never make: a card orphaned by a spec change stays committed, stays read by agents, and nothing
    ///     would ever look at it again.
    /// </summary>
    [Fact]
    public async Task ScopedCards_AreCurrent()
    {
        // Off the walk ArchitectureMd_IsCurrent's model came from. Unlike that model, this one excludes what
        // `render` excludes — the spec project and the plumbing only it references — because here the
        // extraction decides card placement, and a card must land where the command puts it.
        CodebaseModel codebase = await RenderCodebase.Value;

        ContextComposition composition = ContextFileComposer.Compose(SelfModel, codebase, RepoRoot.Directory, SpecName);

        // A skip warning means a declared layer or scope matched no type — a spec that no longer describes
        // this codebase, and a card silently not written.
        composition.Warnings.ShouldBeEmpty();

        foreach (ContextFile file in composition.Files)
        {
            string relative = PathFormat.Relative(RepoRoot.Directory, file.Path);
            File.Exists(file.Path)
                .ShouldBeTrue($"{relative} is not committed; run `sliderule render`.");

            string committed = File.ReadAllText(file.Path);
            committed.ShouldCarryOneManagedBlock(relative)
                .ShouldBe(file.Body, relative);
        }

        CommittedContextFiles()
            .ShouldBe(
                composition.Files.Select(file => Path.GetFullPath(file.Path)),
                ignoreOrder: true,
                "a committed AGENTS.md that no placement produces is an orphan; delete it or restore the rule that placed it.");
    }

    /// <summary>
    ///     The model file, gated like the cards: composed through the calls <c>render</c> makes, over the
    ///     extraction <c>render</c> takes, and compared with the committed text. The file is data and owns
    ///     no markers, so the whole text is the comparison.
    /// </summary>
    [Fact]
    public async Task ModelFile_IsCurrent()
    {
        // The same excluded set the cards take: the file's project list is the universe the spec governs,
        // and the spec project and the plumbing only it references are not in it.
        CodebaseModel codebase = await RenderCodebase.Value;

        ModelDocument document = ModelDocument.From(SelfModel, SpecName, ModelDocumentProject.ListOf(codebase));
        string composed = ModelDocumentComposer.Compose(document);

        string path = Path.Combine(RepoRoot.Directory, ModelDocument.FileName);
        File.Exists(path)
            .ShouldBeTrue($"{ModelDocument.FileName} is not committed; run `sliderule render --model`.");

        // A checkout may have rewritten the line endings, which the writer's own comparison forgives too.
        string committed = File.ReadAllText(path);
        WholeFile.NormalizeNewlines(committed)
            .ShouldBe(composed);

        // Every Because, Fix and Citation lands in this file verbatim, and the tracked-text gates sweep it.
        // Asserted here so a spec edit that leaks a working reference reds beside the spec, where a
        // file-list gate would red far from the edit that caused it.
        DocProse.FindForbidden(composed, DocHygieneTests.InternalReferencePatterns)
            .ShouldReportNothing($"{ModelDocument.FileName} names internal working references");
    }

    /// <summary>
    ///     Every declared layer says what it is for. The language leaves <c>Purpose</c> optional; this
    ///     repository does not, because a row that names a place and not a purpose is the gap the trailer
    ///     exists to close. Held by test so a new layer cannot arrive without its sentence.
    /// </summary>
    [Fact]
    public void EveryLayer_CarriesAPurpose()
    {
        SelfModel.Layers
            .Where(layer => layer.Purpose is null)
            .Select(layer => layer.Name)
            .ShouldBeEmpty(
                "every self-spec layer states what it is for — give these a .Purpose(...) in SlideRuleArchitectureSpec.");
    }

    /// <summary>
    ///     The other half of that pin, read from the solution rather than from the spec: every project
    ///     under <c>src/</c> is declared as a layer. A layer is the subject a layer-scoped rule names, so a
    ///     project nobody declares as one falls outside every such rule and places no scoped card — and it
    ///     does that silently, because no list anywhere goes stale over it. Matching the solution's own
    ///     <c>src/</c> membership against the layers defined as exactly a project makes the next one red
    ///     the day it is added.
    /// </summary>
    [Fact]
    public void EverySrcProject_DefinesADeclaredLayer()
    {
        HashSet<string> layered = SelfModel.Layers
            .Select(layer => LayerNoun.ProjectOf(layer.Definition))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        List<string> unlayered = SolutionInventory.SrcProjects
            .Select(project => project.Name)
            .Where(name => !layered.Contains(name))
            .ToList();

        unlayered.ShouldBeEmpty(
            "every project under src/ is a layer of the self-spec: give these an arch.Layer(name, " +
            "arch.Project(...)) with a Purpose in SlideRuleArchitectureSpec.Define. A project without one is " +
            "governed by no layer-scoped rule and places no card.");
    }

    /// <summary>
    ///     The verb ledger's completeness pin. The self-spec uses every verb this codebase has a real use
    ///     for, and <see cref="VerbLedger" /> names every remaining one with a reason. That is enforced
    ///     rather than asserted: take the public <c>Must*</c> surface from the shared vocabulary sweep,
    ///     subtract what the built model actually uses, and require the remainder to be exactly the ledger. A
    ///     verb that ships with neither a self-use nor a ledger entry turns this red, and so does an entry for
    ///     a verb the spec has since taken up.
    /// </summary>
    [Fact]
    public void VerbLedger_AccountsForEveryUnusedVerb()
    {
        HashSet<string> used = SelfModel.Rules
            .Where(rule => rule.Constraint is not null)
            .Select(rule => VerbName(rule.Constraint!.GetType()))
            .ToHashSet(StringComparer.Ordinal);

        List<string> unused = ShippedVocabulary.VerbNames
            .Where(verb => !used.Contains(verb))
            .ToList();

        List<string> ledger = VerbLedger.Unused.Keys
            .OrderBy(verb => verb, StringComparer.Ordinal)
            .ToList();

        unused.ShouldBe(ledger,
            "the self-spec's unused verbs and VerbLedger have drifted apart — use a new verb on this repo's " +
            "real code or add it to the ledger with a plain reason, and drop an entry the spec now uses.");
    }

    /// <summary>
    ///     One definition of a blocking wait, held across two rules. <c>mcp/no-blocking-waits</c> keeps its
    ///     own ID, subject, <c>ServerShutdown</c> exemption and reason — that is what makes it a local rule
    ///     rather than a pack call — but it takes the member set from the pack instead of restating it, so
    ///     widening the pack's ban cannot leave this repository's own spec the laxer of the two. This pin
    ///     compares the two member sets, so an inlined copy passes until the pack's ban moves without it, and
    ///     then reds.
    /// </summary>
    [Fact]
    public void BlockingWaitBan_IsThePacksOwnMemberSet()
    {
        ArchitectureModel packed = Checker.Model(arch =>
            DotNetGuidance.NoSyncOverAsync(arch, arch.Types, PackPosture.Enforce));

        SelfModel.Rule("mcp/no-blocking-waits")
            .MemberAnchors()
            .ShouldBe(packed.Rule("async/no-sync-over-async").MemberAnchors());
    }

    /// <summary>
    ///     The sanctioned-broad-catcher pin: the discipline that holds an advisory channel empty by test,
    ///     applied to an exemption list. <c>exceptions/no-swallowed-broad-catches</c> carves nine type names
    ///     out of its own subject, and an exemption list is exactly the kind of thing that grows by one name at
    ///     a time until it means nothing. The names live in the rule's own <c>Except</c> operand, so the
    ///     rendered law is the list; pinning that sentence to the exact nine makes every addition a
    ///     deliberate act: the list can only grow by moving this assertion in the same commit, where a
    ///     reviewer sees the name and the reason (the comment on the rule) together.
    /// </summary>
    [Fact]
    public void SanctionedBroadCatchers_AreExactlyTheNineHoldAndContinueBoundaries()
    {
        SelfModel.Rule("exceptions/no-swallowed-broad-catches")
            .Sentence
            .ShouldBe(
                "Types in the Core, Extraction, Analyzers, SpecAnalyzers, SpecCompletion, Workspace, Host, Adapter " +
                "or Pack layers, " +
                "except types named `ArchChecker`, `ArchRuleTests`, `BuildTierAnalyzer`, `CommandEntryPoint`, " +
                "`IdleTimeoutWatchdog`, `ParentProcessWatcher`, `ServerShutdown`, `SpecLiteralAnalyzer` or " +
                "`VsWhereLocator`, must not swallow `Exception`.",
                customMessage: "a name added here leaves the broad-catch law; add it with its reason in the comment " +
                               "on the rule, or rewrite the handler to filter or rethrow.");
    }

    /// <summary>
    ///     The sanctioned-mutable-static pin, the broad-catcher pin's twin over the other exemption list:
    ///     <c>state/no-static-mutable</c> carves fields out of its own subject, each named by its declaring
    ///     type and its own name rather than by type alone, so its list grows one field at a time.
    /// </summary>
    [Fact]
    public void SanctionedMutableStatics_AreExactlyTheIntendedWrites()
    {
        SelfModel.Rule("state/no-static-mutable")
            .Sentence
            .ShouldBe(
                "Static fields of authored types in the Core, Extraction, Analyzers, SpecAnalyzers, SpecCompletion, " +
                "Workspace, Host, Adapter or Pack layers, " +
                "except `s_inFlightCount`, `s_lastActivityTicks`, `s_drainWaiter` or `s_timestampProvider` fields of " +
                "types named `IdleTimeoutWatchdog`, `s_hasExited` fields of types named `ServerShutdown` or " +
                "`_loadCount` fields of types named `WorkspaceLoader`, must be readonly.",
                customMessage: "a field added here leaves the static-mutability law; add it under its family in the " +
                               "comment on the rule (an Interlocked location, lock-guarded one-shot state, or the " +
                               "clock seam), or make the field readonly or const.");
    }

    // A constraint node's type name is its verb plus the `Constraint` suffix, with the member- and
    // project-level twins carrying a `Member` or `Project` prefix where a bare name would collide with the
    // type side's (MemberMustHaveSuffixConstraint is MustHaveSuffix on a member subject;
    // ProjectMustConstraint is Must on a project one). The packaging verbs need no prefix and carry
    // none — their names are theirs alone.
    private static string VerbName(Type constraintType)
    {
        string name = constraintType.Name;
        if (name.EndsWith("Constraint", StringComparison.Ordinal))
            name = name.Substring(0, name.Length - "Constraint".Length);
        if (name.StartsWith("Member", StringComparison.Ordinal))
            name = name.Substring("Member".Length);
        if (name.StartsWith("Project", StringComparison.Ordinal))
            name = name.Substring("Project".Length);

        return name;
    }

    /// <summary>
    ///     The live oracle for <c>.Authored()</c>'s detection contract (GRAMMAR §5.2), so the difference
    ///     between the CLI project noun and the same selection narrowed to authored types is knowable
    ///     exactly. The JSON generator carries its attribute on the generated class, and partial
    ///     declarations merge onto one symbol, so <c>SlideRuleJsonContext</c> lands on the generated side
    ///     even though its declaration is hand-written: the attribute-on-the-generated-class arm of the
    ///     partial-type rule. <see cref="RoslynProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes" />
    ///     works that arm too, and carries the other one, <c>[GeneratedRegex]</c>'s
    ///     attribute-on-the-generated-method shape. Asserted as an equality rather than a containment,
    ///     because a contract that took one authored type with it would be a worse failure than one that
    ///     missed a generated one.
    /// </summary>
    [Fact]
    public async Task CliProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes()
    {
        CodebaseModel codebase = await WholeCodebase.Value;

        var arch = new Arch();
        Selection cliProject = arch.Project("SlideRule.Cli");

        var evaluator = new SelectionEvaluator(codebase);
        IReadOnlyList<string> declared = evaluator.Evaluate(cliProject, SelectionPosition.Subject)
            .SortedFullNames();
        IReadOnlyList<string> authored = evaluator.Evaluate(cliProject.Authored(), SelectionPosition.Subject)
            .SortedFullNames();

        declared.Except(authored)
            .ShouldBe(["SlideRule.Cli.Rendering.SlideRuleJsonContext"]);

        // The synthesized top-level-statements entry point is the nearest thing this project has to a type
        // nobody typed, and no generator emitted it — so it must survive the narrowing.
        authored.ShouldContain("Program");
    }

    /// <summary>
    ///     The same contract against the same real solution, on a project that carries <em>both</em> arms of
    ///     the rule at once. <c>[GeneratedRegex]</c> carries its attribute on the generated <em>method</em>,
    ///     so <c>NuGetAuditDiagnostics</c> — the author's own partial class, which the generator completes —
    ///     stays authored while the three types each regex emits, plus the single <c>Utilities</c> class they
    ///     share, do not. Two of every three are nested and carry no attribute of their own, which is what
    ///     makes the containing-type walk necessary rather than defensive; the <c>_N</c> suffix is the
    ///     generator's declaration index across the whole <em>compilation</em>, not within one file, so a
    ///     <c>[GeneratedRegex]</c> added in an earlier-sorting file renumbers every one after it and moves this
    ///     pin for an edit the test never names. <c>SlnProjectLine_2</c>, which lives in
    ///     <c>SolutionProjectFileParser</c> rather than beside <c>AuditText_0</c>/<c>AuditCode_1</c>, is the
    ///     standing proof. Beside them sits the attribute-on-the-generated-<em>class</em> shape:
    ///     <c>ManifestJsonContext</c>, whose declaration is hand-written and whose partial halves merge onto
    ///     one symbol, lands on the generated side exactly as the CLI's <c>SlideRuleJsonContext</c> does
    ///     (<see cref="CliProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes" />). Both live here rather
    ///     than only in <c>GeneratedTypeExtractionTests</c>' synthetic generator because a hand-written
    ///     generator can only prove the rule as its author understood it, and the shapes these arms exist for
    ///     are ones the real toolchain emits.
    /// </summary>
    [Fact]
    public async Task RoslynProject_MinusAuthored_IsExactlyTheGeneratorEmittedTypes()
    {
        CodebaseModel codebase = await WholeCodebase.Value;

        var arch = new Arch();
        Selection roslynProject = arch.Project("SlideRule.Roslyn");

        var evaluator = new SelectionEvaluator(codebase);
        IReadOnlyList<string> declared = evaluator.Evaluate(roslynProject, SelectionPosition.Subject)
            .SortedFullNames();
        IReadOnlyList<string> authored = evaluator.Evaluate(roslynProject.Authored(), SelectionPosition.Subject)
            .SortedFullNames();

        declared.Except(authored)
            .ShouldBe(
            [
                "System.Text.RegularExpressions.Generated.AuditCode_1",
                "System.Text.RegularExpressions.Generated.AuditCode_1.RunnerFactory",
                "System.Text.RegularExpressions.Generated.AuditCode_1.RunnerFactory.Runner",
                "System.Text.RegularExpressions.Generated.AuditText_0",
                "System.Text.RegularExpressions.Generated.AuditText_0.RunnerFactory",
                "System.Text.RegularExpressions.Generated.AuditText_0.RunnerFactory.Runner",
                "System.Text.RegularExpressions.Generated.SlnProjectLine_2",
                "System.Text.RegularExpressions.Generated.SlnProjectLine_2.RunnerFactory",
                "System.Text.RegularExpressions.Generated.SlnProjectLine_2.RunnerFactory.Runner",
                "System.Text.RegularExpressions.Generated.Utilities",
                "SlideRule.Roslyn.Caching.ManifestJsonContext"
            ], ignoreOrder: true);

        // The type the generator completed rather than emitted. Its own declaration is hand-written, the
        // attribute landed on the method, and the two parts merge onto one symbol — so it must survive the
        // narrowing, and an equality above is what proves it was not quietly taken along.
        authored.ShouldContain("SlideRule.Roslyn.Diagnostics.NuGetAuditDiagnostics");
    }

    /// <summary>
    ///     The live oracle for the signal the built-output search refuses an intermediate assembly with.
    ///     Every probe test <em>fabricates</em> <c>CompilationOutputInfo.AssemblyPath</c>; nothing else in the
    ///     suite proves Roslyn populates it. If it ever stops — a Workspaces change, a BuildHost that no
    ///     longer reads <c>@(IntermediateAssembly)</c> — the refusal degrades to a silent no-op with every
    ///     other test still green, and this is the only gate that can catch it. Asserted as three facts per
    ///     project (present, rooted, and not the output path) because a path that is merely non-empty could
    ///     be the bin-side one, which would exclude the real output instead of the intermediate.
    /// </summary>
    [Fact]
    public async Task CompilationOutputInfo_CarriesARootedIntermediateAssemblyForEveryCSharpProject()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(RepoRoot.Solution, Ct);

        List<Project> cSharpProjects = snapshot.Solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .ToList();

        // The guard against a vacuous pass: an empty project set satisfies every claim below.
        cSharpProjects.ShouldNotBeEmpty();

        List<string> unusable = cSharpProjects
            .Where(project => !IsUsableIntermediateAssemblyPath(project))
            .Select(project => $"{project.Name}: '{project.CompilationOutputInfo.AssemblyPath}'")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        unusable.ShouldBeEmpty(
            "these projects carry no usable intermediate assembly path, so the built-output search has " +
            "nothing to refuse an obj-side result with — it would silently answer with one.");
    }

    private static bool IsUsableIntermediateAssemblyPath(Project project)
    {
        string? assemblyPath = project.CompilationOutputInfo.AssemblyPath;
        return !string.IsNullOrWhiteSpace(assemblyPath)
               && Path.IsPathRooted(assemblyPath)
               && !string.Equals(assemblyPath, project.OutputFilePath, PathComparison.Comparison);
    }

    // Every way the quoted roster lines differ from the printed ones, project by project: a project on one
    // side only, a line that no longer reads as printed, and — only once the lines agree — the order.
    private static List<string> GraphFenceDrift(
        IReadOnlyList<(string Name, string Line)> printed, IReadOnlyList<(string Name, string Line)> quoted)
    {
        List<string> drift = printed
            .Where(entry => quoted.All(line => line.Name != entry.Name))
            .Select(static entry => $"{entry.Name} is drawn, and the fence has no line for it.")
            .Concat(quoted
                .Where(line => printed.All(entry => entry.Name != line.Name))
                .Select(static line => $"{line.Name} has a line in the fence, and is not drawn."))
            .ToList();

        foreach ((string name, string line) in quoted)
            if (printed.FirstOrDefault(entry => entry.Name == name) is { Line: { } live } && live != line)
                drift.Add($"{name}: the fence says '{line.Trim()}' where the survey prints '{live.Trim()}'.");

        if (drift.Count == 0
            && !quoted.Select(static line => line.Name)
                .SequenceEqual(printed.Select(static entry => entry.Name), StringComparer.Ordinal))
            drift.Add("The fence lists the projects in another order than the survey prints them.");

        return drift;
    }

    // The project a roster line is about: everything before the first em-dash, indentation dropped.
    private static string RosterProject(string line)
    {
        string trimmed = line.Trim();
        int dash = trimmed.IndexOf(" — ", StringComparison.Ordinal);

        return dash < 0 ? trimmed : trimmed[..dash];
    }

    // Every AGENTS.md this solution's render could own: the root one plus whatever sits under the source
    // roots. Build output is skipped — a copied artifact under bin/ or obj/ is not a committed card.
    private static IReadOnlyList<string> CommittedContextFiles()
    {
        var files = new List<string> { Path.GetFullPath(RepoRoot.AgentsMd) };
        foreach (string root in ContextFileRoots)
        {
            string directory = Path.Combine(RepoRoot.Directory, root);
            if (!Directory.Exists(directory)) continue;

            files.AddRange(Directory
                .EnumerateFiles(directory, ContextFileComposer.FileName, SearchOption.AllDirectories)
                .Where(path => !TempFixtureWorkspace.IsBuildArtifact(RepoRoot.Relative(path)))
                .Select(Path.GetFullPath));
        }

        return files;
    }
}
