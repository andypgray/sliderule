using Shouldly;
using SlideRule.ModelFile;
using SlideRule.Tests.ModelFile.Nesting;
using SlideRule.Tests.ModelFile.Nesting.Archive;
using SlideRule.Tests.Stubs;
using SlideRule.Tests.Stubs.Billing;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Pins the model file's bytes: the whole layout against a literal, then, one row each, the decisions
///     that layout encodes.
/// </summary>
/// <remarks>
///     The whole-text row is the one that makes a layout change loud. Everything else here says why one
///     part of it reads the way it does, so a red names the decision rather than a diff of six hundred
///     characters.
/// </remarks>
public sealed class ModelDocumentComposerTests
{
    [Fact]
    public void Compose_ASmallSpec_MatchesTheFileLayoutByteForByte()
    {
        string composed = ModelDocumentComposer.Compose(ModelFileSpecs.Document());

        string[] lines =
        [
            "{",
            "  \"schemaVersion\": 2,",
            "  \"spec\": \"MyApp architecture\",",
            "  \"projects\": [",
            "    { \"name\": \"MyApp.Domain\", \"assemblies\": [\"MyApp.Domain\"] },",
            "    { \"name\": \"MyApp.Web\", \"assemblies\": [\"MyApp.Web\"] }",
            "  ],",
            "  \"layers\": [",
            "    { \"name\": \"Domain\", \"globs\": [\"MyApp.Domain.*\"], \"purpose\": \"What the business knows.\" },",
            "    { \"name\": \"Handlers\", \"definition\": { \"noun\": { \"node\": \"types\" }, \"adjectives\": [{ \"node\": \"withSuffix\", \"suffix\": \"Handler\" }] } }",
            "  ],",
            "  \"rules\": [",
            "    {",
            "      \"id\": \"layering/domain-independent\",",
            "      \"posture\": \"enforce\",",
            "      \"sentence\": \"Types in the Domain layer, except types named `*Dto`, must not reference the Handlers layer or types in `MyApp.Web.*`.\",",
            "      \"because\": \"The domain must not know the web.\",",
            "      \"fix\": \"Move the type into the Domain layer.\",",
            "      \"constraint\": { \"node\": \"mustNotReference\", \"subject\": { \"noun\": { \"node\": \"layer\", \"name\": \"Domain\" }, \"adjectives\": [{ \"node\": \"except\", \"payload\": { \"noun\": { \"node\": \"types\" }, \"adjectives\": [{ \"node\": \"withSuffix\", \"suffix\": \"Dto\" }] } }] }, \"operands\": [{ \"union\": [{ \"noun\": { \"node\": \"layer\", \"name\": \"Handlers\" } }, { \"noun\": { \"node\": \"namespace\", \"glob\": \"MyApp.Web.*\" } }] }] }",
            "    },",
            "    {",
            "      \"id\": \"api/handlers-are-public\",",
            "      \"posture\": \"enforce\",",
            "      \"sentence\": \"Methods returning `String` declared in the Handlers layer must be public.\",",
            "      \"because\": \"A handler nobody can call is dead code.\",",
            "      \"constraint\": { \"node\": \"memberMustBePublic\", \"memberSubject\": { \"source\": { \"noun\": { \"node\": \"layer\", \"name\": \"Handlers\" } }, \"kind\": \"method\", \"adjectives\": [{ \"node\": \"returning\", \"anchors\": [{ \"name\": \"System.String\" }] }] } }",
            "    },",
            "    {",
            "      \"id\": \"time/no-ambient-clock\",",
            "      \"posture\": \"enforce\",",
            "      \"sentence\": \"The Domain layer must not use `DateTime.Now`.\",",
            "      \"because\": \"Ambient time cannot be tested.\",",
            "      \"constraint\": { \"node\": \"mustNotUse\", \"subject\": { \"noun\": { \"node\": \"layer\", \"name\": \"Domain\" } }, \"members\": [{ \"declaring\": { \"name\": \"System.DateTime\" }, \"name\": \"Now\", \"isMethod\": false }] }",
            "    }",
            "  ],",
            "  \"omitted\": [",
            "    { \"id\": \"naming/long-names-are-sealed\", \"reason\": \"A Where predicate is code, and this file carries data only.\" }",
            "  ]",
            "}"
        ];

        composed.ShouldBe(string.Join("\n", lines) + "\n");
    }

    [Fact]
    public void Compose_ARuleWithNoFixAndNoCitation_LeavesBothKeysOut()
    {
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/plain")
            .Enforce(arch.Types.MustBeSealed())
            .Because("b"));

        RuleText(composed).ShouldBe(
            "    {\n"
            + "      \"id\": \"area/plain\",\n"
            + "      \"posture\": \"enforce\",\n"
            + "      \"sentence\": \"Types must be sealed.\",\n"
            + "      \"because\": \"b\",\n"
            + "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } }\n"
            + "    }");
    }

    [Fact]
    public void Compose_ARuleWithAFixAndACitation_WritesThemBetweenBecauseAndTheConstraint()
    {
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/cited")
            .Enforce(arch.Types.MustBeSealed())
            .Because("b")
            .Fix("Seal it.")
            .Citation("https://learn.microsoft.com/dotnet"));

        RuleText(composed).ShouldBe(
            "    {\n"
            + "      \"id\": \"area/cited\",\n"
            + "      \"posture\": \"enforce\",\n"
            + "      \"sentence\": \"Types must be sealed.\",\n"
            + "      \"because\": \"b\",\n"
            + "      \"fix\": \"Seal it.\",\n"
            + "      \"citation\": \"https://learn.microsoft.com/dotnet\",\n"
            + "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } }\n"
            + "    }");
    }

    [Fact]
    public void Compose_ARuleWithAnExemplar_WritesItAsASelectionAfterTheCitation()
    {
        // The exemplar is the fix's pointer, so it follows the fix and the page; it is written as the selection
        // the spec authored rather than as its phrase, because the reader re-resolves it against the solution.
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/pointed")
            .Enforce(arch.Types.MustBeSealed())
            .Because("b")
            .Fix("Seal it.")
            .Citation("https://learn.microsoft.com/dotnet")
            .Exemplar(arch.Types.Named("OrdersRepository")));

        RuleText(composed).ShouldBe(
            "    {\n"
            + "      \"id\": \"area/pointed\",\n"
            + "      \"posture\": \"enforce\",\n"
            + "      \"sentence\": \"Types must be sealed.\",\n"
            + "      \"because\": \"b\",\n"
            + "      \"fix\": \"Seal it.\",\n"
            + "      \"citation\": \"https://learn.microsoft.com/dotnet\",\n"
            + "      \"exemplar\": { \"noun\": { \"node\": \"types\" }, \"adjectives\": [{ \"node\": \"named\", "
            + "\"names\": [\"OrdersRepository\"] }] },\n"
            + "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } }\n"
            + "    }");
    }

    [Fact]
    public void Compose_APlainTypeAnchor_WritesNeitherOnDefinitionNorPath()
    {
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/plain-anchor")
            .Enforce(arch.Types.MustDeriveFrom(typeof(LedgerBase)))
            .Because("b"));

        composed.ShouldContain(
            "\"anchor\": { \"name\": \"SlideRule.Tests.Stubs.Billing.LedgerBase\" }");
    }

    [Fact]
    public void Compose_AnOpenGenericAnchor_WritesOnDefinition()
    {
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/open-anchor")
            .Enforce(arch.Types.MustNotImplement(typeof(IHandler<>)))
            .Because("b"));

        composed.ShouldContain(
            "\"anchors\": [{ \"name\": \"SlideRule.Tests.Stubs.IHandler<T>\", \"onDefinition\": true }]");
    }

    [Fact]
    public void Compose_ANestedTypeAnchor_WritesItsPath()
    {
        // A nested type's display path drops the containing type, so splitting its full name would give a
        // different one — and the widening rule that qualifies colliding names reads the path, not the name.
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/nested-anchor")
            .Enforce(arch.Types.MustNotDeriveFrom(typeof(Books.Ledger), typeof(Ledger)))
            .Because("b"));

        composed.ShouldContain(
            "\"anchors\": ["
            + "{ \"name\": \"SlideRule.Tests.ModelFile.Nesting.Books.Ledger\", "
            + "\"path\": [\"SlideRule\", \"Tests\", \"ModelFile\", \"Nesting\", \"Ledger\"] }, "
            + "{ \"name\": \"SlideRule.Tests.ModelFile.Nesting.Archive.Ledger\" }]");
    }

    [Fact]
    public void Compose_AMigrateRule_WritesTheMigrationAfterTheConstraint()
    {
        string composed = ModelFileSpecs.TextOf(arch => arch.Rule("area/migrating")
            .Migrate("Controllers open connections directly.", arch.Types.MustBeSealed())
            .Because("b")
            .Baseline("arch/baseline.json")
            .WhileYoureThere(MigrationPolicy.AlwaysMigrate));

        RuleText(composed).ShouldBe(
            "    {\n"
            + "      \"id\": \"area/migrating\",\n"
            + "      \"posture\": \"migrate\",\n"
            + "      \"sentence\": \"Types must be sealed.\",\n"
            + "      \"because\": \"b\",\n"
            + "      \"constraint\": { \"node\": \"mustBeSealed\", \"subject\": { \"noun\": { \"node\": \"types\" } } },\n"
            + "      \"migrate\": { \"from\": \"Controllers open connections directly.\", "
            + "\"toSentence\": \"Types must be sealed.\", \"baselinePath\": \"arch/baseline.json\", "
            + "\"policy\": \"alwaysMigrate\" }\n"
            + "    }");
    }

    [Fact]
    public void Compose_AScopedRule_WritesTheScopeKeysInOrder()
    {
        string composed = ModelFileSpecs.TextOf(ModelFileSpecs.DefineScope);

        // The containment half is the one that carries a boundary; the tripwire beside it carries none,
        // which is why its surface is an empty array rather than a missing key.
        composed.ShouldContain(
            "\"scope\": { \"role\": \"containment\", \"scopeId\": \"legacy/billing\", "
            + "\"scoped\": { \"noun\": { \"node\": \"namespace\", \"glob\": \"MyApp.Legacy.Billing.*\" } }, "
            + "\"surface\": [\"`IBillingFacade`\"], "
            + "\"baselinePath\": \"arch/baselines/legacy/billing/containment.json\", "
            + "\"dragons\": \"Rounding happens per line item.\" }");
        composed.ShouldContain(
            "\"scope\": { \"role\": \"tripwire\", \"scopeId\": \"legacy/billing\", "
            + "\"scoped\": { \"noun\": { \"node\": \"namespace\", \"glob\": \"MyApp.Legacy.Billing.*\" } }, "
            + "\"surface\": [], \"dragons\": \"Rounding happens per line item.\" }");
    }

    [Fact]
    public void Compose_AnyDocument_EndsWithExactlyOneNewlineAndCarriesNoCarriageReturn()
    {
        string composed = ModelDocumentComposer.Compose(ModelFileSpecs.Document());

        composed.ShouldEndWith("}\n");
        composed.ShouldNotEndWith("}\n\n");
        composed.ShouldNotContain("\r");
    }

    [Fact]
    public void Compose_AModelWithNothingToCarry_WritesEveryRootKeyWithEmptyArrays()
    {
        var document = new ModelDocument("Nothing", [], [], [], []);

        ModelDocumentComposer.Compose(document)
            .ShouldBe(
                "{\n"
                + "  \"schemaVersion\": 2,\n"
                + "  \"spec\": \"Nothing\",\n"
                + "  \"projects\": [],\n"
                + "  \"layers\": [],\n"
                + "  \"rules\": [],\n"
                + "  \"omitted\": []\n"
                + "}\n");
    }

    // The first rule object on its own, so a key-order claim reads as the lines it is about rather than
    // as a whole file with two lines that differ.
    private static string RuleText(string composed)
    {
        int start = composed.IndexOf("  \"rules\": [\n", StringComparison.Ordinal) + "  \"rules\": [\n".Length;
        int end = composed.IndexOf("\n  ],", start, StringComparison.Ordinal);
        return composed.Substring(start, end - start);
    }
}
