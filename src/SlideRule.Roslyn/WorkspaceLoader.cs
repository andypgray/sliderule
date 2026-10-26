using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn;

/// <summary>
///     Loads a solution once, for a host that reads it once: a check, a render, a test run. What it returns is
///     as current as the moment of the load and is never reconciled afterwards; a host that reads one solution
///     many times wants <see cref="WorkspaceSession" />.
/// </summary>
internal static class WorkspaceLoader
{
    // Every box counting this flow, outermost first. Replaced, never mutated, so an inner install is invisible
    // once the async method that made it returns, and an outer counter keeps counting through it.
    private static readonly AsyncLocal<StrongBox<long>[]?> LoadsOnThisFlow = new();

    private static long _loadCount;

    /// <summary>
    ///     The <see cref="MSBuildWorkspace" />s <see cref="LoadAsync" /> has opened in this process, a test
    ///     observable. It counts every caller's loads, so a test that needs one call's loads alone, whatever runs
    ///     beside it, reads <see cref="CountLoadsOnThisFlow" />.
    /// </summary>
    internal static long LoadCount => Interlocked.Read(ref _loadCount);

    /// <summary>
    ///     Starts counting the loads of this async flow and the tasks it starts, and returns the box
    ///     <see cref="LoadAsync" /> increments for each; a test observable. Install it before the call whose loads
    ///     it should count. It is synchronous because a value an async method sets is discarded when it returns,
    ///     so only a synchronous installer leaves the box in place for its caller.
    /// </summary>
    internal static StrongBox<long> CountLoadsOnThisFlow()
    {
        var box = new StrongBox<long>();
        StrongBox<long>[] outer = LoadsOnThisFlow.Value ?? [];
        LoadsOnThisFlow.Value = [.. outer, box];
        return box;
    }

    /// <summary>
    ///     Opens <paramref name="solutionPath" />, the absolute path of a <c>.sln</c>, <c>.slnx</c> or <c>.slnf</c>
    ///     filter, through a fresh MSBuild workspace; the caller disposes the result, which owns the workspace.
    ///     MSBuild must be registered in the process before the first call, for example through the
    ///     <c>Microsoft.Build.Locator</c> package.
    /// </summary>
    /// <remarks>
    ///     A filter loads the projects it selects plus their transitive references. A failure never aborts the
    ///     load, since a partial load still yields a usable model: <paramref name="diagnosticLog" /> receives the
    ///     workspace's failure messages and the load's own warnings for a host to render, and whether the model
    ///     is incomplete is the answer of <see cref="WorkspaceDiagnostics.FailedProjects" /> and
    ///     <see cref="WorkspaceDiagnostics.RestoreFailedProjects" /> on
    ///     <see cref="LoadedSolution.LoadDiagnosticsWith" />, never the messages'.
    /// </remarks>
    /// <exception cref="UserErrorException">
    ///     The <c>.slnf</c> filter at <paramref name="solutionPath" /> could not be read.
    /// </exception>
    // MethodImplOptions.NoInlining keeps the JIT from resolving MSBuild/Roslyn assemblies before
    // MSBuildLocator registration in non-test hosts; in tests registration happens in a
    // [ModuleInitializer], so ordering is already safe there.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<LoadedSolution> LoadAsync(
        string solutionPath, Action<string>? diagnosticLog = null, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _loadCount);
        foreach (StrongBox<long> box in LoadsOnThisFlow.Value ?? []) Interlocked.Increment(ref box.Value);

        var workspace = MSBuildWorkspace.Create();
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) diagnosticLog?.Invoke(e.Diagnostic.Message);
        });

        // The workspace owns an out-of-process build host, so a throw before the result takes ownership of
        // it (a cancel, an unreadable filter) disposes it here rather than orphaning it.
        try
        {
            Solution solution = await OpenAsync(workspace, solutionPath, ct).ConfigureAwait(false);
            SettledSolution settled = LoadBoundary.Settle(solution, solutionPath);

            // Only the analyzer count can be nonzero here: Roslyn's MSBuild loader removes an unresolved metadata
            // reference itself before the boundary sees it.
            if (settled.StrippedAnalyzerReferences > 0)
                diagnosticLog?.Invoke(StrippedAnalyzersMessage(settled.StrippedAnalyzerReferences));

            foreach (UnresolvedReferences.Finding found in UnresolvedReferences.Detect(settled.Solution, settled.Report.Failed))
                diagnosticLog?.Invoke(UnresolvedReferences.Message(found.ProjectPath, found.Names));

            // A project whose restore failed loads completely, so nothing in the loaded structure says so and
            // only its assets file can.
            IReadOnlyList<string> restoreFailed = RestoreFailures.Detect(settled.Solution, settled.Report.Failed);

            return new LoadedSolution(
                workspace, settled.Solution, settled.TargetFrameworks, settled.ProjectFiles, settled.Report,
                restoreFailed);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Loads only <paramref name="projectFiles" /> and their transitive project references out of
    ///     <paramref name="solutionPath" />, a solution or a filter over one, through a temporary solution filter:
    ///     the same load a whole solution takes, seeded with fewer projects, so each project it loads compiles
    ///     exactly as it does in a whole load. Returns null when the subset cannot be expressed as a filter: a
    ///     project the solution does not declare, a solution format the filter cannot name, or a filter the load
    ///     refuses.
    /// </summary>
    /// <remarks>
    ///     The verdict on the result is measured against the temporary filter, so its unchecked projects are
    ///     everything outside the closure. Only the loaded projects' failures and restore failures speak for
    ///     the solution; the caller answers for the rest.
    /// </remarks>
    internal static async Task<LoadedSolution?> LoadClosureAsync(
        string solutionPath, IReadOnlyCollection<string> projectFiles, Action<string>? diagnosticLog = null,
        CancellationToken ct = default)
    {
        // A filter names its solution absolutely here, so where it sits changes nothing: the load resolves it
        // to that solution before reading any project. It sits in a directory of its own because a solution
        // file in the temp root itself would be found by every solution walk that starts below it, another
        // process's included, for as long as the load runs.
        string filterDirectory = Path.Combine(Path.GetTempPath(), $"sliderule-closure-{Guid.NewGuid():N}");
        string filterPath = Path.Combine(filterDirectory, "closure.slnf");
        try
        {
            if (ClosureFilterText(solutionPath, projectFiles) is not { } filterText) return null;

            Directory.CreateDirectory(filterDirectory);
            await File.WriteAllTextAsync(filterPath, filterText, ct).ConfigureAwait(false);
            return await LoadAsync(filterPath, diagnosticLog, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UserErrorException or IOException or UnauthorizedAccessException
                                       or FormatException or XmlException)
        {
            return null;
        }
        finally
        {
            TryDeleteDirectory(filterDirectory);
        }
    }

    // The filter's JSON, naming each requested project by the spelling the solution declares it with, or null
    // when one is not declared there: a filter may only select a solution's own members.
    private static string? ClosureFilterText(string solutionPath, IReadOnlyCollection<string> projectFiles)
    {
        if (!SolutionProjectFileParser.OwnsFormat(solutionPath)) return null;

        if (SolutionProjectFileParser.SolutionFileOf(solutionPath) is not { } referencedSolution
            || !File.Exists(referencedSolution))
            return null;

        Dictionary<string, string> declared = SolutionProjectFileParser.ReadCsprojMembers(referencedSolution)
            .ToDictionary(path => path, path => path, PathComparison.Comparer);
        string solutionDirectory = Path.GetDirectoryName(referencedSolution)!;

        var selected = new List<string>(projectFiles.Count);
        foreach (string projectFile in projectFiles)
        {
            if (!declared.TryGetValue(Path.GetFullPath(projectFile), out string? member)) return null;

            selected.Add(Path.GetRelativePath(solutionDirectory, member));
        }

        var filter = new JsonObject
        {
            ["solution"] = new JsonObject
            {
                ["path"] = referencedSolution,
                ["projects"] = new JsonArray(selected.Select(path => (JsonNode)JsonValue.Create(path)).ToArray())
            }
        };
        return filter.ToJsonString();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temporary filter left behind is harmless; the load already read it.
        }
    }

    /// <summary>
    ///     The refusal when the solution filter at <paramref name="filterPath" /> cannot be read. Pure, and
    ///     internal so the text pins without a filesystem.
    /// </summary>
    internal static string UnreadableFilterMessage(string filterPath)
    {
        return $"Could not read the solution filter '{filterPath}'.\n"
               + "A filter must be well-formed JSON whose 'solution' path resolves to a .sln or .slnx, and "
               + "every project it lists must be a member of that solution.";
    }

    /// <summary>
    ///     The notice a load writes when it strips <paramref name="count" /> unresolved analyzer references, the
    ///     files an analyzer or source generator would have loaded from.
    /// </summary>
    internal static string StrippedAnalyzersMessage(int count)
    {
        string references = Plurals.Counted(count, "unresolved analyzer reference");
        return $"Load stripped {references}; an analyzer or source generator the build names was missing from "
               + "disk, so any code it generates is absent from the model.";
    }

    // Roslyn's SolutionFilterReader signals every filter fault by throwing a bare Exception out of
    // OpenSolutionAsync, measured as the same "Failed to load solution filter" for malformed JSON, a missing
    // 'solution' key, a solution path that does not resolve and a project entry naming a non-member; unmapped
    // it reaches the user as a stack trace. The gate is the extension, never the message. The original is kept
    // as the inner exception, so a fault that is not the filter's is still recoverable from a log.
    private static async Task<Solution> OpenAsync(
        MSBuildWorkspace workspace, string solutionPath, CancellationToken ct)
    {
        try
        {
            return await workspace.OpenSolutionAsync(solutionPath, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException
                                   && SolutionProjectFileParser.IsFilterFormat(solutionPath))
        {
            throw new UserErrorException(UnreadableFilterMessage(solutionPath), ex);
        }
    }
}
