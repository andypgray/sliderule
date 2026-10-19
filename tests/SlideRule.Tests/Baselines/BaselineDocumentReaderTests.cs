using Shouldly;
using SlideRule.Baselines;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     Every refusal the baseline reader can make, and the two files it accepts: a current one and a
///     legacy one. Each row pins the whole detail sentence, which of the four families it belongs to, the
///     rule and entry it names, and where in the text it sits.
/// </summary>
/// <remarks>
///     <para>
///         The position is asserted through <see cref="ShouldSitAt" />, which is handed the text of the
///         offending thing rather than a line and a column: a row written as two numbers would hold
///         whatever the walk happened to do.
///     </para>
///     <para>
///         The all-zero seals the malformed rows carry are never reached: an
///         entry this broken is refused while it is being classified or its values read, which is what
///         the pinned details say — and a row that did reach the comparison would refuse as a seal
///         mismatch rather than pass.
///     </para>
/// </remarks>
public sealed class BaselineDocumentReaderTests
{
    private const string Rule = "data/x";
    private const string ZeroSeal = "0000000000000000";

    [Fact]
    public void Read_TextThatIsNotJson_PassesTheParsersOwnRefusalThroughUnchanged()
    {
        BaselineRefusal refusal = ShouldRefuse(
            "{ this is not json", BaselineRefusalKind.Syntax, "expected a key or '}', found 't'.", "this");

        refusal.RuleId.ShouldBeNull();
        refusal.EntryIdentity.ShouldBeNull();
    }

    [Fact]
    public void Read_ARootThatIsNotAnObject_RefusesNamingTheRoot()
    {
        ShouldRefuse("[]", BaselineRefusalKind.Malformed, "the root must be a JSON object.", "[");
    }

    [Fact]
    public void Read_NoSchemaVersion_ReportsTheMissingPropertyAgainstTheObjectThatLacksIt()
    {
        // The version leads the read, so it is fetched before the envelope is known — and a file without
        // one has to report the missing property rather than fall out of the walk.
        const string text = "{\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "missing property 'schemaVersion'.", "{\n  \"rules\"");
    }

    [Fact]
    public void Read_ALegacyFileWithNoDigest_ReportsTheMissingProperty()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 1,\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "missing property 'digest'.", "{\n  \"schemaVersion\"");
    }

    [Fact]
    public void Read_ARuleSectionWithNoEntries_ReportsTheMissingPropertyAgainstTheSection()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 2,\n"
                            + "  \"rules\": {\n"
                            + "    \"data/x\": {}\n"
                            + "  }\n"
                            + "}\n";

        // A section is not a rule-level complaint: the sentence names the key, not the rule, so the
        // refusal carries no rule ID either.
        BaselineRefusal refusal = ShouldRefuse(
            text, BaselineRefusalKind.Malformed, "missing property 'entries'.", "{}");
        refusal.RuleId.ShouldBeNull();
    }

    [Fact]
    public void Read_ANonIntegerSchemaVersion_RefusesAtTheValue()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": \"2\",\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "schemaVersion must be an integer.", "\"2\"");
    }

    [Fact]
    public void Read_AnUnsupportedSchemaVersion_NamesTheRangeAndDescribesItsPosition()
    {
        // 3 rather than 2: the reader accepts both shipped versions, so the refusal has to be pinned
        // against one that does not exist yet.
        const string text = "{\n"
                            + "  \"schemaVersion\": 3,\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        BaselineRefusal refusal = ShouldRefuse(
            text, BaselineRefusalKind.Malformed, "unsupported schemaVersion 3 (expected 1 or 2).", "3,");

        // Describe() pinned once, because every caller that prints a refusal prints this shape.
        refusal.Describe()
            .ShouldBe("line 2, column 20: unsupported schemaVersion 3 (expected 1 or 2).");
    }

    [Fact]
    public void Read_AnUnknownRootProperty_RefusesAtThePropertyName()
    {
        // The name rather than the value beside it: the name is the word the reader is looking for.
        const string text = "{\n"
                            + "  \"schemaVersion\": 2,\n"
                            + "  \"rules\": {},\n"
                            + "  \"surprise\": true\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "unknown property 'surprise'.", "\"surprise\"");
    }

    [Fact]
    public void Read_ANonStringDigest_RefusesAtTheValue()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 1,\n"
                            + "  \"digest\": 3,\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "digest must be a string.", "3,");
    }

    [Fact]
    public void Read_ADigestThatIsNotSixtyFourLowercaseHex_RefusesAtTheValue()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 1,\n"
                            + "  \"digest\": \"abc\",\n"
                            + "  \"rules\": {}\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "digest must be 64 lowercase hex characters.", "\"abc\"");
    }

    [Fact]
    public void Read_RulesThatAreNotAnObject_RefusesAtTheValue()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 2,\n"
                            + "  \"rules\": []\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "'rules' must be an object.", "[]");
    }

    [Fact]
    public void Read_ARuleSectionThatIsNotAnObject_NamesTheRule()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 2,\n"
                            + "  \"rules\": {\n"
                            + "    \"data/x\": []\n"
                            + "  }\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "rule 'data/x' must be an object.", "[]", Rule);
    }

    [Fact]
    public void Read_EntriesThatAreNotAnArray_NamesTheRule()
    {
        const string text = "{\n"
                            + "  \"schemaVersion\": 2,\n"
                            + "  \"rules\": {\n"
                            + "    \"data/x\": {\n"
                            + "      \"entries\": {}\n"
                            + "    }\n"
                            + "  }\n"
                            + "}\n";

        ShouldRefuse(text, BaselineRefusalKind.Malformed, "rule 'data/x' entries must be an array.", "{}", Rule);
    }

    // Every refusal an entry can make, each against the same envelope: the detail names the rule the
    // entry sits under, and the position is the offending value except where the whole entry is what is
    // wrong, which is the two shape refusals and the missing seal.
    [Theory]
    [InlineData("\"x\"", "rule 'data/x' has a non-object entry.", "\"x\"")]
    [InlineData("{ \"source\": \"T:A\", \"subject\": \"T:B\", \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has an entry that is neither {source, target} nor {subject}.", "{ \"source\"")]
    [InlineData("{ \"subject\": \"T:A\", \"siteCount\": 2, \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has a 'siteCount' on a subject entry — only edge entries carry one.", "2, \"seal\"")]
    [InlineData("{ \"source\": \"T:A\", \"target\": \"T:B\" }", "rule 'data/x' has an entry with no 'seal'.",
        "{ \"source\"")]
    [InlineData("{ \"source\": \"\", \"target\": \"T:B\", \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has an empty or non-string 'source'.", "\"\"")]
    [InlineData("{ \"subject\": \"T:A\", \"because\": 3, \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has an empty or non-string 'because'.", "3,")]
    [InlineData("{ \"source\": \"T:A\", \"target\": \"T:B\", \"siteCount\": 0, \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has a 'siteCount' that is not an integer of at least 1.", "0, \"seal\"")]
    [InlineData("{ \"subject\": \"T:A\", \"because\": \"   \", \"seal\": \"" + ZeroSeal + "\" }",
        "rule 'data/x' has a blank or multi-line 'because'.", "\"   \"")]
    [InlineData("{ \"subject\": \"T:A\", \"seal\": 16 }", "rule 'data/x' has a non-string 'seal'.", "16")]
    [InlineData("{ \"subject\": \"T:A\", \"seal\": \"abc\" }",
        "rule 'data/x' has a 'seal' that is not 16 lowercase hex characters.", "\"abc\"")]
    public void Read_AMalformedEntry_NamesTheRuleAndPointsAtWhatIsWrong(string entryJson, string detail, string at)
    {
        BaselineRefusal refusal = ShouldRefuse(EntryDoc(entryJson), BaselineRefusalKind.Malformed, detail, at, Rule);

        // Only a seal mismatch has an entry to name: everything above is refused before the entry is one.
        refusal.EntryIdentity.ShouldBeNull();
    }

    [Fact]
    public void Read_AHandEditedEntry_RefusesAsASealMismatchNamingTheEntryAndPointingAtIt()
    {
        string composed = BaselineComposer.Compose(Rule, BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db"));
        string tampered = composed.Replace("T:App.Web.Old", "T:App.Web.Hacked");

        BaselineRefusal refusal = ShouldRefuse(
            tampered, BaselineRefusalKind.SealMismatch,
            "the entry 'T:App.Web.Hacked -> T:App.Data.Db' under rule 'data/x' does not match its seal.",
            "{ \"source\"", Rule, "T:App.Web.Hacked -> T:App.Data.Db");

        refusal.Describe()
            .ShouldStartWith("line 6, column 9: ");
    }

    [Fact]
    public void Read_AHandEditedSubjectEntry_NamesTheSubjectAsTheEntry()
    {
        // A subject entry has no arrow to print, so the identity is the one symbol it keys.
        string composed = BaselineComposer.Compose("shape/x", BaselineEntry.ForSubject("T:App.Legacy.Thing"));
        string tampered = composed.Replace("T:App.Legacy.Thing", "T:App.Legacy.Other");

        ShouldRefuse(
            tampered, BaselineRefusalKind.SealMismatch,
            "the entry 'T:App.Legacy.Other' under rule 'shape/x' does not match its seal.",
            "{ \"subject\"", "shape/x", "T:App.Legacy.Other");
    }

    [Fact]
    public void Read_AHandEditedLegacyFile_RefusesAsADigestMismatchPointingAtTheDigest()
    {
        string composed = BaselineComposer.ComposeLegacy(Rule, BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db"));
        string tampered = composed.Replace("T:App.Web.Old", "T:App.Web.Hacked");
        string digest = DigestOf(tampered);

        BaselineRefusal refusal = ShouldRefuse(
            tampered, BaselineRefusalKind.LegacyDigestMismatch, "the digest does not match the entries.",
            "\"" + digest + "\"");

        // The whole file failed, so there is no one entry and no one rule to name.
        refusal.RuleId.ShouldBeNull();
        refusal.EntryIdentity.ShouldBeNull();
    }

    [Fact]
    public void Read_AValidCurrentFile_ReadsEverySectionWithItsCountsAndReasons()
    {
        string text = BaselineComposer.Compose(
            (Rule, [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithSiteCount(2)
                    .WithBecause("INC-1234"),
                BaselineEntry.ForEdge("T:App.Web.Older", "T:App.Data.Db")
            ]),
            ("legacy/billing/containment", [BaselineEntry.ForSubject("T:App.Legacy.Thing")]));

        BaselineDocument document = ShouldRead(text);

        document.Sections.Keys.OrderBy(key => key, StringComparer.Ordinal)
            .ShouldBe([Rule, "legacy/billing/containment"]);
        IReadOnlyList<BaselineEntry> edges = document.Sections[Rule];
        edges.ShouldBe([
            BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db"),
            BaselineEntry.ForEdge("T:App.Web.Older", "T:App.Data.Db")
        ]);
        edges.Select(entry => entry.SiteCount)
            .ShouldBe([2, null]);
        edges.Select(entry => entry.Because)
            .ShouldBe(["INC-1234", null]);
        document.Sections["legacy/billing/containment"]
            .ShouldBe([BaselineEntry.ForSubject("T:App.Legacy.Thing")]);
    }

    [Fact]
    public void Read_AValidLegacyFile_ReadsItsEntriesUncounted()
    {
        string text = BaselineComposer.ComposeLegacy(
            (Rule, [
                BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
                    .WithBecause("INC-1234")
            ]));

        BaselineDocument document = ShouldRead(text);

        IReadOnlyList<BaselineEntry> entries = document.Sections[Rule];
        entries.ShouldBe([BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")]);
        entries[0]
            .SiteCount.ShouldBeNull();
        entries[0]
            .Because.ShouldBe("INC-1234");
    }

    [Fact]
    public void Read_ANullText_ThrowsBecauseThatIsACallerBugRatherThanContent()
    {
        Should.Throw<ArgumentNullException>(() => BaselineDocumentReader.Read(null!))
            .ParamName.ShouldBe("text");
    }

    // The digest a legacy file carries, read back off its own envelope line.
    private static string DigestOf(string text)
    {
        const string key = "\"digest\": \"";
        int at = text.IndexOf(key, StringComparison.Ordinal) + key.Length;
        int end = text.IndexOf('"', at);
        return text.Substring(at, end - at);
    }

    private static string EntryDoc(string entryJson)
    {
        return "{\n"
               + "  \"schemaVersion\": 2,\n"
               + "  \"rules\": {\n"
               + "    \"data/x\": {\n"
               + "      \"entries\": [\n"
               + "        " + entryJson + "\n"
               + "      ]\n"
               + "    }\n"
               + "  }\n"
               + "}\n";
    }

    private static BaselineDocument ShouldRead(string text)
    {
        BaselineReadResult result = BaselineDocumentReader.Read(text);

        result.Refusal?.Describe()
            .ShouldBeNull();
        return result.Document.ShouldNotBeNull();
    }

    private static BaselineRefusal ShouldRefuse(
        string text, BaselineRefusalKind kind, string detail, string at, string? ruleId = null,
        string? entryIdentity = null)
    {
        BaselineReadResult result = BaselineDocumentReader.Read(text);

        result.Document.ShouldBeNull();
        BaselineRefusal refusal = result.Refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(kind);
        refusal.Detail.ShouldBe(detail);
        refusal.RuleId.ShouldBe(ruleId);
        refusal.EntryIdentity.ShouldBe(entryIdentity);
        ShouldSitAt(refusal, text, at);
        return refusal;
    }

    // The position contract: a refusal points at the offending thing's own first character. `at` is that
    // thing as the text spells it, and it has to occur exactly once, or a row could pass while pointing
    // at a different occurrence of the same words.
    private static void ShouldSitAt(BaselineRefusal refusal, string text, string at)
    {
        int offset = text.IndexOf(at, StringComparison.Ordinal);
        offset.ShouldBeGreaterThanOrEqualTo(0, $"the text should carry '{at}'");
        text.IndexOf(at, offset + at.Length, StringComparison.Ordinal)
            .ShouldBe(-1, $"the text should carry '{at}' exactly once");

        refusal.Position.Offset.ShouldBe(offset);
        refusal.Position.Line.ShouldBe(LineOf(text, offset));
        refusal.Position.Column.ShouldBe(ColumnOf(text, offset));
    }

    private static int LineOf(string text, int offset)
    {
        var line = 1;
        for (var i = 0; i < offset; i++)
            if (text[i] == '\n')
                line++;

        return line;
    }

    private static int ColumnOf(string text, int offset)
    {
        var column = 1;
        for (int i = offset - 1; i >= 0 && text[i] != '\n'; i--) column++;

        return column;
    }
}
