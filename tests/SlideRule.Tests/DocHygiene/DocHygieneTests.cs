using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The documentation gates over this repository's own published docs. The docs listed below name no
///     internal working references and are held to the house prose rules, and the inventory checks leave no
///     tracked reader-facing or root-level doc unaccounted for.
/// </summary>
public sealed class DocHygieneTests
{
    private static readonly string[] InternalReferenceFreeDocs =
    [
        "GRAMMAR.md",
        "README.md",
        "SELF-CHECK.md",
        "GETTING-STARTED.md",
        "CODING-AGENTS.md",
        "CLI.md",
        "ARCHITECTURE.md",
        "CHANGELOG.md",
        "AGENTS.md"
    ];

    private static readonly string[] BudgetDocs =
    [
        "README.md",
        "SELF-CHECK.md",
        "GETTING-STARTED.md",
        "CODING-AGENTS.md",
        "CLI.md",
        "ARCHITECTURE.md",
        "CONTRIBUTING.md",
        "SECURITY.md",
        "src/SlideRule/README.md",
        "src/SlideRule.Analyzers/README.md",
        "src/SlideRule.Packs.DotNet/README.md",
        "src/SlideRule.Xunit/README.md",
        "examples/README.md",
        "examples/Meridian/README.md",
        "examples/Meridian/ADOPTING.md",
        "examples/Meridian/ARCHITECTURE.md",
        "examples/Meridian/hooks/README.md",
        "examples/Meridian.Quoting/README.md",
        "examples/Meridian.Quoting/ARCHITECTURE.md",
        "examples/Meridian.Operations/README.md",
        "examples/Meridian.Operations/ARCHITECTURE.md",
        "examples/Meridian.Interchange/README.md",
        "examples/Meridian.Interchange/ARCHITECTURE.md",
        "hooks/README.md"
    ];

    /// <summary>
    ///     The reader-facing filenames a directory can carry. Both inventory checks below sweep for
    ///     these rather than for <c>README.md</c> alone, which is what keeps a per-directory doc of any
    ///     of these names from shipping outside the budget gate.
    /// </summary>
    private static readonly string[] BudgetedFileNames = ["README.md", "ARCHITECTURE.md", "ADOPTING.md"];

    /// <summary>
    ///     The spec-class docs: the language spec, the release history and the agent context file, whose managed
    ///     block renders the self-spec's own sentences. They keep the house voice and the stock-phrase ban but
    ///     not the reader budgets, which are sized for a page read top to bottom.
    /// </summary>
    private static readonly string[] SpecClassDocs = ["GRAMMAR.md", "CHANGELOG.md", "AGENTS.md"];

    private static readonly string[] VoiceDocs = [.. BudgetDocs, .. SpecClassDocs];

    /// <summary>
    ///     The root-level docs adopted verbatim rather than written here. The Contributor Covenant's text
    ///     is not this repository's to reword, so holding it to the house budgets would mean editing
    ///     somebody else's document; it is named rather than silently skipped.
    /// </summary>
    private static readonly string[] AdoptedRootDocs = ["CODE_OF_CONDUCT.md"];

    /// <summary>
    ///     References the published tree must never contain: the project's internal working-document
    ///     filenames, its working-notes directory, the labels its build was coordinated by, and the
    ///     digit-free vocabulary that coordination is narrated in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The digit-bearing and filename forms stay case-sensitive, which is what lets ordinary prose
    ///         share a word with a label. The narration and vocabulary forms are case-insensitive, because
    ///         narration is written in sentence case as often as not — so they are written to be precise
    ///         instead. The article in the narration form is the whole of its precision: a bare
    ///         <c>\bphase\b</c> also reds a concurrency term, while requiring
    ///         <em>this/the/that</em> in front of it reds only prose about a stage of the build.
    ///     </para>
    ///     <para>
    ///         The working-notes directory is matched only as a path of this repository: a relative path,
    ///         bare or climbing out with <c>..</c>, or a link into this repository's tree on GitHub at any
    ///         ref, raw links included. Other sites keep their documentation pages under a directory of the
    ///         same name, and a link to one of them follows a host or a path segment ending in a word
    ///         character, which is what the relative form declines to match after.
    ///     </para>
    /// </remarks>
    internal static readonly Regex[] InternalReferencePatterns =
    [
        new(@"\bPhase\s*-?\s*[0-9]"),
        new(@"\bWP-?\s*[0-9]"),
        new(@"\bWP\b"),
        new(@"\b(this|the|that)\s+(current|next|last|previous|final|first)?\s*phase\b", RegexOptions.IgnoreCase),
        new(@"\btask-[0-9]+\b", RegexOptions.IgnoreCase),
        new(@"\bwork package\b", RegexOptions.IgnoreCase),
        new(@"\btracker\b", RegexOptions.IgnoreCase),
        new(@"\bDESIGN\.md\b"),
        new(@"\bPLAN\.md\b"),
        new(@"\bPLAN-ARCHIVE\.md\b"),
        new(@"\bEXAMPLES\.md\b"),
        new(@"\bGUIDANCE-PACK\.md\b"),
        new(@"\bEVALUATION\.md\b"),
        new(@"\boutside-review\.md\b"),
        new(@"(?<!\w/)\bdocs/"),
        new(@"andypgray/sliderule/(?:(?:blob|tree|raw|edit)/[\w.-]+/|[\w.-]+/)?docs\b")
    ];

    /// <summary>
    ///     The development environment a published tree never names: a machine path, a home directory,
    ///     or a personal identity.
    /// </summary>
    /// <remarks>
    ///     These are shapes only. What can be matched only by name is read from an untracked local file,
    ///     for the reason <see cref="LocalPrivatePatterns" /> gives. Known-benign forms these are drawn
    ///     tightly enough to leave alone: an environment-variable root with no drive letter, and a
    ///     drive-rooted path to a machine-independent location such as an installed toolchain or a build
    ///     output directory.
    /// </remarks>
    internal static readonly Regex[] PrivateEnvironmentPatterns =
    [
        new(@"[A-Za-z]:[\\/](Users|source|repos)\b", RegexOptions.IgnoreCase),
        new(@"/home/[a-z]"),
        new(@"/Users/[a-z]"),
        new(@"%USERPROFILE%", RegexOptions.IgnoreCase),
        new(@"@gmail\.com", RegexOptions.IgnoreCase)
    ];

    /// <summary>
    ///     Prose about running the development tooling: the local workflow one maintainer follows,
    ///     which documents a machine's habits to strangers and dates the day the tool changes.
    /// </summary>
    /// <remarks>
    ///     One pattern rather than a list, spelled the way the publication review spells it, so the gate
    ///     and the review can never disagree about what counts. Case-insensitive because every token is
    ///     a product or command name that prose capitalises however it likes. What it does not cover is
    ///     the ordinary .NET suppression a source file carries above a deliberate site: that is source
    ///     rather than narration, and the source arm blanks those before it reads.
    /// </remarks>
    internal static readonly Regex[] DevToolingPatterns =
    [
        new(@"resharper|jetbrains|inspectcode|cleanupcode|\bjb\b|cleanup profile|dotsettings",
            RegexOptions.IgnoreCase)
    ];

    /// <summary>Off-voice descriptions of a codebase that the reader-facing docs never use.</summary>
    internal static readonly Regex[] HouseVoicePatterns =
    [
        new(@"\bmessy\b", RegexOptions.IgnoreCase),
        new(@"\bspaghetti\b", RegexOptions.IgnoreCase),
        new(@"\bball of mud\b", RegexOptions.IgnoreCase)
    ];

    /// <summary>
    ///     Stock phrases the reader-facing docs never use. Each vouches for a sentence, grades a part or
    ///     announces a point where a fact would do.
    /// </summary>
    /// <remarks>
    ///     Each pattern starts at a word boundary, which keeps a longer word ending in the same letters from
    ///     reading as the phrase: <em>there's the</em>, <em>understand the trap</em>, <em>learns its place</em>,
    ///     <em>dishonest</em>. None ends at one, because <em>honestly</em> and <em>honesty</em> are the phrase too.
    /// </remarks>
    internal static readonly Regex[] StockPhrasePatterns =
    [
        new(@"\bquietly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bgenuinely", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhonest", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bfrankly", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bearns? (its|their) (keep|place)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bworth (stating|noting)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bfull stop", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bthe real [a-z]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhere's (the|what|how|why)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bcarr(y|ies) the (argument|weight)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\band the trap", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    /// <summary>
    ///     The design terms the spec-class docs keep although they contain a stock phrase: <em>honesty boundary</em>
    ///     (or boundaries), <em>honesty cost</em> and <em>honesty is pinned</em>.
    /// </summary>
    /// <remarks>
    ///     The honesty boundary is GRAMMAR's named term for what a verb cannot see, stated beside each verb it
    ///     bounds and repeated in the release notes that shipped it, so it is a term rather than a tic. The
    ///     exemption is the whole phrase, never the word: every other <em>honest</em> in these docs reds,
    ///     released CHANGELOG entries included. Each pattern spans any run of non-word characters between its
    ///     words, because GRAMMAR wraps the term across a line break inside bold.
    /// </remarks>
    internal static readonly Regex[] DesignTermPatterns =
    [
        new(@"\bhonesty\W+(boundar(y|ies)|cost)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"\bhonesty\W+is\W+pinned", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
    ];

    public static TheoryData<string> InternalReferenceFreeDocsCases => [.. InternalReferenceFreeDocs];

    public static TheoryData<string> VoiceDocsCases => [.. VoiceDocs];

    public static TheoryData<string> BudgetDocsCases => [.. BudgetDocs];

    public static TheoryData<string> SpecClassDocsCases => [.. SpecClassDocs];

    [Theory]
    [MemberData(nameof(InternalReferenceFreeDocsCases))]
    public void PublishedDoc_HasNoInternalWorkingReferences(string relativePath)
    {
        // Arrange
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(content, InternalReferencePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} names internal working references");
    }

    [Theory]
    [MemberData(nameof(VoiceDocsCases))]
    public void ReaderDoc_HoldsHouseVoice(string relativePath)
    {
        // Arrange
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(content, HouseVoicePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} uses off-voice wording");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_StaysWithinProseBudgets(string relativePath)
    {
        // Arrange
        string prose = DocProse.BlankFences(RepoRoot.ReadText(relativePath));

        // Act
        int words = DocProse.CountWords(prose);
        int emDashes = DocProse.CountEmDashes(prose);
        int tics = DocProse.CountTics(prose);
        var emDashBudget = (int)Math.Ceiling(words / 1000.0);

        // Assert
        emDashes.ShouldBeLessThanOrEqualTo(
            emDashBudget,
            $"{relativePath}: {emDashes} em-dashes across {words} words of prose (budget {emDashBudget}).");
        tics.ShouldBeLessThanOrEqualTo(
            2,
            $"{relativePath}: {tics} deliberately/intentionally occurrences in prose (budget 2).");
    }

    [Fact]
    public void RootReadme_FirstScreenReadsAsPlainDeclaratives()
    {
        // Arrange
        string content = RepoRoot.ReadText("README.md");

        // Act
        IReadOnlyList<string> findings = FirstScreenShape.Findings(content);

        // Assert
        findings.ShouldReportNothing(
            "README.md's first screen is not plain declaratives (the fold before the first H2: a sentence "
            + "over 25 words, an em-dash, or more than one colon that does not end its line)");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_StaysWithinCadenceBudgets(string relativePath)
    {
        // Arrange
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> findings = ProseCadence.Findings(content);

        // Assert
        findings.ShouldReportNothing(
            $"{relativePath} runs past a cadence budget (at most 1 sentence in 5 over 25 words, and at most 1 in "
            + "10 with a colon mid-sentence; every sentence over a spent budget is listed)");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_UsesEachContrastFrameOnceAndOnePerSection(string relativePath)
    {
        // Arrange
        string content = RepoRoot.ReadText(relativePath);

        // Act
        IReadOnlyList<string> findings = ProseCadence.FrameFindings(content);

        // Assert
        findings.ShouldReportNothing(
            $"{relativePath} spends a contrast frame twice (\"rather than\", \"not just\", \"not only\", "
            + "\"not merely\" and \"isn't X, it's Y\" at most once each in the doc, and at most one frame under "
            + "any heading)");
    }

    [Theory]
    [MemberData(nameof(BudgetDocsCases))]
    public void ReaderDoc_UsesNoStockPhrases(string relativePath)
    {
        // Arrange: fenced blocks are exempt, as they are from the prose budgets, because a fence quotes a spec
        // or a tool whose wording is not the narrator's. Emptying their lines rather than dropping them keeps
        // every hit on the line number it has in the doc.
        string prose = DocProse.BlankFences(RepoRoot.ReadText(relativePath));

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(prose, StockPhrasePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} uses stock phrases in its prose");
    }

    [Theory]
    [MemberData(nameof(SpecClassDocsCases))]
    public void SpecDoc_UsesNoStockPhrasesBeyondItsDesignTerms(string relativePath)
    {
        // Arrange: fences are exempt for the reason the reader docs give, and each design term is blanked in
        // place, so every remaining hit keeps the line number it has in the doc.
        string withoutFences = DocProse.BlankFences(RepoRoot.ReadText(relativePath));
        string prose = DocProse.BlankMatches(withoutFences, DesignTermPatterns);

        // Act
        IReadOnlyList<string> hits = DocProse.FindForbidden(prose, StockPhrasePatterns);

        // Assert
        hits.ShouldReportNothing($"{relativePath} uses stock phrases outside its design terms");
    }

    [Fact]
    public void DesignTermExemptions_EachStillMatchesASpecDoc()
    {
        // Arrange
        string[] specProse = SpecClassDocs
            .Select(static path => DocProse.BlankFences(RepoRoot.ReadText(path)))
            .ToArray();

        // Act
        List<string> dead = DesignTermPatterns
            .Where(pattern => !specProse.Any(pattern.IsMatch))
            .Select(static pattern => $"{pattern} matches no spec-class doc's prose.")
            .ToList();

        // Assert
        dead.ShouldReportNothing("These design-term exemptions no longer exempt anything");
    }

    [Fact]
    public void ExampleAndPackageReaderDocs_AreAllInsideTheBudgetGate()
    {
        // Arrange
        List<string> uncovered = new();

        // Act: every immediate child of examples/ and src/ that carries a top-level reader doc must be
        // budgeted, so a new example or package doc cannot slip past the gate unnoticed.
        foreach (string parent in new[] { "examples", "src" })
        {
            string parentDirectory = RepoRoot.Absolute(parent);
            if (!Directory.Exists(parentDirectory)) continue;

            foreach (string childDirectory in Directory.GetDirectories(parentDirectory))
            foreach (string fileName in BudgetedFileNames)
            {
                string readerDoc = Path.Combine(childDirectory, fileName);
                if (!File.Exists(readerDoc)) continue;

                string relativePath = RepoRoot.Relative(readerDoc);
                if (!BudgetDocs.Contains(relativePath)) uncovered.Add(relativePath);
            }
        }

        // Assert
        uncovered.ShouldReportNothing("Reader doc(s) under examples/ or src/ are outside the budgeted set");
    }

    [Fact]
    public void EveryTrackedReaderDoc_IsInsideTheBudgetGate()
    {
        // Act: the walk above descends one level under examples/ and src/, which is how a reader doc at
        // a new top-level directory stayed invisible to it. Asking git for the set closes that blind
        // spot without another directory name to keep up to date.
        List<string> uncovered = TrackedFiles.All
            .Where(static path => BudgetedFileNames.Contains(Path.GetFileName(path)))
            .Where(static path => !BudgetDocs.Contains(path))
            .ToList();

        // Assert
        uncovered.ShouldReportNothing("Tracked reader doc(s) are outside the budgeted set");
    }

    /// <summary>
    ///     Pins that every doc git tracks beside the landing page is inside a gate: budgeted, or at least
    ///     held to the house voice as a spec-class document, or named above as adopted.
    /// </summary>
    /// <remarks>
    ///     The two inventory checks above sweep for <see cref="BudgetedFileNames" />, so a root-level doc
    ///     under any other name is invisible to them and ships outside every prose gate with no failure
    ///     anywhere. Git already knows the landing page's siblings, so nothing here needs keeping up to
    ///     date but the two lists it reads.
    /// </remarks>
    [Fact]
    public void EveryTrackedRootDoc_IsBudgetedVoicedOrNamedAsAdopted()
    {
        // Arrange
        HashSet<string> gated = [.. VoiceDocs, .. AdoptedRootDocs];

        // Act
        List<string> uncovered = TrackedFiles.Markdown
            .Where(static path => !path.Contains('/'))
            .Where(path => !gated.Contains(path))
            .ToList();
        List<string> dead = AdoptedRootDocs
            .Where(static path => !TrackedFiles.Markdown.Contains(path))
            .Select(static path => $"{path} is named as adopted but git tracks no such root-level doc.")
            .ToList();

        // Assert
        uncovered.ShouldReportNothing(
            "Tracked root-level doc(s) sit outside every prose gate; add each to BudgetDocs, or to "
            + "SpecClassDocs when it is a spec-class document, or name it in AdoptedRootDocs");
        dead.ShouldReportNothing("These adopted-doc exemptions no longer name a tracked root-level doc");
    }

    /// <summary>
    ///     Pins that every doc git tracks beside the landing page is a held page, every fence of which must be
    ///     registered, or is named in <see cref="DocPaths.UnheldRootDocs" /> with the reason it is not.
    /// </summary>
    /// <remarks>
    ///     A tracked root-level doc the list does not name is held by construction, so what this holds is the
    ///     named list itself: an entry git does not track, and one with a blank reason, red, because each leaves
    ///     the inventory saying something the tree does not.
    /// </remarks>
    [Fact]
    public void EveryTrackedRootDoc_IsAHeldPageOrNamedWithItsReason()
    {
        // Arrange
        HashSet<string> rootDocs = [.. TrackedFiles.Markdown.Where(static path => !path.Contains('/'))];

        // Act
        IEnumerable<string> dead = DocPaths.UnheldRootDocs
            .Where(entry => !rootDocs.Contains(entry.Doc))
            .Select(static entry => $"{entry.Doc} is named as unheld, but git tracks no such root-level doc.");
        IEnumerable<string> unexplained = DocPaths.UnheldRootDocs
            .Where(static entry => string.IsNullOrWhiteSpace(entry.Reason))
            .Select(static entry => $"{entry.Doc} is named as unheld with no reason; say why its fences are not held.");
        List<string> findings = [.. dead, .. unexplained];

        // Assert
        findings.ShouldReportNothing(
            "The root-level docs are not all accounted for; each is a held page with every fence registered, "
            + "or is named in DocPaths.UnheldRootDocs with the reason it is not");
    }
}
