using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Project = Microsoft.CodeAnalysis.Project;
using ProjectFile = Microsoft.Build.Evaluation.Project;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     Gives a loaded Visual Basic project back the conditional-compilation symbols its build defines and the
///     workspace left out. Without them the compiler's embedded <c>My</c> template fails to bind, the project has
///     declaration errors, and a C# project's reference to it never reaches the C# compilation.
/// </summary>
/// <remarks>
///     Measured on Workspaces.MSBuild 5.9.0: on Linux the workspace drops a VB project's whole <c>/define:</c>
///     argument, leaving only the compiler's own <c>VBC_VER</c> and <c>TARGET</c>; on Windows it keeps them, and this
///     changes nothing. The symbols must reach the compilation options' parse options as well as the project's,
///     because the <c>My</c> template is parsed with the former. The build's list is <c>FinalDefineConstants</c> once
///     the SDK's <see cref="ImplicitDefinesTarget" /> has run, which is the first point that holds the framework
///     symbols. Every failure to read it is an absence: the project keeps the symbols it loaded with.
/// </remarks>
internal static class VisualBasicDefines
{
    // The SDK target that appends the framework symbols (NET, NET10_0_OR_GREATER, ...) to FinalDefineConstants.
    // It computes properties and runs no tool.
    private const string ImplicitDefinesTarget = "AddImplicitDefineConstants";

    // The symbol every SDK and classic VB build defines (MyType, or Empty in its absence), so its absence is the
    // sign that the workspace dropped the list.
    private const string MyTypeSymbol = "_MyType";

    // An in-process build goes through MSBuild's process-wide default build manager, which runs one build at a time.
    private static readonly Lock BuildGate = new();

    /// <summary>
    ///     <paramref name="solution" /> with each VB project that lacks <c>_MyType</c> given the symbols of its build's
    ///     list it lacks, evaluated for the framework <paramref name="targetFrameworks" /> names for it, or for the
    ///     project's own where it names none. A symbol the project already carries keeps its value.
    /// </summary>
    internal static Solution Restore(Solution solution, IReadOnlyDictionary<ProjectId, string> targetFrameworks)
    {
        Solution restored = solution;
        IReadOnlyDictionary<string, string>? solutionProperties = null;
        foreach (Project project in solution.Projects)
        {
            if (project.FilePath is not { } projectFile
                || project.ParseOptions is not VisualBasicParseOptions parseOptions
                || project.CompilationOptions is not VisualBasicCompilationOptions compilationOptions
                || parseOptions.PreprocessorSymbols.Any(symbol => IsMyType(symbol.Key)))
                continue;

            solutionProperties ??= ProjectFactsEvaluator.SolutionProperties(solution.FilePath);
            targetFrameworks.TryGetValue(project.Id, out string? targetFramework);
            if (BuiltDefineConstants(projectFile, targetFramework, solutionProperties) is not { } defines) continue;

            ImmutableArray<KeyValuePair<string, object>> missing = Missing(parseOptions, defines);
            if (missing.IsEmpty) continue;

            VisualBasicParseOptions withDefines =
                parseOptions.WithPreprocessorSymbols(parseOptions.PreprocessorSymbols.AddRange(missing));
            restored = restored
                .WithProjectParseOptions(project.Id, withDefines)
                .WithProjectCompilationOptions(project.Id, compilationOptions.WithParseOptions(withDefines));
        }

        return restored;
    }

    private static bool IsMyType(string symbol)
    {
        return string.Equals(symbol, MyTypeSymbol, StringComparison.OrdinalIgnoreCase);
    }

    // VB symbol names compare case-insensitively, and the compiler keeps the last definition of a repeated name.
    private static ImmutableArray<KeyValuePair<string, object>> Missing(VisualBasicParseOptions parseOptions, string defines)
    {
        IReadOnlyDictionary<string, object> parsed =
            VisualBasicCommandLineParser.ParseConditionalCompilationSymbols(defines, out IEnumerable<Diagnostic> problems);
        if (problems.Any(problem => problem.Severity == DiagnosticSeverity.Error)) return [];

        HashSet<string> present = parseOptions.PreprocessorSymbols
            .Select(symbol => symbol.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            .. parsed
                .GroupBy(symbol => symbol.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Where(symbol => !present.Contains(symbol.Key))
        ];
    }

    // Split out and never inlined so that a runtime unable to load the MSBuild engine assemblies fails here, inside
    // the catch, rather than while jitting the caller. A project without the target (one that predates the SDK)
    // answers with its evaluated list, which is then the whole of it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? BuiltDefineConstants(
        string projectFile, string? targetFramework, IReadOnlyDictionary<string, string> solutionProperties)
    {
        try
        {
            using var collection = new ProjectCollection(solutionProperties.ToDictionary());

            // The workspace loads under a design-time build, which a project's own build-only targets (an initial
            // target that checks for tooling, for one) skip; the build here must skip them too.
            var globalProperties = new Dictionary<string, string>(StringComparer.Ordinal) { ["DesignTimeBuild"] = "true" };
            if (targetFramework is { Length: > 0 }) globalProperties["TargetFramework"] = targetFramework;

            var options = new ProjectOptions
            {
                ProjectCollection = collection,
                GlobalProperties = globalProperties,
                LoadSettings = ProjectLoadSettings.IgnoreMissingImports
                               | ProjectLoadSettings.IgnoreInvalidImports
                               | ProjectLoadSettings.IgnoreEmptyImports
            };
            ProjectInstance instance = ProjectFile.FromFile(projectFile, options).CreateProjectInstance();
            if (!instance.Targets.ContainsKey(ImplicitDefinesTarget)) return instance.GetPropertyValue("FinalDefineConstants");

            bool built;
            lock (BuildGate)
            {
                built = instance.Build([ImplicitDefinesTarget], loggers: null);
            }

            return built ? instance.GetPropertyValue("FinalDefineConstants") : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }
}
