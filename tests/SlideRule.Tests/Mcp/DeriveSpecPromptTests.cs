using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Mcp.Prompts;
using SlideRule.Rendering;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.SpecAnalyzers;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Pins the <c>derive_spec</c> MCP prompt end to end over the in-memory client/server harness: it is
///     advertised in <c>prompts/list</c> and <c>prompts/get</c> returns a single user message whose body
///     carries the recipe's core commitments — the honesty contract, the human-owned ratchet steps,
///     and the checker's authoring signals. Assertions target a few stable anchor phrases, not the whole
///     blob, so wording can evolve while those commitments cannot silently drift. A load-time guard turns a
///     renamed resource or drifted manifest id into a test failure instead of a runtime surprise on the
///     first <c>prompts/get</c>. The scaffold's spec class is compiled against the contract as the recipe
///     prints it.
/// </summary>
public sealed class DeriveSpecPromptTests
{
    /// <summary>
    ///     Ceiling on the body every <c>prompts/get</c> serves, in the same UTF-16 code units
    ///     <see cref="string.Length" /> counts. First set at 69,500 — the 69,418 measured that day,
    ///     rounded up to the next 500 — and standing at 70,000 since 2026-09-22.
    /// </summary>
    /// <remarks>
    ///     The recipe arrives whole at every kickoff, before the reader has done anything, and it only grew
    ///     until it met this constant: 39,348 characters on 2026-08-09, 56,555 on 2026-08-26, 69,418 when
    ///     this was set. Each of those figures was measured on its date and none of them gated anything, so
    ///     every addition was free. That is what this constant changes, and the reason it was set close
    ///     rather than generously.
    ///     Raised once, to 70,000 on 2026-09-22, and what it bought is the accessibility adjectives
    ///     (<c>.ThatArePublic()</c> / <c>.ThatAreInternal()</c> / <c>.ThatArePrivate()</c>, GRAMMAR §5.2)
    ///     with the <c>Except</c> idiom that is the only way to spell their negation. The ceiling had 50
    ///     characters of headroom, so the vocabulary alone did not fit, and the floor below says
    ///     vocabulary is not what gets cut. Two asides were trimmed first and recovered 65 of the 134
    ///     needed; the rest is this.
    ///     <para>
    ///         Cut once, from 69,999 to 62,236 on 2026-10-15, by rewriting the procedure. What paid for it: the
    ///         survey's key glossary and grain ladder, which the <c>arch_graph</c> description already carries
    ///         when the tool is called; the spec-discovery refusals, which carry their own cures; a
    ///         closing list of routes no derivation followed; and what the recipe said twice. The rewrite added
    ///         the stated-intent list, five warnings at the steps where their traps are met, and a receipt whose
    ///         posture lines are copied from the check. The ceiling stays at 70,000: set at the new length, it
    ///         would restore the zero headroom that made each of the three additions before the rewrite pay for
    ///         itself with a trim.
    ///     </para>
    ///     <see cref="DeriveSpecVocabularyTests" /> holds the other side as a floor on content — every
    ///     shipped constraint verb and adjective must be named in the prose outside the generated table —
    ///     so vocabulary can never be the thing that gets cut here.
    ///     <para>
    ///         When this reds, cut or move content before reaching for the number. Raising it is a
    ///         decision in its own right: move the constant in the same commit as the text that needed
    ///         the room, and record here what it bought.
    ///     </para>
    ///     <para>
    ///         The ceiling is not a claim that a shorter recipe derives better. Nothing here measures the
    ///         quality of a derivation, and that question is answered by running derivations against real
    ///         codebases rather than by a character count.
    ///     </para>
    /// </remarks>
    private const int ServedBodyCeiling = 70_000;

    // The text only the receipt's fence carries, which picks that fence out of the recipe's others.
    private const string ReceiptMarker = "curation: pending";

    private static readonly Regex CheckLine = new(
        @"^check: exit (?<exit>\d+), rules (?<rules>\d+), passed (?<passed>\d+), failed (?<failed>\d+), skipped (?<skipped>\d+), violations (?<violations>\d+)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex PosturesLine = new(
        @"^postures: enforce (?<enforce>\d+) / migrate (?<migrate>\d+) / quarantine (?<quarantine>\d+) / caution (?<caution>\d+)$",
        RegexOptions.CultureInvariant);

    // A prompt call never resolves the binding (prompts read no solution/spec), so any working directory
    // serves; a real one keeps StartAsync's host build honest.
    private static McpServerBinding Binding => new(null, null, Directory.GetCurrentDirectory());

    [Fact]
    public async Task ListPrompts_AdvertisesDeriveSpec()
    {
        // Arrange
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        // Act
        IList<McpClientPrompt> prompts = await harness.Client.ListPromptsAsync(cancellationToken: Ct);

        // Assert — registering the prompt advertises the capability and lists it by name.
        prompts.Select(prompt => prompt.Name)
            .ShouldContain(ArchPrompts.DeriveSpecName);
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ReturnsSingleUserMessageCarryingCoreCommitments()
    {
        GetPromptResult result = await GetDeriveSpecAsync();

        // Assert — a string-returning prompt method maps to one Role.User text message.
        PromptMessage message = result.Messages.ShouldHaveSingleItem();
        message.Role.ShouldBe(Role.User);
        string text = message.Content.ShouldBeOfType<TextContentBlock>()
            .Text;
        text.ShouldContain("does not infer"); // the server infers no architecture
        text.ShouldContain("do not guess"); // the curation gate belongs to the human
        text.ShouldContain("data, not failures"); // mid-derive reds are the evidence pass working
        text.ShouldContain("by the human"); // the ratchet steps are human-owned...
        text.ShouldContain("baseline init"); // ...specifically this one
        text.ShouldContain("implementation type"); // BoundaryOnlyVia must list the facade impl
        // The no-load boundary: a facade the spec cannot compile against is still law, and the prompt
        // must reach for that before telling an adopter to open the product project up.
        text.ShouldContain("arch.Types.Named(\"IBillingFacade\", \"BillingFacade\")");
        text.ShouldContain("rather than grandfathering it");
        // The layer recipe shows the purpose trailer and says where its sentence comes from: the
        // stated-intent prose the adopter was already sent to read in step 0.
        text.ShouldContain(".Purpose(\"Domain holds the order and customer model.\")");
        text.ShouldContain("one sentence on what the layer is for");
        // The scope recipe teaches the second scope posture and the rule that picks between the two.
        text.ShouldContain(".Caution(");
        text.ShouldContain("new callers welcome");
        // The family recipe: one rule over a partition of plugins or modules, the three family verbs, and
        // what "itself" means on a family — the cell, as declared — so the recipe never proposes one
        // rule per cell where one sentence says it. The cycle gate is taught as the law for peers whose
        // order nobody has stated, so an adopter reaches for it before writing an order they do not know.
        text.ShouldContain("arch.Each(arch.Projects.Matching(\"MyApp.Plugin.*\"))");
        text.ShouldContain("MustNotReferenceEachOther()");
        text.ShouldContain("MustOnlyBeReferencedByItself()");
        text.ShouldContain("MustNotHaveCircularReferences()");
        text.ShouldContain("but never in a circle");
        text.ShouldContain("over the family, not one per unit");
        text.ShouldContain("arch_graph"); // the survey tool
        text.ShouldContain("arch_check"); // the evidence tool
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_CollectsStatedIntentWithItsSources()
    {
        string text = await ServedRecipeAsync();

        // Assert — the first step was a conditional bullet inside a checklist, and derivations read it as one grep
        // over the repository: none followed a link to the project's published documentation. It is now an action
        // whose artifact the receipt transcribes, so what is pinned is the artifact's three fields, the reason a
        // published page counts as part of the codebase, and the receipt lines the list becomes.
        text.ShouldContain(
            "with its words as written, its source (a path in this repository, or a URL) and, from step 3 on, the rule it became");
        text.ShouldContain("part of the project wherever it is hosted");
        text.ShouldContain("the receipt's `statedIntent:` lines");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WritesEachPostureIntoTheSpecBeforeTheGate()
    {
        string text = await ServedRecipeAsync();

        // Assert — postures proposed in the curation table alone left the spec all Enforce, and Enforce has no
        // baseline, so the human's baseline captured nothing and the check stayed red. The recipe has the agent
        // write each posture into the code, test the rebuilt spec's check for a red Enforce rule it cannot explain,
        // and show the human what the code says rather than what the agent meant.
        text.ShouldContain("the posture goes into the code now, not into a table");
        text.ShouldContain("`posture: enforce` and `status: failed`");
        text.ShouldContain("never a proposal");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WarnsAtTheStepWhereEachTrapIsMet()
    {
        string text = await ServedRecipeAsync();

        // Assert — each warning sits where its trap is met: an SDK pinned below 10 at the scaffold's framework, a
        // props file's versionless package references under the central-management opt-out, MVC action names under
        // the async-suffix rule, a test host's registrations under the captive-dependency rule, and the analyzer
        // rollout in the next steps handed to the human.
        text.ShouldContain("NETSDK1045");
        text.ShouldContain("NU1015");
        text.ShouldContain("take the controllers out first");
        text.ShouldContain("test projects included");
        text.ShouldContain("roll out the build analyzer");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_EmbedsAuthoringSignalAnchors()
    {
        string text = await ServedRecipeAsync();

        // Assert — the checker's authoring signals must survive prose edits: they are what stop an agent
        // misreading an empty subject or an inert target as evidence about the code. The cure itself
        // rides each signal as its `hint`, so what the recipe pins is the diagnosis, the route to the hint,
        // and the rule that ends the loop — never the glob semantics the hint carries.
        text.ShouldContain("emptySubject");
        text.ShouldContain("This rule is inert: its target selection matched no types.");
        text.ShouldContain("This rule is partly inert");
        text.ShouldContain("apply the hint rather than guessing");
        text.ShouldContain("two consecutive re-checks");
        text.ShouldContain("carry it to step 6 as `undecided`");
        text.ShouldContain("subject could not be made to match; human to resolve");
        text.ShouldContain("^[a-z0-9-]+(/[a-z0-9-]+)*$");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ClosesWithTheFixedKeyReceipt()
    {
        string text = await ServedRecipeAsync();

        // Assert — the outcome report was a prose list, so every derivation closed in a different shape and
        // nothing downstream could read one. The keys are the contract a reader or a script finds each claim
        // under, and the sentence keeping the three claims apart is what stops a spec that builds from being
        // read as a spec somebody curated, or a pass on a partial model from being read as obedience.
        string[] receiptKeys =
        [
            "statedIntent:",
            "specBuild: ok",
            "evidencePass: complete",
            "check: exit 1, rules 23, passed 12, failed 9, skipped 2, violations 70",
            "postures: enforce",
            "leftRed:",
            "dropped:",
            "undecided:",
            "curation: pending",
            "baseline: not run (human)",
            "render: not run (human)"
        ];
        foreach (string key in receiptKeys) text.ShouldContain(key);
        text.ShouldContain("none implies another");
        text.ShouldContain("has *not observed* the rule");

        // The posture and red-rule lines once had no defined source, so a derivation wrote its own proposal into
        // them. Each is now a copy of the last whole check, defined by the fields it is copied from.
        text.ShouldContain("a transcription, never a recount");
        text.ShouldContain("counts its `rules[]` by `posture`");
        text.ShouldContain("is every entry with `posture: enforce` and `status: failed`");
    }

    [Fact]
    public void DeriveSpec_ReceiptFence_PrescribesEachKeyInOrder()
    {
        // Arrange: the recipe's one fence carrying the receipt. A receipt is read by its keys, each at the start of
        // a line and in this order, so the fence is the contract every derivation closes on.
        QuotedFence fence = ShouldFindReceiptFence();
        string[] lineKeys = fence.Lines
            .Select(static line => line.Split(':')[0])
            .ToArray();

        // Act: a key written once per entry reads as the one key it is.
        List<string> keys = lineKeys
            .Where((key, index) => index == 0 || key != lineKeys[index - 1])
            .ToList();

        // Assert
        keys.ShouldBe([
            "statedIntent", "specBuild", "evidencePass", "check", "postures", "leftRed", "dropped", "undecided",
            "curation", "baseline", "render"
        ]);
    }

    [Fact]
    public void DeriveSpec_ReceiptExample_IsATranscriptionACheckCouldProduce()
    {
        // Arrange: the example is the one receipt every derivation sees, and its numbers are copied as readily as
        // its keys, so they must add up the way a check document's do.
        QuotedFence fence = ShouldFindReceiptFence();

        // Act
        Match check = ShouldFindReceiptLine(fence, CheckLine);
        Match postures = ShouldFindReceiptLine(fence, PosturesLine);
        int rules = NumberIn(check, "rules");
        int failed = NumberIn(check, "failed");
        int exitCode = NumberIn(check, "exit");
        int accountedFor = NumberIn(check, "passed") + failed + NumberIn(check, "skipped");
        int postured = NumberIn(postures, "enforce") + NumberIn(postures, "migrate") + NumberIn(postures, "quarantine")
                       + NumberIn(postures, "caution");

        // Assert
        accountedFor.ShouldBe(rules, "a check's passed, failed and skipped rules account for every rule it checked");
        postured.ShouldBe(rules, "the postures line counts each entry of the same document once");
        exitCode.ShouldBe(failed > 0 ? 1 : 0, "a check with a failed rule exits 1");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_SubstitutesRunningVersionIntoScaffold()
    {
        string text = await ServedRecipeAsync();

        // Assert
        text.ShouldContain($"<PackageReference Include=\"SlideRule\" Version=\"{ServerVersion.SemVer}\" />");
        text.ShouldNotContain(ArchPrompts.ScaffoldVersionPlaceholder);
        text.ShouldContain("`sliderule --version` prints");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_NamesTheDnxPrefixForASessionWithNoInstalledCommand()
    {
        string text = await ServedRecipeAsync();

        // Assert — the recipe names `sliderule` commands throughout and is served to a session that may have no
        // such command: the registry manifest's dnx launch runs the package without installing the tool.
        // Those spellings stay right for every installed reader, so what the recipe owes the other population
        // is the prefix, pinned to the running build for the same reason the scaffold's version is. Bare
        // `dnx` would not resolve in the shell an agent drives — see ServerInstructionsTests.
        text.ShouldContain($"`dotnet dnx SlideRule.Cli@{ServerVersion.SemVer} --yes --`");
        text.ShouldNotContain(ArchPrompts.CliPackagePlaceholder);

        // And where to run it: `dotnet dnx` selects its SDK through global.json, so inside a repository that
        // pins an SDK below 10 the prefix names a verb that does not exist there.
        text.ShouldContain("run from outside any `global.json` that pins an SDK below 10");

        // The substitution is a plain replace over the whole recipe, and the product's own repository URL
        // carries the package's name too. This is what keeps it from being rewritten into a dead link.
        text.ShouldContain("https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ScaffoldOptsOutOfCentralPackageManagement()
    {
        string text = await ServedRecipeAsync();

        // Assert — the scaffold pins its own version inline, which is NU1008 on a repository that manages
        // package versions centrally. The opt-out is what makes one csproj valid either way, so it cannot
        // drop out of the recipe quietly; CI executes the same commitment against a real central file.
        text.ShouldContain("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_ScaffoldSetsTheLanguageVersionItsSpecClassNeeds()
    {
        string text = await ServedRecipeAsync();

        // Assert — the recipe sends a legacy adopter to change the TFM and keep the rest, and net48 and
        // netstandard2.0 both default to C# 7.3, where the spec class below the csproj is CS8370. What is
        // pinned is the agreement rather than either line: the csproj names a language version, and the
        // class is written in a form that needs one. Either half moving alone is a scaffold an adopter
        // cannot build, so either half moving alone must red here.
        text.ShouldContain("<LangVersion>latest</LangVersion>");
        text.ShouldContain("namespace MyApp.Architecture;");
    }

    [Fact]
    public async Task DeriveSpec_ScaffoldSpecClass_CompilesAgainstTheContract()
    {
        // Arrange: the recipe's one fence carrying the spec class, read off disk as CI cuts it. CI builds that class
        // from the packed contract, but only after a push; this reds a recipe edit that breaks it in its own commit.
        QuotedFence fence = QuotedFence.ShouldFindOne(
            DocPaths.DeriveSpecPrompt, "IArchitectureSpec", "where the scaffold's spec class should be exactly one");
        string source = string.Join("\n", fence.Lines);

        // Act: the harness holds the class to compiling with no errors before it returns.
        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        // Assert
        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WarnsThatAnArchNamespaceSegmentShadowsTheContractType()
    {
        string text = await ServedRecipeAsync();

        // Assert — the recipe's own arch/ folder convention makes an Arch-suffixed project name the
        // natural reach, and neither resulting compiler error mentions this library: the recipe must
        // carry the cause (a namespace segment named Arch shadows the contract type) and the way out.
        text.ShouldContain("CS0118");
        text.ShouldContain("'Arch' is a namespace but is used like a type");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_WarnsAgainstHandTrimmingTheSolutionAddRewrite()
    {
        string text = await ServedRecipeAsync();

        // Assert — on a multi-configuration solution the CLI's own rewrite dwarfs the spec project it
        // adds, which reads as damage against the recipe's one-diff close; the tidy-looking repair is a
        // hand-written Debug/Release entry that leaves the spec project unbuilt everywhere else. Both
        // halves must survive prose edits: that the rewrite is correct, and that trimming it is not.
        text.ShouldContain("rewrite more of a `.sln` than the one project it adds");
        text.ShouldContain("skips a project with no mapping for it");
        text.ShouldContain("not to be trimmed by hand");
    }

    [Fact]
    public async Task GetPrompt_DeriveSpec_RoutesToTheGrammar()
    {
        string text = await ServedRecipeAsync();

        // Assert — the authoring reference is a condensed subset of the published grammar; the recipe
        // must say where the full vocabulary lives, or an agent mid-derive has nowhere to go past the
        // subset but improvisation.
        text.ShouldContain("https://github.com/andypgray/sliderule/blob/main/GRAMMAR.md");
    }

    [Fact]
    public void DeriveSpec_LoadsEmbeddedRecipe_NonTrivial()
    {
        // A rename of the .md or its manifest id would otherwise surface only when a client calls
        // prompts/get; this load-time assertion turns manifest-id drift into a test failure instead.
        ArchPrompts.DeriveSpec()
            .Length.ShouldBeGreaterThan(500);
    }

    [Fact]
    public void DeriveSpec_StaysUnderTheServedBodyCeiling()
    {
        // The failure this catches is the free append: a paragraph added because it seemed worth saying,
        // to a document nothing was measuring, that every session then pays for at kickoff whether or not
        // it ever reads the paragraph.
        ArchPrompts.DeriveSpec()
            .Length.ShouldBeLessThanOrEqualTo(ServedBodyCeiling);
    }

    [Fact]
    public void DeriveSpec_ServesTheVocabularyTableWithoutItsMarkers()
    {
        // Arrange: the committed recipe's table, which the vocabulary sync gate holds to the shipped API.
        string block = ManagedBlock.ExtractBody(RepoRoot.ReadText(DocPaths.DeriveSpecPrompt))!;
        block.ShouldNotBeEmpty("derive-spec.md carries no vocabulary table between its markers.");

        // Act
        string served = ArchPrompts.DeriveSpec();

        // Assert
        TextNormalization.Occurrences(served, block)
            .ShouldBe(1, "the served recipe must carry the committed vocabulary table exactly once");
        served.ShouldNotContain(ManagedBlock.BeginMarker);
        served.ShouldNotContain(ManagedBlock.EndMarker);
    }

    // One prompts/get over a host of the fact's own, disposed before the caller reads the result it returns.
    private static async Task<GetPromptResult> GetDeriveSpecAsync()
    {
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(Binding, Ct);

        return await harness.Client.GetPromptAsync(ArchPrompts.DeriveSpecName, cancellationToken: Ct);
    }

    // The recipe a prompts/get serves, as the text of its message.
    private static async Task<string> ServedRecipeAsync()
    {
        GetPromptResult result = await GetDeriveSpecAsync();

        return result.ShouldHaveTextContent();
    }

    // The recipe's one fence carrying the receipt, failing when none does or several do.
    private static QuotedFence ShouldFindReceiptFence()
    {
        return QuotedFence.ShouldFindOne(
            DocPaths.DeriveSpecPrompt, ReceiptMarker, "where the receipt's example should be exactly one");
    }

    // The one line of the receipt fence that the pattern reads whole, failing when none does or several do.
    private static Match ShouldFindReceiptLine(QuotedFence fence, Regex pattern)
    {
        List<Match> matches = fence.Lines
            .Select(line => pattern.Match(line))
            .Where(static match => match.Success)
            .ToList();

        return matches.ShouldHaveSingleItem($"the receipt fence should carry exactly one line reading {pattern}");
    }

    private static int NumberIn(Match line, string group)
    {
        return int.Parse(line.Groups[group].Value);
    }
}
