using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit tests for <see cref="HeadingAnchors" />. They pin the anchors GitHub gives headings, the links the
///     scan reads and the ones it leaves alone, and how a link resolves, so that the gate over this repository's
///     files is known to red on a stranded link rather than only to pass on today's.
/// </summary>
public sealed class HeadingAnchorsTests
{
    private const string Blob = "https://github.com/andypgray/sliderule/blob";

    [Theory]
    [InlineData("`check --json` output", "check---json-output")]
    [InlineData(".NET Framework", "net-framework")]
    [InlineData("C# & .NET", "c--net")]
    [InlineData("The `List<T>` rule", "the-listt-rule")]
    [InlineData("SlideRule — the spec language (grammar)", "sliderule--the-spec-language-grammar")]
    [InlineData("`snake_case` ½ way", "snake_case--way")]
    [InlineData("5.7 Member vocabulary (member subjects, §4.6)", "57-member-vocabulary-member-subjects-46")]
    public void Anchors_Heading_TakesGitHubsSlug(string heading, string anchor)
    {
        // Act
        IReadOnlyList<string> anchors = HeadingAnchors.Anchors($"## {heading}\n");

        // Assert
        anchors.ShouldHaveSingleItem()
            .ShouldBe(anchor);
    }

    [Theory]
    [InlineData("[Linked](other.md) text", "linked-text")]
    [InlineData("An ![logo](logo.png) image", "an--image")]
    [InlineData("A <sup>tagged</sup> word", "a-tagged-word")]
    [InlineData("AT&amp;T", "att")]
    [InlineData("_Emphasis_ and snake_case", "emphasis-and-snake_case")]
    [InlineData("`__init__` stays", "__init__-stays")]
    [InlineData("Closing hashes ##", "closing-hashes")]
    public void Anchors_HeadingMarkup_SlugsTheRenderedText(string heading, string anchor)
    {
        // Act
        IReadOnlyList<string> anchors = HeadingAnchors.Anchors($"## {heading}\n");

        // Assert
        anchors.ShouldHaveSingleItem()
            .ShouldBe(anchor);
    }

    [Fact]
    public void Anchors_RepeatedHeading_TakesTheNextFreeNumber()
    {
        // Arrange
        const string doc = "## Example\n## Example\n### Example\n";

        // Act
        IReadOnlyList<string> anchors = HeadingAnchors.Anchors(doc);

        // Assert
        anchors.ShouldBe(["example", "example-1", "example-2"]);
    }

    [Fact]
    public void Anchors_NumberedSuffixAlreadyTaken_SkipsToTheNextFreeOne()
    {
        // Arrange: the third heading would be foo-1, which the first heading's own text already took.
        const string doc = "## Foo-1\n## Foo\n## Foo\n";

        // Act
        IReadOnlyList<string> anchors = HeadingAnchors.Anchors(doc);

        // Assert
        anchors.ShouldBe(["foo-1", "foo", "foo-2"]);
    }

    [Fact]
    public void Anchors_HashLinesInAFence_AreNotHeadings()
    {
        // Arrange
        const string doc =
            """
            ## Real heading
            ```bash
            # a shell comment
            ```
            #not-a-heading either
            """;

        // Act
        IReadOnlyList<string> anchors = HeadingAnchors.Anchors(doc);

        // Assert
        anchors.ShouldHaveSingleItem()
            .ShouldBe("real-heading");
    }

    [Fact]
    public void Links_LinkInAFenceInlineCodeOrComment_IsNotRead()
    {
        // Arrange
        const string doc =
            """
            See [the kept one](#kept) and `[code](#code)`.
            <!-- [a comment](#comment) -->
            ```text
            [fenced](#fenced)
            ```
            [kept]: other.md#definition
            """;

        // Act
        IReadOnlyList<HeadingAnchors.FragmentLink> links = HeadingAnchors.Links("page.md", doc);

        // Assert
        links.ShouldBe(
        [
            new HeadingAnchors.FragmentLink("page.md", 1, "#kept"),
            new HeadingAnchors.FragmentLink("page.md", 6, "other.md#definition")
        ]);
    }

    [Fact]
    public void Links_AbsoluteLinkInProse_TrimsTheSentencesPunctuation()
    {
        // Arrange
        var doc = $"Read {Blob}/main/README.md#installing.\n";

        // Act
        IReadOnlyList<HeadingAnchors.FragmentLink> links = HeadingAnchors.Links("page.md", doc);

        // Assert
        links.ShouldHaveSingleItem()
            .Target.ShouldBe($"{Blob}/main/README.md#installing");
    }

    [Fact]
    public void Links_InlineAbsoluteLink_IsReadOnce()
    {
        // Arrange: the inline form and the absolute form both find this link, on the same line.
        var doc = $"See [Installing]({Blob}/main/README.md#installing).\n";

        // Act
        IReadOnlyList<HeadingAnchors.FragmentLink> links = HeadingAnchors.Links("page.md", doc);

        // Assert
        links.ShouldHaveSingleItem()
            .Target.ShouldBe($"{Blob}/main/README.md#installing");
    }

    [Fact]
    public void Links_SourceFile_ReadsOnlyTheAbsoluteForm()
    {
        // Arrange
        var source =
            $"""
             // [not a link](README.md#usage)
             private const string HelpLink = "{Blob}/main/src/Pkg/README.md#diagnostics";
             """;

        // Act
        IReadOnlyList<HeadingAnchors.FragmentLink> links = HeadingAnchors.Links("src/Pkg/Descriptors.cs", source);

        // Assert
        links.ShouldBe([new HeadingAnchors.FragmentLink("src/Pkg/Descriptors.cs", 2, $"{Blob}/main/src/Pkg/README.md#diagnostics")]);
    }

    [Theory]
    [InlineData("#install")]
    [InlineData("guide.md#install")]
    [InlineData("./guide.md#install")]
    [InlineData("/guide.md#install")]
    [InlineData(Blob + "/main/guide.md#install")]
    public void Check_LinkToAHeadingThatExists_Passes(string target)
    {
        // Arrange
        IReadOnlyDictionary<string, IReadOnlySet<string>> anchorsOf = AnchorsOf(("guide.md", "# Guide\n## Install\n"));
        HeadingAnchors.FragmentLink link = new("guide.md", 3, target);

        // Act
        string? finding = HeadingAnchors.Check(link, anchorsOf);

        // Assert
        finding.ShouldBeNull();
    }

    [Fact]
    public void Check_ParentRelativeLink_ResolvesFromTheLinkingFilesDirectory()
    {
        // Arrange
        IReadOnlyDictionary<string, IReadOnlySet<string>> anchorsOf =
            AnchorsOf(("examples/App/README.md", "## Run it yourself\n"));
        HeadingAnchors.FragmentLink link = new("examples/App/hooks/storyboard.md", 9, "../README.md#run-it-yourself");

        // Act
        string? finding = HeadingAnchors.Check(link, anchorsOf);

        // Assert
        finding.ShouldBeNull();
    }

    [Fact]
    public void Check_RenamedHeading_RedsAtTheStrandedLink()
    {
        // Arrange: the pair agrees until the target's heading is renamed.
        const string linking = "See [how to install](guide.md#installing).\n";
        HeadingAnchors.FragmentLink link = HeadingAnchors.Links("index.md", linking)
            .ShouldHaveSingleItem();

        // Act
        string? before = HeadingAnchors.Check(link, AnchorsOf(("guide.md", "## Installing\n")));
        string? after = HeadingAnchors.Check(link, AnchorsOf(("guide.md", "## Install\n")));

        // Assert
        before.ShouldBeNull();
        after.ShouldBe("index.md:1: guide.md#installing has no heading #installing in guide.md.");
    }

    [Fact]
    public void Check_FileViewAtAnotherRef_AsksForMain()
    {
        // Arrange
        HeadingAnchors.FragmentLink link = new("page.md", 4, $"{Blob}/v0.8.0/README.md#installing");

        // Act
        string? finding = HeadingAnchors.Check(link, AnchorsOf(("README.md", "## Installing\n")));

        // Assert
        finding.ShouldBe(
            $"page.md:4: {Blob}/v0.8.0/README.md#installing views the file at 'v0.8.0'; link it on main, the branch "
            + "this gate reads.");
    }

    [Theory]
    [InlineData("missing.md#install", "points at missing.md, which is not a tracked markdown file.")]
    [InlineData("../../guide.md#install", "climbs above the repository root.")]
    public void Check_TargetThatIsNoTrackedMarkdown_Reds(string target, string reason)
    {
        // Arrange
        HeadingAnchors.FragmentLink link = new("page.md", 2, target);

        // Act
        string? finding = HeadingAnchors.Check(link, AnchorsOf(("guide.md", "## Install\n")));

        // Assert
        finding.ShouldBe($"page.md:2: {target} {reason}");
    }

    [Theory]
    [InlineData("https://github.com/andypgray/sliderule#readme")]
    [InlineData("https://github.com/andypgray/sliderule/issues/12#issuecomment-1")]
    [InlineData("https://github.com/andypgray/sliderule/releases/tag/v0.1.0#assets")]
    [InlineData("https://learn.microsoft.com/dotnet/core/tools/#options")]
    public void Check_LinkThatViewsNoFileHere_IsNotRead(string target)
    {
        // Arrange
        HeadingAnchors.FragmentLink link = new("page.md", 1, target);

        // Act
        string? finding = HeadingAnchors.Check(link, AnchorsOf(("README.md", "## Installing\n")));

        // Assert
        finding.ShouldBeNull();
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> AnchorsOf(params (string Path, string Text)[] docs)
    {
        return HeadingAnchors.AnchorIndex(docs);
    }
}
