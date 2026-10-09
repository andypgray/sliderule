using Shouldly;
using Xunit;
using SlideRule.Internal;
using SlideRule.ModelFile;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Pins every refusal the reader can make: the two version arms and the whole malformed set, each
///     produced by one surgical edit of a file that read clean a moment earlier, so the line and column
///     each one reports are the real ones.
/// </summary>
/// <remarks>
///     <para>
///         The positions are asserted through <c>Describe()</c> rather than separately, because a refusal
///         that names the right thing at the wrong place is the failure mode worth catching and an
///         assertion on the message alone would not see it.
///     </para>
/// </remarks>
public sealed class ModelDocumentReaderRefusalTests
{
    [Fact]
    public void Read_ASchemaVersionAboveTheOneItUnderstands_RefusesAsNewer()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"schemaVersion\": 2,", "\"schemaVersion\": 3,");

        refusal.Kind.ShouldBe(ModelRefusalKind.Newer);
        refusal.Describe()
            .ShouldBe("line 2, column 20: schemaVersion 3 is newer than this reader understands (2).");
    }

    [Fact]
    public void Read_ASchemaVersionBelowTheOneItUnderstands_RefusesAsOlder()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"schemaVersion\": 2,", "\"schemaVersion\": 1,");

        refusal.Kind.ShouldBe(ModelRefusalKind.Older);
        refusal.Describe()
            .ShouldBe("line 2, column 20: schemaVersion 1 is older than this reader understands (2).");
    }

    [Fact]
    public void Read_AValueOfTheWrongKind_RefusesNamingTheKindItWanted()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"spec\": \"MyApp architecture\",", "\"spec\": 1,");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 3, column 11: \"spec\" must be a string.");

        // The offset counts from the start of the text: two code units for the opening line and
        // twenty-two for the version line put line 3 at 24, and column 11 is ten further on.
        refusal.Position.Offset.ShouldBe(34);
    }

    [Fact]
    public void Read_ARootThatIsNotAnObject_RefusesNamingTheRoot()
    {
        // The one shape no edit of a valid file can reach, and the only place `the root` is the subject.
        ModelFileSpecs.SampleText()
            .ShouldReadBack();

        ModelReadResult result = ModelDocumentReader.Read("[]");

        result.Document.ShouldBeNull();
        result.Refusal.ShouldNotBeNull()
            .Describe()
            .ShouldBe("line 1, column 1: the root must be an object.");
    }

    [Fact]
    public void Read_AMissingRequiredKey_RefusesNamingTheKindOfThingItIsMissingFrom()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "      \"because\": \"The domain must not know the web.\",\n", "");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 13, column 5: a rule has no \"because\".");
    }

    [Fact]
    public void Read_AnUnknownKey_RefusesRatherThanIgnoringIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "      \"id\": \"layering/domain-independent\",\n",
            "      \"id\": \"layering/domain-independent\",\n      \"extra\": 1,\n");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 15, column 7: a rule does not take \"extra\".");
    }

    [Fact]
    public void Read_AWordThatNamesNothing_RefusesNamingTheFamilyItLookedIn()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "\"id\": \"api/handlers-are-public\",\n      \"posture\": \"enforce\",",
            "\"id\": \"api/handlers-are-public\",\n      \"posture\": \"encourage\",");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 23, column 18: \"encourage\" is not a posture.");
    }

    [Fact]
    public void Read_ALayerNameNothingDeclared_RefusesNamingIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "{ \"name\": \"Domain\", \"globs\":", "{ \"name\": \"Realm\", \"globs\":");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 19, column 99: layer \"Domain\" is not declared above this point.");
    }

    [Fact]
    public void Read_ALayerNameDeclaredTwice_RefusesNamingIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "    { \"name\": \"Handlers\", \"definition\"", "    { \"name\": \"Domain\", \"definition\"");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 10, column 15: layer \"Domain\" is declared twice.");
    }

    [Fact]
    public void Read_ARuleIdUsedTwice_RefusesNamingIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "\"id\": \"time/no-ambient-clock\",", "\"id\": \"api/handlers-are-public\",");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 29, column 13: rule \"api/handlers-are-public\" appears twice.");
    }

    [Fact]
    public void Read_ARuleIdOfTheWrongShape_RefusesQuotingIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"id\": \"time/no-ambient-clock\",", "\"id\": \"Time/No Ambient\",");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 29, column 13: \"Time/No Ambient\" is not a rule id.");
    }

    [Fact]
    public void Read_ANamespaceGlobThatCanNeverMatch_RefusesInTheValidatorsOwnWords()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"glob\": \"MyApp.Web.*\"", "\"glob\": \"MyApp.*.Web.*\"");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 19, column 373: namespace pattern \"MyApp.*.Web.*\" has a trailing `.*` subtree "
                      + "operator but its literal prefix contains a `*`, which never matches; anchor the "
                      + "subtree on a literal prefix.");
    }

    [Fact]
    public void Read_ABlankTypeNameGlob_RefusesQuotingIt()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "{ \"node\": \"withSuffix\", \"suffix\": \"Dto\" }", "{ \"node\": \"withNameMatching\", \"glob\": \"\" }");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 19, column 241: type-name pattern \"\" is blank.");
    }

    [Fact]
    public void Read_ASelectionCarryingBothANounAndAUnion_RefusesNamingBothKeys()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "[{ \"union\": [", "[{ \"noun\": { \"node\": \"types\" }, \"union\": [");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 19, column 267: a selection takes exactly one of \"noun\" and \"union\".");
    }

    [Fact]
    public void Read_AFamilyNounCarryingBothArms_RefusesNamingBothKeys()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.TextOf(ModelFileSpecs.DefineFamily),
            "{ \"node\": \"each\", \"layers\": [\"Alpha\", \"Beta\"] }",
            "{ \"node\": \"each\", \"layers\": [\"Alpha\", \"Beta\"], \"projects\": {} }");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 18, column 81: an \"each\" noun takes exactly one of \"layers\" and \"projects\".");
    }

    [Fact]
    public void Read_ALayerCarryingBothGlobsAndADefinition_RefusesNamingBothKeys()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "{ \"name\": \"Domain\", \"globs\": [\"MyApp.Domain.*\"], ",
            "{ \"name\": \"Domain\", \"globs\": [\"MyApp.Domain.*\"], \"definition\": { \"noun\": { \"node\": \"types\" } }, ");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 9, column 5: a layer takes exactly one of \"globs\" and \"definition\".");
    }

    [Fact]
    public void Read_ALayerWhoseGlobsAreNotAnArray_RefusesRatherThanThrowing()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"globs\": [\"MyApp.Domain.*\"]", "\"globs\": \"MyApp.Domain.*\"");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 9, column 34: \"globs\" must be an array.");
    }

    [Fact]
    public void Read_ATripwireCarryingAConstraint_RefusesNamingTheRule()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.TextOf(ModelFileSpecs.DefineScope),
            "      \"scope\": { \"role\": \"tripwire\"",
            "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } },\n"
            + "      \"scope\": { \"role\": \"tripwire\"");

        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe("line 19, column 5: rule \"legacy/billing/tripwire\" carries a \"constraint\" unless its "
                      + "scope is a tripwire, and a tripwire carries none.");
    }

    [Fact]
    public void Read_TextThatIsNotJson_PassesTheParsersOwnRefusalThroughUnchanged()
    {
        string valid = ModelFileSpecs.SampleText();
        valid.ShouldReadBack();
        string edited = Edited(valid, "\"schemaVersion\": 2,", "\"schemaVersion\": 2");
        edited.ShouldNotBe(valid);
        StrictJson.TryParse(edited, out _, out StrictJsonError? parserError)
            .ShouldBeFalse();

        ModelReadResult result = ModelDocumentReader.Read(edited);

        result.Document.ShouldBeNull();
        ModelRefusal refusal = result.Refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe(parserError!.Describe());
        refusal.Position.Offset.ShouldBe(parserError.Position.Offset);
        refusal.Describe()
            .ShouldBe("line 3, column 3: expected ',' or '}', found '\"'.");
    }

    [Fact]
    public void Read_ANullText_ThrowsBecauseThatIsACallerBugRatherThanContent()
    {
        Should.Throw<ArgumentNullException>(() => ModelDocumentReader.Read(null!))
            .ParamName.ShouldBe("text");
    }

    // One surgical edit of a text that read clean, with the three guards a refusal row needs: that the
    // text read before the edit, that the edit changed it, and that what comes back is the reader's
    // refusal rather than the JSON parser's.
    private static ModelRefusal ShouldRefuseAfterEdit(string valid, string find, string replace)
    {
        valid.ShouldReadBack();

        string edited = Edited(valid, find, replace);
        edited.ShouldNotBe(valid);
        StrictJson.TryParse(edited, out _, out StrictJsonError? parserError)
            .ShouldBeTrue($"a model-level refusal is meant, but the text stopped being JSON: {parserError?.Describe()}");

        ModelReadResult result = ModelDocumentReader.Read(edited);
        result.Document.ShouldBeNull();
        return result.Refusal.ShouldNotBeNull();
    }

    // Replaces the one occurrence of `find`, and says so if there is not exactly one: an edit that landed
    // somewhere else would move the position every row above asserts.
    private static string Edited(string text, string find, string replace)
    {
        int at = text.IndexOf(find, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, $"the text should carry '{find}'");
        text.IndexOf(find, at + find.Length, StringComparison.Ordinal)
            .ShouldBe(-1, $"the text should carry '{find}' exactly once");

        return text.Substring(0, at) + replace + text.Substring(at + find.Length);
    }
}
