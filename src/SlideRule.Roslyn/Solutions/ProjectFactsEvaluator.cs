using System.Runtime.CompilerServices;
using Microsoft.Build.Construction;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Internal;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     One project file to evaluate: its path, and the framework to evaluate it for where the load reported
///     one. A null framework is the single-target case, where the project's own
///     <c>&lt;TargetFramework&gt;</c> already decides every condition.
/// </summary>
internal readonly record struct ProjectEvaluationRequest(string? ProjectFile, string? TargetFramework);

/// <summary>
///     Reads a project's artifact facts by <em>evaluating</em> it — the packages it declares and which of them
///     are private, the projects it declares a reference to, whether it packs, whether its restore locks, and
///     the frameworks it targets — rather than by parsing its XML.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why evaluation and not the csproj's own text.</b> Every fact here is regularly decided
///         somewhere the project file does not mention: a <c>Directory.Build.props</c> above it, a package's
///         imported targets, or an SDK default nobody wrote (<c>IsPackable</c> is true unless something says
///         otherwise). A reader of the XML sees the declarations and misses the value; an evaluation sees
///         the value and can still say which declaration won.
///     </para>
///     <para>
///         <b>Lifetime and threading.</b> A batch is split by project file into a few shards that evaluate in
///         parallel, and each shard evaluates serially in a <see cref="ProjectCollection" /> of its own, created
///         for the call and disposed with it — a collection is not safe for concurrent evaluation, and it
///         caches both loaded projects and parsed imports by path, so one shared across calls would answer a
///         later call from an earlier call's read of a file that has since changed. That is the exact
///         staleness the warm session's per-call reconcile exists to prevent, so nothing here outlives the
///         call. The collection is what makes a shard affordable: the projects of one solution share nearly
///         all of their imports, so a shard's first evaluation pays for them and the rest do not, and a few
///         shards pay for them a few times. Each project is unloaded as soon as its facts have been copied out
///         of it — the parsed imports it shares are held by the collection rather than by it, so a shard keeps
///         them while holding one evaluated project at a time rather than all of them.
///     </para>
///     <para>
///         <b>Every failure is an absence.</b> MSBuild may be unavailable in this process, an import may not
///         resolve, a project may not parse. Each of those records nothing for the project rather than a
///         default, because a default here is indistinguishable from a fact and would be checked as one. The
///         filter on the catch is what keeps a cancellation travelling.
///     </para>
/// </remarks>
internal static class ProjectFactsEvaluator
{
    // The line a fact falls back to when it has no declaration of its own to point at, or when the winning
    // declaration is not one this repository owns. The project file is always somewhere a reader can go.
    private const int ProjectFileLine = 1;

    // The two spellings of a project's frameworks, in the order ReadTargetFrameworks tries them.
    private static readonly string[] FrameworkProperties = ["TargetFrameworks", "TargetFramework"];

    // The most shards one batch is split into. Binding runs beside the batch and wants the rest of the machine:
    // past four shards it slows by as much as evaluation speeds up.
    private const int MaxShards = 4;

    /// <summary>
    ///     Evaluates <paramref name="requests" />, returning one result per request by index —
    ///     <see langword="null" /> where that project could not be evaluated at all.
    /// </summary>
    /// <param name="requests">The project files and frameworks to evaluate, in the caller's order.</param>
    /// <param name="solutionFile">
    ///     The solution file the projects were loaded from, which <see cref="SolutionProperties" /> turns into the
    ///     solution-level properties the load evaluated under; <see langword="null" /> evaluates each project as a
    ///     build of that project alone would.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    internal static IReadOnlyList<ProjectArtifactFacts?> EvaluateAll(
        IReadOnlyList<ProjectEvaluationRequest> requests, string? solutionFile, CancellationToken ct)
    {
        int shardCount = Math.Clamp(Environment.ProcessorCount / 2, 1, MaxShards);
        return EvaluateAll(requests, solutionFile, shardCount, ct);
    }

    /// <summary>
    ///     Evaluates <paramref name="requests" /> as at most <paramref name="shardCount" /> shards in parallel,
    ///     returning one result per request by index — <see langword="null" /> where that project could not be
    ///     evaluated at all.
    /// </summary>
    /// <param name="requests">The project files and frameworks to evaluate, in the caller's order.</param>
    /// <param name="solutionFile">
    ///     The solution file the projects were loaded from, or <see langword="null" /> for none; see the overload
    ///     without a shard count.
    /// </param>
    /// <param name="shardCount">The most shards to evaluate at once; one evaluates the batch serially.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static IReadOnlyList<ProjectArtifactFacts?> EvaluateAll(
        IReadOnlyList<ProjectEvaluationRequest> requests, string? solutionFile, int shardCount, CancellationToken ct)
    {
        var facts = new ProjectArtifactFacts?[requests.Count];
        if (requests.Count == 0) return facts;

        IReadOnlyDictionary<string, string> solutionProperties = SolutionProperties(solutionFile);
        IReadOnlyList<IReadOnlyList<int>> shards = Shards(requests, shardCount);
        try
        {
            var options = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = shards.Count };
            Parallel.For(
                0, shards.Count, options,
                shard => EvaluateInto(facts, requests, shards[shard], solutionProperties, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Parallel.For unwraps a cancellation every shard saw, but one shard cancelled beside another shard's
            // failure arrives here in an AggregateException, and still leaves as the caller's cancellation.
            ct.ThrowIfCancellationRequested();

            // MSBuild itself did not load, or a collection could not be built. Every project stays absent,
            // which is the same answer a hand-built extraction gives and is read the same way downstream.
            Array.Clear(facts);
        }

        return facts;
    }

    /// <summary>
    ///     The global properties a project loaded from <paramref name="solutionFile" /> is evaluated under:
    ///     <c>SolutionDir</c>, the solution's directory with a trailing separator, when that directory exists, and
    ///     nothing otherwise.
    /// </summary>
    /// <param name="solutionFile">The solution file the projects were loaded from, or <see langword="null" />.</param>
    // These must be the properties the workspace load evaluated under, or a declared reference that the load
    // followed is lost here and the two reference lists differ by more than the closure: MSBuild's common targets
    // define an unset SolutionDir as *Undefined*, so an include spelled with it becomes a wildcard that matches no
    // file and evaluates to no item at all. Roslyn's
    // MSBuildProjectLoader.SetSolutionProperties defines SolutionDir alone, with this value and under this
    // condition (Workspaces.MSBuild 5.9.0), and WorkspaceLoader passes no properties of its own to
    // MSBuildWorkspace.Create. A solution build also defines SolutionName, SolutionPath, SolutionExt and
    // SolutionFileName; the load does not, so neither does this. If either side changes, this set moves with it.
    internal static IReadOnlyDictionary<string, string> SolutionProperties(string? solutionFile)
    {
        string? directory = solutionFile is { Length: > 0 } ? Path.GetDirectoryName(solutionFile) : null;
        if (directory is not { Length: > 0 } || !Directory.Exists(directory))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SolutionDir"] = directory + Path.DirectorySeparatorChar
        };
    }

    /// <summary>
    ///     The request indices each shard evaluates: project files dealt round-robin over at most
    ///     <paramref name="shardCount" /> shards, in request order.
    /// </summary>
    // Every request for one project file lands in one shard, so a shard's own dedup still sees each repeat of
    // a project (a multi-target project is requested once per framework) and its cone memo every framework.
    internal static IReadOnlyList<IReadOnlyList<int>> Shards(
        IReadOnlyList<ProjectEvaluationRequest> requests, int shardCount)
    {
        int limit = Math.Max(shardCount, 1);
        var shardOfFile = new Dictionary<string, int>(StringComparer.Ordinal);
        var shards = new List<List<int>>();
        for (var i = 0; i < requests.Count; i++)
        {
            string file = requests[i].ProjectFile is { Length: > 0 } projectFile
                ? PathComparison.Fold(projectFile)
                : string.Empty;
            if (!shardOfFile.TryGetValue(file, out int shard))
            {
                shard = shardOfFile.Count % limit;
                shardOfFile[file] = shard;
                if (shard == shards.Count) shards.Add([]);
            }

            shards[shard].Add(i);
        }

        return shards;
    }

    // Split out and never inlined so that a runtime unable to load the MSBuild engine assemblies fails at
    // the call above — inside its catch — rather than while jitting the caller. Same discipline the
    // extraction entry points keep around Roslyn's own types. Shards write disjoint indices of `facts`.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EvaluateInto(
        ProjectArtifactFacts?[] facts,
        IReadOnlyList<ProjectEvaluationRequest> requests,
        IReadOnlyList<int> shard,
        IReadOnlyDictionary<string, string> solutionProperties,
        CancellationToken ct)
    {
        // Every project of the batch evaluates under the solution's properties, so the collection carries them:
        // MSBuild lays a project's own global properties, its framework here, over its collection's.
        using var collection = new ProjectCollection(solutionProperties.ToDictionary());
        var evaluated = new Dictionary<(string File, string? Framework), ProjectArtifactFacts?>();
        var cones = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (int i in shard)
        {
            ct.ThrowIfCancellationRequested();

            ProjectEvaluationRequest request = requests[i];
            if (request.ProjectFile is not { Length: > 0 } projectFile) continue;

            (string, string?) key = (PathComparison.Fold(projectFile), request.TargetFramework);
            if (!evaluated.TryGetValue(key, out ProjectArtifactFacts? result))
            {
                result = TryEvaluate(collection, cones, projectFile, request.TargetFramework);
                evaluated[key] = result;
            }

            facts[i] = result;
        }
    }

    private static ProjectArtifactFacts? TryEvaluate(
        ProjectCollection collection,
        Dictionary<string, HashSet<string>> cones,
        string projectFile,
        string? targetFramework)
    {
        try
        {
            return Evaluate(collection, cones, projectFile, targetFramework);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private static ProjectArtifactFacts Evaluate(
        ProjectCollection collection,
        Dictionary<string, HashSet<string>> cones,
        string projectFile,
        string? targetFramework)
    {
        Dictionary<string, string>? globalProperties = targetFramework is { Length: > 0 }
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["TargetFramework"] = targetFramework }
            : null;
        var options = new ProjectOptions
        {
            ProjectCollection = collection,
            GlobalProperties = globalProperties,
            LoadSettings = ProjectLoadSettings.IgnoreMissingImports
                           | ProjectLoadSettings.IgnoreInvalidImports
                           | ProjectLoadSettings.IgnoreEmptyImports
        };
        Project project = Project.FromFile(projectFile, options);

        try
        {
            var projectSite = new FragmentSite(projectFile, ProjectFileLine);
            string projectDirectory = Path.GetDirectoryName(projectFile) ?? projectFile;
            HashSet<string> projectCone = ProjectCone(cones, projectDirectory);

            (IReadOnlyList<string> frameworks, FragmentSite frameworksSite) =
                ReadTargetFrameworks(project, projectCone, projectSite);
            (bool? packable, FragmentSite packableSite) = ReadPackable(project, projectCone, projectSite);
            (bool locks, FragmentSite locksSite) = ReadLockPolicy(project, projectCone, projectSite);
            IReadOnlyList<FragmentPackageReference> packages =
                ReadPackageReferences(project, projectCone, projectSite);
            IReadOnlyList<string> projectReferences = ReadProjectReferences(project);

            return new ProjectArtifactFacts(
                frameworks, frameworksSite, packages, packable, packableSite, locks, locksSite, projectReferences);
        }
        finally
        {
            collection.UnloadProject(project);
        }
    }

    // The SDK spelling first, because an SDK project defines both properties and the plural is authoritative
    // for a project that targets several. The classic spelling is the fallback and the only one that needs
    // normalizing: a project predating the SDK states a framework identifier and a version rather than a
    // moniker, and nothing downstream should have to know that two spellings of net48 exist.
    private static (IReadOnlyList<string> Frameworks, FragmentSite Site) ReadTargetFrameworks(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        foreach (string property in FrameworkProperties)
        {
            ProjectProperty? declared = project.GetProperty(property);
            if (declared is null || declared.EvaluatedValue.Length == 0) continue;

            List<string> frameworks = MsBuildList.Items(declared.EvaluatedValue)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(framework => framework, StringComparer.Ordinal)
                .ToList();
            if (frameworks.Count > 0)
                return (frameworks, SiteOf(declared, projectCone, projectSite));
        }

        ProjectProperty? version = project.GetProperty("TargetFrameworkVersion");
        string? classic = ClassicMoniker(project.GetPropertyValue("TargetFrameworkIdentifier"), version?.EvaluatedValue);

        return classic is null
            ? ([], projectSite)
            : ([classic], SiteOf(version, projectCone, projectSite));
    }

    // ".NETFramework" + "v4.8" -> "net48". Only the .NET Framework identifier is spelled out, because it is
    // the only one that reaches here: every other identifier belongs to a project the SDK gives a
    // TargetFramework property to, which the caller above has already read. An unrecognised pair records
    // nothing rather than a guess — a wrong moniker would be checked as if it were the project's own word.
    private static string? ClassicMoniker(string identifier, string? version)
    {
        if (version is not { Length: > 1 } || version[0] != 'v') return null;
        if (identifier.Length > 0 && !string.Equals(identifier, ".NETFramework", StringComparison.Ordinal)) return null;

        string digits = version.Substring(1).Replace(".", "");
        return digits.Length > 0 && digits.All(char.IsDigit) ? $"net{digits}" : null;
    }

    // Undefined is null, not false: the property is defined by the SDK's pack targets, so a project that
    // does not import them (every classic project) has no answer here at all. A value that is neither
    // true nor false is the same nothing — MSBuild would treat it as false, but a rule reading it as a
    // deliberate opt-out would be reasoning from a typo.
    private static (bool? Packable, FragmentSite Site) ReadPackable(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        ProjectProperty? declared = project.GetProperty("IsPackable");
        if (declared is null || !bool.TryParse(declared.EvaluatedValue.Trim(), out bool packable))
            return (null, projectSite);

        return (packable, SiteOf(declared, projectCone, projectSite));
    }

    // Undefined is false here, and that asymmetry with IsPackable is the fact rather than an inconsistency:
    // NuGet writes a lock file only when told to, so a project that says nothing has said no. Its site is
    // then the project file, which is where the missing declaration would go.
    private static (bool Locks, FragmentSite Site) ReadLockPolicy(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        ProjectProperty? declared = project.GetProperty("RestorePackagesWithLockFile");
        if (declared is null) return (false, projectSite);

        bool locks = bool.TryParse(declared.EvaluatedValue.Trim(), out bool parsed) && parsed;
        return (locks, SiteOf(declared, projectCone, projectSite));
    }

    // Declared references only. The SDK adds some of its own — NETStandard.Library to a netstandard project,
    // for one — and marks each of them implicit; they are nobody's declaration and nobody can remove them,
    // so a rule about what a project depends on would only be able to fail on them. Privacy is the evaluated
    // metadata, so an ItemDefinitionGroup default or an Update written after the Include decides it, while the
    // site stays the Include — the line a reader would delete. Which of two declarations of one name survives,
    // and in what order the survivors come out, is the shared fold's business rather than this reader's: the
    // merge meets the same duplicates a framework at a time and must answer alike.
    private static IReadOnlyList<FragmentPackageReference> ReadPackageReferences(
        Project project, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        var declarations = new List<FragmentPackageReference>();
        foreach (ProjectItem item in project.GetItems("PackageReference"))
        {
            if (string.Equals(item.GetMetadataValue("IsImplicitlyDefined"), "true", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = item.EvaluatedInclude.Trim();
            if (name.Length == 0) continue;

            FragmentSite site = SiteOf(item.Xml?.Location, projectCone, projectSite);
            bool isPrivate = PrivateAssets.CoversEveryAsset(item.GetMetadataValue("PrivateAssets"));
            declarations.Add(new FragmentPackageReference(name, site, isPrivate));
        }

        return FragmentSiteSets.OrderedPackages(declarations);
    }

    // Declared references only, and evaluation is what makes that the whole of it: the SDK adds the
    // transitive closure in a target (IncludeTransitiveProjectReferences), which only a build runs, so these
    // items are what the project file and its imports state. An item that only orders the build is left
    // out by the test Roslyn applies when it builds the compilation's references, so this list and the
    // workspace's differ by the closure alone. FullPath is MSBuild's own resolution of the include against
    // the project directory.
    private static IReadOnlyList<string> ReadProjectReferences(Project project)
    {
        var paths = new List<string>();
        foreach (ProjectItem item in project.GetItems("ProjectReference"))
        {
            if (string.Equals(item.GetMetadataValue("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
                continue;

            string path = item.GetMetadataValue("FullPath");
            if (path.Length > 0) paths.Add(path);
        }

        return paths;
    }

    private static FragmentSite SiteOf(
        ProjectProperty? declared, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        return SiteOf(declared?.Xml?.Location, projectCone, projectSite);
    }

    // A declaration is only worth citing where somebody reading this repository can go and change it. The
    // SDK's own defaults have real locations, but they name a directory on whichever machine ran the build,
    // so they degrade to the project file — which is where the override would be written anyway.
    private static FragmentSite SiteOf(
        ElementLocation? location, IReadOnlySet<string> projectCone, FragmentSite projectSite)
    {
        if (location is null || location.File.Length == 0) return projectSite;

        return IsUnderProjectCone(location.File, projectCone)
            ? new FragmentSite(location.File, Math.Max(location.Line, ProjectFileLine))
            : projectSite;
    }

    // Memoized for the batch, keyed on the folded directory: the cone depends on nothing but the directory,
    // and a multi-target project is evaluated once per framework, so without this each framework walks the
    // same chain to the drive root and folds every level of it again.
    private static HashSet<string> ProjectCone(Dictionary<string, HashSet<string>> cones, string projectDirectory)
    {
        string key = PathComparison.Fold(projectDirectory);
        if (!cones.TryGetValue(key, out HashSet<string>? cone))
        {
            cone = BuildProjectCone(projectDirectory);
            cones[key] = cone;
        }

        return cone;
    }

    // The project's own directory and every directory above it, each folded — precisely the cone MSBuild
    // discovers Directory.Build.props in, so a solution-wide policy file is cited and an SDK or package-cache
    // import is not. Built once per project because every site this reads is tested against it, and the
    // common case — a declaration outside the cone — is the one that walks the chain to the drive root.
    private static HashSet<string> BuildProjectCone(string projectDirectory)
    {
        var cone = new HashSet<string>(StringComparer.Ordinal);
        for (string? directory = projectDirectory;
             !string.IsNullOrEmpty(directory);
             directory = Path.GetDirectoryName(directory))
            cone.Add(PathComparison.Fold(Normalize(directory)));

        return cone;
    }

    private static bool IsUnderProjectCone(string file, IReadOnlySet<string> projectCone)
    {
        string? declaringDirectory = Path.GetDirectoryName(file);
        if (string.IsNullOrEmpty(declaringDirectory)) return false;

        return projectCone.Contains(PathComparison.Fold(Normalize(declaringDirectory)));
    }

    private static string Normalize(string directory)
    {
        return directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
