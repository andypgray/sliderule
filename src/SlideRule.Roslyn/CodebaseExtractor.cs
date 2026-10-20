using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>Builds the <see cref="CodebaseModel" /> a check reads out of Roslyn compilations.</summary>
internal static class CodebaseExtractor
{
    /// <summary>
    ///     Extracts the model from compilations built without MSBuild. Where two of <paramref name="inputs" />
    ///     declare the same type, the first supplies its facts.
    /// </summary>
    public static CodebaseModel ExtractFromCompilations(IReadOnlyList<CompilationInput> inputs)
    {
        return CodebaseModelBuilder.Build(inputs, null, CancellationToken.None);
    }

    /// <summary>
    ///     Extracts the model from a loaded solution's C# projects, less those named in
    ///     <paramref name="excludeProjects" />.
    /// </summary>
    /// <remarks>
    ///     <paramref name="targetFrameworks" /> is the load's <see cref="LoadedSolution.TargetFrameworks" />: a fact
    ///     from a multi-targeted project records the framework it came from, and a project absent from the map, or
    ///     a null map, leaves the framework unstamped, the single-framework norm.
    ///     <paramref name="declaredMembers" />, the absolute symlink-resolved <c>.csproj</c> paths the solution file
    ///     declares, sets <see cref="ProjectNode.SolutionMember" />, so a project the workspace loaded only because
    ///     a <c>ProjectReference</c> named it can be told from one the solution claims; null, which an unreadable
    ///     solution file degrades to, leaves every project unlabeled.
    /// </remarks>
    // MethodImplOptions.NoInlining keeps the JIT from resolving Roslyn types before MSBuildLocator
    // registration in non-test hosts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<CodebaseModel> ExtractFromSolutionAsync(
        Solution solution,
        IReadOnlyCollection<string>? excludeProjects = null,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        ExtractionInputs collected = await CollectInputsAsync(
            solution, p => excludeProjects is null || !excludeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct).ConfigureAwait(false);
        return CodebaseModelBuilder.Build(collected.Inputs, collected.ArtifactFacts, ct);
    }

    /// <summary>
    ///     Extracts one self-contained <see cref="CodebaseFragment" /> per C# project matched by
    ///     <paramref name="includeProjects" /> (null extracts every C# project), from the same inputs
    ///     <see cref="ExtractFromSolutionAsync" /> builds, so stored fragments merged through the one
    ///     <see cref="FragmentMerger" /> yield the model a cold run would.
    /// </summary>
    // MethodImplOptions.NoInlining keeps the JIT from resolving Roslyn types before MSBuildLocator
    // registration in non-test hosts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static async Task<IReadOnlyList<CodebaseFragment>> ExtractFragmentsAsync(
        Solution solution,
        IReadOnlyCollection<string>? includeProjects,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks = null,
        IReadOnlySet<string>? declaredMembers = null,
        CancellationToken ct = default)
    {
        ExtractionInputs collected = await CollectInputsAsync(
            solution, p => includeProjects is null || includeProjects.Contains(p.Name), targetFrameworks,
            declaredMembers, ct).ConfigureAwait(false);
        return FragmentExtractor.ExtractAll(collected.Inputs, collected.ArtifactFacts, ct);
    }

    // The one project enumeration behind both entry points, ordered by (name, target framework), so the
    // extract-all-then-merge cold path, the extract-fragments cache path and cache hits all order identically.
    //
    // The framework tiebreak is required. One project file's several compilations share a name, so ordering
    // by name alone would leave them in whatever order the solution enumerated them (<TargetFrameworks>
    // declaration order), and the merge gives each type they share the facts of the FIRST input that declares
    // it. Ordering on the framework fixes which one that is, so reordering a csproj's framework list cannot
    // silently move a type's facts.
    private static async Task<ExtractionInputs> CollectInputsAsync(
        Solution solution,
        Func<Project, bool> include,
        IReadOnlyDictionary<ProjectId, string>? targetFrameworks,
        IReadOnlySet<string>? declaredMembers,
        CancellationToken ct)
    {
        List<Project> projects = solution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .Where(include)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ThenBy(p => TargetFrameworkOf(targetFrameworks, p) ?? "", StringComparer.Ordinal)
            .ToList();

        ct.ThrowIfCancellationRequested();

        // The artifact facts come from MSBuild rather than from Roslyn, so this batch shares nothing with the
        // binding below and runs beside it: evaluation is a few serial walks, each of its own ProjectCollection,
        // and binding is the half it would otherwise wait behind. One request per input, in input order, so the
        // results read back by the same index as everything else here. The solution's own file, not the path a
        // caller was handed, because it is what the workspace load derived its solution properties from: under a
        // solution filter it is the solution the filter names, and a replayed solution carries the one it was
        // stamped with.
        List<ProjectEvaluationRequest> evaluationRequests = projects
            .Select(project => new ProjectEvaluationRequest(project.FilePath, TargetFrameworkOf(targetFrameworks, project)))
            .ToList();
        string? solutionFile = solution.FilePath;
        Task<IReadOnlyList<ProjectArtifactFacts?>> artifactFactsTask =
            Task.Run(() => ProjectFactsEvaluator.EvaluateAll(evaluationRequests, solutionFile, ct), ct);

        // Binding is the expensive half and the projects are independent, so they bind together rather than
        // one after another. The ordered list above decides input order — the results are read back by
        // index, never in completion order — so the merge's first-declarer-wins rule sees that order.
        IEnumerable<Task<Compilation?>> compilationTasks = projects.Select(project => project.GetCompilationAsync(ct));
        Compilation?[] compilations = await Task.WhenAll(compilationTasks).ConfigureAwait(false);

        // The provenance half of the generated-code signal (GRAMMAR §5.2), read once per project. It runs
        // AFTER the compilation batch rather than beside it because binding is what executes the generators:
        // by here every generated document is realized, so this batch is a cache read rather than a second
        // round of generator work.
        IEnumerable<Task<IReadOnlySet<SyntaxTree>>> generatedTreeTasks =
            projects.Select(project => GeneratedTreesOfAsync(project, ct));
        IReadOnlySet<SyntaxTree>[] generatedTrees = await Task.WhenAll(generatedTreeTasks).ConfigureAwait(false);

        // One canonicalizer for the whole enumeration: every project's membership is tested against a
        // canonicalized path, and the projects of one solution share nearly all of their ancestors.
        var canonicalProjectFiles = new ProjectFileCanonicalizer();

        IReadOnlyList<ProjectArtifactFacts?> evaluated = await artifactFactsTask.ConfigureAwait(false);

        // Every loaded project, whatever its language and whether or not this call extracts it, because a
        // declared reference may name any project of the workspace, and the filter above can hold as few as
        // the one project that changed.
        IEnumerable<(string? FilePath, string Name)> loadedProjects = solution.Projects.Select(p => (p.FilePath, p.Name));
        IReadOnlyDictionary<string, string> namesByProjectFile =
            DeclaredProjectReferences.NamesByProjectFile(loadedProjects, canonicalProjectFiles.Resolve);
        Func<string, IEnumerable<string>> workspaceReach = WorkspaceReach(solution);

        List<CompilationInput> inputs = [];
        List<ProjectArtifactFacts?> artifactFacts = [];
        for (var i = 0; i < projects.Count; i++)
        {
            if (compilations[i] is not { } compilation) continue;

            // The references the project file declares, and the workspace references they do not account for.
            // Without an evaluation the workspace's own list is all there is, and that list carries the closure
            // the SDK adds, so it accounts for itself.
            Project project = projects[i];
            IReadOnlyList<string> workspaceReferences = WorkspaceReferenceNames(solution, project);
            IReadOnlyList<string> projectReferences = evaluated[i] is { } facts
                ? DeclaredProjectReferences.Names(
                    facts.ProjectReferencePaths, namesByProjectFile, canonicalProjectFiles.Resolve)
                : workspaceReferences;
            IReadOnlyList<string> undeclared = evaluated[i] is null
                ? []
                : DeclaredProjectReferences.Undeclared(workspaceReferences, projectReferences, workspaceReach);

            inputs.Add(new CompilationInput(
                compilation, project.Name, projectReferences, TargetFrameworkOf(targetFrameworks, project),
                SpecExclusion.SolutionMembershipOf(
                    declaredMembers, project.FilePath, canonicalProjectFiles.Resolve(project.FilePath)),
                generatedTrees[i],
                undeclared));
            artifactFacts.Add(evaluated[i]);
        }

        return new ExtractionInputs(inputs, artifactFacts);
    }

    // What each loaded project reaches through the workspace's own references, transitively, by project name: a
    // multi-targeted project's compilations are unioned under the one name they share. Read off the solution's
    // dependency graph, which spans every project of the workspace, because a declared project need not be one
    // this call extracts.
    private static Func<string, IEnumerable<string>> WorkspaceReach(Solution solution)
    {
        ProjectDependencyGraph graph = solution.GetProjectDependencyGraph();
        ILookup<string, ProjectId> idsByName = solution.Projects.ToLookup(p => p.Name, p => p.Id, StringComparer.Ordinal);

        return name => idsByName[name]
            .SelectMany(graph.GetProjectsThatThisProjectTransitivelyDependsOn)
            .Select(id => solution.GetProject(id)?.Name)
            .OfType<string>();
    }

    private static IReadOnlyList<string> WorkspaceReferenceNames(Solution solution, Project project)
    {
        return project.ProjectReferences
            .Select(r => solution.GetProject(r.ProjectId)?.Name)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    // The trees one project's source generators produced, by reference — the identity the extractor tests
    // each declaring tree against. Holding trees rather than the documents' paths is what survives a build
    // that sets EmitCompilerGeneratedFiles, which moves every generated document off its pseudo-path.
    private static async Task<IReadOnlySet<SyntaxTree>> GeneratedTreesOfAsync(Project project, CancellationToken ct)
    {
        IEnumerable<SourceGeneratedDocument> documents = await project.GetSourceGeneratedDocumentsAsync(ct).ConfigureAwait(false);

        var trees = new HashSet<SyntaxTree>();
        foreach (SourceGeneratedDocument document in documents)
            if (await document.GetSyntaxTreeAsync(ct).ConfigureAwait(false) is { } tree)
                trees.Add(tree);

        return trees;
    }

    private static string? TargetFrameworkOf(IReadOnlyDictionary<ProjectId, string>? targetFrameworks, Project project)
    {
        if (targetFrameworks is null) return null;

        return targetFrameworks.GetValueOrDefault(project.Id);
    }
}
