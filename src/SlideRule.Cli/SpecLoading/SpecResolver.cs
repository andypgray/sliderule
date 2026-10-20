using Microsoft.CodeAnalysis;
using SlideRule.Cli.Pipeline;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     One candidate spec project reduced to the tuple the convention needs — so the core resolves without a
///     workspace.
/// </summary>
/// <remarks>
///     <see cref="ReferencePaths" /> carries the project's PE metadata reference paths <em>plus</em> the output
///     paths of its direct project references. <see cref="FilePath" /> is the candidate's own <c>.csproj</c>, which
///     the convention counts by; it carries no default on purpose, so a caller with no project file in hand has to
///     say so out loud. <see cref="IsDeclaredMember" /> says whether the solution file declares this project, and
///     <see cref="IntermediateAssemblyPath" /> is its <c>obj</c>-side assembly.
/// </remarks>
internal sealed record SpecProjectCandidate(
    string Name,
    IReadOnlyList<string> ReferencePaths,
    string? OutputFilePath,
    string? FilePath,
    bool IsDeclaredMember = true,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     The resolved spec: the DLL to load and, when the spec is a solution member, its project name plus the
///     projects to exclude from the checked universe — the spec project and its private plumbing
///     (<see cref="SpecExclusion" />). Both are empty/null for a prebuilt DLL, which excludes nothing.
/// </summary>
/// <remarks>
///     <see cref="OutputFilePaths" /> are the evaluated output paths <see cref="DllPath" /> was chosen from, every
///     framework's, ordinal-sorted, and <see cref="IntermediateAssemblyPath" /> is the <c>obj</c>-side assembly the
///     built-output search refused an intermediate result under; both are null for a prebuilt DLL, which was never
///     searched for.
/// </remarks>
internal sealed record SpecResolution(
    string DllPath,
    string? SpecProjectName,
    IReadOnlyCollection<string> ExcludeProjectNames,
    IReadOnlyList<string>? OutputFilePaths = null,
    string? IntermediateAssemblyPath = null);

/// <summary>
///     Resolves which spec DLL to load. The CLI
///     never builds: <c>--spec</c> takes a prebuilt DLL or a solution-member csproj (resolved to its
///     output DLL); with no <c>--spec</c>, the convention picks the unique <em>declared</em> solution project
///     that references <c>SlideRule.dll</c>. Every failure is a loud <see cref="UserErrorException" />;
///     a spec project that is a solution member is excluded from the codebase the checker sees, along with the
///     plumbing only it pulls in.
/// </summary>
internal static class SpecResolver
{
    private const string CoreAssemblyFile = "SlideRule.dll";

    // How many entries a refusal quotes before it counts the rest. A refusal nobody reads to the end names
    // nothing; three is enough to see whether the failures share a cause.
    private const int MaxQuotedDiagnostics = 3;

    /// <summary>Resolves the spec against a loaded solution.</summary>
    /// <remarks>
    ///     <paramref name="declaredMembers" /> is null when membership could not be read.
    ///     <paramref name="diagnostics" /> is deliberately not defaulted: whether the workspace loaded cleanly
    ///     decides what zero convention candidates mean (<see cref="ResolveConventionProject" />), and every caller
    ///     knows it. A default is also untypeable: <see cref="WorkspaceDiagnostics.None" /> is a property, and
    ///     <c>default(WorkspaceDiagnostics)</c> carries null lists.
    /// </remarks>
    internal static SpecResolution Resolve(
        Solution solution, IReadOnlySet<string>? declaredMembers, string? specArgument,
        WorkspaceDiagnostics diagnostics)
    {
        return string.IsNullOrWhiteSpace(specArgument)
            ? ResolveByConvention(solution, declaredMembers, diagnostics)
            : ResolveExplicit(solution, declaredMembers, specArgument);
    }

    /// <summary>
    ///     The workspace-free half of resolution: a built-DLL <c>--spec</c> resolves directly, because that
    ///     branch never touches the <see cref="Solution" />.
    /// </summary>
    /// <returns>
    ///     The resolution, or <see langword="null" /> when resolution needs the workspace — the convention
    ///     default (no <c>--spec</c>) and a solution-member csproj.
    /// </returns>
    /// <remarks>A DLL path that does not exist is still a loud error.</remarks>
    internal static SpecResolution? TryResolveWithoutSolution(string? specArgument)
    {
        if (string.IsNullOrWhiteSpace(specArgument)) return null;
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return null;

        string fullPath = Path.GetFullPath(specArgument);
        if (!File.Exists(fullPath))
            throw new UserErrorException($"--spec '{specArgument}' was not found. Pass a built spec DLL or a solution-member csproj.");

        return new SpecResolution(fullPath, null, []);
    }

    private static SpecResolution ResolveExplicit(
        Solution solution, IReadOnlySet<string>? declaredMembers, string specArgument)
    {
        if (specArgument.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            string canonicalPath = PathCanonicalizer.Resolve(Path.GetFullPath(specArgument));
            List<CanonicalProject> projects = CanonicalProjectsOf(solution);
            List<CanonicalProject> sharingProjects = ProjectsSharingFile(projects, canonicalPath);
            if (sharingProjects.Count == 0)
                throw new UserErrorException(
                    $"--spec '{specArgument}' is not a project in the solution. " +
                    "Pass a built spec DLL or a csproj that is a member of the target solution.");

            BuiltOutputInputs built = BuiltOutputsOf(sharingProjects);
            return MemberResolution(solution, projects, declaredMembers, sharingProjects[0].Project.Name, built);
        }

        // A DLL path — the branch that needs no solution.
        return TryResolveWithoutSolution(specArgument)!;
    }

    private static SpecResolution ResolveByConvention(
        Solution solution, IReadOnlySet<string>? declaredMembers, WorkspaceDiagnostics diagnostics)
    {
        List<CanonicalProject> projects = CanonicalProjectsOf(solution);

        List<SpecProjectCandidate> candidates = projects
            .Where(entry => entry.Project.Language == LanguageNames.CSharp)
            .Select(entry => new SpecProjectCandidate(
                entry.Project.Name,
                ReferencePathsOf(entry.Project, solution),
                entry.Project.OutputFilePath,
                entry.Project.FilePath,
                SpecExclusion.IsDeclaredMember(declaredMembers, entry.Project.FilePath, entry.CanonicalFilePath),
                entry.Project.CompilationOutputInfo.AssemblyPath))
            .ToList();

        SpecProjectCandidate chosen = ResolveConventionProject(candidates, diagnostics);
        BuiltOutputInputs built = BuiltOutputsOfProjectFile(
            projects, chosen.FilePath, chosen.OutputFilePath, chosen.IntermediateAssemblyPath);
        return MemberResolution(solution, projects, declaredMembers, chosen.Name, built);
    }

    // A null declaredMembers is the unreadable case, which SpecExclusion degrades to just the spec project.
    private static SpecResolution MemberResolution(
        Solution solution,
        IReadOnlyList<CanonicalProject> projects,
        IReadOnlySet<string>? declaredMembers,
        string specProjectName,
        BuiltOutputInputs built)
    {
        List<string> evaluatedPaths = NormalizeEvaluatedPaths(built.OutputFilePaths);

        return new SpecResolution(
            RequireBuiltOutput(specProjectName, evaluatedPaths, built.IntermediateAssemblyPath),
            specProjectName,
            SpecExclusion.Compute(ExclusionProjectsOf(solution, projects), declaredMembers, specProjectName),
            evaluatedPaths,
            built.IntermediateAssemblyPath);
    }

    // The exclusion walk's view of the solution, projected off this resolution's own canonical view rather
    // than re-derived from the workspace: the walk's membership test canonicalizes each project file, and
    // every one of those paths is already resolved here. Calling the pure core directly is what carries the
    // canonical spelling in — the workspace-taking overload has no view to carry.
    private static List<SpecExclusionProject> ExclusionProjectsOf(
        Solution solution, IReadOnlyList<CanonicalProject> projects)
    {
        return projects
            .Select(entry => new SpecExclusionProject(
                entry.Project.Name,
                entry.Project.FilePath,
                ReferencedProjectNames(entry.Project, solution),
                entry.CanonicalFilePath))
            .ToList();
    }

    private static IReadOnlyList<string> ReferencedProjectNames(Project project, Solution solution)
    {
        return project.ProjectReferences
            .Select(reference => solution.GetProject(reference.ProjectId)?.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    // Both sides of the scan are canonicalized exactly once: canonicalizing probes the filesystem for a
    // reparse point per path segment, so re-resolving the needle per candidate — or the haystack per scan —
    // costs one such walk per project in the solution, on every warm tool call.
    private static BuiltOutputInputs BuiltOutputsOfProjectFile(
        IReadOnlyList<CanonicalProject> projects,
        string? projectFilePath,
        string? evaluatedOutputFilePath,
        string? evaluatedIntermediatePath)
    {
        if (string.IsNullOrEmpty(projectFilePath))
            return new BuiltOutputInputs([evaluatedOutputFilePath], evaluatedIntermediatePath);

        string canonicalPath = PathCanonicalizer.Resolve(projectFilePath);
        List<CanonicalProject> sharingProjects = ProjectsSharingFile(projects, canonicalPath);
        return BuiltOutputsOf(sharingProjects);
    }

    // A multi-target-framework csproj yields one Roslyn project per framework, so any single project's
    // OutputFilePath names an arbitrary framework's DLL; the built-output check needs them all so it can pick
    // one that is actually on disk — and so a cache hit, which records the same set, replays the identical
    // choice. One intermediate path suffices even then: the search derives the intermediate ROOT by peeling
    // the prefix the evaluated and intermediate paths share, and the two diverge at the same segment level
    // whichever framework's pair it starts from, so the derived root is framework-invariant. Ordinal-first
    // keeps that choice deterministic rather than load-order dependent.
    private static BuiltOutputInputs BuiltOutputsOf(IReadOnlyList<CanonicalProject> sharingProjects)
    {
        List<string?> outputFilePaths = sharingProjects
            .Select(entry => entry.Project.OutputFilePath)
            .ToList();
        string? intermediateAssemblyPath = sharingProjects
            .Select(entry => entry.Project.CompilationOutputInfo.AssemblyPath)
            .Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

        return new BuiltOutputInputs(outputFilePaths, intermediateAssemblyPath);
    }

    // Every project the solution carries, beside the canonicalized spelling of its own .csproj, resolved once for
    // the whole resolution, so every scan a resolution runs asks its question against this one view.
    private static List<CanonicalProject> CanonicalProjectsOf(Solution solution)
    {
        return solution.Projects
            .Select(project => new CanonicalProject(project, CanonicalFileOf(project)))
            .ToList();
    }

    private static string? CanonicalFileOf(Project project)
    {
        return string.IsNullOrEmpty(project.FilePath) ? null : PathCanonicalizer.Resolve(project.FilePath);
    }

    // The projects built from one already-canonicalized .csproj, in solution order. Canonicalizing this side too
    // (symlinks resolved) is what lets a solution opened through a symlinked root still match, and the per-OS
    // comparison (PathComparison) is what stops the match from missing across symlink spellings or over-matching
    // case-variant paths on a case-sensitive file system.
    private static List<CanonicalProject> ProjectsSharingFile(
        IReadOnlyList<CanonicalProject> projects, string canonicalProjectFile)
    {
        return projects
            .Where(entry => entry.CanonicalFilePath is { } canonical
                            && string.Equals(canonical, canonicalProjectFile, PathComparison.Comparison))
            .ToList();
    }

    /// <summary>
    ///     A project's reference paths as the convention sees them: PE metadata references (the package /
    ///     built-DLL shape) plus the output paths of direct project references (the source-checkout shape,
    ///     where the contract library arrives as a <c>ProjectReference</c> and never appears among the PE
    ///     metadata references).
    /// </summary>
    private static IReadOnlyList<string> ReferencePathsOf(Project project, Solution solution)
    {
        IEnumerable<string> metadataPaths = project.MetadataReferences
            .OfType<PortableExecutableReference>()
            .Select(r => r.FilePath ?? string.Empty);

        IEnumerable<string> projectReferenceOutputs = project.ProjectReferences
            .Select(r => solution.GetProject(r.ProjectId)?.OutputFilePath ?? string.Empty);

        return metadataPaths.Concat(projectReferenceOutputs).ToList();
    }

    /// <summary>
    ///     The pure convention core: the unique <em>declared</em> solution member that references
    ///     <c>SlideRule.dll</c>. Zero candidates and multiple candidates are both loud errors. Undeclared projects
    ///     are workspace passengers — a spec project's own <c>ProjectReference</c>s pull the contract library and
    ///     any rule-pack library in — and a passenger that references the contract is not a spec project the user
    ///     chose to have. Several Roslyn projects sharing one <c>.csproj</c> (a multi-target-framework spec
    ///     project) count once.
    /// </summary>
    /// <remarks>
    ///     Zero candidates is reported against <paramref name="diagnostics" />, because a workspace that did
    ///     not load cleanly produces exactly the same zero: a project that failed to load declares no
    ///     reference to match, and an unresolved package reference means the spec project's reference to the
    ///     contract library is not there to match either.
    /// </remarks>
    internal static SpecProjectCandidate ResolveConventionProject(
        IReadOnlyList<SpecProjectCandidate> candidates, WorkspaceDiagnostics diagnostics)
    {
        List<SpecProjectCandidate> matches = candidates.Where(c => c.IsDeclaredMember && ReferencesCore(c)).ToList();

        if (matches.Count == 0) throw NoSpecProjectFound(candidates, diagnostics);

        List<IGrouping<string, SpecProjectCandidate>> groups = matches
            .Select((candidate, index) => (Candidate: candidate, Key: GroupKey(candidate, index)))
            .GroupBy(match => match.Key, match => match.Candidate, StringComparer.Ordinal)
            .ToList();

        if (groups.Count > 1)
        {
            IOrderedEnumerable<string> lines = groups
                .Select(group => DisambiguationLine(group.First()))
                .OrderBy(line => line, StringComparer.Ordinal);

            throw new UserErrorException(
                "Multiple spec projects found; pass --spec to disambiguate:\n  " + string.Join("\n  ", lines));
        }

        return matches[0];
    }

    // Zero candidates has causes whose remedies do not overlap, and a refusal naming only the first sends readers
    // to write an argument that cannot help them, so the strongest evidence decides the refusal. Failed loads and
    // unresolved packages are the incomplete-model gate's two causes, said in its words and composed when one run
    // breaks both ways: no --spec argument repairs a load, and a broken restore (the measured locked-mode shape)
    // leaves the spec project's reference to the contract library unresolved while the project itself still loads
    // completely. Load problems that blame no project come next, worded "may" because that is all they show. A
    // clean load means the solution really has no spec project; that sentence stays byte-identical
    // (GETTING-STARTED.md quotes it), with the count of projects considered, which tells a reader whether the
    // workspace held what they expected. NuGetAudit advisories are kept out of the "may" arm's input, and only
    // that arm's: an advisory's publication date says nothing about whether this solution's references resolved,
    // so a solution with no spec project must not be sent to fix its restore. Every arm is the same refusal with
    // different evidence, so no exit code turns on which one fires.
    private static UserErrorException NoSpecProjectFound(
        IReadOnlyList<SpecProjectCandidate> candidates, WorkspaceDiagnostics diagnostics)
    {
        // The refusal is thrown before a CodebaseSource exists, so no runner renders the evidence beside it —
        // the message has to carry it itself, in the shape every other evidence block takes. CliErrorMapper
        // writes it a line apiece, so this reads the same on stderr and in an MCP error result.
        if (diagnostics.IsIncomplete)
            return new UserErrorException(IncompleteModelGate.SpecRefusal(diagnostics));

        IReadOnlyList<string> actionable = diagnostics.ActionableDiagnostics;
        if (actionable.Count > 0)
            return new UserErrorException(
                EvidenceBlock.Compose(
                    "No spec project found: the workspace did not load cleanly, so a project that references "
                    + "SlideRule.dll may have failed to resolve it:",
                    actionable,
                    "Restore and build the solution first (dotnet restore, dotnet build), then retry.",
                    withSelectionNote: true,
                    quoteCap: MaxQuotedDiagnostics));

        return new UserErrorException(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.\n"
            + $"Considered {candidates.Count} C# project(s) in the workspace.");
    }

    // Group on the canonicalized project file, folded so a plain ordinal comparer carries the per-OS case rule.
    // A candidate with no project file is its own group: folding the unknown-path candidates together
    // would turn genuine ambiguity green, and '\0' cannot occur in a path so the sentinel cannot collide.
    private static string GroupKey(SpecProjectCandidate candidate, int index)
    {
        return string.IsNullOrEmpty(candidate.FilePath)
            ? $"\0{index}"
            : PathComparison.Fold(PathCanonicalizer.Resolve(candidate.FilePath));
    }

    // One line of the ambiguity error. The project file is what the reader has to pass to --spec, so it is
    // named whenever it is known; a candidate with no project file renders as the bare name alone.
    private static string DisambiguationLine(SpecProjectCandidate candidate)
    {
        return string.IsNullOrEmpty(candidate.FilePath)
            ? candidate.Name
            : $"{candidate.Name}  ({candidate.FilePath})";
    }

    // Over the file name as a span: a large solution carries tens of thousands of metadata reference paths,
    // and the substring GetFileName(string) would allocate for each is one comparison's worth of garbage.
    private static bool ReferencesCore(SpecProjectCandidate candidate)
    {
        return candidate.ReferencePaths.Any(path =>
            Path.GetFileName(path.AsSpan()).Equals(CoreAssemblyFile, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     The one built output the spec loads from, given every path the projects behind one <c>.csproj</c>
    ///     evaluated to — one per target framework for a multi-target-framework spec project. The first in
    ///     ordinal path order that is actually on disk wins, each path getting the
    ///     <see cref="BuiltOutputProbe">built-output search</see> before the next is tried; none built is a
    ///     loud error naming every path tried.
    /// </summary>
    /// <remarks>
    ///     Which framework's DLL wins is deliberately "whichever is built", not "whichever matches the host":
    ///     the CLI never builds, so existence is the question that matters, and a spec DLL is loaded across
    ///     frameworks routinely (this repo loads a <c>net48</c> spec from a <c>net10.0</c> host).
    /// </remarks>
    internal static string RequireBuiltOutput(
        string projectName, IReadOnlyList<string?> outputFilePaths, string? intermediateAssemblyPath = null)
    {
        List<string> evaluatedPaths = NormalizeEvaluatedPaths(outputFilePaths);

        foreach (string outputFilePath in evaluatedPaths)
        {
            // The evaluated path exists: it is the answer, with no search and — deliberately — no
            // intermediate refusal. Binlog replay hands an obj-side assembly as this primary argument
            // (BinlogReplayer.NormalizeProjects), because a capture records no other path, so refusing an
            // intermediate here would refuse every replayed run.
            if (File.Exists(outputFilePath)) return outputFilePath;

            // The evaluated path can name a directory no build ever writes, in any configuration — the
            // shapes are BuiltOutputProbe's remarks — so search the tree the build actually wrote.
            if (BuiltOutputProbe.Find(outputFilePath, intermediateAssemblyPath) is { } built) return built;
        }

        throw new UserErrorException(
            $"The spec project '{projectName}' has no built output" +
            (evaluatedPaths.Count == 0 ? "" : $" at '{string.Join("' or '", evaluatedPaths)}'") +
            ". Build the solution first (dotnet build).");
    }

    /// <summary>
    ///     Replays a recorded resolution: the candidate half is kept exactly as recorded, and only the built
    ///     output is resolved again — live, against disk, through the same
    ///     <see cref="RequireBuiltOutput" /> a cold run uses.
    /// </summary>
    /// <remarks>
    ///     A replayed candidate half comes from a workspace its holder no longer has, so a spec output that has since
    ///     been rebuilt, moved or deleted answers in the cold run's words: the bounded search from the output root,
    ///     what that search refuses, and its refusal text. <paramref name="fallbackProjectName" /> is what a refusal
    ///     names when <paramref name="specProjectName" /> is null: the normalized <c>--spec</c> argument, the only
    ///     spelling of the spec the reader ever typed.
    /// </remarks>
    internal static SpecResolution Replay(
        string? specProjectName,
        string fallbackProjectName,
        IReadOnlyCollection<string> excludeProjectNames,
        IReadOnlyList<string>? outputFilePaths,
        string? intermediateAssemblyPath)
    {
        IReadOnlyList<string> evaluatedPaths = outputFilePaths ?? [];
        string dllPath = RequireBuiltOutput(
            specProjectName ?? fallbackProjectName, evaluatedPaths, intermediateAssemblyPath);

        return new SpecResolution(
            dllPath, specProjectName, excludeProjectNames, evaluatedPaths, intermediateAssemblyPath);
    }

    // The evaluated output paths in the one order the built-output search consumes them: blanks dropped,
    // ordinal-sorted, and idempotent. One owner because MemberResolution records for the extraction cache
    // exactly the set RequireBuiltOutput chose from — a hit replays the recorded set, so the two agreeing on
    // that shape is what keeps the replayed choice identical to the cold one.
    private static List<string> NormalizeEvaluatedPaths(IReadOnlyList<string?> outputFilePaths)
    {
        return outputFilePaths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     One Roslyn project beside the canonicalized spelling of its <c>.csproj</c> — <see langword="null" />
    ///     where the workspace reported no project file at all, which is its own answer to every path
    ///     question and never a match.
    /// </summary>
    private sealed record CanonicalProject(Project Project, string? CanonicalFilePath);

    /// <summary>
    ///     The built-output inputs one project file yields, read together because one scan produces both:
    ///     every framework's evaluated output path (nulls included, as the workspace carried them) and the
    ///     single intermediate assembly path the search refuses an <c>obj</c>-side result under.
    /// </summary>
    private sealed record BuiltOutputInputs(
        IReadOnlyList<string?> OutputFilePaths,
        string? IntermediateAssemblyPath);
}
