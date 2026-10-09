using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Roslyn.Baselines;
using SlideRule.Roslyn.Files;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     The example solutions on the model-file pin every other spec in this repository already takes — the
///     file is a fixed point of parse then compose, and the parsed rules reach the same verdict as the live
///     ones — plus the claim only a committed file can make: that the bytes on disk are the bytes the
///     composer writes now.
/// </summary>
/// <remarks>
///     <para>
///         Nothing in this suite builds an example, so a row skips unless that example's spec assembly is on
///         disk: on a machine that never built them the class is one skip per example and never a red. The
///         build that makes the rows run is the one CI does before it runs this class.
///     </para>
///     <para>
///         The spec, its name, the excluded projects and the extraction are the ones <c>render --model</c>
///         takes, so a difference here is a difference in the committed file rather than in how it was asked
///         for. The committed comparison is line-ending normalized for the same reason the writer's own is: a
///         checkout may have rewritten them.
///     </para>
/// </remarks>
public sealed class ExampleModelRoundTripTests
{
    /// <summary>Every example solution on disk, one row each.</summary>
    public static TheoryData<string> Examples
    {
        get
        {
            TheoryData<string> data = new();
            foreach (ExampleSolution example in ExampleSolutions.Discovered()) data.Add(example.Name);

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public async Task EveryExample_ChecksIdenticallyThroughTheModelFileItCommits(string name)
    {
        // Arrange
        ExampleSolution example = ShouldFind(name);
        string specProject = example.SpecProjectDirectory
            .ShouldNotBeNull($"{name} has no spec project, which the inventory below reports in full.");
        string searched = ExampleBuilds.SpecOutputDirectory(specProject);
        Assert.SkipUnless(
            ExampleBuilds.IsBuilt(searched, specProject),
            $"Nothing in this suite builds the example solutions, and this pin reads the spec assembly a "
            + $"build of {name} produces. Build it and run this again. Looked in: {searched}");

        SpecfulCodebase extracted = await SpecfulCodebase.ExtractAsync(
            example.SolutionPath, spec: null, example.SolutionDirectory, Ct);

        ArchitectureModel model = extracted.Model;
        BaselineIndex baselines = BaselineStore.LoadForModel(model, example.SolutionDirectory);

        // Act
        ModelDocument document = model.ShouldSurviveItsFile(extracted.SpecName, extracted.Governed, baselines);

        // Assert
        string committedPath = Path.Combine(example.SolutionDirectory, ModelDocument.FileName);
        string solution = RepoRoot.Relative(example.SolutionPath);
        File.Exists(committedPath)
            .ShouldBeTrue(
                $"{RepoRoot.Relative(committedPath)} is not committed; run "
                + $"`sliderule render {solution} --model`.");

        string committed = File.ReadAllText(committedPath);
        WholeFile.NormalizeNewlines(committed)
            .ShouldBe(
                ModelDocumentComposer.Compose(document),
                $"the committed model file is stale; run `sliderule render {solution}`.");
    }

    /// <summary>
    ///     The examples this class covers are the example solutions on disk, so a fifth arrives on the pin
    ///     rather than beside it — and each of them has the spec project the rows above look for.
    /// </summary>
    [Fact]
    public void ExampleInventory_IsEveryExampleSolutionOnDisk()
    {
        // Arrange
        IReadOnlyList<ExampleSolution> examples = ExampleSolutions.Discovered();

        // Assert: every row above passes over nothing if discovery matched nothing, and the on-ramp example
        // every reader knows by name is what holds the enumerator to finding the real tree.
        examples.Select(example => example.Name)
            .ShouldContain("Meridian");

        List<string> unlocatable = examples
            .Where(example => example.SpecProjectDirectory is null)
            .Select(example => RepoRoot.Relative(example.SolutionPath))
            .ToList();

        unlocatable.ShouldReportNothing(
            $"Example solution(s) with no directory under {ExampleSolutions.ArchDirectory}/ whose name ends with "
            + $"'{ExampleSolutions.SpecProjectSuffix}', so this class cannot find their spec assembly and skips them forever");
    }

    /// <summary>
    ///     The discovered example a row names.
    /// </summary>
    /// <remarks>
    ///     Asserting, hence the name: a row whose solution left the tree between discovery and the run would
    ///     otherwise arrive as a null reference in the arrange block.
    /// </remarks>
    private static ExampleSolution ShouldFind(string name)
    {
        ExampleSolution? found = ExampleSolutions.Discovered()
            .FirstOrDefault(example => string.Equals(example.Name, name, StringComparison.Ordinal));

        return found.ShouldNotBeNull($"No example solution named '{name}' is on disk.");
    }
}
