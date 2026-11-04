using Shouldly;
using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Roslyn.Files;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Guidance;

/// <summary>
///     The model <c>SlideRule.Guidance.DotNet</c> embeds is the model of <see cref="GuidanceModelSpec" />, written
///     as <c>render --model</c> writes one, and every rule in it is one a single project's build decides.
/// </summary>
public sealed class GuidanceModelTests
{
    internal const string ModelPath = "src/SlideRule.Guidance.DotNet/guidance.model.json";

    internal static readonly ArchitectureModel Model = ArchModelBuilder.Build(new GuidanceModelSpec());

    /// <summary>The model file as the generator spec composes it today.</summary>
    internal static string Composed()
    {
        ModelDocument document = ModelDocument.From(Model, GuidanceModelSpec.SpecName, []);

        return ModelDocumentComposer.Compose(document);
    }

    [Fact]
    public void ModelFile_IsCurrent()
    {
        // A checkout may have rewritten the line endings, which the writer's own comparison forgives too.
        string committed = WholeFile.NormalizeNewlines(RepoRoot.ReadText(ModelPath));
        string composed = Composed();
        if (string.Equals(committed, composed, StringComparison.Ordinal)) return;

        // Nothing renders this file, so the red leaves the composed one beside the test assembly to copy over.
        IReadOnlyList<string> drift =
            RegeneratedFiles.DriftReport(ModelPath, [$"is not what {nameof(GuidanceModelSpec)} composes"], composed);
        committed.ShouldBe(composed, string.Join("\n", drift));
    }

    [Fact]
    public void ModelFile_CarriesEveryRule_AndEachIsDecidedByOneProjectsBuild()
    {
        ModelDocument document = ModelDocument.From(Model, GuidanceModelSpec.SpecName, []);

        document.Omitted.Select(omitted => $"{omitted.Id}: {omitted.Reason}")
            .ShouldBeEmpty("The package carries only rules its model file can hold.");
        document.Rules
            .Select(rule => (rule.Id, OneProjectDecidability.Classify(rule).Reason))
            .Where(verdict => verdict.Reason is not null)
            .Select(verdict => $"{verdict.Id}: {verdict.Reason}")
            .ShouldBeEmpty("A rule one project's build cannot decide would never report.");
    }

    [Fact]
    public void ModelFile_EnforcesEveryRule()
    {
        // No ratchet ships: the package has no baseline to read, and a ratcheted rule without one is never
        // reported.
        Model.Rules
            .Where(rule => rule.Posture != Posture.Enforce || rule.BaselinePath is not null)
            .Select(rule => rule.Id)
            .ShouldBeEmpty();
    }
}
