using Shouldly;
using SlideRule.Baselines;
using SlideRule.Cli.Pipeline;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Cli;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Every real spec this repository holds, put through a model file and checked twice: once as the
///     spec built it and once as the file gives it back. The two reports must be the same document.
/// </summary>
/// <remarks>
///     <para>
///         The inline specs the fast harness runs are small by design — one rule, one verb, a codebase of
///         a few types — and a format that survived only those would still be a format nobody could ship a
///         spec through. These are the specs people actually wrote: this repository's own, the canonical
///         sample, the two halves of the pack parity oracle, and every fixture spec, each against the bed
///         it governs with that bed's baselines loaded, so a grandfathered violation is grandfathered on
///         both sides or on neither.
///     </para>
///     <para>
///         Every row also requires the file to carry something. A model whose every rule was omitted round
///         trips perfectly and proves nothing, and most of the suite's inline specs are exactly that shape.
///     </para>
///     <para>
///         The first row to reach this repository's own codebase shares the extraction
///         <see cref="SelfSpecTests" /> already pays for.
///     </para>
/// </remarks>
public sealed class ModelDocumentCorpusTests
{
    private const string LayoutSpecCsproj =
        "tests/SlideRule.Tests/Fixtures/OutputLayoutSolutions/LayoutApp/LayoutApp.Spec/LayoutApp.Spec.csproj";

    // The test assembly declares specs of its own — the canonical sample, the harness's one-off wrapper,
    // the parity pair, the remainder — and they are put through the file by the harness pin and by the
    // corpus above rather than as a spec project. Everything else that declares one is a row below.
    private const string TestAssemblyCsproj = "tests/SlideRule.Tests/SlideRule.Tests.csproj";

    private const string SmallSource = """
                                       namespace Corpus.Domain
                                       {
                                           public interface IOrder { }
                                           public sealed class Order : IOrder { }
                                       }

                                       namespace Corpus.App
                                       {
                                           public sealed class OrderHandler
                                           {
                                               public void Handle(Corpus.Domain.Order order) { }
                                           }
                                       }
                                       """;

    /// <summary>
    ///     The spec projects this repository tracks outside <c>examples/</c> that cannot produce a model in
    ///     process, each listed with the reason, and the reason is asserted by the fact named beside it — none
    ///     is skipped quietly. Every other spec project is put through a model file by a row of
    ///     <see cref="Bedded" /> or <see cref="Bedless" />.
    /// </summary>
    private static readonly (string Csproj, string Reason)[] SpecProjects =
    [
        ("tests/Fixtures/SlideRule.SharedFrameworkSpec/SlideRule.SharedFrameworkSpec.csproj",
            "unloadable by design: its anchor needs a .NET shared framework no spec output can carry"),
        ("tests/Fixtures/SlideRule.LegacyBrokenSpec/SlideRule.LegacyBrokenSpec.csproj",
            "unloadable by design: its anchor names a type with no counterpart on .NET"),
        (LayoutSpecCsproj,
            "built only inside a copy a test makes, from a contract path no committed file supplies")
    ];

    private static readonly Lazy<CodebaseModel> SmallCodebase = new(() => CompilationFactory.Extract(SmallSource));

    /// <summary>
    ///     The fixture specs checked against the bed each governs: the name the corpus records it under, and
    ///     that bed's solution.
    /// </summary>
    private static readonly (string Name, string Solution)[] Bedded =
        [.. CliRunner.BeddedSpecs.Select(spec => (spec.Name, spec.Solution))];

    /// <summary>
    ///     The fixture specs with no bed to check against, each checked against a source-built codebase
    ///     instead: no bed declares LegacySpec's net48 product or the namespace SpecFixture governs.
    /// </summary>
    private static readonly string[] Bedless = ["LegacySpec", "SpecFixture"];

    public static TheoryData<string, string> BeddedSpecs => [.. Bedded];

    public static TheoryData<string> BedlessSpecs => [.. Bedless];

    public static TheoryData<string, string> UnloadableSpecs =>
        new()
        {
            { CliRunner.SharedFrameworkSpecDll, "SlideRule.SharedFrameworkSpec" },
            { CliRunner.LegacyBrokenSpecDll, "SlideRule.LegacyBrokenSpec" }
        };

    [Fact]
    public async Task ThisRepositorysOwnSpec_ChecksIdenticallyThroughItsModelFile()
    {
        // Arrange: the shared no-exclusions extraction and this repository's own baselines, so a
        // grandfathered violation is grandfathered on both sides of the file or on neither.
        CodebaseModel codebase = await SelfSpecTests.WholeCodebase.Value;
        BaselineIndex baselines = BaselineStore.LoadForModel(SelfSpecTests.SelfModel, RepoRoot.Directory);

        // Act + Assert
        SelfSpecTests.SelfModel.ShouldSurviveItsFile("SlideRule.Architecture", codebase, baselines);
    }

    [Theory]
    [MemberData(nameof(BeddedSpecs))]
    public async Task EveryFixtureSpec_ChecksIdenticallyAgainstTheBedItGoverns(string name, string solutionPath)
    {
        // Arrange
        CodebaseModel codebase = await FixtureCodebase.Of(solutionPath);
        ArchitectureModel model = ModelFileCorpus.ModelOf(name);
        BaselineIndex baselines = BaselineStore.LoadForModel(model, Path.GetDirectoryName(solutionPath)!);

        // Act + Assert
        model.ShouldSurviveItsFile(name, codebase, baselines);
    }

    [Theory]
    [MemberData(nameof(BedlessSpecs))]
    public void EveryBedlessFixtureSpec_ChecksIdenticallyAgainstASourceBuiltCodebase(string name)
    {
        // Arrange
        ArchitectureModel model = ModelFileCorpus.ModelOf(name);

        // Act + Assert
        model.ShouldSurviveItsFile(name, SmallCodebase.Value, BaselineIndex.Empty);
    }

    [Fact]
    public void TheCanonicalSample_ChecksIdenticallyThroughItsModelFile()
    {
        Checker.Canonical.ShouldSurviveItsFile("Canonical sample", SmallCodebase.Value, BaselineIndex.Empty);
    }

    [Fact]
    public void BothHalvesOfThePackParityOracle_CheckIdenticallyThroughTheirModelFiles()
    {
        // Arrange: the pack's nine rules and the hand-written nine they are measured against. A pack call
        // reifies to ordinary nodes, so a file that lost one would make the parity oracle unrepeatable.
        ArchitectureModel packed = ModelFileCorpus.ModelOf("PackedNineSpec");
        ArchitectureModel inline = ModelFileCorpus.ModelOf("InlineNineSpec");

        // Act + Assert
        packed.ShouldSurviveItsFile("PackedNineSpec", SmallCodebase.Value, BaselineIndex.Empty);
        inline.ShouldSurviveItsFile("InlineNineSpec", SmallCodebase.Value, BaselineIndex.Empty);
    }

    [Fact]
    public void TheVocabularyRemainderSpec_ChecksIdenticallyThroughItsModelFile()
    {
        ModelFileCorpus.ModelOf("VocabularyRemainderSpec")
            .ShouldSurviveItsFile("VocabularyRemainderSpec", SmallCodebase.Value, BaselineIndex.Empty);
    }

    [Theory]
    [MemberData(nameof(UnloadableSpecs))]
    public void EveryUnloadableFixtureSpec_RefusesToProduceAModelAtAll(string specDll, string assemblyName)
    {
        // Act: the reason these two are listed rather than routed. Which load arm catches, and the whole
        // remedy each hands the reader, is pinned by Cli.ModelPipelineSpecLoadFailureTests; what matters
        // here is that no model exists to put through a file.
        var thrown = Should.Throw<UserErrorException>(() => ModelPipeline.LoadModel(specDll));

        // Assert
        thrown.Message.ShouldContain(assemblyName);
    }

    [Fact]
    public void TheLayoutBedsSpec_IsBuiltFromAContractPathNoCommittedFileSupplies()
    {
        // Act: the reason it is listed rather than routed. Nothing restores or builds it in place — each
        // consumer copies the bed and writes the props file that gives the reference somewhere to point —
        // so a checkout holds its source and never its assembly.
        string project = RepoRoot.ReadText(LayoutSpecCsproj);
        List<string> suppliers = TrackedFiles.NonSourceText
            .Where(path => RepoRoot.ReadText(path)
                .Contains("<SlideRuleContractPath>", StringComparison.Ordinal))
            .ToList();

        // Assert
        project.ShouldContain("$(SlideRuleContractPath)");
        suppliers.ShouldReportNothing(
            "Committed file(s) now define the contract path the layout bed's spec builds against, so it "
            + "builds in place and belongs on a route above");
    }

    [Fact]
    public void EveryTrackedSpecProject_IsAccountedForByTheTableAbove()
    {
        // Arrange: the test assembly declares specs of its own, and is the one project whose specs this
        // class does not route. Held live rather than merely excluded — an exclusion that stopped matching
        // would hide the next project to arrive under that path.
        List<string> declaring = TrackedFiles.Projects
            .Where(static path => path.StartsWith("tests/", StringComparison.Ordinal))
            .Where(DeclaresASpec)
            .ToList();

        // Each routed name is its fixture project's name after the `SlideRule.` prefix, so a project
        // is accounted for only when a theory routes it or a reason lists it.
        HashSet<string> accounted = Bedded.Select(row => row.Name)
            .Concat(Bedless)
            .Select(name => $"tests/Fixtures/SlideRule.{name}/SlideRule.{name}.csproj")
            .Concat(SpecProjects.Select(row => row.Csproj))
            .ToHashSet(StringComparer.Ordinal);

        // Act
        List<string> unaccounted = declaring
            .Where(static path => path != TestAssemblyCsproj)
            .Where(path => !accounted.Contains(path))
            .ToList();

        // Assert
        declaring.ShouldContain(TestAssemblyCsproj);
        unaccounted.ShouldReportNothing(
            "Spec project(s) this class neither routes through a model file nor lists with an asserted reason");
    }

    // Whether a project declares an architecture spec, by the sources it owns: a tracked C# file under its
    // directory that no nested project owns instead.
    private static bool DeclaresASpec(string csprojPath)
    {
        string directory = csprojPath[..(csprojPath.LastIndexOf('/') + 1)];
        List<string> nested = TrackedFiles.Projects
            .Where(path => path.StartsWith(directory, StringComparison.Ordinal)
                           && !string.Equals(path, csprojPath, StringComparison.Ordinal))
            .Select(path => path[..(path.LastIndexOf('/') + 1)])
            .ToList();

        return TrackedFiles.CSharp
            .Where(path => path.StartsWith(directory, StringComparison.Ordinal))
            .Where(path => !nested.Any(inner => path.StartsWith(inner, StringComparison.Ordinal)))
            .Any(path => RepoRoot.ReadText(path)
                .Contains("IArchitectureSpec", StringComparison.Ordinal));
    }
}
