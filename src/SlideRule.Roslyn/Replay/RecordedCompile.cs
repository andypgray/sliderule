using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Roslyn.Replay;

/// <summary>
///     One C# compiler call a binlog recorded, reduced to what the compiler-lag verdict reads: the project file and
///     target framework it compiled, the last <c>/langversion</c> its command line passed (null when it passed none),
///     and the compiler executable it ran.
/// </summary>
internal sealed record RecordedCompile(
    string ProjectFilePath,
    string? TargetFramework,
    string? LangVersion,
    string? CompilerFilePath);

/// <summary>Reads <see cref="RecordedCompile" />s off a binlog and pairs them with the replayed projects.</summary>
internal static class RecordedCompiles
{
    /// <summary>
    ///     Every regular C# compiler call <paramref name="binlogPath" /> recorded with a project file, ordered by project
    ///     file and then framework, so a capture manifest that records them reads the same on every ingest.
    /// </summary>
    /// <remarks>
    ///     A reader of its own, because the replay's <see cref="SolutionReader" /> keeps the call reader that holds the
    ///     arguments private. It hosts no analyzers, so it copies none.
    /// </remarks>
    internal static IReadOnlyList<RecordedCompile> Read(string binlogPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using ICompilerCallReader reader = CompilerCallReaderUtil.Create(binlogPath, BasicAnalyzerKind.None);
        List<CompilerCall> calls = reader.ReadAllCompilerCalls(call => call is { Kind: CompilerCallKind.Regular, IsCSharp: true, ProjectFilePath.Length: > 0 });

        var compiles = new List<RecordedCompile>();
        foreach (CompilerCall call in calls)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyCollection<string> arguments = reader.ReadArguments(call);
            compiles.Add(
                new RecordedCompile(
                    Path.GetFullPath(call.ProjectFilePath), call.TargetFramework, LangVersionOf(arguments),
                    call.CompilerFilePath));
        }

        return compiles
            .OrderBy(compile => compile.ProjectFilePath, StringComparer.Ordinal)
            .ThenBy(compile => compile.TargetFramework, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     The value of the last <c>/langversion:</c> or <c>-langversion:</c> in <paramref name="arguments" />, as the
    ///     compiler takes the last one; null when there is none.
    /// </summary>
    internal static string? LangVersionOf(IEnumerable<string> arguments)
    {
        string? last = null;
        foreach (string argument in arguments)
        {
            if (argument.Length < 2 || (argument[0] != '/' && argument[0] != '-')) continue;

            const string option = "langversion:";
            if (string.Compare(argument, 1, option, 0, option.Length, StringComparison.OrdinalIgnoreCase) != 0)
                continue;

            last = argument[(1 + option.Length)..].Trim('"');
        }

        return last;
    }

    /// <summary>
    ///     Each C# project of <paramref name="solution" /> paired with the call that compiled it: the one call recorded
    ///     for its project file, or, when the file was compiled once per framework, the call for the framework
    ///     <paramref name="targetFrameworks" /> gives the project. A project with no call it can be told apart by is
    ///     left out.
    /// </summary>
    internal static IReadOnlyDictionary<ProjectId, RecordedCompile> ByProject(
        Solution solution, IReadOnlyDictionary<ProjectId, string> targetFrameworks,
        IReadOnlyList<RecordedCompile> compiles)
    {
        ILookup<string, RecordedCompile> byFile = compiles.ToLookup(
            compile => compile.ProjectFilePath, PathComparison.Comparer);

        var paired = new Dictionary<ProjectId, RecordedCompile>();
        foreach (Project project in solution.Projects)
        {
            if (!ProjectLanguages.IsCSharp(project) || project.FilePath is null) continue;

            IEnumerable<RecordedCompile> candidates = byFile[Path.GetFullPath(project.FilePath)];
            if (targetFrameworks.TryGetValue(project.Id, out string? framework))
                candidates = candidates.Where(candidate => string.Equals(
                    candidate.TargetFramework, framework, StringComparison.OrdinalIgnoreCase));

            List<RecordedCompile> matching = candidates.ToList();
            if (matching.Count == 1) paired[project.Id] = matching[0];
        }

        return paired;
    }
}
