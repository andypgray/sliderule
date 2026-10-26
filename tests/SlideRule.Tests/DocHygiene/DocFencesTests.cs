using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     Unit tests for <see cref="DocFences" />, over hand-built docs rather than the committed pages.
/// </summary>
public sealed class DocFencesTests
{
    [Fact]
    public void FencedBlocks_EachBlock_KeepsItsOwnLinesWithDocLineNumbers()
    {
        // Arrange: block identity and position at once — a sub-line takes its rule id from the header
        // above it in the same fence, and must still fail naming the doc line it sits on.
        string[] lines =
        [
            "prose above",
            "```text",
            "pass a/rule",
            "  grandfathered: 3",
            "```",
            "prose between",
            "~~~json",
            "\"id\": \"b/rule\"",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<IReadOnlyList<(string Text, int Number)>> blocks = DocFences.FencedBlocks(doc);

        // Assert
        blocks.Count.ShouldBe(2);
        blocks[0]
            .Select(line => line.Text)
            .ShouldBe(["pass a/rule", "  grandfathered: 3"]);
        blocks[0]
            .Select(line => line.Number)
            .ShouldBe([3, 4]);
        blocks[1]
            .ShouldHaveSingleItem()
            .Number.ShouldBe(8);
    }

    [Fact]
    public void FencedBlocks_UnclosedFence_KeepsTheRemainderAsItsFinalBlock()
    {
        // Arrange
        string doc = string.Join("\n", "```text", "first", "second");

        // Act
        IReadOnlyList<IReadOnlyList<(string Text, int Number)>> blocks = DocFences.FencedBlocks(doc);

        // Assert
        blocks.ShouldHaveSingleItem()
            .Select(line => line.Text)
            .ShouldBe(["first", "second"]);
    }

    [Fact]
    public void FencedLinesAndFences_AreTheSameScannerAsFencedBlocks()
    {
        // Arrange: a doc mixing fence characters, indentation and an empty block, on which the three views
        // would disagree if any of them scanned the doc with a loop of its own.
        string[] lines =
        [
            "intro",
            "  ```text",
            "  indented content",
            "  ```",
            "````",
            "``` not a close: shorter run",
            "````",
            "~~~",
            "~~~"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<IReadOnlyList<(string Text, int Number)>> blocks = DocFences.FencedBlocks(doc);

        // Assert
        DocFences.FencedLines(doc)
            .ShouldBe(blocks.SelectMany(block => block)
                .ToArray());
        DocFences.Fences(doc)
            .ShouldBe(blocks
                .Select(block => block.Select(line => line.Text)
                    .ToArray())
                .ToArray());
    }

    [Fact]
    public void TaggedBlocks_KeepEachFencesInfoString()
    {
        // Arrange: a tagged fence, an indented tilde fence padded both sides, a bare fence, and a longer run whose
        // info string carries a second word.
        string[] lines =
        [
            "```json",
            "{}",
            "```",
            "  ~~~text  ",
            "x",
            "  ~~~",
            "```",
            "bare",
            "```",
            "````csharp title",
            "y",
            "````"
        ];
        string doc = string.Join("\n", lines);

        // Act
        IReadOnlyList<DocFences.TaggedBlock> tagged = DocFences.TaggedBlocks(doc);

        // Assert
        tagged
            .Select(static block => block.Info)
            .ShouldBe(["json", "text", "", "csharp title"]);
        tagged
            .Select(static block => block.OpeningLine)
            .ShouldBe([1, 4, 7, 10]);
        DocFences.FencedBlocks(doc)
            .ShouldBe(tagged
                .Select(static block => block.Lines)
                .ToArray());
    }

    [Fact]
    public void TaggedBlocks_UnclosedFinalFence_KeepsItsInfoStringAndOpeningLine()
    {
        // Arrange
        string doc = string.Join("\n", "```json", "{}");

        // Act
        DocFences.TaggedBlock block = DocFences.TaggedBlocks(doc)
            .ShouldHaveSingleItem();

        // Assert
        block.Info.ShouldBe("json");
        block.OpeningLine.ShouldBe(1);
        block.Lines.ShouldBe([("{}", 2)]);
    }
}
