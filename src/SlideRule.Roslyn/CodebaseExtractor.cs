using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>
///     Builds the <see cref="CodebaseModel" /> a check reads out of Roslyn compilations: the types a codebase
///     declares, the members they declare, and the <c>file:line</c> edges between them. Use
///     <see cref="ExtractFromSolutionAsync" /> for a solution loaded by <see cref="WorkspaceLoader" /> or a
///     <see cref="WorkspaceSession" />, or <see cref="ExtractFromCompilations" /> for compilations you
///     built yourself, which needs no MSBuild at all.
/// </summary>
internal static class CodebaseExtractor
{
    /// <summary>
    ///     Extracts the model from compilations you built yourself, with no MSBuild involved. Every input's
    ///     types are declared before any reference is resolved, so a type one compilation references and
    ///     another declares is one node in the model, matched by fully-qualified name. Where two inputs declare
    ///     the same type, the first in <paramref name="inputs" /> supplies its facts.
    /// </summary>
    /// <param name="inputs">The compilations to extract from, in the order they are declared.</param>
    /// <returns>The model built from every compilation in <paramref name="inputs" />.</returns>
    public static CodebaseModel ExtractFromCompilations(IReadOnlyList<CompilationInput> inputs)
    {
        return CodebaseModelBuilder.Build(inputs, null, CancellationToken.None);
    }

    /// <summary>
    ///     Extracts the model from a loaded solution: its C# projects in ordinal name order, each with its
    ///     compilation and the names of the projects it declares a reference to. Load the solution first,
    ///     through <see cref="WorkspaceLoader" /> or a <see cref="WorkspaceSession" />; restore or build it
    ///     before that, because a project whose packages were never restored loads without them and the types
    ///     it gets from them are absent from the model.
    /// </summary>
    /// <param name="solution">The loaded solution.</param>
    /// <param name="excludeProjects">
    ///     Project names to leave out of the model — the way a spec project that is itself a member of the
    ///     solution stays out of its own check. Null excludes nothing.
    /// </param>
    /// <param name="targetFrameworks">
    ///     The per-project target frameworks the load reported
    ///     (<see cref="LoadedSolution.TargetFrameworks" />), so a fact taken from a multi-target-framework
    ///     project records the framework it came from. Null, or a project absent from it, leaves the framework
    ///     unstamped, which is the single-framework norm.
    /// </param>
    /// <param name="declaredMembers">
    ///     The absolute <c>.csproj</c> paths the solution file itself declares, symlink-resolved, stamped onto
    ///     each project as <see cref="ProjectNode.SolutionMember" /> so that a project the workspace loaded
    ///     only because a <c>ProjectReference</c> named it can be told from one the solution claims. Null
    ///     leaves every project unlabeled, which is what an unreadable solution file has to degrade to.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     The model built from the solution's C# projects, less any named in
    ///     <paramref name="excludeProjects" />.
    /// </returns>
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
    ///     <paramref name="includeProjects" /> (null extracts every C# project), in the same ordinal
    ///     project order and with the same per-project <see cref="CompilationInput" />s the merge-producing
    ///     path uses — so the fragments a workspace-loaded run persists and the fragments a
    ///     cache hit replays go through the identical <see cref="FragmentMerger" />, and the cache cannot
    ///     change results by construction.
    /// </summary>
    /// <remarks>
    ///     <see cref="MethodImplOptions.NoInlining" /> keeps the JIT from resolving Roslyn types before
    ///     <c>MSBuildLocator</c> registration in non-test hosts, matching <see cref="ExtractFromSolutionAsync" />.
    /// </remarks>
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

    // The shared project enumeration behind both entry points: C# projects passing the filter, ordered by
    // (name, target framework), each turned into a CompilationInput carrying the names of the projects it
    // declares a reference to, and its framework. One enumeration means the extract-all-then-merge cold
    // path, the extract-fragments cache path, and cache hits all order identically.
    //
    // The framework tiebreak is required, not cosmetic. One project file's several compilations share
    // a name, so ordering by name alone would leave them in whatever order the solution enumerated them —
    // <TargetFrameworks> declaration order — and the merge gives each type they share the facts of the FIRST
    // input that declares it. Ordering on the framework fixes which one that is, so reordering a csproj's
    // framework list cannot silently move a type's facts.
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
