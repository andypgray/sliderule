using Shouldly;
using SlideRule.Internal;
using SlideRule.ModelFile;
using SlideRule.Tests.TestSupport;
using Xunit;

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
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"schemaVersion\": 2,", "\"schemaVersion\": 3,",
            ModelRefusalKind.Newer, "line 2, column 20: schemaVersion 3 is newer than this reader understands (2).");
    }

    [Fact]
    public void Read_ASchemaVersionBelowTheOneItUnderstands_RefusesAsOlder()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"schemaVersion\": 2,", "\"schemaVersion\": 1,",
            ModelRefusalKind.Older, "line 2, column 20: schemaVersion 1 is older than this reader understands (2).");
    }

    [Fact]
    public void Read_AValueOfTheWrongKind_RefusesNamingTheKindItWanted()
    {
        ModelRefusal refusal = ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"spec\": \"MyApp architecture\",", "\"spec\": 1,",
            ModelRefusalKind.Malformed, "line 3, column 11: \"spec\" must be a string.");

        // The offset counts from the start of the text: two code units for the opening line and
        // twenty-two for the version line put line 3 at 24, and column 11 is ten further on.
        refusal.Position.Offset.ShouldBe(34);
    }

    [Fact]
    public void Read_ARootThatIsNotAnObject_RefusesNamingTheRoot()
    {
        // The one shape no edit of a valid file can reach, and the only place `the root` is the subject.
        ModelReadResult result = ModelDocumentReader.Read("[]");

        result.Document.ShouldBeNull();
        result.Refusal.ShouldNotBeNull()
            .Describe()
            .ShouldBe("line 1, column 1: the root must be an object.");
    }

    [Fact]
    public void Read_AMissingRequiredKey_RefusesNamingTheKindOfThingItIsMissingFrom()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "      \"because\": \"The domain must not know the web.\",\n", "",
            ModelRefusalKind.Malformed, "line 13, column 5: a rule has no \"because\".");
    }

    [Fact]
    public void Read_AnUnknownKey_RefusesRatherThanIgnoringIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "      \"id\": \"layering/domain-independent\",\n",
            "      \"id\": \"layering/domain-independent\",\n      \"extra\": 1,\n",
            ModelRefusalKind.Malformed, "line 15, column 7: a rule does not take \"extra\".");
    }

    [Fact]
    public void Read_AWordThatNamesNothing_RefusesNamingTheFamilyItLookedIn()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "\"id\": \"api/handlers-are-public\",\n      \"posture\": \"enforce\",",
            "\"id\": \"api/handlers-are-public\",\n      \"posture\": \"encourage\",",
            ModelRefusalKind.Malformed, "line 23, column 18: \"encourage\" is not a posture.");
    }

    [Fact]
    public void Read_ALayerNameNothingDeclared_RefusesNamingIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "{ \"name\": \"Domain\", \"globs\":", "{ \"name\": \"Realm\", \"globs\":",
            ModelRefusalKind.Malformed, "line 19, column 99: layer \"Domain\" is not declared above this point.");
    }

    [Fact]
    public void Read_ALayerNameDeclaredTwice_RefusesNamingIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "    { \"name\": \"Handlers\", \"definition\"", "    { \"name\": \"Domain\", \"definition\"",
            ModelRefusalKind.Malformed, "line 10, column 15: layer \"Domain\" is declared twice.");
    }

    [Fact]
    public void Read_ARuleIdUsedTwice_RefusesNamingIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "\"id\": \"time/no-ambient-clock\",", "\"id\": \"api/handlers-are-public\",",
            ModelRefusalKind.Malformed, "line 29, column 13: rule \"api/handlers-are-public\" appears twice.");
    }

    [Fact]
    public void Read_ARuleIdOfTheWrongShape_RefusesQuotingIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"id\": \"time/no-ambient-clock\",", "\"id\": \"Time/No Ambient\",",
            ModelRefusalKind.Malformed, "line 29, column 13: \"Time/No Ambient\" is not a rule id.");
    }

    [Fact]
    public void Read_ANamespaceGlobThatCanNeverMatch_RefusesInTheValidatorsOwnWords()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"glob\": \"MyApp.Web.*\"", "\"glob\": \"MyApp.*.Web.*\"",
            ModelRefusalKind.Malformed,
            "line 19, column 373: namespace pattern \"MyApp.*.Web.*\" has a trailing `.*` subtree "
            + "operator but its literal prefix contains a `*`, which never matches; anchor the "
            + "subtree on a literal prefix.");
    }

    [Fact]
    public void Read_ABlankTypeNameGlob_RefusesQuotingIt()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "{ \"node\": \"withSuffix\", \"suffix\": \"Dto\" }", "{ \"node\": \"withNameMatching\", \"glob\": \"\" }",
            ModelRefusalKind.Malformed, "line 19, column 241: type-name pattern \"\" is blank.");
    }

    [Fact]
    public void Read_AMalformedPathGlob_RefusesInTheMatchersOwnWords()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "{ \"node\": \"withSuffix\", \"suffix\": \"Dto\" }", "{ \"node\": \"under\", \"globs\": [\"src/../Widgets\"] }",
            ModelRefusalKind.Malformed,
            "line 19, column 232: path glob \"src/../Widgets\" has a `.` or `..` segment; name the "
            + "directories from the solution directory down.");
    }

    [Fact]
    public void Read_ASelectionCarryingBothANounAndAUnion_RefusesNamingBothKeys()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "[{ \"union\": [", "[{ \"noun\": { \"node\": \"types\" }, \"union\": [",
            ModelRefusalKind.Malformed,
            "line 19, column 267: a selection takes exactly one of \"noun\" and \"union\".");
    }

    [Fact]
    public void Read_AFamilyNounCarryingBothArms_RefusesNamingBothKeys()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.TextOf(ModelFileSpecs.DefineFamily),
            "{ \"node\": \"each\", \"layers\": [\"Alpha\", \"Beta\"] }",
            "{ \"node\": \"each\", \"layers\": [\"Alpha\", \"Beta\"], \"projects\": {} }",
            ModelRefusalKind.Malformed,
            "line 18, column 81: an \"each\" noun takes exactly one of \"layers\" and \"projects\".");
    }

    [Fact]
    public void Read_ALayerCarryingBothGlobsAndADefinition_RefusesNamingBothKeys()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(),
            "{ \"name\": \"Domain\", \"globs\": [\"MyApp.Domain.*\"], ",
            "{ \"name\": \"Domain\", \"globs\": [\"MyApp.Domain.*\"], \"definition\": { \"noun\": { \"node\": \"types\" } }, ",
            ModelRefusalKind.Malformed, "line 9, column 5: a layer takes exactly one of \"globs\" and \"definition\".");
    }

    [Fact]
    public void Read_ALayerWhoseGlobsAreNotAnArray_RefusesRatherThanThrowing()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.SampleText(), "\"globs\": [\"MyApp.Domain.*\"]", "\"globs\": \"MyApp.Domain.*\"",
            ModelRefusalKind.Malformed, "line 9, column 34: \"globs\" must be an array.");
    }

    [Fact]
    public void Read_ATripwireCarryingAConstraint_RefusesNamingTheRule()
    {
        ShouldRefuseAfterEdit(
            ModelFileSpecs.TextOf(ModelFileSpecs.DefineScope),
            "      \"scope\": { \"role\": \"tripwire\"",
            "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } },\n"
            + "      \"scope\": { \"role\": \"tripwire\"",
            ModelRefusalKind.Malformed,
            "line 19, column 5: rule \"legacy/billing/tripwire\" carries a \"constraint\" unless its "
            + "scope is a tripwire, and a tripwire carries none.");
    }

    [Fact]
    public void Read_TextThatIsNotJson_PassesTheParsersOwnRefusalThroughUnchanged()
    {
        string valid = ModelFileSpecs.SampleText();
        valid.ShouldReadBack();
        string edited = ShouldReplaceOnce(valid, "\"schemaVersion\": 2,", "\"schemaVersion\": 2");
        edited.ShouldNotBe(valid);
        StrictJson.TryParse(edited, out _, out StrictJsonError? parserError)
            .ShouldBeFalse();

        ModelReadResult result = ModelDocumentReader.Read(edited);

        result.Document.ShouldBeNull();
        ModelRefusal refusal = result.Refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(ModelRefusalKind.Malformed);
        refusal.Describe()
            .ShouldBe(parserError!.Position.Describe(parserError.Reason));
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

    // Each list the spec refuses empty when it loads, by the node that holds it and the key the list sits under.
    public static TheoryData<string, string> EmptiableLists =>
        new()
        {
            { "\"union\": [", "union" },
            { "{ \"node\": \"projectNamed\"", "names" },
            { "{ \"node\": \"projectMatching\"", "globs" },
            { "{ \"node\": \"memberNamed\"", "names" },
            { "{ \"node\": \"memberExcept\"", "payloads" },
            { "{ \"node\": \"mustNotUse\"", "members" }
        };

    [Theory]
    [MemberData(nameof(EmptiableLists))]
    public void Read_AnEmptyListTheSpecRefusesWhenItLoads_RefusesAtTheList(string node, string key)
    {
        // A file holding one was not written from a spec that loaded, so reading it would mint a model no spec
        // could have built.
        string valid = ModelFileSpecs.TextOf(DefineEveryEmptiableList);
        (string edited, int at) = ShouldEmptyTheArray(valid, node, key);

        ShouldRefuseEdited(
            valid, edited, ModelRefusalKind.Malformed, $"{PositionOf(edited, at)}: \"{key}\" must not be empty.");
    }

    private static void DefineEveryEmptiableList(Arch arch)
    {
        arch.Rule("lists/union")
            .Enforce(arch.AnyOf(arch.Namespace("MyApp.Solo.*")).MustBeSealed())
            .Because("Reason.");
        arch.Rule("lists/project-named")
            .Enforce(arch.Projects.Named("MyApp.Web").MustNotBePackable())
            .Because("Reason.");
        arch.Rule("lists/project-matching")
            .Enforce(arch.Projects.Matching("MyApp.*").MustLockPackages())
            .Because("Reason.");
        arch.Rule("lists/member-named")
            .Enforce(arch.Types.Methods.Named("Main").MustBePublic())
            .Because("Reason.");
        arch.Rule("lists/member-except")
            .Enforce(arch.Types.Methods.Except(arch.Types.Named("Program").Methods).MustBePublic())
            .Because("Reason.");
        arch.Rule("lists/must-not-use")
            .Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .Because("Reason.");
    }

    // The text with the array under `key` in the one node opening with `node` emptied, and the offset of that
    // array's opening bracket. The bracket is matched to its close, so the edit holds whatever the entries spell.
    private static (string Edited, int At) ShouldEmptyTheArray(string text, string node, string key)
    {
        int start = ShouldFindOnce(text, node);

        var keyText = $"\"{key}\": [";
        int open = text.IndexOf(keyText, start, StringComparison.Ordinal) + keyText.Length - 1;
        var depth = 0;
        int close = open;
        for (; close < text.Length; close++)
            if (text[close] == '[') depth++;
            else if (text[close] == ']' && --depth == 0) break;

        return (text.Substring(0, open + 1) + text.Substring(close), open);
    }

    private static string PositionOf(string text, int offset)
    {
        ReadOnlySpan<char> before = text.AsSpan(0, offset);

        return $"line {before.Count('\n') + 1}, column {offset - before.LastIndexOf('\n')}";
    }

    // One surgical edit of a text that read clean, with the three guards a refusal row needs: that the
    // text read before the edit, that the edit changed it, and that what comes back is the reader's
    // refusal rather than the JSON parser's; then the row's kind and its described position.
    private static ModelRefusal ShouldRefuseAfterEdit(
        string valid, string find, string replace, ModelRefusalKind kind, string described)
    {
        string edited = ShouldReplaceOnce(valid, find, replace);
        return ShouldRefuseEdited(valid, edited, kind, described);
    }

    private static ModelRefusal ShouldRefuseEdited(
        string valid, string edited, ModelRefusalKind kind, string described)
    {
        valid.ShouldReadBack();

        edited.ShouldNotBe(valid);
        StrictJson.TryParse(edited, out _, out StrictJsonError? parserError)
            .ShouldBeTrue($"a model-level refusal is meant, but the text stopped being JSON: {parserError?.Position.Describe(parserError.Reason)}");

        ModelReadResult result = ModelDocumentReader.Read(edited);
        result.Document.ShouldBeNull();
        ModelRefusal refusal = result.Refusal.ShouldNotBeNull();
        refusal.Kind.ShouldBe(kind);
        refusal.Describe()
            .ShouldBe(described);

        return refusal;
    }

    // Replaces the one occurrence of `find`, and says so if there is not exactly one: an edit that landed
    // somewhere else would move the position every row above asserts.
    private static string ShouldReplaceOnce(string text, string find, string replace)
    {
        int at = ShouldFindOnce(text, find);
        return text.Substring(0, at) + replace + text.Substring(at + find.Length);
    }

    private static int ShouldFindOnce(string text, string find)
    {
        text.Occurrences(find)
            .ShouldBe(1, $"the text should carry '{find}' exactly once");

        return text.IndexOf(find, StringComparison.Ordinal);
    }
}
