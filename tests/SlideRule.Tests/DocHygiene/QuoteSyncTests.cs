using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using Xunit;
using SlideRule.Tests.DocHygiene.Walkthroughs;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over the hand-written docs' fenced excerpts. The landing page, the self-check
///     page, the getting-started page, the language spec and the example walkthroughs quote committed files
///     inside fenced code blocks, and this gate holds each quote to the file it was cut from, so an edit to
///     the source that the doc does not follow fails the suite instead of publishing a stale quote. A
///     deliberate excerpt is held line by line, in order, as verbatim substrings of its source; a quote that
///     presents something whole, a file, a fence or a statement, is held whole, line for line, because an
///     in-order check passes a quote with lines left out.
/// </summary>
/// <remarks>
///     <para>
///         This gate proves doc → committed file, and stops there. That a committed generated file still
///         matches what the renderer emits is proved separately — by the dogfood self-spec tests
///         and by CI's <c>render --check</c> over the examples — because the render comparison is
///         expensive and serialized, and this gate reads only committed bytes so it stays cheap and
///         parallel-safe. <see cref="RuleQuoteSyncTests" /> covers the other quote shape: a rule-header line
///         quoted from captured <c>check</c> output, whose rendered sentence sits mid-bullet rather than on a
///         line of its own.
///     </para>
///     <para>
///         The fences with no committed source are registered beside this gate rather than in it. Captured
///         tool output on the root pages is <see cref="CapturedFences" />', each re-run by the test it names.
///         The example walkthroughs' fences are <see cref="WalkthroughFences" />': runs the replay re-makes,
///         the code a scenario writes, other tools' output declared as captures, and code shown for reading.
///         Commands for the reader to run are <see cref="CommandFences" />, the coding-agents page's launch lines
///         among them. This gate holds every marker of every registry to exactly one fence, and holds every held
///         page to having no fence that none of them registers.
///     </para>
///     <para>
///         A fence that CI runs, an excerpt or a command, names the workflow step that cuts it out of its page,
///         and <see cref="ExecutedFences_AreCutByTheirWorkflowStep" /> holds that step to the cut, by the
///         fence's marker and by the language its info string names. Every fence on the getting-started page
///         runs there, so every row for that page names its step.
///     </para>
/// </remarks>
public sealed class QuoteSyncTests
{
    private const string SelfSpec = "arch/SlideRule.Architecture/SlideRuleArchitectureSpec.cs";
    private const string QuotingSpec = "examples/Meridian.Quoting/arch/Meridian.Quoting.Architecture/QuotingArchitectureSpec.cs";
    private const string MeridianSpec = "examples/Meridian/arch/Meridian.Architecture/MeridianArchitectureSpec.cs";
    private const string InterchangeSpec = "examples/Meridian.Interchange/arch/Meridian.Interchange.Architecture/InterchangeArchitectureSpec.cs";
    private const string CanonicalSample = "tests/SlideRule.Tests/CanonicalSampleSpec.cs";

    private const string Workflow = ".github/workflows/ci.yml";

    /// <summary>
    ///     The workflow step that runs the getting-started page and the README quickstart as written, with the
    ///     tool and the contract package packed from the same commit.
    /// </summary>
    private const string GettingStartedWalk = "Walk GETTING-STARTED.md and the README quickstart from the packed package";

    /// <summary>
    ///     The registered quoted excerpts, each keyed by the doc it lives in and by a distinctive marker
    ///     substring that must pick out exactly one of that doc's fences, paired with the committed source
    ///     the fence is synced against, and marked with how much of that source it quotes.
    /// </summary>
    private static readonly Excerpt[] Excerpts =
    [
        new(
            // The landing page follows this fence with "That is the whole rule.", and this extent is what holds
            // that sentence true: the fence is compared with the rule's whole statement in the example spec, so a
            // line the spec adds to the rule, or the fence leaves out, reds here. The fence is quoted dedented,
            // and the extent sets indentation aside.
            DocPaths.RootReadme,
            "starter-rule",
            "arch.Rule(\"layering/domain-independent\")",
            QuotingSpec,
            Extent.WholeStatements),
        new(
            // The page presents the rule as the spec writes it, so it is held to the whole statement, as the
            // landing page's starter rule is.
            DocPaths.SelfCheck,
            "self-spec-rule",
            "arch.Rule(\"cli/no-stdout\")",
            SelfSpec,
            Extent.WholeStatements),
        new(
            // The rendered bullet carries the derived rule sentence and the rule's Because on one line,
            // which the FAIL stanza below splits across two; that join is what makes this marker pick out
            // the AGENTS.md fence rather than the hook report.
            DocPaths.SelfCheck,
            "agents-block",
            "`Console.WriteLine()`. Stdout is a protocol channel",
            "AGENTS.md"),
        new(
            // The page calls this class the whole of the adapter dogfood, so it is held whole from its declaration
            // to the end of the file, and a member added to the class reds here.
            DocPaths.SelfCheck,
            "adapter-test",
            "public sealed class AdapterSelfSpecTests",
            "tests/SlideRule.Tests/Dogfood/AdapterSelfSpecTests.cs",
            Extent.TailFromMarker),
        new(
            // The one Migrate fence on the self-check page. It is cut from Meridian rather than from the
            // self-spec, which carries no Migrate rule: a ratchet needs live debt to show.
            DocPaths.SelfCheck,
            "migrate-rule",
            "arch.Rule(\"data-access/no-inline-sql\")",
            MeridianSpec,
            Extent.WholeStatements),
        new(
            // Tool output with a committed counterpart: `render --diagram` writes the block into
            // ARCHITECTURE.md, and the self-check page quotes each of its two drawings whole, so each is
            // held line for line to the fence there that carries the same marker. The marker is the
            // accessible title rather than `flowchart LR`, which both drawings open with.
            DocPaths.SelfCheck,
            "graph-diagram",
            "accTitle: Codebase survey:",
            DocPaths.Architecture,
            Extent.WholeFence),
        new(
            // The law fence out of the same block, drawn from the spec rather than from the codebase.
            DocPaths.SelfCheck,
            "law-diagram",
            "accTitle: Architecture law:",
            DocPaths.Architecture,
            Extent.WholeFence),
        new(
            // The getting-started page's spec class is the contract package's own sample, held whole to it. CI
            // writes this fence into the spec project its walk makes, and compiles it.
            DocPaths.GettingStarted,
            "getting-started-spec-class",
            "public sealed class ArchitectureSpec : IArchitectureSpec",
            DocPaths.ContractReadme,
            Extent.WholeFence,
            GettingStartedWalk),
        new(
            // nuget.org shows the README as the tool package's page, so the README keeps the coding-agents page's
            // client config whole rather than a shorter copy of it. On drift the red prints the page's fence.
            DocPaths.RootReadme,
            "readme-mcp-json",
            "\"args\": [\"mcp\", \"MyApp.sln\"]",
            DocPaths.CodingAgents,
            Extent.WholeFence),
        new(
            // The language spec prints the canonical sample from its class declaration down, and the suite
            // compiles the same class, so the two change in one commit or the suite reds. The red prints the
            // compiled file's text to paste into the grammar, because the compiled side is the one verified.
            DocPaths.Grammar,
            "canonical-sample",
            "public sealed class ArchitectureSpec : IArchitectureSpec",
            CanonicalSample,
            Extent.TailFromMarker),

        // The example walkthroughs quote the AGENTS.md their own spec renders — the root managed block
        // for a subsystem, or a whole scoped card dropped beside a module's code, markers and all. Each is
        // synced against the committed file `render` writes, so a spec edit that re-renders the block
        // without the walkthrough following it fails here; a card quoted whole is held whole. The markers
        // are structural — a heading or the card's dragons line — because what must stay distinctive is
        // which fence, not which wording.
        new(
            DocPaths.MeridianReadme,
            "meridian-agents-block",
            "### Quarantined scopes",
            "examples/Meridian/AGENTS.md"),
        new(
            // The quarantined module's card, quoted without its markers or its generated-by line: the
            // in-order substring check reads straight past the source lines the walkthrough leaves out.
            DocPaths.MeridianReadme,
            "meridian-dragons-card",
            "Dragons: ISO 6346 check digit:",
            "examples/Meridian/src/Meridian.Clearance/AGENTS.md"),
        new(
            DocPaths.QuotingReadme,
            "quoting-agents-block",
            "`time/injected-clock`",
            "examples/Meridian.Quoting/AGENTS.md"),
        new(
            DocPaths.OperationsReadme,
            "operations-invoicing-card",
            "## Layer `Invoicing`",
            "examples/Meridian.Operations/src/Meridian.Operations/Invoicing/AGENTS.md",
            Extent.WholeFile),
        new(
            DocPaths.OperationsReadme,
            "operations-demurrage-card",
            "## Quarantined scope `demurrage/engine`",
            "examples/Meridian.Operations/src/Meridian.Operations/Demurrage/AGENTS.md",
            Extent.WholeFile),
        new(
            DocPaths.InterchangeReadme,
            "interchange-agents-block",
            "- `di/hosted-services-scope-their-work`",
            "examples/Meridian.Interchange/AGENTS.md"),

        // The walkthroughs also quote their own spec source, and each of those fences is held the way the
        // landing page's starter rule is: every statement it quotes is the spec's whole statement, continuation
        // columns included, so a line added to a rule or a reflow of the spec fails here rather than shipping a
        // stale quote. The markers are a double-quoted rule head or a pack call, which is what keeps each off
        // the rendered AGENTS.md fence that names the same rule id in backticks. Absent on purpose: the
        // Operations walkthrough, whose fence inlines the Selection locals the spec declares, and the three
        // Interchange fences that spell arch.Types.InNamespace(...) where the spec uses its `interchange`
        // local — none of those is its source's text, so no comparison here can hold on them.
        new(
            DocPaths.QuotingReadme,
            "quoting-spec-rules",
            "arch.Rule(\"layering/application-boundaries\")",
            QuotingSpec,
            Extent.WholeStatements),
        new(
            DocPaths.MeridianReadme,
            "meridian-pack-calls",
            "DotNetGuidance.AsyncSuffix(arch, arch.AnyOf(domain, web),",
            MeridianSpec,
            Extent.WholeStatements),
        new(
            DocPaths.InterchangeReadme,
            "interchange-hosted-service-rule",
            "arch.Rule(\"di/hosted-services-scope-their-work\")",
            InterchangeSpec,
            Extent.WholeStatements),
        new(
            DocPaths.InterchangeReadme,
            "interchange-captive-dependencies",
            "DotNetGuidance.NoCaptiveDependencies(",
            InterchangeSpec,
            Extent.WholeStatements),
        new(
            // Not a rendered block: the adoption walkthrough shows the spec project "exactly as
            // committed", a promise only a whole-file comparison keeps.
            DocPaths.MeridianAdopting,
            "adopting-spec-csproj",
            "<CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>",
            "examples/Meridian/arch/Meridian.Architecture/Meridian.Architecture.csproj",
            Extent.WholeFile)
    ];

    /// <summary>
    ///     The fences that quote nothing: commands for the reader to run, and files for the reader to save.
    ///     Each is registered so that its page can promise every fence is held by something, and held to its
    ///     place so that promise cannot silently lapse.
    /// </summary>
    private static readonly CommandFence[] CommandFences =
    [
        new(
            // The commands that reproduce the SARIF file the page quotes a result object from.
            DocPaths.SelfCheck,
            "meridian-sarif-commands",
            "dotnet build examples/Meridian/Meridian.slnx"),

        // The getting-started page's commands and the README quickstart, which CI's walk runs as written. The two
        // install lines are compared with the install rather than run, because CI installs from the pack.
        new(DocPaths.GettingStarted, "getting-started-install", "dotnet tool install -g SlideRule.Cli", GettingStartedWalk),
        new(DocPaths.GettingStarted, "getting-started-spec-project", "dotnet new classlib", GettingStartedWalk),
        new(DocPaths.GettingStarted, "getting-started-first-check", "dotnet build MyApp.sln", GettingStartedWalk),
        // The settings file the page tells its reader to save, which the walk writes where the page says before it
        // runs the page's CI lines, since those name no path and so run on what the file names.
        new(DocPaths.GettingStarted, "getting-started-settings", "\"solution\": \"MyApp.sln\"", GettingStartedWalk),
        new(DocPaths.GettingStarted, "getting-started-ci", "sliderule render --check", GettingStartedWalk),
        new(DocPaths.RootReadme, "readme-quickstart", "dotnet tool install -g SlideRule.Cli", GettingStartedWalk),

        // The coding-agents page's three launch lines: a client config, Claude Code's own registration command,
        // and the dnx launch a registry client runs. McpLaunchLineTests parses each of them against the tool the
        // CLI project ships, so this row holds each to its place and that gate holds what it says.
        new(DocPaths.CodingAgents, "coding-agents-mcp-json", "\"args\": [\"mcp\", \"MyApp.sln\"]"),
        new(DocPaths.CodingAgents, "coding-agents-claude-mcp-add", "claude mcp add"),
        new(DocPaths.CodingAgents, "coding-agents-dnx", "dotnet dnx"),

        // The example walkthroughs' install lines and the builds and checks each page tells its reader to run.
        new(DocPaths.MeridianReadme, "meridian-install", "dotnet tool install -g SlideRule.Cli"),
        new(DocPaths.MeridianReadme, "meridian-build-and-check", "sliderule check examples/Meridian/Meridian.slnx"),
        new(DocPaths.MeridianAdopting, "adopting-sandbox", "git worktree add ../meridian-sandbox"),
        new(DocPaths.MeridianAdopting, "adopting-sln-remove", "dotnet sln examples/Meridian/Meridian.slnx remove"),
        new(DocPaths.MeridianStoryboard, "storyboard-build-and-check", "dotnet build src/SlideRule.Cli"),
        new(DocPaths.MeridianStoryboard, "storyboard-beat3-check", "# exit 1: the Beat 3 board"),
        new(DocPaths.MeridianStoryboard, "storyboard-beat5-checks", "# the warning, as hook context"),
        new(DocPaths.QuotingReadme, "quoting-install", "dotnet tool install -g SlideRule.Cli"),
        new(DocPaths.QuotingReadme, "quoting-build-and-check", "sliderule check examples/Meridian.Quoting/Meridian.Quoting.slnx"),
        new(DocPaths.OperationsReadme, "operations-install", "dotnet tool install -g SlideRule.Cli"),
        new(DocPaths.OperationsReadme, "operations-build-and-check", "sliderule check examples/Meridian.Operations/Meridian.Operations.slnx"),
        new(DocPaths.InterchangeReadme, "interchange-install", "dotnet tool install -g SlideRule.Cli"),
        new(DocPaths.InterchangeReadme, "interchange-build-and-check", "sliderule check examples/Meridian.Interchange/Meridian.Interchange.slnx"),

        // The README's own build and test lines, the contributor guide's, the lock-file regeneration that guide
        // walks a maintainer through, and the security policy's release verification.
        new(DocPaths.RootReadme, "readme-building", "dotnet build SlideRule.slnx"),
        new(DocPaths.Contributing, "contributing-build-and-test", "dotnet build SlideRule.slnx"),
        new(DocPaths.Contributing, "contributing-lock-files", "gh pr checkout <number>"),
        new(DocPaths.Security, "security-attestation", "gh attestation verify")
    ];

    /// <summary>
    ///     The registered docs and their fences. Unlike its siblings this gate scans fences rather than a
    ///     quote type — its unit of registration is a marker inside a doc, not the doc — so it takes the
    ///     harness for the emptiness guard and the one scan per doc, and states no registry sweep.
    /// </summary>
    private static readonly DocGate<IReadOnlyList<string>> Gate = new(
        Markers()
            .Select(static marked => marked.Doc)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        static doc => SourceAnchors.Fences(RepoRoot.ReadText(doc)),
        quotes: "fenced code blocks",
        scanner: "fence scanner");

    [Fact]
    public void ExcerptRegistry_IsNotEmpty()
    {
        // Act & Assert: the excerpt tests below iterate the registry, so an empty one would make them pass
        // over nothing.
        Excerpts.ShouldNotBeEmpty("The excerpt registry is empty; the excerpt tests below would pass vacuously.");
    }

    [Fact]
    public void EachExcerpt_MatchesExactlyOneFence()
    {
        // Arrange
        List<string> failures = new();

        // Act: every registered marker — excerpt, captured output or command — must pick out exactly one
        // fence of its own doc; zero means the fence was dropped or reworded, more than one means the
        // marker is no longer distinctive.
        foreach (IGrouping<string, (string Doc, string Key, string Marker)> group in Markers()
                     .GroupBy(static marked => marked.Doc))
        {
            IReadOnlyList<IReadOnlyList<string>> fences = Gate.Scan(group.Key);
            foreach ((string Doc, string Key, string Marker) marked in group)
            {
                int matches = fences.Count(fence => ContainsMarker(fence, marked.Marker));
                if (matches != 1) failures.Add($"{group.Key} {marked.Key}: marker '{marked.Marker}' matched {matches} fences (expected 1).");
            }
        }

        // Assert
        failures.ShouldReportNothing("Registered excerpt markers no longer pick out exactly one fence each");
    }

    [Fact]
    public void SyncedExcerpts_AppearVerbatimInTheirSource()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> readSource = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: every non-blank line of an excerpt's fence must appear, in order, as a verbatim substring
        // of its source's lines; a miss means the quote has drifted from the committed file. A whole quote
        // is held to more than this, below.
        foreach (Excerpt excerpt in Excerpts.Where(static excerpt => excerpt.Extent == Extent.Slice))
        {
            IReadOnlyList<string>? source = readSource(excerpt.Source);
            if (source is null)
            {
                drift.Add($"{excerpt.Doc} {excerpt.Key}: source {excerpt.Source} does not exist.");
                continue;
            }

            string? unmatched = FirstLineNotInSource(FenceOf(excerpt), source);
            if (unmatched is not null)
                drift.Add($"{excerpt.Doc} {excerpt.Key}: fence line '{unmatched}' is not a verbatim substring, in order, of {excerpt.Source}.");
        }

        // Assert
        drift.ShouldReportNothing("Quoted excerpts no longer match their committed sources");
    }

    [Fact]
    public void WholeExcerpts_EqualTheirSource()
    {
        // Arrange
        Func<string, IReadOnlyList<string>?> readSource = SourceAnchors.DiskReader(RepoRoot.Directory);
        List<string> drift = new();

        // Act: a quote that presents itself as whole must be its source line for line, as much of the source as
        // its extent names. The in-order substring check above passes a quote with lines left out, which is how
        // an abridged drawing went on calling itself the committed one.
        foreach (Excerpt excerpt in Excerpts.Where(static excerpt => excerpt.Extent != Extent.Slice))
        {
            IReadOnlyList<string> fence = FenceOf(excerpt);
            (string whole, IReadOnlyList<string>? source) = excerpt.Extent switch
            {
                Extent.WholeFile => ($"{excerpt.Source} whole", readSource(excerpt.Source)),
                Extent.TailFromMarker => ($"{excerpt.Source} from its one line beginning '{excerpt.Marker}' to its end",
                    SourceTail(excerpt, readSource)),
                Extent.WholeStatements => SourceStatements(excerpt, fence, readSource),
                _ => ($"the one fence of {excerpt.Source} marked '{excerpt.Marker}'", SourceFence(excerpt))
            };
            if (source is null)
            {
                drift.Add($"{excerpt.Doc} {excerpt.Key}: there is no {whole} to compare the fence with.");
                continue;
            }

            if (!fence.SequenceEqual(source, StringComparer.Ordinal))
                drift.Add(
                    $"{excerpt.Doc} {excerpt.Key}: the fence is not {whole}, line for line; replace its body with:\n"
                    + string.Join("\n", source));
        }

        // Assert
        drift.ShouldReportNothing("Quotes presented as whole no longer equal their committed sources");
    }

    public static TheoryData<string> HeldPageCases => [.. DocPaths.HeldPages];

    [Theory]
    [MemberData(nameof(HeldPageCases))]
    public void EveryFence_IsRegistered(string page)
    {
        // Arrange: a held page promises that something holds every fence on it, which is true only while every
        // fence carries a marker some registry holds.
        List<string> markers = Markers()
            .Where(marked => marked.Doc == page)
            .Select(static marked => marked.Marker)
            .ToList();

        // Act
        List<string> unregistered = SourceAnchors.FencedBlocks(RepoRoot.ReadText(page))
            .Where(block => !markers.Any(marker => block.Any(line => line.Text.Contains(marker, StringComparison.Ordinal))))
            .Select(block => $"{page}:{block.FirstOrDefault().Number}")
            .ToList();

        // Assert
        unregistered.ShouldReportNothing(
            "These fences carry no registered marker, so nothing holds them; register each as an excerpt, a "
            + "captured or replayed run, a scenario's code, another tool's output, code for reading, or a command; "
            + "or name a root page in DocPaths.UnheldRootDocs with the reason its fences are not held");
    }

    [Fact]
    public void ExecutedFences_AreCutByTheirWorkflowStep()
    {
        // Arrange: CI cuts a fence it runs out of its page by the fence's marker, in the step the row names. A
        // reworded marker, a renamed step or a retagged fence leaves that step cutting nothing, which CI's own guard
        // reports only after a push.
        string workflow = RepoRoot.ReadText(Workflow);
        (string Doc, string Key, string Marker, string? RunBy)[] rows = Excerpts
            .Select(static excerpt => (excerpt.Doc, excerpt.Key, excerpt.Marker, excerpt.RunBy))
            .Concat(CommandFences.Select(static command => (command.Doc, command.Key, command.Marker, command.RunBy)))
            .ToArray();
        List<string> findings = new();

        // Act
        if (!rows.Any(static row => row.RunBy is not null))
            findings.Add("No registered fence names the workflow step that runs it, so this fact would hold over nothing.");

        IEnumerable<string> unrun = rows
            .Where(static row => row.Doc == DocPaths.GettingStarted && row.RunBy is null)
            .Select(static row => $"{row.Doc} {row.Key}: every fence on this page runs in CI, so its row must name the step that runs it.");
        findings.AddRange(unrun);

        foreach ((string doc, string key, string marker, string? runBy) in rows.Where(static row => row.RunBy is not null))
        {
            IReadOnlyList<string> steps = WorkflowSteps(workflow, runBy!);
            if (steps.Count != 1)
            {
                findings.Add($"{doc} {key}: {Workflow} has {steps.Count} steps named '{runBy}', not one.");
                continue;
            }

            if (CutFinding(doc, key, marker, runBy!, steps[0], RepoRoot.ReadText(doc)) is { } finding)
                findings.Add(finding);
        }

        // Assert
        findings.ShouldReportNothing("These fences run in CI, but the workflow step each names does not cut it out of its page");
    }

    [Fact]
    public void CutFinding_ReportsACutWhoseLanguageIsNotItsFencesTag()
    {
        // Arrange
        string docText = string.Join("\n", "```sh", "dotnet build MyApp.sln", "```");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet build MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBe("PAGE.md page-build: the step 'Walk' cuts this fence as 'bash', but the fence is tagged 'sh', so the cut comes back empty.");
    }

    [Fact]
    public void CutFinding_PassesACutThatNamesItsFencesTag()
    {
        // Arrange
        string docText = string.Join("\n", "```bash", "dotnet build MyApp.sln", "```");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet build MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBeNull();
    }

    [Fact]
    public void CutFinding_ReportsAStepThatDoesNotCutTheFence()
    {
        // Arrange
        string docText = string.Join("\n", "```bash", "dotnet build MyApp.sln", "```");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet test MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBe("PAGE.md page-build: the step 'Walk' does not cut this fence with fence PAGE.md <lang> 'dotnet build MyApp.sln'.");
    }

    [Fact]
    public void CutFinding_ReportsATildeFence()
    {
        // Arrange
        string docText = string.Join("\n", "~~~bash", "dotnet build MyApp.sln", "~~~");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet build MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBe("PAGE.md page-build: the step 'Walk' cuts this fence, but it opens with '~~~bash', and CI's cut opens only a fence of three backticks at column 0, so the cut comes back empty.");
    }

    [Fact]
    public void CutFinding_ReportsAnIndentedFence()
    {
        // Arrange
        string docText = string.Join("\n", "- Build it:", "", "  ```bash", "  dotnet build MyApp.sln", "  ```");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet build MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBe("PAGE.md page-build: the step 'Walk' cuts this fence, but it opens with '  ```bash', and CI's cut opens only a fence of three backticks at column 0, so the cut comes back empty.");
    }

    [Fact]
    public void CutFinding_ReportsASpaceBeforeTheTag()
    {
        // Arrange
        string docText = string.Join("\n", "``` bash", "dotnet build MyApp.sln", "```");
        const string step = "first=\"$(fence PAGE.md bash 'dotnet build MyApp.sln')\"";

        // Act
        string? finding = CutFinding("PAGE.md", "page-build", "dotnet build MyApp.sln", "Walk", step, docText);

        // Assert
        finding.ShouldBe("PAGE.md page-build: the step 'Walk' cuts this fence as 'bash', but the fence is tagged ' bash', so the cut comes back empty.");
    }

    [Fact]
    public void EveryRegisteredDoc_YieldsAtLeastOneFence()
    {
        // Act & Assert: guard against the fence scanner silently matching nothing if a doc's quoting style
        // changes out from under this gate. Every registered doc is reported, not just the first to come
        // up empty.
        Gate.ShouldYieldFromEveryRegisteredDoc();
    }

    // Every marker any registry holds a fence to, with the doc it names a fence in.
    private static IEnumerable<(string Doc, string Key, string Marker)> Markers()
    {
        return Excerpts.Select(static excerpt => (excerpt.Doc, excerpt.Key, excerpt.Marker))
            .Concat(CapturedFences.All.Select(static fence => (fence.Doc, fence.Key, fence.Marker)))
            .Concat(CommandFences.Select(static command => (command.Doc, command.Key, command.Marker)))
            .Concat(WalkthroughFences.Markers());
    }

    // What is wrong with how one step cuts one registered fence, or null when nothing is. CI's cut (fence.sh) opens a
    // fence only on a line starting with three backticks at column 0, takes the rest of that line as the language
    // with only its trailing side trimmed, and selects by language as well as by marker; so an indented or tilde
    // fence, or a cut naming a language the opening line does not carry, comes back empty. The opening line is read
    // raw, because TaggedBlocks opens fences CI never does and trims the tag both sides. A marker that resolves to no
    // fence or to several is EachExcerpt_MatchesExactlyOneFence's to report. CI closes a fence on any line starting
    // with three backticks, so a fence nested inside another is not modelled; no tracked doc nests one in a cut fence.
    private static string? CutFinding(string doc, string key, string marker, string runBy, string step, string docText)
    {
        Regex cut = new($@"\bfence {Regex.Escape(doc)} (\S+) '{Regex.Escape(marker)}'", RegexOptions.CultureInvariant);
        Match match = cut.Match(step);
        if (!match.Success) return $"{doc} {key}: the step '{runBy}' does not cut this fence with fence {doc} <lang> '{marker}'.";

        string language = match.Groups[1].Value;
        SourceAnchors.TaggedBlock[] marked = SourceAnchors.TaggedBlocks(docText)
            .Where(block => block.Lines.Any(line => line.Text.Contains(marker, StringComparison.Ordinal)))
            .ToArray();
        if (marked is not [var fence]) return null;

        string opening = docText.Split('\n')[fence.OpeningLine - 1].TrimEnd('\r');
        if (!opening.StartsWith("```", StringComparison.Ordinal))
            return $"{doc} {key}: the step '{runBy}' cuts this fence, but it opens with '{opening}', and CI's cut opens only a fence of three backticks at column 0, so the cut comes back empty.";

        string tag = opening[3..].TrimEnd(' ', '\t');
        if (string.Equals(tag, language, StringComparison.Ordinal)) return null;

        return $"{doc} {key}: the step '{runBy}' cuts this fence as '{language}', but the fence is tagged '{tag}', so the cut comes back empty.";
    }

    // The run body of every step the workflow gives this name: the lines after its "- name:" line, up to the first
    // non-blank line indented no deeper than that line. A blank line inside a run block carries no indentation at
    // all, so a blank line cannot end the step.
    private static IReadOnlyList<string> WorkflowSteps(string workflow, string name)
    {
        string[] lines = workflow.NormalizedLines()
            .Split('\n');
        List<string> steps = new();

        for (var index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (line.Trim() != $"- name: {name}") continue;

            int indent = line.Length - line.TrimStart().Length;
            IEnumerable<string> body = lines
                .Skip(index + 1)
                .TakeWhile(candidate => candidate.Trim().Length == 0 || candidate.Length - candidate.TrimStart().Length > indent);
            steps.Add(string.Join("\n", body));
        }

        return steps;
    }

    private static IReadOnlyList<string> FenceOf(Excerpt excerpt)
    {
        return Gate.Scan(excerpt.Doc)
            .Single(fence => ContainsMarker(fence, excerpt.Marker));
    }

    // The source's own fence for a whole-fence excerpt: the one carrying the excerpt's marker, or null when
    // the source carries none or several.
    private static IReadOnlyList<string>? SourceFence(Excerpt excerpt)
    {
        List<IReadOnlyList<string>> carrying = SourceAnchors.Fences(RepoRoot.ReadText(excerpt.Source))
            .Where(candidate => ContainsMarker(candidate, excerpt.Marker))
            .ToList();

        return carrying is [var fence] ? fence : null;
    }

    // The source's tail for a tail excerpt: its lines from the one line that begins with the excerpt's marker,
    // once indentation is set aside, to its end. Null when the source is missing or has no such line or several.
    private static IReadOnlyList<string>? SourceTail(Excerpt excerpt, Func<string, IReadOnlyList<string>?> readSource)
    {
        if (readSource(excerpt.Source) is not { } lines) return null;
        if (OneLineBeginning(lines, excerpt.Marker) is not { } start) return null;

        return lines.Skip(start)
            .ToArray();
    }

    // The fence as its source writes it, for a whole-statements excerpt, and what a red calls that. When a
    // statement the fence quotes begins no statement of the source, there is nothing to compare, and the red
    // names the statement's opening line.
    private static (string Whole, IReadOnlyList<string>? Source) SourceStatements(
        Excerpt excerpt,
        IReadOnlyList<string> fence,
        Func<string, IReadOnlyList<string>?> readSource)
    {
        if (readSource(excerpt.Source) is not { } lines) return (excerpt.Source, null);

        (IReadOnlyList<string>? asWritten, string? unfound) = StatementsAsWritten(fence, lines);

        return asWritten is null
            ? ($"statement of {excerpt.Source} from its one line beginning '{unfound}'", null)
            : ($"the statements it quotes from {excerpt.Source}, each whole and indented as the fence is", asWritten);
    }

    /// <summary>
    ///     Returns <paramref name="fence" /> as <paramref name="source" /> writes it: each statement the fence
    ///     quotes replaced by the source's statement that begins with the same line, once indentation is set
    ///     aside, from that line to the line that ends the statement, indented as the fence is. Blank lines
    ///     between the statements are kept and any other line outside them is dropped, so the result equals the
    ///     fence exactly when every statement it quotes is whole and nothing else is in it. The lines are
    ///     <see langword="null" />, and <c>Unfound</c> holds the statement's opening line, when that line begins
    ///     no line of the source or several, or begins one that no statement starts on.
    /// </summary>
    internal static (IReadOnlyList<string>? AsWritten, string? Unfound) StatementsAsWritten(
        IReadOnlyList<string> fence,
        IReadOnlyList<string> source)
    {
        string fenceText = string.Join("\n", fence);
        string blockText = "{\n" + fenceText + "\n}";
        SourceText block = SourceText.From(blockText);

        // The fence is parsed with the parser's error recovery on purpose: a fence that leaves out its rule's last
        // line loses the semicolon with it and still parses into its statements, so the red can print the whole
        // statement to paste. Recovery cannot hide drift, because the result is built from the source's own
        // statements and equals the fence only when the fence is their text.
        var quoted = (BlockSyntax)SyntaxFactory.ParseStatement(blockText);
        string sourceText = string.Join("\n", source);
        SyntaxTree written = CSharpSyntaxTree.ParseText(sourceText);
        int fenceIndentation = Indentation(fence);
        var indentation = new string(' ', fenceIndentation);
        List<string> asWritten = new();
        var next = 0;

        foreach (StatementSyntax statement in quoted.Statements)
        {
            // The block opens with its brace on a line of its own, so each fence line sits one line further down.
            int first = block.Lines.GetLineFromPosition(statement.SpanStart).LineNumber - 1;
            int last = block.Lines.GetLineFromPosition(statement.Span.End - 1).LineNumber - 1;
            IEnumerable<string> blanksBefore = fence.Skip(next)
                .Take(first - next)
                .Where(string.IsNullOrWhiteSpace);
            asWritten.AddRange(blanksBefore);

            string opening = fence[first]
                .TrimStart();
            if (WrittenStatement(written, source, opening) is not { } lines) return (null, opening);

            int common = Indentation(lines);
            IEnumerable<string> reindented = lines
                .Select(line => string.IsNullOrWhiteSpace(line) ? "" : indentation + line[common..]);
            asWritten.AddRange(reindented);
            next = last + 1;
        }

        IEnumerable<string> blanksAfter = fence.Skip(next)
            .Where(string.IsNullOrWhiteSpace);
        asWritten.AddRange(blanksAfter);

        return (asWritten, null);
    }

    // The lines of the source's statement that begins on its one line beginning with the opening line, once
    // indentation is set aside, from that line to the line that ends the statement. Null when no line begins
    // that way or several do, or when no statement starts where that line's text does.
    private static IReadOnlyList<string>? WrittenStatement(SyntaxTree written, IReadOnlyList<string> source, string opening)
    {
        if (OneLineBeginning(source, opening) is not { } start) return null;

        TextLineCollection textLines = written.GetText()
            .Lines;
        string line = source[start];
        int position = textLines[start].Start + line.Length - line.TrimStart().Length;
        StatementSyntax? statement = written.GetRoot()
            .FindToken(position)
            .Parent?
            .AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate => candidate.SpanStart == position);
        if (statement is null) return null;

        int end = textLines.GetLineFromPosition(statement.Span.End - 1).LineNumber;

        return source.Skip(start)
            .Take(end - start + 1)
            .ToArray();
    }

    // The index of the one line that begins with the given text once indentation is set aside, or null when no
    // line does or several do. A line that quotes the text further along, a comment naming it, never counts.
    private static int? OneLineBeginning(IReadOnlyList<string> lines, string beginning)
    {
        List<int> starts = Enumerable.Range(0, lines.Count)
            .Where(index => lines[index]
                .TrimStart()
                .StartsWith(beginning, StringComparison.Ordinal))
            .ToList();

        return starts is [var start] ? start : null;
    }

    // The indentation every non-blank line shares.
    private static int Indentation(IEnumerable<string> lines)
    {
        return lines.Where(static line => !string.IsNullOrWhiteSpace(line))
            .Select(static line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();
    }

    private static bool ContainsMarker(IReadOnlyList<string> fence, string marker)
    {
        return fence.Any(line => line.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Returns the first non-blank line of <paramref name="fence" /> that is not a verbatim ordinal
    ///     substring of some <paramref name="source" /> line at a strictly greater index than the previous
    ///     match (greedy first match after the previous), or <see langword="null" /> when every non-blank
    ///     fence line is found in order. Blank fence lines are skipped, so a quoted blank separator needs no
    ///     counterpart in the source. Nothing is trimmed: the docs quote with original indentation, so
    ///     containment must hold on the raw line.
    /// </summary>
    internal static string? FirstLineNotInSource(IReadOnlyList<string> fence, IReadOnlyList<string> source)
    {
        var searchFrom = 0;
        foreach (string fenceLine in fence)
        {
            if (string.IsNullOrWhiteSpace(fenceLine)) continue;

            int found = -1;
            for (int index = searchFrom; index < source.Count; index++)
                if (source[index]
                    .Contains(fenceLine, StringComparison.Ordinal))
                {
                    found = index;
                    break;
                }

            if (found < 0) return fenceLine;
            searchFrom = found + 1;
        }

        return null;
    }

    /// <summary>How much of its source a registered excerpt quotes, and so how it is held.</summary>
    private enum Extent
    {
        /// <summary>A deliberate excerpt: every non-blank line in order, each a verbatim substring of a source line.</summary>
        Slice,

        /// <summary>One whole fence of the source, the one carrying the same marker, held line for line.</summary>
        WholeFence,

        /// <summary>The whole source file, held line for line.</summary>
        WholeFile,

        /// <summary>The source file from its one line that begins with the marker to its end, held line for line.</summary>
        TailFromMarker,

        /// <summary>
        ///     Each statement the fence quotes, held line for line to the source's statement that begins with the
        ///     same line, from that line to the one that ends it, once indentation is set aside on both sides.
        /// </summary>
        WholeStatements
    }

    /// <summary>
    ///     One registered excerpt: the <paramref name="Doc" /> it is quoted in, its <paramref name="Key" />,
    ///     the <paramref name="Marker" /> substring that picks out its fence within that doc, the committed
    ///     <paramref name="Source" /> it is synced against, how much of that source it quotes
    ///     (<paramref name="Extent" />), and the workflow step that cuts it out of the doc and runs it
    ///     (<paramref name="RunBy" />), where CI runs it.
    /// </summary>
    private sealed record Excerpt(
        string Doc,
        string Key,
        string Marker,
        string Source,
        Extent Extent = Extent.Slice,
        string? RunBy = null);

    /// <summary>
    ///     One registered command fence: the <paramref name="Doc" /> it sits in, its <paramref name="Key" />,
    ///     the <paramref name="Marker" /> that picks it out, and the workflow step that cuts it out of the doc
    ///     and runs it (<paramref name="RunBy" />), where CI runs it.
    /// </summary>
    private sealed record CommandFence(string Doc, string Key, string Marker, string? RunBy = null);
}
