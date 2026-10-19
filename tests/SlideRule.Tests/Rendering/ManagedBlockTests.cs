using Shouldly;
using SlideRule.Rendering;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The managed-block splice contract: create-absent, append-without-markers with dominant
///     line-ending selection, replace-strictly-between-markers preserving hand content above and
///     below byte-for-byte (CRLF and LF), idempotence, and the malformed marker
///     states. BOM preservation is proven at the CLI file-adapter level (the Core splicer is string-pure).
/// </summary>
public class ManagedBlockTests
{
    private const string Body = "line one\nline two";
    private const string Begin = "<!-- sliderule:begin -->";
    private const string End = "<!-- sliderule:end -->";

    [Fact]
    public void Splice_AbsentFile_IsBlockPlusSingleTrailingNewline_Lf()
    {
        ManagedBlock.Splice(null, Body)
            .ShouldBe("<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n");
    }

    [Fact]
    public void Splice_EmptyFile_IsTreatedAsAbsent()
    {
        ManagedBlock.Splice(string.Empty, Body)
            .ShouldBe(ManagedBlock.Splice(null, Body));
    }

    [Fact]
    public void Splice_WhitespaceOnlyFile_IsTreatedAsAbsent()
    {
        ManagedBlock.Splice("   \n\t\n", Body)
            .ShouldBe(ManagedBlock.Splice(null, Body));
    }

    [Fact]
    public void Splice_AppendIntoLfFile_UsesLfBlockAfterOneBlankLine()
    {
        const string existing = "# Title\n\nHand-written intro.\n";

        ManagedBlock.Splice(existing, Body)
            .ShouldBe(
                "# Title\n\nHand-written intro.\n\n" +
                "<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n");
    }

    [Fact]
    public void Splice_AppendIntoCrlfFile_UsesCrlfBlockAfterOneBlankLine()
    {
        const string existing = "# Title\r\n\r\nIntro.\r\n";

        ManagedBlock.Splice(existing, Body)
            .ShouldBe(
                "# Title\r\n\r\nIntro.\r\n\r\n" +
                "<!-- sliderule:begin -->\r\nline one\r\nline two\r\n<!-- sliderule:end -->\r\n");
    }

    [Fact]
    public void Splice_AppendIntoFileWithoutTrailingNewline_NormalizesToOneBlankLine()
    {
        ManagedBlock.Splice("# Title", Body)
            .ShouldBe(
                "# Title\n\n<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n");
    }

    [Fact]
    public void Splice_AppendCollapsesExtraTrailingNewlinesToOneBlankLine()
    {
        ManagedBlock.Splice("# Title\n\n\n\n", Body)
            .ShouldBe(
                "# Title\n\n<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n");
    }

    [Fact]
    public void Splice_ReplaceBetweenMarkers_Lf_PreservesHandContentAboveAndBelowByteForByte()
    {
        const string existing =
            "# Title\nAbove the block.\n\n" +
            "<!-- sliderule:begin -->\nOLD BODY\nsecond old line\n<!-- sliderule:end -->\n\n" +
            "Below the block.\n";

        ManagedBlock.Splice(existing, Body)
            .ShouldBe(
                "# Title\nAbove the block.\n\n" +
                "<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n\n" +
                "Below the block.\n");
    }

    [Fact]
    public void Splice_ReplaceBetweenMarkers_Crlf_PreservesHandContentAboveAndBelowByteForByte()
    {
        const string existing =
            "# Title\r\nAbove the block.\r\n\r\n" +
            "<!-- sliderule:begin -->\r\nOLD BODY\r\n<!-- sliderule:end -->\r\n\r\n" +
            "Below the block.\r\n";

        ManagedBlock.Splice(existing, Body)
            .ShouldBe(
                "# Title\r\nAbove the block.\r\n\r\n" +
                "<!-- sliderule:begin -->\r\nline one\r\nline two\r\n<!-- sliderule:end -->\r\n\r\n" +
                "Below the block.\r\n");
    }

    [Fact]
    public void Splice_IsIdempotent_OnFreshFile()
    {
        string once = ManagedBlock.Splice(null, Body);
        ManagedBlock.Splice(once, Body)
            .ShouldBe(once);
    }

    [Fact]
    public void Splice_IsIdempotent_OnFileWithHandContent_Lf()
    {
        const string existing = "# Title\nAbove.\n\nBelow.\n";
        string once = ManagedBlock.Splice(existing, Body);

        ManagedBlock.Splice(once, Body)
            .ShouldBe(once);
    }

    [Fact]
    public void Splice_IsIdempotent_OnFileWithHandContent_Crlf()
    {
        const string existing = "# Title\r\nAbove.\r\n\r\nBelow.\r\n";
        string once = ManagedBlock.Splice(existing, Body);

        ManagedBlock.Splice(once, Body)
            .ShouldBe(once);
    }

    [Fact]
    public void Splice_DominantEndingTie_ChoosesLf()
    {
        // One CRLF, one LF — a tie, which resolves to LF for the written block.
        ManagedBlock.Splice("a\r\nb\n", Body)
            .ShouldBe(
                "a\r\nb\n\n<!-- sliderule:begin -->\nline one\nline two\n<!-- sliderule:end -->\n");
    }

    [Fact]
    public void Splice_MarkersMatchedByTrimmedWholeLine_PreservingTheirIndentationByteForByte()
    {
        // Hand-indented markers still match (trimmed exact) and are preserved verbatim outside the body.
        const string existing =
            "intro\n   <!-- sliderule:begin -->\nOLD\n\t<!-- sliderule:end -->   \noutro\n";

        ManagedBlock.Splice(existing, Body)
            .ShouldBe(
                "intro\n   <!-- sliderule:begin -->\nline one\nline two\n\t<!-- sliderule:end -->   \noutro\n");
    }

    [Theory]
    [InlineData("<!-- sliderule:begin -->\nx\n", "without a matching")] // begin, no end
    [InlineData("x\n<!-- sliderule:end -->\n", "without a matching")] // end, no begin
    [InlineData("<!-- sliderule:end -->\nx\n<!-- sliderule:begin -->\n", "precedes")] // end before begin
    public void Splice_MalformedMarkers_ThrowWithoutWriting(string existing, string messageFragment)
    {
        var exception = Should.Throw<MalformedManagedBlockException>(() => ManagedBlock.Splice(existing, Body));
        exception.Message.ShouldContain(messageFragment);
    }

    // The duplicate-marker pins assert the whole message rather than a fragment, because the stray-marker
    // guidance sentence is part of it: the marker text must not appear anywhere else in the file.
    [Fact]
    public void Splice_DuplicateBeginMarker_ThrowsWithStrayMarkerGuidance()
    {
        var exception = Should.Throw<MalformedManagedBlockException>(() => ManagedBlock.Splice("<!-- sliderule:begin -->\na\n<!-- sliderule:begin -->\nb\n<!-- sliderule:end -->\n", Body));
        exception.Message.ShouldBe(
            "Malformed managed block: 2 '<!-- sliderule:begin -->' markers (expected exactly one). "
            + "The marker text must not appear anywhere else in the file, including in examples.");
    }

    [Fact]
    public void Splice_DuplicateEndMarker_ThrowsWithStrayMarkerGuidance()
    {
        var exception = Should.Throw<MalformedManagedBlockException>(() => ManagedBlock.Splice("<!-- sliderule:begin -->\na\n<!-- sliderule:end -->\nb\n<!-- sliderule:end -->\n", Body));
        exception.Message.ShouldBe(
            "Malformed managed block: 2 '<!-- sliderule:end -->' markers (expected exactly one). "
            + "The marker text must not appear anywhere else in the file, including in examples.");
    }

    [Fact]
    public void ExtractBody_NoMarkers_ReturnsNull()
    {
        ManagedBlock.ExtractBody("# Just prose\nno markers here\n")
            .ShouldBeNull();
    }

    [Fact]
    public void ExtractBody_RoundTripsTheSplicedBody_Lf()
    {
        string spliced = ManagedBlock.Splice(null, Body);
        ManagedBlock.ExtractBody(spliced)
            .ShouldBe(Body);
    }

    [Fact]
    public void ExtractBody_NormalizesCrlfBodyToLf()
    {
        string spliced = ManagedBlock.Splice("# Title\r\n", Body); // CRLF file → CRLF block
        ManagedBlock.ExtractBody(spliced)
            .ShouldBe(Body);
    }

    [Fact]
    public void ExtractBody_MalformedMarkers_Throws()
    {
        Should.Throw<MalformedManagedBlockException>(() => ManagedBlock.ExtractBody("<!-- sliderule:begin -->\nx\n"));
    }

    [Fact]
    public void ExtractBody_EmptyBodyBetweenAdjacentMarkers_ReturnsEmptyString()
    {
        // Adjacent markers (a hand-authored empty managed block) leave a zero-length region, which
        // StripOneTrailingNewline returns unchanged — there is no trailing newline to strip.
        ManagedBlock.ExtractBody(Begin + "\n" + End + "\n")
            .ShouldBe(string.Empty);
    }
}
