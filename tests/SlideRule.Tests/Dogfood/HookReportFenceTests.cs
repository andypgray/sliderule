using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.Checking;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     The hook report both public pages quote, re-run. The pages imagine an agent adding a progress printer
///     to the CLI that writes to the console; this class adds exactly that file to the CLI project in memory,
///     checks it, and holds each page's stanza to what the check prints. A type added to the CLI, a reworded
///     rule or a changed report layout reds here, printing the stanza to paste.
/// </summary>
/// <remarks>
///     <para>
///         Only the CLI project is extracted, with and without the imagined file. The rule the stanza reports
///         sweeps the Host layer, which is that project, so its subject is the same over the one project as
///         over the whole solution — <see cref="CliOnlyModel_SweepsTheHostLayerAsTheWholeModelDoes" /> pins
///         that — and a walk of the whole solution would add no line to the stanza.
///     </para>
/// </remarks>
public sealed class HookReportFenceTests
{
    /// <summary>Where the pages imagine the file, relative to the repository root.</summary>
    internal const string ProgressPrinterPath = "src/SlideRule.Cli/Rendering/ProgressPrinter.cs";

    private const string CliProject = "SlideRule.Cli";

    private const string ProgressPrinter = "SlideRule.Cli.Rendering.ProgressPrinter";

    private const string RuleId = "cli/no-stdout";

    // The change the pages imagine, whole. Its two writes sit on lines 10 and 15, the lines the stanza names
    // and the anchor gate holds as demonstration anchors.
    private const string ProgressPrinterSource = """
                                                 namespace SlideRule.Cli.Rendering;

                                                 /// <summary>
                                                 ///     Prints a line per project while a slow solution load runs, so the load stops looking hung.
                                                 /// </summary>
                                                 internal static class ProgressPrinter
                                                 {
                                                     public static void Loading(string project)
                                                     {
                                                         Console.WriteLine($"Loading {project}...");
                                                     }

                                                     public static void Loaded(int projectCount)
                                                     {
                                                         Console.WriteLine($"Loaded {projectCount} projects.");
                                                     }
                                                 }
                                                 """;

    private static readonly Lazy<Task<CliModels>> Models = new(ExtractAsync);

    [Theory]
    [InlineData(DocPaths.SelfCheck)]
    [InlineData(DocPaths.RootReadme)]
    public async Task HookReportFence_IsTheStanzaTheImaginedChangeDraws(string doc)
    {
        // Arrange
        CliModels models = await Models.Value;
        ArchRule rule = SelfSpecTests.SelfModel.Rule(RuleId);

        // Act: relativized against the repository root, where the pages' paths hang from, rather than
        // against the working directory the test host happens to run in.
        RuleResult result = ArchChecker.Check([rule], models.Imagined, BaselineIndex.Empty, diff: null)
            .Results.Single();
        string stanza = HumanReportRenderer.RuleBlock(result, RepoRoot.Directory);

        // Assert
        HookReportOn(doc)
            .ShouldMatchTheRun(stanza);
    }

    /// <summary>
    ///     The pages say the imagined file breaks this one rule, and quote that rule's stanza alone. Every rule
    ///     is checked over the CLI project with and without the file, the way the build analyzer checks one
    ///     project's compilation, and only this one may reach a different verdict.
    /// </summary>
    [Fact]
    public async Task ImaginedChange_BreaksCliNoStdoutAndNoOtherRule()
    {
        // Arrange
        CliModels models = await Models.Value;

        // Act: the one-project narrowing cuts a subject that spans several projects down to this one. Without
        // it, a rule over a union of layers selects nothing here, reports an empty subject on both sides, and
        // would hide any difference it had.
        IReadOnlyList<RuleResult> committed = CheckOneProject(models.Committed);
        IReadOnlyList<RuleResult> imagined = CheckOneProject(models.Imagined);

        List<string> changed = committed.Zip(imagined)
            .Where(static pair => RuleVerdict.Of(pair.First) != RuleVerdict.Of(pair.Second))
            .Select(static pair => pair.First.Rule.Id)
            .ToList();

        // Assert
        changed.ShouldBe([RuleId], "the imagined file breaks exactly the rule whose stanza the pages quote");

        // The rules one project cannot decide skip on both sides above. A new edge could only move one of
        // them by reaching a type this solution declares, and every edge out of the imagined file reaches
        // outside it.
        IReadOnlyList<TypeNode> reached = EdgeTargetsOf(models.Imagined, ProgressPrinter);
        reached.ShouldNotBeEmpty("the imagined file reaches nothing, so the model does not carry it");
        reached.Where(static target => !target.IsExternal)
            .Select(static target => target.FullName)
            .ShouldBeEmpty("an edge out of the imagined file reaches a type this solution declares");
    }

    /// <summary>
    ///     The narrowing the stanza's <c>subject:</c> line relies on: over the CLI project alone, the rule
    ///     sweeps exactly what it sweeps over the whole solution.
    /// </summary>
    [Fact]
    public async Task CliOnlyModel_SweepsTheHostLayerAsTheWholeModelDoes()
    {
        // Arrange
        CliModels models = await Models.Value;
        CodebaseModel whole = await SelfSpecTests.WholeCodebase.Value;
        ArchRule rule = SelfSpecTests.SelfModel.Rule(RuleId);

        // Act
        RuleResult narrowed = ArchChecker.Check([rule], models.Committed, BaselineIndex.Empty, diff: null)
            .Results.Single();
        RuleResult widest = ArchChecker.Check([rule], whole, BaselineIndex.Empty, diff: null)
            .Results.Single();

        // Assert
        (narrowed.SubjectTypes, narrowed.SubjectGeneratedTypes)
            .ShouldBe((widest.SubjectTypes, widest.SubjectGeneratedTypes));
    }

    // The CLI project extracted twice, as committed and with the imagined file added in memory. Every other
    // project is left out before the walk, so each model costs that one project.
    private static async Task<CliModels> ExtractAsync()
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(RepoRoot.Solution, CancellationToken.None);
        Project cli = snapshot.Solution.Projects.Single(project => project.Name == CliProject);
        string imaginedPath = RepoRoot.Absolute(ProgressPrinterPath);

        // Committed, the file would put this very violation in the tree, and the pages' anchors would stop
        // being demonstrations.
        cli.Documents
            .Where(document => string.Equals(document.FilePath, imaginedPath, PathComparison.Comparison))
            .ShouldBeEmpty($"{ProgressPrinterPath} is the change the pages imagine, and must never be committed.");

        Solution imagined = cli.AddDocument(
                "ProgressPrinter.cs", SourceText.From(ProgressPrinterSource), ["Rendering"], imaginedPath)
            .Project.Solution;

        List<string> others = snapshot.Solution.Projects
            .Select(static project => project.Name)
            .Where(static name => name != CliProject)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(RepoRoot.Solution);

        CodebaseModel committed = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, others, snapshot.TargetFrameworks, declaredMembers, CancellationToken.None);
        CodebaseModel withImaginedFile = await CodebaseExtractor.ExtractFromSolutionAsync(
            imagined, others, snapshot.TargetFrameworks, declaredMembers, CancellationToken.None);

        return new CliModels(committed, withImaginedFile);
    }

    private static CapturedFence HookReportOn(string doc)
    {
        CapturedFence[] copies = [CapturedFences.SelfCheckHookReport, CapturedFences.ReadmeHookReport];

        return copies.Single(copy => copy.Doc == doc);
    }

    private static IReadOnlyList<RuleResult> CheckOneProject(CodebaseModel model)
    {
        return ArchChecker.Check(
                SelfSpecTests.SelfModel.Rules, model, BaselineIndex.Empty, diff: null,
                NarrowedUniverse.OneProject, incompleteModel: null)
            .Results;
    }

    // Every type an edge out of the named type reaches, over every edge family the model carries.
    private static IReadOnlyList<TypeNode> EdgeTargetsOf(CodebaseModel model, string source)
    {
        return model.Edges.Where(edge => edge.Source.FullName == source)
            .Select(static edge => edge.Target)
            .Concat(model.MemberEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Member.ContainingType))
            .Concat(model.ConstructorEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Constructed))
            .Concat(model.InjectionEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Injected))
            .Concat(model.CatchEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Caught))
            .Concat(model.ThrowEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Thrown))
            .Concat(model.ExposureEdges.Where(edge => edge.Source.FullName == source)
                .Select(static edge => edge.Exposed))
            .ToList();
    }

    /// <summary>The CLI project's model as committed, and with the imagined file added.</summary>
    /// <param name="Committed">The CLI project as the tree holds it.</param>
    /// <param name="Imagined">The same project with the imagined file compiled into it.</param>
    private sealed record CliModels(CodebaseModel Committed, CodebaseModel Imagined);
}
