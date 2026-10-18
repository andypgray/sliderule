using Shouldly;
using SlideRule.Model;
using SlideRule.Prose;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The facts a <see cref="TypeAnchor" /> computes once at mint, and the two keys it hands a checker
///     position: a definition-level position (the type noun, the member verbs) must be given a definition
///     and refuses a construction; a construction-level position (the hierarchy and attribute matchers)
///     admits one. Both refuse a type with no source-level display form, and the message they refuse with
///     is the one the reader sees.
/// </summary>
public sealed class TypeAnchorFactsTests
{
    private const string Ns = "SlideRule.Tests";

    [Fact]
    public void FullName_PlainTypeofAnchor_IsTheExtractionFormatName()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(AnchorFactsWidget));

        anchor.FullName.ShouldBe($"{Ns}.AnchorFactsWidget");
        anchor.IsConstructed.ShouldBeFalse();
        anchor.Unrepresentable.ShouldBeNull();
    }

    [Fact]
    public void BothKeys_PlainTypeofAnchor_ResolveWithoutRefusing()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(AnchorFactsWidget));

        ShouldKeyBothPositions(anchor, $"{Ns}.AnchorFactsWidget", onDefinition: false);
    }

    [Fact]
    public void OpenGenericDefinition_IsOnDefinitionAndKeysBothPositions()
    {
        // An open definition means "any construction of it", so it is the name both positions compare on.
        TypeAnchor anchor = TypeAnchor.FromType(typeof(IEnumerable<>));

        anchor.OnDefinition.ShouldBeTrue();
        anchor.IsConstructed.ShouldBeFalse();

        ShouldKeyBothPositions(anchor, "System.Collections.Generic.IEnumerable<T>", onDefinition: true);
    }

    [Fact]
    public void ClosedGeneric_DefinitionKey_RefusesAsAClosedGeneric()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(Task<int>));

        anchor.IsConstructed.ShouldBeTrue();
        anchor.TryDefinitionKey(out string key, out AnchorRefusal refusal)
            .ShouldBeFalse();
        key.ShouldBe(string.Empty);
        refusal.ShouldBe(AnchorRefusal.ClosedGeneric);
    }

    [Fact]
    public void ClosedGeneric_ConstructionKey_IsTheConstructedName()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(Task<int>));

        anchor.TryConstructionKey(out string key, out bool onDefinition, out AnchorRefusal refusal)
            .ShouldBeTrue();
        key.ShouldBe("System.Threading.Tasks.Task<System.Int32>");
        onDefinition.ShouldBeFalse();
        refusal.ShouldBe(AnchorRefusal.None);
    }

    [Fact]
    public void Pointer_BothKeys_RefuseAsUnrepresentable()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(int).MakePointerType());

        anchor.Unrepresentable.ShouldBe("Type 'System.Int32*' has no source-level display form the checker can match against.");
        anchor.FullName.ShouldBeNull();

        ShouldRefuseBothPositions(anchor, AnchorRefusal.Unrepresentable, AnchorRefusal.Unrepresentable);
    }

    [Fact]
    public void ArrayOfPointers_Unrepresentable_NamesTheInnerOffendingType()
    {
        // The rendering recurses into the element type and refuses there, so the message a reader sees
        // names `System.Int32*` rather than the array that carried it.
        TypeAnchor anchor = TypeAnchor.FromType(typeof(int).MakePointerType().MakeArrayType());

        anchor.Unrepresentable.ShouldBe("Type 'System.Int32*' has no source-level display form the checker can match against.");
        ShouldRefuseBothPositions(anchor, AnchorRefusal.Unrepresentable, AnchorRefusal.Unrepresentable);
    }

    [Fact]
    public void ByRefType_BothKeys_RefuseAsUnrepresentable()
    {
        TypeAnchor anchor = TypeAnchor.FromType(typeof(int).MakeByRefType());

        anchor.Unrepresentable.ShouldNotBeNull();
        ShouldRefuseBothPositions(anchor, AnchorRefusal.Unrepresentable, AnchorRefusal.Unrepresentable);
    }

    [Fact]
    public void PartiallyOpenConstruction_IsBoth_AndTheRefusalOrderDiffersByPosition()
    {
        // Both constructed and unrepresentable at once. A definition-level position refuses the
        // construction first — the actionable complaint is "name the open definition" — while a
        // construction-level position admits constructions and so can only report the missing name.
        Type partiallyOpen = typeof(Dictionary<,>)
            .MakeGenericType(typeof(string), typeof(Dictionary<,>).GetGenericArguments()[1]);
        TypeAnchor anchor = TypeAnchor.FromType(partiallyOpen);

        anchor.IsConstructed.ShouldBeTrue();
        anchor.Unrepresentable.ShouldNotBeNull();

        ShouldRefuseBothPositions(anchor, AnchorRefusal.ClosedGeneric, AnchorRefusal.Unrepresentable);
    }

    [Theory]
    [InlineData("MyApp.Web.IHandler<T>")]
    [InlineData("N.IHandler<System.Int32>")]
    [InlineData("Dotless")]
    public void StringAnchor_IsTheVerbatimNameOnDefinition_AndRefusesNothing(string name)
    {
        // Nothing is inferred from the shape of the string: a constructed spelling is a legal name that
        // matches nothing, so it keys both positions exactly as written.
        TypeAnchor anchor = TypeAnchor.FromName(name);

        anchor.FullName.ShouldBe(name);
        anchor.OnDefinition.ShouldBeTrue();
        anchor.IsConstructed.ShouldBeFalse();
        anchor.Unrepresentable.ShouldBeNull();

        ShouldKeyBothPositions(anchor, name, onDefinition: true);
    }

    [Theory]
    [InlineData(typeof(AnchorFactsWidget))]
    [InlineData(typeof(AnchorFactsOuter.Inner))]
    [InlineData(typeof(IEnumerable<>))]
    [InlineData(typeof(IEnumerable<AnchorFactsWidget>))]
    [InlineData(typeof(Dictionary<string, AnchorFactsOuter.Inner>))]
    public void SimpleDisplay_TypeofAnchor_IsTheSimpleName(Type type)
    {
        // The noun's reference fragment reads this, so it must stay the unqualified generic-aware name a
        // sentence backticks — never the full path, and never the reflection arity spelling.
        TypeAnchor.FromType(type)
            .SimpleDisplay.ShouldBe(TypeName.Simple(type));
    }

    // Both positions key the anchor on the same name without refusing it; the construction position also
    // says whether that name is a definition.
    private static void ShouldKeyBothPositions(TypeAnchor anchor, string key, bool onDefinition)
    {
        anchor.TryDefinitionKey(out string definitionKey, out AnchorRefusal definitionRefusal)
            .ShouldBeTrue();
        definitionKey.ShouldBe(key);
        definitionRefusal.ShouldBe(AnchorRefusal.None);

        anchor.TryConstructionKey(out string constructionKey, out bool keyedOnDefinition, out AnchorRefusal constructionRefusal)
            .ShouldBeTrue();
        constructionKey.ShouldBe(key);
        keyedOnDefinition.ShouldBe(onDefinition);
        constructionRefusal.ShouldBe(AnchorRefusal.None);
    }

    // Both positions refuse the anchor, each with its own refusal, and hand back no key.
    private static void ShouldRefuseBothPositions(TypeAnchor anchor, AnchorRefusal definition, AnchorRefusal construction)
    {
        anchor.TryDefinitionKey(out string definitionKey, out AnchorRefusal definitionRefusal)
            .ShouldBeFalse();
        definitionKey.ShouldBe(string.Empty);
        definitionRefusal.ShouldBe(definition);

        anchor.TryConstructionKey(out string constructionKey, out bool onDefinition, out AnchorRefusal constructionRefusal)
            .ShouldBeFalse();
        constructionKey.ShouldBe(string.Empty);
        onDefinition.ShouldBeFalse();
        constructionRefusal.ShouldBe(construction);
    }
}

// Reflectable helper types for the anchor-fact tests: a plain type, and a nested one whose own path has a
// declaring link between the namespace and the leaf.
internal sealed class AnchorFactsWidget;

internal sealed class AnchorFactsOuter
{
    internal sealed class Inner;
}
