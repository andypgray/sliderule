using Shouldly;
using SlideRule.Baselines;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Prose;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.ModelFile.Nesting;
using SlideRule.Tests.ModelFile.Nesting.Archive;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Pins that a model file says the same thing after a trip through its own text: composing what was
///     read reproduces the bytes, every parsed rule re-renders the sentence it was stored with, and the
///     nodes a reader mints are the ones the spec would have.
/// </summary>
/// <remarks>
///     The fixed point is the strong claim. A writer and a reader that agreed on nothing but the shape of
///     the braces would still pass a "it parses" test; they cannot both drop the same field and still
///     reproduce the text, and they cannot disagree about a discriminator at all.
/// </remarks>
public sealed class ModelDocumentRoundTripTests
{
    private static readonly (string Shape, Action<Arch> Define)[] Shapes =
    [
        ("scope", ModelFileSpecs.DefineScope),
        ("family", ModelFileSpecs.DefineFamily),
        ("assorted", ModelFileSpecs.DefineAssorted)
    ];

    public static TheoryData<string> OtherShapes => [.. Shapes.Select(row => row.Shape)];

    [Fact]
    public void TheSampleSpec_RoundTripsThroughItsFile()
    {
        ModelDocument parsed = ModelFileSpecs.Document()
            .ShouldRoundTrip();

        ShouldCarryAConstraint(parsed);
    }

    [Theory]
    [MemberData(nameof(OtherShapes))]
    public void TheOtherNodeShapes_RoundTripThroughTheirFile(string shape)
    {
        Action<Arch> define = Shapes.Single(row => string.Equals(row.Shape, shape, StringComparison.Ordinal))
            .Define;

        ModelDocument parsed = ModelFileSpecs.DocumentOf(define)
            .ShouldRoundTrip();

        ShouldCarryAConstraint(parsed);
    }

    [Fact]
    public void Read_TheOmittedList_IsTheOneTheFileCarries()
    {
        ModelDocument written = ModelFileSpecs.Document();

        ModelDocument read = ModelDocumentComposer.Compose(written)
            .ShouldReadBack();

        read.Omitted.Select(entry => (entry.Id, entry.Reason))
            .ShouldBe(written.Omitted.Select(entry => (entry.Id, entry.Reason)));
        read.Omitted.ShouldNotBeEmpty();
    }

    [Fact]
    public void Read_ABareLayerReference_MintsTheRegisteredLayerItself()
    {
        ModelDocument read = ModelFileSpecs.SampleText()
            .ShouldReadBack();

        Constraint constraint = read.Rules.Single(rule => rule.Id == "time/no-ambient-clock")
            .Constraint!;

        constraint.Subject.ShouldBeOfType<Layer>()
            .Name.ShouldBe("Domain");
    }

    [Fact]
    public void Read_ALayerReferenceCarryingAdjectives_MintsARefinedSelection()
    {
        ModelDocument read = ModelFileSpecs.SampleText()
            .ShouldReadBack();

        Constraint constraint = read.Rules.Single(rule => rule.Id == "layering/domain-independent")
            .Constraint!;

        Selection subject = constraint.Subject!;
        subject.ShouldBeOfType<RefinedSelection>();
        subject.Noun.ShouldBeOfType<LayerNoun>()
            .Name.ShouldBe("Domain");
    }

    [Fact]
    public void Read_ANestedAnchorAndItsCollidingSibling_ReRenderTheSameWidenedSentence()
    {
        // A nested type's display path drops its containing type. Without the path in the file, splitting
        // the name would widen this pair to `Books.Ledger` instead, so the pin is the widened word itself
        // as much as the equality beside it.
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/nested")
            .Enforce(arch.Types.MustNotDeriveFrom(typeof(Books.Ledger), typeof(Ledger)))
            .Because("b"));
        ArchRule original = model.Rules.ShouldHaveSingleItem();
        original.Sentence.ShouldBe("Types must not derive from `Nesting.Ledger` or `Archive.Ledger`.");

        string text = ModelDocumentComposer.Compose(ModelDocument.From(model, "S", ModelFileSpecs.Projects));
        ArchRule parsed = text.ShouldReadBack()
            .Rules.Single();

        SentenceRenderer.Sentence(parsed.Constraint!)
            .ShouldBe(original.Sentence);
    }

    [Fact]
    public void ToModel_OfWhatItRead_CarriesTheSameRulesAndLayers()
    {
        ModelDocument read = ModelFileSpecs.SampleText()
            .ShouldReadBack();

        ArchitectureModel model = read.ToModel();

        model.Rules.ShouldBe(read.Rules);
        model.Layers.ShouldBe(read.Layers);
    }

    [Fact]
    public void ARuleWithAnExemplar_ChecksIdenticallyThroughItsFile()
    {
        // One live name and one dead: the report compared carries the resolved type with its sites and the
        // dead name's warning, so a reader that dropped the exemplar, or kept only its phrase, reds here.
        CodebaseModel codebase = CompilationFactory.Extract(
            "namespace App { public sealed class OrdersRepository {} public class Controller {} }");
        ArchitectureModel model = Checker.Model(arch => arch.Rule("area/pointed")
            .Enforce(arch.Types.MustBeSealed())
            .Because("b")
            .Exemplar(arch.Types.Named("OrdersRepository", "GhostRepository")));

        ModelDocument parsed = model.ShouldSurviveItsFile("Pointed", codebase, BaselineIndex.Empty)
            .ShouldRoundTrip();

        parsed.Rules.ShouldHaveSingleItem()
            .Exemplar
            .ShouldBe(model.Rules.ShouldHaveSingleItem().Exemplar);
    }

    // Without this the round trip would hold of a file whose rules all lost their constraint on the way.
    private static void ShouldCarryAConstraint(ModelDocument parsed)
    {
        parsed.Rules.Where(rule => rule.Constraint is not null)
            .ShouldNotBeEmpty();
    }
}
