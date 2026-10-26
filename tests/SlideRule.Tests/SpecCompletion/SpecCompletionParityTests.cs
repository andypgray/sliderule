using System.Reflection;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.SpecAnalyzers;
using SlideRule.SpecCompletion;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.Extraction;
using SlideRule.Validation;
using Xunit;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     The agreement gates: everything the completion provider mirrors, held against the thing it mirrors.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a mirror at all.</b> The provider references no project in this repository, deliberately:
///         an editor constructs a project's completion providers by reflection inside catch blocks that
///         report nothing, so an assembly that fails to load produces no list and no message anywhere, and
///         the fewer assemblies have to resolve beside it the fewer ways that silence has to happen. The
///         price is that the compiler cannot check one spelling in it against the contract that owns the
///         other half. These arms are the whole of what holds them equal.
///     </para>
///     <para>
///         <b>The name is not the point; the match is.</b> An offered name that matches nothing is the
///         silent failure these gates exist to prevent: a rule anchored on it is legal, loads clean, checks
///         green and holds nothing to anything. So the first two arms do not compare spellings — they take
///         the name a commit would write and hold it against the name the extractor renders for the same
///         type, which is the name a rule is matched by.
///     </para>
/// </remarks>
public sealed class SpecCompletionParityTests(SpecCompletionFixture fixture)
    : IClassFixture<SpecCompletionFixture>
{
    /// <summary>
    ///     The slots the provider offers names in, which is what the arms below hold the contract's own
    ///     catalog against.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, SlotKind> Completing =
        new Dictionary<string, SlotKind>(StringComparer.Ordinal)
        {
            [nameof(SpecSlot.NamespacePattern)] = SlotKind.NamespacePattern,
            [nameof(SpecSlot.ExactTypeName)] = SlotKind.ExactTypeName,
            [nameof(SpecSlot.InterfaceName)] = SlotKind.InterfaceName,
            [nameof(SpecSlot.BaseTypeName)] = SlotKind.BaseTypeName,
            [nameof(SpecSlot.AttributeName)] = SlotKind.AttributeName,
            [nameof(SpecSlot.ReturnTypeName)] = SlotKind.ReturnTypeName,
            [nameof(SpecSlot.ParameterTypeName)] = SlotKind.ParameterTypeName
        };

    // The catalog's own size, stated here so that a slot added to the contract reds on this side too and
    // someone decides whether it is one an editor can offer names in. The package pattern and the property
    // name and value complete nothing: the compilation a spec is written in holds no package identifiers and
    // no MSBuild properties to offer.
    private const int CatalogSize = 36;

    [Fact]
    public async Task EveryProductType_IsOfferedUnderTheNameTheExtractorRendersForIt()
    {
        // Arrange: the extractor is the authority, because the name it renders is the name a rule's anchor
        // is compared against.
        IReadOnlyList<TypeNode> declared = ProductTypes();
        Dictionary<string, OfferedList> offeredUnder = new(StringComparer.Ordinal);
        List<string> unreachable = [];

        // Act
        foreach (TypeNode type in declared)
        {
            (string path, string leaf) = SplitLeaf(type.FullName);
            if (!offeredUnder.TryGetValue(path, out OfferedList? offered))
            {
                offered = await fixture.ShouldCompileAndOfferAsync(
                    $$"""arch.Types.Methods.Returning("{{path}}.$$");""", Ct);
                offeredUnder.Add(path, offered);
            }

            if (offered.Texts.Contains(leaf, StringComparer.Ordinal)) continue;

            unreachable.Add(offered.Describe($"'{type.FullName}' needs '{leaf}' offered under '{path}'"));
        }

        // Assert
        declared.ShouldNotBeEmpty("The product model has no declared types in it, so this arm measured nothing.");
        unreachable.ShouldBeEmpty(
            "A name the completion list offers has to compose to the name the extractor renders, or an "
            + "author picks a name from a list and gets a rule that matches nothing — which loads clean, "
            + "checks green and holds nothing to anything.");
    }

    [Fact]
    public async Task EveryProductType_IsOfferedUnderTheSimpleNameTheExtractorRendersForIt()
    {
        // Arrange
        string[] names = ProductTypes()
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Act
        OfferedList offered = await fixture.ShouldCompileAndOfferAsync("""arch.Types.Named("$$");""", Ct);

        // Assert: the exact-name slot compares against the simple name, so that is the whole of what it
        // may offer.
        names.ShouldNotBeEmpty("The product model has no types in it, so this arm measured nothing.");
        offered.ShouldOffer(names);
    }

    [Fact]
    public void TheSevenSlotNames_AreTheContractsOwn()
    {
        // Act
        string[] mirrored =
        [
            SpecSlotNames.NamespacePattern, SpecSlotNames.ExactTypeName, SpecSlotNames.InterfaceName,
            SpecSlotNames.BaseTypeName, SpecSlotNames.AttributeName, SpecSlotNames.ReturnTypeName,
            SpecSlotNames.ParameterTypeName
        ];

        // Assert
        mirrored.ShouldBe(Completing.Keys.ToArray(),
            "A slot name spelled differently on the two sides leaves the provider matching nothing and "
            + "offering nothing, silently, in every editor.");
    }

    [Fact]
    public void BothReaders_FindTheMarkerUnderTheNameTheContractDeclaresItAt()
    {
        // Arrange: the analyzer holds its own copy of the same spelling, and the two are read here rather
        // than one being read from the other, because neither assembly can see the other's.
        string declared = typeof(SpecSlotAttribute).FullName!;
        string analyzers = SpecLiteralAnalyzer.MarkerMetadataName;

        // Assert
        SpecSlotNames.MarkerMetadataName.ShouldBe(declared,
            "The completion provider finds the marker by this name and by nothing else.");
        analyzers.ShouldBe(declared,
            "The spec-literal analyzer finds the marker by this name and by nothing else.");
    }

    [Fact]
    public void TheMarker_TakesExactlyOneStringAndNothingElse()
    {
        // Act: both readers decode the attribute blob by taking the single constructor argument, so a
        // second parameter would leave both of them reading nothing.
        ConstructorInfo[] constructors = typeof(SpecSlotAttribute).GetConstructors();

        // Assert
        ConstructorInfo only = constructors.ShouldHaveSingleItem(
            "The marker has to carry one public constructor: a reader with no access to the constructor "
            + "cannot decode the blob at all.");
        ParameterInfo parameter = only.GetParameters()
            .ShouldHaveSingleItem("The marker's constructor takes the slot name and nothing else.");
        parameter.ParameterType.ShouldBe(typeof(string));
    }

    [Fact]
    public void OverTheWholeAuthoringSurface_TheTwoReadersClassifyEveryMarkedParameterAlike()
    {
        // Arrange: one walk, shared with the inventory gate, so the two cannot agree about different sets
        // of the surface.
        IReadOnlyDictionary<string, string> marked = SpecSlotMarkerInventoryTests.MarkedParameters();
        List<string> drift = [];

        // Act
        foreach ((string parameter, string slot) in marked.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            SlotKind expected = Completing.TryGetValue(slot, out SlotKind kind) ? kind : SlotKind.None;
            SlotKind actual = SpecSlotNames.KindOf(slot);
            if (actual == expected) continue;

            drift.Add($"{parameter} fills {slot}: the contract says {expected} and the provider says {actual}.");
        }

        // Assert: the vacuity guard first, because a walk that found nothing would agree about nothing and
        // read exactly like a surface both readers understood.
        marked.ShouldNotBeEmpty("The authoring-surface walk found no marked parameters at all.");
        Completing.Values
            .ShouldBeUnique("Seven slots complete, and each of them has to be a kind of its own.");
        marked.Values
            .Where(slot => Completing.ContainsKey(slot))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ShouldBe(Completing.Keys.Order(StringComparer.Ordinal),
                "Every completing slot has to appear somewhere on the authoring surface, or this arm is "
                + "agreeing about a slot no author can write in.");
        drift.ShouldReportNothing("The contract and the completion provider disagree about which slots complete");
    }

    [Fact]
    public void EverySlotBesidesTheSeven_CompletesNothing()
    {
        // Act
        List<string> offering = SpecSlot.All
            .Select(slot => slot.Name)
            .Where(name => !Completing.ContainsKey(name))
            .Where(name => SpecSlotNames.KindOf(name) != SlotKind.None)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        SpecSlot.All.Count.ShouldBe(CatalogSize,
            "A slot was added to or taken out of the contract's catalog. Decide whether an editor can "
            + "offer names in it — a slot with no universe to draw from completes nothing — and move this "
            + "number with the decision.");
        offering.ShouldReportNothing("Slot(s) the contract declares complete here without being one of the seven");
    }

    [Fact]
    public void TheProviderAssembly_ReferencesNothingThisRepositoryBuilds()
    {
        // Act
        List<string> referenced = typeof(SpecSlotCompletionProvider).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? "")
            .Where(name => name == "SlideRule" || name.StartsWith("SlideRule.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        referenced.ShouldReportNothing(
            "The completion provider ships in a directory holding the contract and the spec-literal "
            + "analyzer, and an editor resolves its dependencies from that directory and nowhere else. A "
            + "reference it cannot resolve there is no list and no message anywhere, so it carries none");
    }

    // The product the fixture's spec governs, read through the extractor, so that both sides of the first
    // two arms are the same source seen by the two readers that matter. Declared types only: the model also
    // carries a node for every type the source merely references, and System.Object is not a name this
    // product could be completed with.
    private static IReadOnlyList<TypeNode> ProductTypes()
    {
        CodebaseModel model = CompilationFactory.Extract("MyApp", ("Product.cs", SpecCompletionFixture.ProductSource));

        return model.Types
            .Where(type => !type.IsExternal)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    // A full name split at its last segment. Type-parameter lists carry identifiers and commas and never a
    // dot, so the last dot is always the one between the container and the leaf.
    private static (string Path, string Leaf) SplitLeaf(string fullName)
    {
        int lastDot = fullName.LastIndexOf('.');

        return lastDot < 0 ? ("", fullName) : (fullName.Substring(0, lastDot), fullName.Substring(lastDot + 1));
    }
}
