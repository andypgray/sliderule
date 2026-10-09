using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The documentation gates over this repository's own published docs. The docs listed below name no
///     internal working references, and every tracked root-level doc is a held page or is named with the reason
///     it is not.
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

    public static TheoryData<string> InternalReferenceFreeDocsCases => [.. InternalReferenceFreeDocs];

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
