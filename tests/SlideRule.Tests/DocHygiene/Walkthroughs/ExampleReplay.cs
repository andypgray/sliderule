using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.ModelFile;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Baselines;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Files;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     Every run the example walkthroughs quote, re-made in memory: each example's committed tree, each scenario
///     its pages imagine, and the adoption walkthrough's day-zero steps, all checked and rendered by the product's
///     own code over the example's warm workspace.
/// </summary>
/// <remarks>
///     <para>
///         Composed rather than run through the verbs, for three reasons: a diff-aware run shells to git over the
///         real tree, which holds none of the scenarios' edits; <c>render</c> and <c>baseline</c> write files; and
///         <c>graph</c> cannot leave out a spec project the adoption walkthrough had not written yet.
///         <see cref="WalkthroughReplayTests" /> holds the composition to the read-only verbs on the committed tree.
///     </para>
///     <para>
///         One run per example, made once and cached. The cache is started with no cancellation token, so the
///         first test to ask cannot leave a cancelled run behind for every test after it, and it keeps reports and
///         strings rather than a <see cref="Solution" />, so the warm pool may evict the workspace at no cost. The
///         read-only verbs run inside it too, right after the load, so what they print is read without reloading
///         the workspace.
///     </para>
/// </remarks>
internal static class ExampleReplay
{
    private const string AdoptingSpec = "Meridian.Architecture";

    private static readonly IReadOnlyDictionary<string, Lazy<Task<ExampleRun>>> Runs =
        ExampleSolutions.Discovered()
            .ToDictionary(
                static example => example.Name,
                static example => new Lazy<Task<ExampleRun>>(() => RunAsync(example)),
                StringComparer.Ordinal);

    /// <summary>The replay of <paramref name="example" />, made on first use.</summary>
    internal static Task<ExampleRun> RunOf(string example)
    {
        return Runs[example].Value;
    }

    private static async Task<ExampleRun> RunAsync(ExampleSolution solution)
    {
        string directory = solution.SolutionDirectory;

        SpecfulCodebase extracted = await ExampleCodebase.Of(solution);
        ArchitectureModel model = extracted.Model;

        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(solution.SolutionPath, CancellationToken.None);
        ShouldHaveLoadedCleanly(solution.Name, snapshot.LoadDiagnostics);

        // The read-only verbs over the committed tree, run while this example's session is still the pool's most
        // recent, so reading what they print never reloads an evicted workspace.
        CliResult checkVerb = await CliRunner.InvokeAsync("check", solution.SolutionPath, "--no-cache");
        CliResult statusVerb = await CliRunner.InvokeAsync("status", solution.SolutionPath, "--no-cache");

        IReadOnlySet<string>? declaredMembers = SpecExclusion.TryReadDeclaredMembers(solution.SolutionPath);
        var edits = new ScenarioEditor(snapshot, extracted.ExcludeProjectNames, declaredMembers);

        CodebaseModel committed = await edits.ExtractAsync(snapshot.Solution);
        committed.MergeNotes.ShouldBeEmpty($"{solution.Name}'s committed tree merged with notes the pages never show.");

        BaselineIndex baselines = BaselineStore.LoadForModel(model, directory);
        ScenarioRun tree = Check(model, committed, baselines, directory, []);

        Dictionary<string, ScenarioRun> scenarios = new(StringComparer.Ordinal);
        foreach (WalkthroughScenario scenario in WalkthroughScenarios.All.Where(scenario => scenario.Example == solution.Name))
        {
            (CodebaseModel codebase, IReadOnlyList<string> changed) = await edits.ShouldApplyAsync(scenario, directory);
            scenarios[scenario.Key] = Check(model, codebase, baselines, directory, changed);
        }

        AdoptionRun? adoption = solution.Name == WalkthroughScenarios.Meridian
            ? await ShouldAdoptAsync(solution, model, extracted.Governed)
            : null;

        return new ExampleRun(
            solution.Name, directory, extracted.Governed, committed, tree, scenarios,
            ShouldRenderLines(model, extracted.Governed, directory, extracted.SpecName), adoption, checkVerb,
            statusVerb);
    }

    // The pages say the examples build green, and a partial model would quote a smaller codebase than the one
    // on disk, so a load that is anything less than whole fails the replay before any fence is compared.
    private static void ShouldHaveLoadedCleanly(string example, WorkspaceDiagnostics diagnostics)
    {
        string[] problems =
        [
            .. diagnostics.FailedProjects.Select(static path => $"failed to load: {path}"),
            .. diagnostics.RestoreFailedProjects.Select(static path => $"failed to restore: {path}"),
            .. diagnostics.UncheckedProjects.Select(static path => $"unchecked: {path}"),
            .. diagnostics.UnsupportedProjects.Select(static project => $"unsupported: {project.Path}"),
            .. diagnostics.Rendered.Select(static line => $"diagnostic: {line}")
        ];

        problems.ShouldReportNothing($"The {example} workspace did not load cleanly, so no walkthrough fence can be replayed over it");
    }

    // The check a run prints, with and without the changed files a diff-aware run reads. Both are always made:
    // the pages say which one they quote, and the moves are judged without the diff and the warnings under it.
    private static ScenarioRun Check(
        ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines, string directory,
        IReadOnlyList<string> changed)
    {
        CheckReport plain = Harness.CheckUncancellable(model.Rules, codebase, baselines, null);
        CheckReport diffAware = Harness.CheckUncancellable(model.Rules, codebase, baselines, new DiffContext(directory, changed));

        return new ScenarioRun(plain, diffAware, directory);
    }

    // What `render` reports over the committed tree, composed as the verb composes it and judged as
    // `render --check` judges each target, without writing it.
    private static IReadOnlyList<string> ShouldRenderLines(
        ArchitectureModel model, CodebaseModel governed, string directory, string specName)
    {
        string modelPath = Path.Combine(directory, ModelDocument.FileName);
        bool wantsModel = File.Exists(modelPath);
        CodebaseModel? codebase = ContextFileComposer.HasAnythingToPlace(model) || wantsModel ? governed : null;

        ContextComposition composition = ContextFileComposer.Compose(model, codebase, directory, specName);
        composition.Warnings.ShouldBeEmpty($"render warns over {directory}, which no walkthrough quotes.");

        List<string> lines = composition.Files
            .Select(file => WriteReport.Line(SpliceOutcome(file), directory, file.Path))
            .ToList();

        if (!wantsModel) return lines;

        string composed = ModelDocumentComposer.Compose(ModelDocument.From(model, specName, ModelDocumentProject.ListOf(governed)));
        WriteOutcome outcome = WholeFile.IsCurrent(modelPath, composed) ? WriteOutcome.Unchanged : WriteOutcome.Wrote;
        lines.Add(WriteReport.Line(outcome, directory, modelPath));

        return lines;
    }

    private static WriteOutcome SpliceOutcome(ContextFile file)
    {
        return ManagedBlockFile.IsCurrent(file.Path, file.Body) ? WriteOutcome.Unchanged : WriteOutcome.Wrote;
    }

    // The adoption walkthrough's day-zero steps over Meridian: the survey before any spec existed, the check of the
    // drafted spec with nothing yet grandfathered, and the baseline capture of the curated one.
    private static async Task<AdoptionRun> ShouldAdoptAsync(
        ExampleSolution solution, ArchitectureModel model, CodebaseModel governed)
    {
        string directory = solution.SolutionDirectory;

        // The survey of the three projects the solution held before the spec project was added to it.
        string survey = GraphJsonRenderer.Document(
            GraphSummarizer.Summarize(governed), Path.GetFileName(solution.SolutionPath), [],
            WorkspaceTrustStamp.From(WorkspaceDiagnostics.None, directory), DocumentGrain.Full, []);

        ArchitectureModel draft = await ShouldLoadDraftAsync(solution);
        CheckReport draftCheck = Harness.CheckUncancellable(draft.Rules, governed, BaselineIndex.Empty, null);
        string draftReport = ScenarioRun.Rendered(draftCheck, directory, HumanReportRenderer.Render);

        (IReadOnlyList<string> initLines, IReadOnlyDictionary<string, byte[]> baselineFiles) = CaptureBaselines(model, governed);

        return new AdoptionRun(survey, draftReport, initLines, baselineFiles);
    }

    // The draft the walkthrough's step 3 prints, compiled exactly as the page quotes it — with the spec project's
    // implicit usings, which the page leaves to the SDK — into a copy of the built spec's output, and loaded from
    // there so its package references resolve as the committed spec's do.
    private static async Task<ArchitectureModel> ShouldLoadDraftAsync(ExampleSolution solution)
    {
        string specProject = solution.SpecProjectDirectory
                             ?? throw new InvalidOperationException($"{solution.Name} has no spec project to stage the draft beside.");
        string searched = ExampleBuilds.SpecOutputDirectory(specProject);
        string built = Directory.EnumerateFiles(searched, $"{AdoptingSpec}.dll", SearchOption.AllDirectories)
            .ShouldHaveSingleItem($"Looking for one {AdoptingSpec}.dll under {searched}.");
        string builtDirectory = Path.GetDirectoryName(built)!;

        string outputPath = Path.GetRelativePath(Path.Combine(specProject, "bin"), builtDirectory);
        string globalUsings = await File.ReadAllTextAsync(
            Path.Combine(specProject, "obj", outputPath, $"{AdoptingSpec}.GlobalUsings.g.cs"));
        string draft = string.Join("\n", WalkthroughFences.AdoptingDraft.ShouldFindFence().Lines);

        MetadataReference[] references =
        [
            .. TrustedPlatform.References,
            MetadataReference.CreateFromFile(Path.Combine(builtDirectory, "Meridian.Clearance.dll")),
            MetadataReference.CreateFromFile(Path.Combine(builtDirectory, "Microsoft.Data.SqlClient.dll"))
        ];
        byte[] image = SpecAssemblyCompiler.EmitImage($"{globalUsings}\n{draft}", AdoptingSpec, references);

        string staged = SpecOutputStager.StageWithout(built, $"{AdoptingSpec}.dll", $"{AdoptingSpec}.pdb");
        await File.WriteAllBytesAsync(staged, image);

        return ModelPipeline.LoadModel(staged);
    }

    // `baseline init` over the curated spec with nothing yet on disk: the survey against an empty baseline, the
    // capture written into a directory of its own, and the notice that closes the verb's output.
    private static (IReadOnlyList<string> Lines, IReadOnlyDictionary<string, byte[]> Files) CaptureBaselines(
        ArchitectureModel model, CodebaseModel governed)
    {
        CheckReport survey = Harness.CheckUncancellable(model.Rules, governed, BaselineIndex.Empty, null);
        List<RuleResult> ratcheted = survey.Results.Where(static result => result.Rule.IsRatcheted)
            .ToList();

        using TempDirectory capture = TestTempRoot.Fresh("walkthrough-baseline-init");
        var output = new StringWriter { NewLine = "\n" };
        BaselineRunner.ApplyFiles(output, BaselineRequests.Init(capture.Path), ratcheted, capture.Path);
        foreach (string line in RatchetSurveyNotice.Lines(survey)) output.WriteLine(line);

        Dictionary<string, byte[]> files = Directory.EnumerateFiles(capture.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(capture.Path, path)
                    .Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

        string[] lines = output.ToString()
            .TrimEnd('\n')
            .Split('\n');

        return (lines, files);
    }

    /// <summary>
    ///     Makes a scenario's edits to the example's loaded solution and extracts the result the way the verbs
    ///     extract the committed tree: the same exclusions, target frameworks and declared members.
    /// </summary>
    private sealed class ScenarioEditor(
        WorkspaceSnapshot snapshot,
        IReadOnlyCollection<string> exclude,
        IReadOnlySet<string>? declaredMembers)
    {
        internal Task<CodebaseModel> ExtractAsync(Solution solution)
        {
            return CodebaseExtractor.ExtractFromSolutionAsync(
                solution, exclude, snapshot.TargetFrameworks, declaredMembers, snapshot.ProjectFiles,
                CancellationToken.None);
        }

        /// <summary>The scenario's edited codebase, and the absolute paths of the files it changed.</summary>
        internal async Task<(CodebaseModel Codebase, IReadOnlyList<string> Changed)> ShouldApplyAsync(
            WalkthroughScenario scenario, string directory)
        {
            IReadOnlyDictionary<string, string> texts = scenario.Apply(path => ReadCommitted(directory, path));
            IReadOnlySet<string> added = scenario.AddedPaths;

            Solution committed = snapshot.Solution;
            Solution edited = committed;
            List<DocumentId> changed = [];
            foreach ((string path, string text) in texts)
            {
                string absolute = Path.GetFullPath(Path.Combine(directory, path));
                (edited, IReadOnlyList<DocumentId> ids) = added.Contains(path)
                    ? Add(edited, absolute, text, scenario.Key)
                    : Replace(edited, absolute, text, scenario.Key);
                changed.AddRange(ids);
            }

            await ShouldCompileAsCommittedDoesAsync(committed, edited, changed, scenario.Key);

            IReadOnlyList<string> changedPaths = changed.Select(id => edited.GetDocument(id)!.FilePath!)
                .Distinct(PathComparison.Comparer)
                .ToList();

            return (await ExtractAsync(edited), changedPaths);
        }

        private static string? ReadCommitted(string directory, string path)
        {
            string absolute = Path.Combine(directory, path);
            return File.Exists(absolute) ? File.ReadAllText(absolute) : null;
        }

        private static (Solution, IReadOnlyList<DocumentId>) Replace(Solution solution, string absolute, string text, string scenario)
        {
            ImmutableArray<DocumentId> ids = solution.GetDocumentIdsWithFilePath(absolute);
            if (ids.IsEmpty)
                throw new InvalidOperationException($"Scenario '{scenario}' edits {absolute}, which no project of the solution compiles.");

            foreach (DocumentId id in ids) solution = solution.WithDocumentText(id, SourceText.From(text));
            return (solution, ids);
        }

        // An imagined file goes into the project whose directory holds it, under the folders between the two, as
        // a file dropped there would.
        private static (Solution, IReadOnlyList<DocumentId>) Add(Solution solution, string absolute, string text, string scenario)
        {
            Project project = solution.Projects
                                  .Where(candidate => candidate.FilePath is not null
                                                      && absolute.StartsWith(Path.GetDirectoryName(candidate.FilePath)! + Path.DirectorySeparatorChar, PathComparison.Comparison))
                                  .MaxBy(candidate => candidate.FilePath!.Length)
                              ?? throw new InvalidOperationException($"Scenario '{scenario}' imagines {absolute}, which no project's directory holds.");

            string relative = Path.GetRelativePath(Path.GetDirectoryName(project.FilePath)!, absolute);
            string[] folders = (Path.GetDirectoryName(relative) ?? "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            Document document = project.AddDocument(Path.GetFileName(absolute), SourceText.From(text), folders, absolute);

            return (document.Project.Solution, [document.Id]);
        }

        // The pages say every scenario builds, which is what makes each a thing an agent could write: an edit that
        // does not compile would be quoted as output no build could produce. Judged against the committed
        // compilation, so a diagnostic the workspace raises on the tree as it stands is not charged to the edit.
        private static async Task ShouldCompileAsCommittedDoesAsync(
            Solution committed, Solution edited, IReadOnlyList<DocumentId> changed, string scenario)
        {
            List<string> introduced = [];
            foreach (ProjectId projectId in changed.Select(static id => id.ProjectId)
                         .Distinct())
            {
                IReadOnlyList<Diagnostic> before = await ErrorsAsync(committed.GetProject(projectId));
                IReadOnlyList<Diagnostic> after = await ErrorsAsync(edited.GetProject(projectId));

                HashSet<string> known = before.Select(Identity)
                    .ToHashSet(StringComparer.Ordinal);
                introduced.AddRange(after.Where(diagnostic => !known.Contains(Identity(diagnostic)))
                    .Select(static diagnostic => diagnostic.ToString()));
            }

            introduced.ShouldReportNothing($"Scenario '{scenario}' does not compile, so no build could print what its fences quote");
        }

        private static async Task<IReadOnlyList<Diagnostic>> ErrorsAsync(Project? project)
        {
            Compilation? compilation = project is null ? null : await project.GetCompilationAsync(CancellationToken.None);
            if (compilation is null) return [];

            return compilation.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();
        }

        private static string Identity(Diagnostic diagnostic)
        {
            return $"{diagnostic.Id} {diagnostic.GetMessage(CultureInfo.InvariantCulture)}";
        }
    }
}

/// <summary>
///     One example's replay: its <see cref="Tree" /> as committed, each of its <see cref="Scenarios" />, what
///     <c>render</c> reports over it, what <c>check</c> and <c>status</c> print over it, and for Meridian the
///     adoption walkthrough's day-zero steps.
/// </summary>
/// <param name="Example">The example's name, which is its directory under <c>examples/</c>.</param>
/// <param name="SolutionDirectory">The example's directory, which every printed path is relative to.</param>
/// <param name="Governed">The codebase the verbs extract, the spec project and its plumbing left out.</param>
/// <param name="Committed">The same codebase, extracted by the call every scenario's edit is extracted by.</param>
/// <param name="Tree">The committed tree's check, as the verbs print it.</param>
/// <param name="Scenarios">Each scenario's check, keyed by the scenario.</param>
/// <param name="RenderLines">The lines <c>render</c> prints over the committed tree.</param>
/// <param name="Adoption">The day-zero steps, which only Meridian's adoption walkthrough takes.</param>
/// <param name="CheckVerb">What <c>check --no-cache</c> printed over the committed tree.</param>
/// <param name="StatusVerb">What <c>status --no-cache</c> printed over it.</param>
internal sealed record ExampleRun(
    string Example,
    string SolutionDirectory,
    CodebaseModel Governed,
    CodebaseModel Committed,
    ScenarioRun Tree,
    IReadOnlyDictionary<string, ScenarioRun> Scenarios,
    IReadOnlyList<string> RenderLines,
    AdoptionRun? Adoption,
    CliResult CheckVerb,
    CliResult StatusVerb)
{
    /// <summary>The run of the scenario keyed <paramref name="key" />.</summary>
    internal ScenarioRun Scenario(string key)
    {
        return Scenarios.TryGetValue(key, out ScenarioRun? run)
            ? run
            : throw new InvalidOperationException($"{Example}'s replay holds no scenario keyed '{key}'.");
    }

    /// <summary>The adoption walkthrough's run, which exists for Meridian alone.</summary>
    internal AdoptionRun AdoptionSteps => Adoption ?? throw new InvalidOperationException($"{Example} has no adoption walkthrough.");
}

/// <summary>
///     One tree's check, as <paramref name="Plain" /> ran it and as a diff-aware run over the files the tree
///     changed ran it (<paramref name="Diff" />), with every path printed relative to
///     <paramref name="SolutionDirectory" />.
/// </summary>
internal sealed record ScenarioRun(CheckReport Plain, CheckReport Diff, string SolutionDirectory)
{
    /// <summary>The whole report <c>check</c> prints.</summary>
    internal string Report => Rendered(Plain, SolutionDirectory, HumanReportRenderer.Render);

    /// <summary>The whole report <c>check --diff-base</c> prints.</summary>
    internal string DiffReport => Rendered(Diff, SolutionDirectory, HumanReportRenderer.Render);

    /// <summary>The report a hook's context carries: the diff-aware run with its quiet passes left out.</summary>
    internal string DiffWithoutPasses => Rendered(Diff, SolutionDirectory, HumanReportRenderer.RenderWithoutPasses);

    /// <summary>The document a <c>Stop</c> hook writes when the diff-aware run passes with something to say.</summary>
    internal string StopHookDocument => HookReportRenderer.Document(DiffWithoutPasses, HookReportRenderer.StopEvent);

    /// <summary>The board <c>status</c> prints.</summary>
    internal IReadOnlyList<string> StatusLines => StatusFormatter.Lines(Plain);

    /// <summary>The block <c>check</c> prints for <paramref name="ruleId" />, which is also a test's failure message.</summary>
    internal string RuleBlock(string ruleId)
    {
        RuleResult result = Plain.Results.SingleOrDefault(candidate => candidate.Rule.Id == ruleId)
                            ?? throw new InvalidOperationException($"The run reports no rule '{ruleId}'.");

        return HumanReportRenderer.RuleBlock(result, SolutionDirectory);
    }

    /// <summary>
    ///     <paramref name="report" /> as <paramref name="render" /> writes it, paths relative to
    ///     <paramref name="solutionDirectory" />, LF throughout and with no trailing newline.
    /// </summary>
    internal static string Rendered(
        CheckReport report, string solutionDirectory, Action<TextWriter, CheckReport, string> render)
    {
        var writer = new StringWriter { NewLine = "\n" };
        render(writer, report, solutionDirectory);
        return writer.ToString()
            .TrimEnd('\n');
    }
}

/// <summary>
///     The adoption walkthrough's day-zero steps over Meridian, as the verbs would have printed them.
/// </summary>
/// <param name="Survey">The <c>graph --json</c> document over the three projects the solution held.</param>
/// <param name="DraftReport">The <c>check</c> report over the drafted spec, nothing yet grandfathered.</param>
/// <param name="BaselineInit">The lines <c>baseline init</c> prints over the curated spec.</param>
/// <param name="BaselineFiles">What it wrote, keyed by the path relative to the solution directory.</param>
internal sealed record AdoptionRun(
    string Survey,
    string DraftReport,
    IReadOnlyList<string> BaselineInit,
    IReadOnlyDictionary<string, byte[]> BaselineFiles);
