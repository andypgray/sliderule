using System.Text;
using Shouldly;
using SlideRule.Internal;
using Xunit;

namespace SlideRule.Tests.Internal;

/// <summary>
///     The strict JSON reader (<see cref="StrictJson" />): the grammar it admits, the refusals it
///     returns instead of throwing, and the position each refusal points at.
/// </summary>
/// <remarks>
///     Every refusal is asserted as a whole <see cref="JsonPosition.Describe" /> string rather than by
///     probing a code or a substring. The description is what a person reading a rejected file sees, so
///     the line, the column and the sentence are one pinned artefact; a change to any of them is a change
///     to the product and moves a row here deliberately.
/// </remarks>
public sealed class StrictJsonTests
{
    [Theory]
    [InlineData("", "line 1, column 1: expected a value, found the end of the text.")]
    [InlineData("  \n ", "line 2, column 2: expected a value, found the end of the text.")]
    [InlineData("null", "line 1, column 1: null is not allowed; an optional field is left out instead.")]
    [InlineData("{\"a\":null}", "line 1, column 6: null is not allowed; an optional field is left out instead.")]
    [InlineData("1.5", "line 1, column 2: a number must be an integer, with no fraction and no exponent.")]
    [InlineData("1e3", "line 1, column 2: a number must be an integer, with no fraction and no exponent.")]
    [InlineData("01", "line 1, column 2: a number may not start with a zero unless it is zero.")]
    [InlineData("-x", "line 1, column 2: expected a digit after '-', found 'x'.")]
    [InlineData("-", "line 1, column 2: expected a digit after '-', found the end of the text.")]
    [InlineData("\"abc", "line 1, column 1: the string is not closed before the end of the text.")]
    [InlineData("\"ab\\", "line 1, column 1: the string is not closed before the end of the text.")]
    [InlineData("\"a\nb\"", "line 1, column 3: a control character (U+000A) must be escaped inside a string.")]
    [InlineData("\"\\q\"", "line 1, column 2: '\\q' is not a JSON escape.")]
    [InlineData("\"\\u12G4\"", "line 1, column 2: '\\u' must be followed by four hex digits.")]
    [InlineData("\"\\u12", "line 1, column 2: '\\u' must be followed by four hex digits.")]
    [InlineData("\"\\uD83D\"",
        "line 1, column 2: the escape '\\uD83D' is one half of a surrogate pair and the other half is missing.")]
    [InlineData("\"\\ude00\"",
        "line 1, column 2: the escape '\\uDE00' is one half of a surrogate pair and the other half is missing.")]
    [InlineData("\"\\uD83D\\u0041\"",
        "line 1, column 2: the escape '\\uD83D' is one half of a surrogate pair and the other half is missing.")]
    [InlineData("{} x", "line 1, column 4: expected the end of the text, found 'x'.")]
    [InlineData("1 2", "line 1, column 3: expected the end of the text, found '2'.")]
    [InlineData("{\"a\":1,}", "line 1, column 8: expected a key, found '}'.")]
    [InlineData("[1,]", "line 1, column 4: expected a value, found ']'.")]
    [InlineData("{\"a\" 1}", "line 1, column 6: expected ':', found '1'.")]
    [InlineData("[1 2]", "line 1, column 4: expected ',' or ']', found '2'.")]
    [InlineData("{\"a\":1 \"b\":2}", "line 1, column 8: expected ',' or '}', found '\"'.")]
    [InlineData("{1:2}", "line 1, column 2: expected a key or '}', found '1'.")]
    [InlineData("[", "line 1, column 2: expected a value or ']', found the end of the text.")]
    [InlineData("tru", "line 1, column 1: expected a value, found 'tru'.")]
    [InlineData("True", "line 1, column 1: expected a value, found 'True'.")]
    [InlineData("\u0007", "line 1, column 1: expected a value, found U+0007.")]
    [InlineData("\uFEFF{}", "line 1, column 1: expected a value, found U+FEFF.")]
    [InlineData("// x", "line 1, column 1: expected a value, found '/'.")]
    [InlineData("{\r\n  \"a\": nul\r\n}", "line 2, column 8: expected a value, found 'nul'.")]
    public void TryParse_MalformedText_RefusesWithThePinnedDescription(string text, string description)
    {
        ShouldRefuse(text, description);
    }

    [Fact]
    public void TryParse_ADuplicateKey_CarriesThePositionItsDescriptionNames()
    {
        StrictJsonError error = ShouldRefuse(
            "{\"id\":1,\"id\":2}",
            "line 1, column 9: the key \"id\" appears twice in one object.");

        // The three position members are one point stated three ways, and a caller that slices the text
        // by Offset must land on the character the sentence points at.
        error.Position.Line.ShouldBe(1);
        error.Position.Column.ShouldBe(9);
        error.Position.Offset.ShouldBe(8);
    }

    [Fact]
    public void TryParse_OneContainerPastTheCap_RefusesAtTheBracketThatCrossesIt()
    {
        int past = StrictJson.MaxDepth + 1;
        string text = new string('[', past) + new string(']', past);

        ShouldRefuse(text, "line 1, column 65: the nesting is deeper than 64 levels.");
    }

    [Fact]
    public void TryParse_ObjectsAndArraysNested_CountTowardsTheOneCapTogether()
    {
        // Six characters per repetition, two containers opened by each, so thirty-two of them stand
        // exactly at the cap and the bracket after them is the sixty-fifth.
        string pairs = string.Concat(Enumerable.Repeat("{\"a\":[", StrictJson.MaxDepth / 2));
        string text = pairs + "[";

        ShouldRefuse(text, "line 1, column 193: the nesting is deeper than 64 levels.");
    }

    [Fact]
    public void TryParse_NestingExactlyAtTheCap_ReadsToAnEmptyInnermostArray()
    {
        string text = new string('[', StrictJson.MaxDepth) + new string(']', StrictJson.MaxDepth);

        StrictJsonValue value = ShouldRead(text);

        for (var open = 1; open < StrictJson.MaxDepth; open++)
        {
            value.ValueKind.ShouldBe(StrictJsonKind.Array);
            value = value.EnumerateArray().ShouldHaveSingleItem();
        }

        value.ValueKind.ShouldBe(StrictJsonKind.Array);
        value.EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public void TryParse_EveryEscape_ReadsTheCharacterEachOneStandsFor()
    {
        var text = "\"\\\" \\\\ \\/ \\b \\f \\n \\r \\t \\u0041 \\u00e9\"";

        string read = ShouldRead(text).GetString();

        read.ShouldBe("\" \\ / \b \f \n \r \t A \u00e9");
        read.Length.ShouldBe(19);
    }

    [Fact]
    public void TryParse_ASurrogatePairEscape_ReadsBothCodeUnitsInEitherCase()
    {
        string upper = ShouldRead("\"\\uD83D\\uDE00\"").GetString();
        string lower = ShouldRead("\"\\ud83d\\ude00\"").GetString();

        upper.Length.ShouldBe(2);
        upper[0].ShouldBe((char)0xD83D);
        upper[1].ShouldBe((char)0xDE00);
        lower.ShouldBe(upper);
    }

    [Fact]
    public void TryParse_RawNonAsciiInsideAString_PassesItThroughVerbatim()
    {
        var expected = "\u00e9\u2028";
        string text = "\"" + expected + "\"";

        ShouldRead(text).GetString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("7", 7)]
    [InlineData("-7", -7)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void TryParse_AnIntegerThatFits_ReadsAsANumberWithThatValue(string text, int expected)
    {
        StrictJsonValue value = ShouldRead(text);

        value.ValueKind.ShouldBe(StrictJsonKind.Number);
        value.TryGetInt32(out int read).ShouldBeTrue();
        read.ShouldBe(expected);
    }

    [Theory]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("123456789012345678901234567890")]
    public void TryParse_AnIntegerTooLargeForInt32_ReadsAsANumberThatSaysSoWithoutThrowing(string text)
    {
        StrictJsonValue value = ShouldRead(text);

        value.ValueKind.ShouldBe(StrictJsonKind.Number);
        value.TryGetInt32(out int read).ShouldBeFalse();
        read.ShouldBe(0);
    }

    [Fact]
    public void TryParse_TrueAndFalse_ReadAsTheirOwnKinds()
    {
        StrictJsonValue yes = ShouldRead("true");
        StrictJsonValue no = ShouldRead("false");

        yes.ValueKind.ShouldBe(StrictJsonKind.True);
        yes.GetBoolean().ShouldBeTrue();
        no.ValueKind.ShouldBe(StrictJsonKind.False);
        no.GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public void EnumerateObject_AnObject_YieldsItsMembersInFileOrder()
    {
        StrictJsonValue value = ShouldRead("{\"id\":1,\"name\":\"x\",\"on\":true}");

        value.ValueKind.ShouldBe(StrictJsonKind.Object);
        string[] names = value.EnumerateObject()
            .Select(member => member.Name)
            .ToArray();
        StrictJsonKind[] kinds = value.EnumerateObject()
            .Select(member => member.Value.ValueKind)
            .ToArray();

        names.ShouldBe(["id", "name", "on"]);
        kinds.ShouldBe([StrictJsonKind.Number, StrictJsonKind.String, StrictJsonKind.True]);
    }

    [Fact]
    public void TryGetProperty_AName_HitsOrdinallyAndMissesWithANullValue()
    {
        StrictJsonValue value = ShouldRead("{\"id\":7}");

        value.TryGetProperty("id", out StrictJsonValue? hit).ShouldBeTrue();
        hit.ShouldNotBeNull();
        hit.TryGetInt32(out int read).ShouldBeTrue();
        read.ShouldBe(7);

        value.TryGetProperty("nope", out StrictJsonValue? miss).ShouldBeFalse();
        miss.ShouldBeNull();

        // Ordinal and case-sensitive: a key differing only in case is a different key.
        value.TryGetProperty("Id", out StrictJsonValue? wrongCase).ShouldBeFalse();
        wrongCase.ShouldBeNull();
    }

    [Fact]
    public void GetProperty_AMissingName_ThrowsKeyNotFound()
    {
        StrictJsonValue value = ShouldRead("{\"id\":7}");

        Should.Throw<KeyNotFoundException>(() => value.GetProperty("nope"));
    }

    [Fact]
    public void TryParse_TheSameKeyInTwoDifferentObjects_Reads()
    {
        StrictJsonValue value = ShouldRead("[{\"id\":1},{\"id\":2}]");

        int[] ids = value.EnumerateArray()
            .Select(element => element.GetProperty("id"))
            .Select(ReadInt32)
            .ToArray();

        ids.ShouldBe([1, 2]);
    }

    [Fact]
    public void EnumerateArray_AnArray_YieldsItsElementsInFileOrder()
    {
        StrictJsonValue value = ShouldRead("[3,1,2]");

        int[] elements = value.EnumerateArray()
            .Select(ReadInt32)
            .ToArray();

        elements.ShouldBe([3, 1, 2]);
    }

    [Fact]
    public void TryParse_AnEmptyObjectOrArray_ReadsAsAnEmptyOne()
    {
        StrictJsonValue array = ShouldRead("[]");
        StrictJsonValue map = ShouldRead("{}");

        array.ValueKind.ShouldBe(StrictJsonKind.Array);
        array.EnumerateArray().ShouldBeEmpty();
        map.ValueKind.ShouldBe(StrictJsonKind.Object);
        map.EnumerateObject().ShouldBeEmpty();
    }

    [Fact]
    public void TryParse_WhitespaceBetweenEveryToken_ReadsTheSameDocument()
    {
        const string gap = " \t\r\n";
        string text = gap + "{" + gap + "\"a\"" + gap + ":" + gap + "[" + gap + "1" + gap + ","
                      + gap + "2" + gap + "]" + gap + "}" + gap;

        StrictJsonValue value = ShouldRead(text);

        int[] elements = value.GetProperty("a")
            .EnumerateArray()
            .Select(ReadInt32)
            .ToArray();

        elements.ShouldBe([1, 2]);
    }

    [Fact]
    public void TryParse_AMultiLineDocument_PositionsEveryValueWhereItStarts()
    {
        StrictJsonValue value = ShouldRead("{\n  \"a\": [1, \"x\"]\n}");

        StrictJsonProperty member = value.EnumerateObject().Single();
        member.Position.Line.ShouldBe(2);
        member.Position.Column.ShouldBe(3);
        member.Position.Offset.ShouldBe(4);

        // The member's own position is its name's opening quote; its value's is the bracket.
        member.Value.Position.Line.ShouldBe(2);
        member.Value.Position.Column.ShouldBe(8);
        member.Value.Position.Offset.ShouldBe(9);

        // An offset counts from the start of the text where a column restarts on every line.
        StrictJsonValue element = member.Value.EnumerateArray().Last();
        element.Position.Line.ShouldBe(2);
        element.Position.Column.ShouldBe(12);
        element.Position.Offset.ShouldBe(13);
    }

    [Fact]
    public void GetString_ANumber_ThrowsRatherThanRefusing()
    {
        StrictJsonValue value = ShouldRead("1");

        // A kind mismatch cannot come from a text that parsed, so it is the caller's mistake and not the
        // file's: an exception naming both kinds, never a StrictJsonError.
        var thrown = Should.Throw<InvalidOperationException>(() => value.GetString());
        thrown.Message.ShouldContain("String");
        thrown.Message.ShouldContain("Number");
    }

    [Fact]
    public void TryParse_WhateverTheWriterQuoted_ReadsBackTheSameString()
    {
        var controls = new StringBuilder();
        for (var c = 0; c <= 0x1F; c++) controls.Append((char)c);

        string[] written =
        [
            "a \" and a \\ in one string",
            controls.ToString(),
            "\u2028 and a pair \uD83D\uDE00"
        ];

        foreach (string value in written)
        {
            string quoted = JsonText.Quote(value);
            ShouldRead(quoted).GetString().ShouldBe(value);
        }
    }

    private static int ReadInt32(StrictJsonValue value)
    {
        value.TryGetInt32(out int read).ShouldBeTrue();
        return read;
    }

    private static StrictJsonValue ShouldRead(string text)
    {
        bool read = StrictJson.TryParse(text, out StrictJsonValue? root, out StrictJsonError? error);

        read.ShouldBeTrue(error?.Position.Describe(error.Reason));
        error.ShouldBeNull();
        root.ShouldNotBeNull();
        return root;
    }

    private static StrictJsonError ShouldRefuse(string text, string description)
    {
        bool read = StrictJson.TryParse(text, out StrictJsonValue? root, out StrictJsonError? error);

        read.ShouldBeFalse();
        root.ShouldBeNull();
        error.ShouldNotBeNull();
        error.Position.Describe(error.Reason).ShouldBe(description);
        return error;
    }
}
