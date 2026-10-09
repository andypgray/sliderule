using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over links that carry a <c>#fragment</c>. Every such link in a tracked markdown file, and every
///     absolute link into this repository written into any other tracked file outside the tests, must name a
///     heading of the file it points at, by the anchors GitHub gives headings. A renamed heading or a moved
///     section then fails the suite at every link it strands, instead of publishing a link that lands at the
///     top of the page.
/// </summary>
/// <remarks>
///     The files outside the markdown are read whole rather than through the comment mask, because the links
///     they carry are string literals, which the mask blanks. The analyzers' help links, which a build prints
///     beside every diagnostic, are the reason the scan reaches C#. The tests are left out because their strings
///     are inputs to other checks, not links anybody follows.
/// </remarks>
public sealed class HeadingAnchorGateTests
{
    private static readonly Lazy<IReadOnlyList<HeadingAnchors.FragmentLink>> MarkdownLinks =
        new(() => ScanAll(TrackedFiles.Markdown));

    private static readonly Lazy<IReadOnlyList<HeadingAnchors.FragmentLink>> OtherLinks =
        new(() => ScanAll(TrackedFiles.CSharp.Concat(TrackedFiles.NonSourceText)
            .Where(static path => !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Where(static path => !path.StartsWith("tests/", StringComparison.Ordinal))));

    [Fact]
    public void EveryFragmentLink_ResolvesToAHeadingOfItsTarget()
    {
        // Arrange
        IReadOnlyDictionary<string, IReadOnlySet<string>> anchorIndex = HeadingAnchors.AnchorIndex(
            TrackedFiles.Markdown.Select(static path => (path, RepoRoot.ReadText(path))));

        // Act
        List<string> stranded = MarkdownLinks.Value.Concat(OtherLinks.Value)
            .Select(link => HeadingAnchors.Check(link, anchorIndex))
            .OfType<string>()
            .ToList();

        // Assert
        stranded.ShouldReportNothing(
            "These links name no heading of the file they point at, by the anchors GitHub gives headings; point "
            + "each at a heading that exists, or restore the heading it names");
    }

    [Fact]
    public void MarkdownScan_FindsFragmentLinks()
    {
        // Assert: a scan that stopped recognising links would turn the gate green over nothing.
        MarkdownLinks.Value.ShouldNotBeEmpty("The scan found no fragment link in any tracked markdown file.");
    }

    [Fact]
    public void ScanOutsideTheMarkdown_FindsFragmentLinks()
    {
        // Assert: the analyzers' help links are string literals in C#, and nothing else would notice this half of
        // the scan reading nothing.
        OtherLinks.Value.ShouldNotBeEmpty(
            "The scan found no fragment link outside the markdown, where the analyzers' help links should be.");
    }

    private static IReadOnlyList<HeadingAnchors.FragmentLink> ScanAll(IEnumerable<string> paths)
    {
        return paths
            .SelectMany(static path => HeadingAnchors.Links(path, RepoRoot.ReadText(path)))
            .ToList();
    }
}
