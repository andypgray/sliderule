using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SlideRule.Internal;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>Why a project's code reached the model through a compiler older than the one its build uses.</summary>
internal enum CompilerLag
{
    /// <summary>The carried parser rejected the C# version the project asks for, and read it as its own default.</summary>
    UnknownVersion,

    /// <summary>The project asks for an alias, and its build's compiler reads that as a newer C# than the carried one.</summary>
    NewerDefault
}

/// <summary>
///     Which projects lost code to the compiler this tool carries lagging the one their build uses: a C# version
///     the carried parser does not know, or a source generator it cannot load. The third cause the fail-closed gate
///     keys on, beside <see cref="ProjectLoadFailures" /> and <see cref="RestoreFailures" />.
/// </summary>
/// <remarks>
///     <para>
///         The language verdict is anchored on what the load did, not on an evaluation alone: the in-process MSBuild
///         runs on this process's runtime, so it can pick an older SDK than the BuildHost that loaded the project
///         (measured: SDK 10.0.404 in process, 11.0.100 in the BuildHost, under one dotnet root). The loader drops the
///         parser's errors, and an unknown <c>/langversion</c> leaves the project at
///         <see cref="LanguageVersion.Default" />, so a load at Default from a project whose evaluation asks for a
///         version is a rejected version whichever SDK either side ran.
///     </para>
///     <para>
///         The generator verdict must be taken before anything compiles the solution: an
///         <see cref="AnalyzerFileReference" /> raises <see cref="AnalyzerFileReference.AnalyzerLoadFailed" /> once,
///         on the first read of its generators, and caches the empty answer (measured). Only generators are asked
///         for: an assembly that declares none raises nothing there, newer compiler or not (measured), and an
///         analyzer contributes nothing to the model.
///     </para>
///     <para>Every read fault degrades to "no lag", because the alternative is refusing a healthy solution.</para>
/// </remarks>
internal static class CompilerLagFailures
{
    private const string CompilerAssemblyName = "Microsoft.CodeAnalysis.CSharp.dll";

    /// <summary>The newest C# major version the carried compiler declares.</summary>
    internal static int CarriedHighestLanguageVersion { get; } =
        Enum.GetNames<LanguageVersion>().Select(MajorOf).Max();

    /// <summary>One lagging project and the sentence of evidence the load log carries for it.</summary>
    internal readonly record struct Finding(string ProjectPath, string Evidence);

    /// <summary>
    ///     Whether a project lags, from what its evaluation asks for (<paramref name="asked" />, null when the
    ///     evaluation failed), the version the load parsed it at, and the newest C# major its build's compiler and
    ///     the carried one declare, 0 where the build's compiler could not be read.
    /// </summary>
    internal static CompilerLag? Judge(
        string? asked, LanguageVersion specified, int yardstickHighest, int carriedHighest)
    {
        if (asked is null) return null;

        // The rejection needs no yardstick, so it is decided before one is consulted.
        if (specified == LanguageVersion.Default && !AsksForNothing(asked)) return CompilerLag.UnknownVersion;

        bool alias = specified is LanguageVersion.Default or LanguageVersion.Latest or LanguageVersion.LatestMajor
            or LanguageVersion.Preview;
        if (!alias || yardstickHighest <= 0) return null;

        return yardstickHighest > carriedHighest ? CompilerLag.NewerDefault : null;
    }

    /// <summary>
    ///     The newest C# major version the <c>LanguageVersion</c> enum in the compiler assembly at
    ///     <paramref name="compilerAssemblyPath" /> declares, read from its metadata without loading it; 0 when it
    ///     cannot be read.
    /// </summary>
    internal static int HighestLanguageVersion(string compilerAssemblyPath)
    {
        try
        {
            using FileStream stream = File.OpenRead(compilerAssemblyPath);
            using var pe = new PEReader(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                if (!metadata.StringComparer.Equals(type.Name, nameof(LanguageVersion))
                    || !metadata.StringComparer.Equals(type.Namespace, "Microsoft.CodeAnalysis.CSharp"))
                    continue;

                return type.GetFields()
                    .Select(field => MajorOf(metadata.GetString(metadata.GetFieldDefinition(field).Name)))
                    .DefaultIfEmpty(0)
                    .Max();
            }

            return 0;
        }
        catch (Exception ex) when (IsReadFault(ex))
        {
            return 0;
        }
    }

    /// <summary>
    ///     The C# projects whose source generators the carried compiler cannot load, with one evidence line per
    ///     project, less <paramref name="alreadyFailed" />. Each analyzer file is asked once, and its failure counts
    ///     against every project that references it.
    /// </summary>
    /// <remarks>Must run before anything compiles <paramref name="solution" />; the class remarks say why.</remarks>
    internal static IReadOnlyList<Finding> DetectGenerators(Solution solution, IReadOnlyList<string> alreadyFailed)
    {
        var skip = new HashSet<string>(alreadyFailed, PathComparison.Comparer);
        var referencesByPath = new Dictionary<string, AnalyzerFileReference>(PathComparison.Comparer);
        var projectsByPath = new Dictionary<string, SortedSet<string>>(PathComparison.Comparer);

        foreach (Project project in solution.Projects)
        {
            if (!ProjectLanguages.IsCSharp(project) || project.FilePath is not { } filePath) continue;

            string projectPath = Path.GetFullPath(filePath);
            if (skip.Contains(projectPath)) continue;

            foreach (AnalyzerFileReference reference in project.AnalyzerReferences.OfType<AnalyzerFileReference>())
            {
                referencesByPath.TryAdd(reference.FullPath, reference);
                if (!projectsByPath.TryGetValue(reference.FullPath, out SortedSet<string>? projects))
                {
                    projects = new SortedSet<string>(StringComparer.Ordinal);
                    projectsByPath[reference.FullPath] = projects;
                }

                projects.Add(projectPath);
            }
        }

        var unloadedByProject = new Dictionary<string, List<string>>(PathComparison.Comparer);
        foreach ((string analyzerPath, AnalyzerFileReference reference) in referencesByPath.OrderBy(
                     pair => pair.Key, StringComparer.Ordinal))
        {
            if (FirstGeneratorLoadFailure(reference) is not { } failure) continue;

            string reason = ReasonOf(failure);
            foreach (string projectPath in projectsByPath[analyzerPath])
            {
                if (!unloadedByProject.TryGetValue(projectPath, out List<string>? unloaded))
                {
                    unloaded = [];
                    unloadedByProject[projectPath] = unloaded;
                }

                unloaded.Add($"{Path.GetFileName(analyzerPath)}: {reason}");
            }
        }

        return unloadedByProject
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new Finding(pair.Key, GeneratorEvidence(pair.Key, pair.Value)))
            .ToList();
    }

    /// <summary>
    ///     The C# projects whose C# version the carried compiler cannot read, with one evidence line each, less
    ///     <paramref name="alreadyFailed" />. <paramref name="solutionFile" /> is the file the load derived its
    ///     solution properties from, as the artifact facts' evaluation takes it.
    /// </summary>
    internal static IReadOnlyList<Finding> DetectLanguage(
        Solution solution,
        IReadOnlyDictionary<ProjectId, string> targetFrameworks,
        IReadOnlyList<string> alreadyFailed,
        string? solutionFile,
        CancellationToken ct)
    {
        var skip = new HashSet<string>(alreadyFailed, PathComparison.Comparer);
        List<Project> projects = solution.Projects
            .Where(project => ProjectLanguages.IsCSharp(project) && project.FilePath is not null)
            .Where(project => !skip.Contains(Path.GetFullPath(project.FilePath!)))
            .Where(project => project.ParseOptions is CSharpParseOptions)
            .ToList();
        if (projects.Count == 0) return [];

        List<ProjectEvaluationRequest> requests = projects
            .Select(project => new ProjectEvaluationRequest(
                project.FilePath, targetFrameworks.GetValueOrDefault(project.Id)))
            .ToList();
        IReadOnlyList<CompilerFacts?> facts = ProjectFactsEvaluator.EvaluateCompilerFacts(requests, solutionFile, ct);

        string? solutionSdk = LoadSdkOf(solution);
        var highestByAssembly = new ConcurrentDictionary<string, int>(PathComparison.Comparer);
        var findings = new SortedDictionary<string, Finding>(StringComparer.Ordinal);
        for (var i = 0; i < projects.Count; i++)
        {
            Project project = projects[i];
            string projectPath = Path.GetFullPath(project.FilePath!);
            if (findings.ContainsKey(projectPath)) continue;

            var parseOptions = (CSharpParseOptions)project.ParseOptions!;
            string? yardstick = YardstickOf(project, facts[i]?.RoslynTargetsPath, solutionSdk);
            int yardstickHighest = yardstick is null
                ? 0
                : highestByAssembly.GetOrAdd(yardstick, HighestLanguageVersion);

            CompilerLag? lag = Judge(
                facts[i]?.LangVersion, parseOptions.SpecifiedLanguageVersion, yardstickHighest,
                CarriedHighestLanguageVersion);
            if (lag is { } kind)
                findings[projectPath] = new Finding(
                    projectPath, LanguageEvidence(projectPath, kind, facts[i]!.LangVersion, yardstickHighest));
        }

        return findings.Values.ToList();
    }

    /// <summary>
    ///     The SDK directory the load took <paramref name="project" />'s own analyzers from, null when none of them
    ///     sits in an SDK.
    /// </summary>
    internal static string? LoadSdkOf(Project project)
    {
        return project.AnalyzerReferences
            .Select(reference => reference.FullPath)
            .OfType<string>()
            .Select(SdkDirectoryOf)
            .FirstOrDefault(directory => directory is not null);
    }

    /// <summary>
    ///     The SDK directory the load took the analyzers of the first C# project that has any from, null when none
    ///     does: the SDK every project of the load was read with, for a project such as a <c>netstandard2.0</c> one
    ///     that the SDK gives no analyzers of its own.
    /// </summary>
    internal static string? LoadSdkOf(Solution solution)
    {
        return solution.Projects
            .Where(ProjectLanguages.IsCSharp)
            .Select(LoadSdkOf)
            .FirstOrDefault(directory => directory is not null);
    }

    /// <summary>
    ///     The compiler assembly a project's build reads C# with: the one under a <c>RoslynTargetsPath</c> outside
    ///     any SDK (a compiler package or Visual Studio's), else the one in the SDK the load took the project's own
    ///     analyzers from, else the one in <paramref name="solutionSdk" />, else the one under the in-process
    ///     <c>RoslynTargetsPath</c>; null when none is on disk.
    /// </summary>
    internal static string? YardstickOf(Project project, string? roslynTargetsPath, string? solutionSdk)
    {
        string? inProcess = roslynTargetsPath is { Length: > 0 } ? FullPathOrNull(roslynTargetsPath) : null;
        string? loadSdk = LoadSdkOf(project) ?? solutionSdk;

        string? directory = inProcess is not null && SdkDirectoryOf(inProcess) is null
            ? inProcess
            : loadSdk is not null
                ? Path.Combine(loadSdk, "Roslyn")
                : inProcess;

        return directory is null ? null : CompilerAssemblyIn(directory);
    }

    /// <summary>
    ///     The distinct compiler assemblies <see cref="YardstickOf" /> finds for the C# projects of
    ///     <paramref name="solution" /> from the load alone, ordinal-sorted.
    /// </summary>
    internal static IReadOnlyList<string> CompilerAssembliesOf(Solution solution)
    {
        string? solutionSdk = LoadSdkOf(solution);
        return solution.Projects
            .Where(ProjectLanguages.IsCSharp)
            .Select(project => YardstickOf(project, null, solutionSdk))
            .OfType<string>()
            .Distinct(PathComparison.Comparer)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Every SDK version directory installed beside an SDK that one of <paramref name="paths" /> sits in,
    ///     absolute and ordinal-sorted: what changes when an SDK is installed or removed side by side, which no file
    ///     of the SDK a load used records. A directory that cannot be listed contributes nothing.
    /// </summary>
    internal static IReadOnlyList<string> SdkInstallsBeside(IEnumerable<string> paths)
    {
        var installs = new SortedSet<string>(StringComparer.Ordinal);
        IEnumerable<string> roots = paths
            .Select(SdkDirectoryOf)
            .OfType<string>()
            .Select(Path.GetDirectoryName)
            .OfType<string>()
            .Distinct(PathComparison.Comparer);
        foreach (string root in roots)
            try
            {
                installs.UnionWith(Directory.GetDirectories(root));
            }
            catch (Exception ex) when (IsReadFault(ex))
            {
                // An unlistable root records nothing, and an unlistable root later compares equal to it.
            }

        return installs.ToList();
    }

    /// <summary>
    ///     The <c>sdk/&lt;version&gt;</c> directory <paramref name="path" /> sits in, recognised by the
    ///     <c>Sdks</c> or <c>Roslyn</c> directory a .NET SDK keeps beside its version; null for any other path.
    /// </summary>
    internal static string? SdkDirectoryOf(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i + 2 < segments.Length; i++)
        {
            if (!segments[i].Equals("sdk", StringComparison.OrdinalIgnoreCase)) continue;
            if (segments[i + 1] is not { Length: > 0 } version || !char.IsAsciiDigit(version[0])) continue;
            if (!segments[i + 2].Equals("Sdks", StringComparison.OrdinalIgnoreCase)
                && !segments[i + 2].Equals("Roslyn", StringComparison.OrdinalIgnoreCase))
                continue;

            string joined = string.Join(Path.DirectorySeparatorChar, segments.Take(i + 2));
            return joined.Length == 0 ? null : joined;
        }

        return null;
    }

    // The .NET SDK keeps its compiler in bincore below RoslynTargetsPath; Visual Studio and a compiler package keep
    // it in the directory itself.
    private static string? CompilerAssemblyIn(string directory)
    {
        string[] candidates =
        [
            Path.Combine(directory, "bincore", CompilerAssemblyName),
            Path.Combine(directory, CompilerAssemblyName)
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FullPathOrNull(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or SecurityException)
        {
            return null;
        }
    }

    private static bool AsksForNothing(string asked)
    {
        string trimmed = asked.Trim();
        return trimmed.Length == 0 || trimmed.Equals("default", StringComparison.OrdinalIgnoreCase);
    }

    // "CSharp7_3" is 7 and "CSharp15" is 15; a member not named for a C# version is 0.
    private static int MajorOf(string memberName)
    {
        const string prefix = "CSharp";
        if (!memberName.StartsWith(prefix, StringComparison.Ordinal)) return 0;

        string digits = new(memberName.Skip(prefix.Length).TakeWhile(char.IsAsciiDigit).ToArray());
        return int.TryParse(digits, out int major) ? major : 0;
    }

    private static AnalyzerLoadFailureEventArgs? FirstGeneratorLoadFailure(AnalyzerFileReference reference)
    {
        AnalyzerLoadFailureEventArgs? first = null;

        void OnFailed(object? sender, AnalyzerLoadFailureEventArgs e)
        {
            first ??= e;
        }

        reference.AnalyzerLoadFailed += OnFailed;
        try
        {
            reference.GetGenerators(LanguageNames.CSharp);
        }
        finally
        {
            reference.AnalyzerLoadFailed -= OnFailed;
        }

        return first;
    }

    private static string ReasonOf(AnalyzerLoadFailureEventArgs failure)
    {
        if (failure.ErrorCode == AnalyzerLoadFailureEventArgs.FailureErrorCode.ReferencesNewerCompiler)
            return $"it was built against Microsoft.CodeAnalysis {failure.ReferencedCompilerVersion}, newer than this "
                   + "tool's";

        return failure.Exception?.Message ?? failure.Message;
    }

    private static bool IsReadFault(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or NotSupportedException or SecurityException
            or ArgumentException or BadImageFormatException or InvalidOperationException;
    }

    private static string LanguageEvidence(string projectPath, CompilerLag lag, string asked, int yardstickHighest)
    {
        var carried = $"C# {CarriedHighestLanguageVersion}";
        return lag == CompilerLag.UnknownVersion
            ? $"'{projectPath}' asks for C# version '{asked.Trim()}', which the {carried} compiler this tool carries "
              + "does not know, so its code was read as another C# version."
            : $"'{projectPath}' asks for C# '{(AsksForNothing(asked) ? "default" : asked.Trim())}', which its "
              + $"build's compiler reads as C# {yardstickHighest}, newer than the {carried} compiler this tool "
              + "carries, so its code was read as an older C# version.";
    }

    private static string GeneratorEvidence(string projectPath, IReadOnlyList<string> unloaded)
    {
        string generators = Plurals.Counted(unloaded.Count, "source generator");
        return $"'{projectPath}' runs {generators} the C# compiler this tool carries cannot load "
               + $"({string.Join("; ", unloaded)}), so the code {(unloaded.Count == 1 ? "it generates" : "they generate")} "
               + "is missing from the model.";
    }
}
