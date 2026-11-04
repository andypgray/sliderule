using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.DocHygiene.Walkthroughs;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The showcase's pin, its page's references to the fork, and the CI job that reads both. The gated classes
///     skip on every run that names no checkout, so these facts run everywhere and keep the job that does name one
///     pointed at the commit the page quotes.
/// </summary>
public sealed class ShowcasePinTests
{
    /// <summary>The classes that skip unless a checkout of the pinned fork is named.</summary>
    internal static readonly string[] ShowcaseGatedClasses =
    [
        nameof(ShowcaseReplayTests),
        nameof(ShowcaseExcerptTests)
    ];

    private const string GatedStep = "The showcase page's fences and excerpts match the pinned fork";

    private static readonly Regex CommitId = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

    // A path below the root in forward slashes: no leading slash, drive letter, backslash or '..' segment.
    private static readonly Regex RelativePath = new(@"^(?!/)(?![A-Za-z]:)(?!.*\\)(?!.*(^|/)\.\.(/|$)).+$", RegexOptions.CultureInvariant);

    // A Markdown link's target, or an autolink's.
    private static readonly Regex LinkTarget = new(@"\]\((?<url>[^)\s]+)\)|<(?<url>https?://[^>\s]+)>", RegexOptions.CultureInvariant);

    private static string ShowcaseFilter => string.Join("|", ShowcaseGatedClasses.Select(static name => $"FullyQualifiedName~{name}"));

    [Fact]
    public void Pin_NamesBothCommitsAndRelativePaths()
    {
        // Act
        ShowcasePin pin = ShowcaseCheckout.Pin;
        List<string> findings = [];

        // Assert
        foreach ((string what, ShowcaseRepository repository) in new[] { ("fork", pin.Fork), ("upstream", pin.Upstream) })
        {
            if (!repository.Repository.StartsWith("https://github.com/", StringComparison.Ordinal))
                findings.Add($"the {what} repository '{repository.Repository}' is not a GitHub URL.");
            if (!CommitId.IsMatch(repository.Commit)) findings.Add($"the {what} commit '{repository.Commit}' is not a full commit id.");
        }

        if (string.IsNullOrWhiteSpace(pin.Fork.Tag)) findings.Add("the fork names no tag, which is what keeps its commit reachable.");

        findings.AddRange(new[] { pin.Solution, pin.SpecProject }
            .Concat(pin.Declared.UnsupportedProjects)
            .Where(path => !RelativePath.IsMatch(path))
            .Select(static path => $"'{path}' is not a forward-slash path relative to the checkout's root."));

        findings.ShouldReportNothing($"{ShowcaseCheckout.PinPath} is malformed");
    }

    [Fact]
    public void ShowcasePage_ReferencesTheForkOnlyAtItsPin()
    {
        // Arrange
        ShowcasePin pin = ShowcaseCheckout.Pin;
        string page = RepoRoot.ReadText(DocPaths.ShowcaseReadme);
        string[] pinned = [pin.Fork.Commit, pin.Fork.Tag!];

        // Act: a link to the fork's moving branch would show a reader a tree no fence was checked against.
        List<string> links = LinkTarget.Matches(page)
            .Select(static match => match.Groups["url"].Value)
            .Where(url => url.StartsWith(pin.Fork.Repository, StringComparison.Ordinal))
            .ToList();
        List<string> unpinned = links.Where(url => !pinned.Any(reference => NamesSegment(url, reference)))
            .Select(static url => $"{url} names neither the pinned commit nor its tag.")
            .ToList();
        if (links.Count == 0) unpinned.Add($"{DocPaths.ShowcaseReadme} links the fork nowhere, so this fact holds over nothing.");

        var clone = $"git clone --branch {pin.Fork.Tag} {pin.Fork.Repository}.git";
        if (!page.Contains(clone, StringComparison.Ordinal)) unpinned.Add($"the page's commands no longer run '{clone}'.");

        // Assert
        unpinned.ShouldReportNothing($"{DocPaths.ShowcaseReadme} sends its reader somewhere other than the pinned fork");
    }

    [Fact]
    public void GatedClasses_AreRunByTheShowcaseJobWithTheCheckoutNamed()
    {
        // Arrange
        string workflow = RepoRoot.ReadText(TrackedFiles.CiWorkflow);

        // Act
        IReadOnlyList<string> steps = QuoteSyncTests.WorkflowSteps(workflow, GatedStep);

        // Assert: the one place a checkout is fetched, so the one place the gated classes run at all. Without the
        // filter or the variable they skip there as they do everywhere else, in silence.
        string step = steps.ShouldHaveSingleItem($"{TrackedFiles.CiWorkflow} should have one step named '{GatedStep}'.");
        step.ShouldContain($"--filter \"{ShowcaseFilter}\"", customMessage: $"The step '{GatedStep}' no longer runs the showcase's gated classes.");
        step.ShouldContain(
            $"{ShowcaseCheckout.RootVariable}: ${{{{ env.SHOWCASE_ROOT }}}}",
            customMessage: $"The step '{GatedStep}' no longer names the checkout through {ShowcaseCheckout.RootVariable}.");
    }

    [Fact]
    public void ShowcaseAndExamplesFilters_SelectDisjointClasses()
    {
        // Arrange: a filter's ~ is a substring match, so a name in one filter that is part of a class name in the
        // other would run that class in both jobs, where one of them cannot have built what it reads.
        string[] testClasses = typeof(ShowcasePinTests).Assembly.GetTypes()
            .Where(static type => type is { IsClass: true, IsPublic: true, FullName: not null })
            .Select(static type => type.FullName!)
            .ToArray();

        // Act
        string[] showcase = Selected(testClasses, ShowcaseGatedClasses);
        string[] examples = Selected(testClasses, SelfCheckMeridianFenceTests.ExampleGatedClasses);

        // Assert
        showcase.Select(static name => name[(name.LastIndexOf('.') + 1)..])
            .ShouldBe(ShowcaseGatedClasses, ignoreOrder: true, "The showcase filter selects exactly its gated classes");
        examples.Select(static name => name[(name.LastIndexOf('.') + 1)..])
            .ShouldBe(SelfCheckMeridianFenceTests.ExampleGatedClasses, ignoreOrder: true, "The examples filter selects exactly its gated classes");
        showcase.Intersect(examples, StringComparer.Ordinal)
            .ShouldBeEmpty("A class selected by both filters runs in both jobs");
    }

    [Fact]
    public void ShowcaseGatedClasses_AreEveryClassThatCallsTheCheckoutGate()
    {
        // Arrange: a class is gated when its file calls the gate, and a test file is named for its class.
        const string gate = "ShowcaseCheckout.ShouldBeAtThePin(";

        // Act
        List<string> gated = TrackedFiles.CSharp
            .Where(static path => path.StartsWith("tests/", StringComparison.Ordinal))
            .Where(static path => RepoRoot.ReadText(path)
                .Contains(gate, StringComparison.Ordinal))
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .Where(static name => name != nameof(ShowcasePinTests))
            .ToList();

        // Assert
        gated.ShouldBe(
            ShowcaseGatedClasses,
            ignoreOrder: true,
            $"Every class that calls the showcase checkout's gate must be in the filter {TrackedFiles.CiWorkflow}'s showcase "
            + "job runs; one left out skips in every job with nothing red.");
    }

    // Whether the URL names the reference as a whole path segment or a compare endpoint, so a later tag that begins
    // with the pinned one does not pass for it.
    private static bool NamesSegment(string url, string reference)
    {
        return Regex.IsMatch(url, $@"(/|\.\.\.){Regex.Escape(reference)}(/|#|$)", RegexOptions.CultureInvariant);
    }

    // The test classes a filter of '~name' clauses selects.
    private static string[] Selected(IEnumerable<string> testClasses, IEnumerable<string> names)
    {
        return testClasses.Where(fullName => names.Any(name => fullName.Contains(name, StringComparison.Ordinal)))
            .ToArray();
    }
}
