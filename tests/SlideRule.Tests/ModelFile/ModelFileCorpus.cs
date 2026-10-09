using SlideRule.Cli.Pipeline;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.Packs;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Every real spec this assembly can build a model from without opening a workspace, loaded once: this
///     repository's own, the canonical sample, both halves of the pack parity oracle, each fixture spec
///     that loads in process, and the remainder spec written to reach what none of them do.
/// </summary>
/// <remarks>
///     <para>
///         Shared because three claims are made over the same set and each of them would otherwise pay for
///         it: the corpus round trips, the omitted lists are exact, and between them the composed texts
///         name every node the writer can write. Loading a spec assembly is cheap next to extracting a
///         codebase, but ten of them repeated three times is not.
///     </para>
///     <para>
///         The set is deliberately wider than any one spec author would write. A model file is only worth
///         building on if it carries what real specs actually say, and the vocabulary a single spec
///         exercises is a small slice of the language.
///     </para>
/// </remarks>
internal static class ModelFileCorpus
{
    /// <summary>
    ///     The projects a corpus file records where the model is put through the file with no codebase
    ///     beside it — two names, so the projects array is neither empty nor a copy of one real solution's.
    /// </summary>
    internal static readonly IReadOnlyList<ModelDocumentProject> Projects =
    [
        new("Corpus.App", ["Corpus.App"]),
        new("Corpus.Domain", ["Corpus.Domain"])
    ];

    private static readonly Lazy<IReadOnlyList<CorpusSpec>> LazyAll = new(Build);

    /// <summary>Every corpus spec, each with the name its file records.</summary>
    internal static IReadOnlyList<CorpusSpec> All => LazyAll.Value;

    /// <summary>The model of one fixture spec assembly, loaded the way the CLI loads it.</summary>
    internal static ArchitectureModel FixtureModel(string specDllPath)
    {
        return ModelPipeline.LoadModel(specDllPath);
    }

    /// <summary>The model of one corpus spec, by the name its file records.</summary>
    internal static ArchitectureModel ModelOf(string name)
    {
        return All.Single(spec => string.Equals(spec.Name, name, StringComparison.Ordinal))
            .Model;
    }

    /// <summary>One corpus spec as a model file carries it.</summary>
    internal static ModelDocument DocumentOf(CorpusSpec spec)
    {
        return ModelDocument.From(spec.Model, spec.Name, Projects);
    }

    private static IReadOnlyList<CorpusSpec> Build()
    {
        return
        [
            new CorpusSpec("SlideRule.Architecture", SelfSpecTests.SelfModel),
            new CorpusSpec("Canonical sample", Checker.Canonical),
            new CorpusSpec("PackedNineSpec", ArchModelBuilder.Build(new PackedNineSpec())),
            new CorpusSpec("InlineNineSpec", ArchModelBuilder.Build(new InlineNineSpec())),
            .. CliRunner.BeddedSpecs.Select(spec => new CorpusSpec(spec.Name, FixtureModel(spec.SpecDll))),
            new CorpusSpec("LegacySpec", FixtureModel(CliRunner.LegacySpecDll)),
            new CorpusSpec("SpecFixture", FixtureModel(CliRunner.SpecFixtureDll)),
            new CorpusSpec("VocabularyRemainderSpec", Checker.Model(VocabularyRemainderSpec.Define))
        ];
    }

    /// <summary>One corpus spec: the name its file records and the model it builds.</summary>
    internal sealed record CorpusSpec(string Name, ArchitectureModel Model);
}
