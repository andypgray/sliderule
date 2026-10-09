using Shouldly;
using Xunit;
using SlideRule.Cli.Verbs;
using SlideRule.ModelFile;
using SlideRule.Tests.ModelFile;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The guard <c>render</c> puts in front of the first write: composed text that does not read back as
///     it was written refuses the whole run, and says which half disagreed.
/// </summary>
/// <remarks>
///     <para>
///         Reachable in the product only through a defect in the writer or the reader, which is why the
///         check is a function of the text rather than a branch inside the run: a test can hand it text no
///         composer would produce without the run needing a seam to inject one through.
///     </para>
///     <para>
///         Both refusing rows feed it text the composer would not have emitted, so what is under test is the
///         disagreement rather than the JSON reader.
///     </para>
/// </remarks>
public sealed class ModelReadBackRefusalTests
{
    private const string Lede =
        "error: sliderule.model.json was not written, and nothing else was rendered: the composed file "
        + "did not read back as it was written, which is a defect in sliderule and not in the spec:";

    private const string Tail = "Please report it with the spec that produced it.";

    [Fact]
    public void ModelReadBackRefusal_TextTheComposerProduced_IsNull()
    {
        // The whole sample spec, not a fragment: the useful negative control is that a real file passes.
        RenderRunner.ModelReadBackRefusal(ModelFileSpecs.SampleText())
            .ShouldBeNull();
    }

    [Fact]
    public void ModelReadBackRefusal_TextTheReaderRefuses_CarriesTheReadersOwnPosition()
    {
        // A truncated file. The reader's words and position pass through untouched, because it has already
        // said what is wrong in the register the reader of this report is in.
        string truncated = ModelFileSpecs.SampleText()[..40];
        string described = ModelDocumentReader.Read(truncated)
            .Refusal!.Describe();

        RenderRunner.ModelReadBackRefusal(truncated)
            .ShouldBe($"{Lede}\n  {described}\n{Tail}");
    }

    [Fact]
    public void ModelReadBackRefusal_TextThatReadsButRecomposesDifferently_SaysTheTwoDisagreed()
    {
        // Re-indented, so it is the same document and not the same bytes — the shape of every drift this
        // guard exists for, without needing a real defect to produce one.
        string reindented = ModelFileSpecs.SampleText()
            .Replace("\n  \"spec\"", "\n    \"spec\"");

        RenderRunner.ModelReadBackRefusal(reindented)
            .ShouldBe(
                $"{Lede}\n  the file read back, and composing what was read gave different text.\n{Tail}");
    }
}
