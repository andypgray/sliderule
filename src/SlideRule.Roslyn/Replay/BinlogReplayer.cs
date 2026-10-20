using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Replay;

/// <summary>
///     One-shot binlog replay for the enforcement path: read a real build's binlog into a workspace-shaped
///     <see cref="Solution" /> without a design-time build, strip unresolved references, and return. The replayed
///     solution carries the structure the build recorded (project files, output paths, references, the csc
///     source-file list) while the document text is read from current disk state at materialisation time, so
///     edits made after the capture appear in the model and the binlog only goes stale on a structural change.
/// </summary>
/// <remarks>
///     <para>
///         It uses <c>Basic.CompilerLog.Util</c> plus an <see cref="AdhocWorkspace" />, so unlike
///         <see cref="WorkspaceLoader" /> it never opens an <c>MSBuildWorkspace</c> and never spawns an
///         out-of-process build host. It is not free of MSBuild assemblies, though: the reader's binlog parser
///         (MSBuild.StructuredLogger, pulled in by <c>Basic.CompilerLog.Util</c>) binds
///         <c>Microsoft.Build.Framework</c> at runtime through the host's <c>MSBuildLocator</c> registration, so a
///         caller must register MSBuildLocator before replaying or the parser fails to load.
///     </para>
///     <para>
///         Replaying analyzers/generators executes the build's own analyzer assemblies from their
///         on-disk paths (see <see cref="Replay" /> for the analyzer-host choice) — the same trust
///         boundary as building the solution in the first place.
///     </para>
/// </remarks>
internal static class BinlogReplayer
{
    /// <summary>
    ///     Replays <paramref name="binlogPath" /> into a <see cref="ReplayedSolution" /> of its regular C# compiler
    ///     invocations, stripping unresolved references exactly as <see cref="WorkspaceLoader" /> does so
    ///     downstream behaviour is identical.
    /// </summary>
    /// <param name="binlogPath">Absolute path to the <c>.binlog</c> a real build of the current tree produced.</param>
    /// <param name="solutionPath">
    ///     The solution the build was run over (a <c>.sln</c>, <c>.slnx</c> or <c>.slnf</c>), or null when there is
    ///     none. It is stamped on the replayed solution as the file a workspace load would have opened (the
    ///     referenced solution, under a filter): the capture records no solution of its own, and the project files'
    ///     evaluation reads its solution properties from this one.
    /// </param>
    /// <param name="diagnosticSink">
    ///     Optional sink for replay diagnostics that do not abort the load, on <see cref="WorkspaceLoader" />'s
    ///     <c>diagnosticLog</c> contract.
    /// </param>
    /// <param name="ct">Honoured on a best-effort basis around the synchronous reader calls.</param>
    /// <remarks>
    ///     Synchronous, because the underlying <see cref="SolutionReader" /> API is synchronous. Throws
    ///     <see cref="ArgumentException" /> for a blank <paramref name="binlogPath" />,
    ///     <see cref="FileNotFoundException" /> when no file is there, and <see cref="InvalidOperationException" />
    ///     when the binlog holds no replayable C# compiler invocation; whatever the reader raises for an
    ///     unreadable, corrupt or non-binlog file propagates unwrapped. The analyzer host is
    ///     <see cref="BasicAnalyzerKind.OnDisk" />, the shape the <c>MSBuildWorkspace</c> path itself produces;
    ///     the differential fidelity test gates that choice.
    /// </remarks>
    public static ReplayedSolution Replay(
        string binlogPath, string? solutionPath, Action<string>? diagnosticSink = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogPath);
        if (!File.Exists(binlogPath))
            throw new FileNotFoundException($"Binlog not found for replay: '{binlogPath}'.", binlogPath);

        ct.ThrowIfCancellationRequested();

        // Filled by the predicate below as the reader walks the capture — the one moment the dropped
        // projects are visible on this path.
        var unsupportedProjects = new HashSet<string>(PathComparison.Comparer);

        var reader = SolutionReader.Create(
            binlogPath, BasicAnalyzerKind.OnDisk,
            predicate: call => IsReplayableCSharpCall(call, unsupportedProjects));

        AdhocWorkspace? workspace = null;
        try
        {
            if (reader.ProjectCount == 0)
                throw new InvalidOperationException(
                    $"The binlog '{binlogPath}' contains no C# compiler invocations to replay. SlideRule "
                    + "replays C# projects only; a Visual Basic-only or empty binlog cannot produce a model.");

            ct.ThrowIfCancellationRequested();
            string? solutionFile = solutionPath is { Length: > 0 }
                ? SolutionProjectFileParser.SolutionFileOf(solutionPath)
                : null;
            SolutionInfo captured = reader.ReadSolutionInfo();
            var solutionInfo = SolutionInfo.Create(
                captured.Id, captured.Version, solutionFile, captured.Projects, captured.AnalyzerReferences);

            workspace = new AdhocWorkspace();
            workspace.RegisterWorkspaceFailedHandler(e =>
            {
                if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure) diagnosticSink?.Invoke(e.Diagnostic.Message);
            });

            Solution solution = workspace.AddSolution(solutionInfo);
            (Solution normalized, IReadOnlyDictionary<ProjectId, string> targetFrameworks) =
                NormalizeProjects(solution);
            (Solution stripped, int analyzerCount, int metadataCount) = normalized.StripUnresolvedReferences();
            if (analyzerCount > 0 || metadataCount > 0)
                diagnosticSink?.Invoke(
                    $"Replay stripped {analyzerCount} unresolved analyzer and {metadataCount} unresolved "
                    + "metadata reference(s); a build artifact recorded in the binlog was missing from disk.");

            // The same computation the MSBuild path's gate keys on; ReplayedSolution.FailedProjects says what it can
            // find on this path.
            ProjectLoadReport report = ProjectLoadFailures.Detect(stripped, solutionPath);
            foreach (string unbuilt in report.Failed)
                diagnosticSink?.Invoke(UnbuiltMemberMessage(unbuilt));

            // Every one of them is NotCsharp by construction: the predicate below drops a compiler call for
            // exactly one reason, and a shared project mints no compiler call of its own to be dropped.
            List<UnsupportedProject> unsupported = unsupportedProjects
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new UnsupportedProject(path, UnsupportedProjectKind.NotCsharp))
                .ToList();

            return new ReplayedSolution(
                workspace, reader, stripped, targetFrameworks, report.Failed, report.Unchecked, unsupported);
        }
        catch
        {
            workspace?.Dispose();
            reader.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     The replay diagnostic for a project the solution declares and the replayed binlog never built, one
    ///     line per project, so the incomplete-model refusal that follows has a reason above it to point at.
    /// </summary>
    internal static string UnbuiltMemberMessage(string csprojPath)
    {
        return $"The binlog does not build '{csprojPath}', which the solution declares, so the model has none of "
               + "its types. Build the whole solution with -bl and pass that binlog.";
    }

    // Two SolutionReader project-metadata spellings need aligning with what MSBuildWorkspace produces, so every
    // consumer sees identical data on either path. The shared project-name normalization then runs over the
    // result, exactly as the MSBuild path runs it: SolutionReader never discriminates a multi-target-framework
    // project's name, so that pass renames nothing here and only reports each project's framework (read from its
    // output path) for the merge.
    private static (Solution Solution, IReadOnlyDictionary<ProjectId, string> TargetFrameworks) NormalizeProjects(
        Solution solution)
    {
        foreach (ProjectId projectId in solution.ProjectIds.ToList())
        {
            Project project = solution.GetProject(projectId)!;

            // (1) Name: SolutionReader names each project after its csproj file *with* the extension
            //     (CompilerCall.ProjectFileName, e.g. "MyApp.Domain.csproj"); MSBuildWorkspace names it
            //     without ("MyApp.Domain"). Everything downstream keys on the extension-less spelling, so strip
            //     it for byte-parity with the cold path.
            string trimmedName = Path.GetFileNameWithoutExtension(project.Name);
            if (!string.Equals(trimmedName, project.Name, StringComparison.Ordinal))
                solution = solution.WithProjectName(projectId, trimmedName);

            // (2) OutputFilePath: SolutionReader builds it from the assembly *name* with no extension
            //     (<obj-dir>/<AssemblyName>), so it names a file that is not on disk. Append the extension
            //     the OutputKind implies to point at the real obj-intermediate assembly. The obj path (vs
            //     MSBuild's bin path) is kept because nothing compares its bytes, only resolves it, and
            //     SpecResolverTests' RequireBuiltOutput tests pin what that costs: the built-output check returns
            //     an obj path handed to it directly, and refuses to go looking for one. CompilationOutputInfo is
            //     left unset here: the only true value is this same path, and setting it would change nothing.
            if (project.OutputFilePath is { } outputFilePath)
            {
                string withExtension = EnsureAssemblyExtension(outputFilePath, project.CompilationOptions?.OutputKind);
                if (!string.Equals(withExtension, outputFilePath, StringComparison.Ordinal))
                    solution = solution.WithProjectOutputFilePath(projectId, withExtension);
            }
        }

        return solution.NormalizeProjectNames();
    }

    // Path.GetExtension is unusable here: a dotted assembly name ("MyApp.Legacy.Billing") reads as extension
    // ".Billing", so the guard tests the known assembly extensions explicitly. Internal so tests can drive the
    // extension arms without a binlog.
    internal static string EnsureAssemblyExtension(string outputPath, OutputKind? outputKind)
    {
        string[] assemblyExtensions = [".dll", ".exe", ".netmodule", ".winmdobj"];
        if (assemblyExtensions.Any(ext => outputPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return outputPath;

        string extension = outputKind switch
        {
            OutputKind.ConsoleApplication or OutputKind.WindowsApplication or OutputKind.WindowsRuntimeApplication => ".exe",
            OutputKind.NetModule => ".netmodule",
            OutputKind.WindowsRuntimeMetadata => ".winmdobj",
            _ => ".dll"
        };
        return outputPath + extension;
    }

    // The C#-only filter, layered onto SolutionReader's own default (regular compiler calls), so a mixed-language
    // capture replays its C# projects and states the rest. The dropped paths are collected because nothing
    // downstream could recover them: the solution file says what was declared, and only these invocations say
    // what this build dropped.
    private static bool IsReplayableCSharpCall(CompilerCall call, ICollection<string> droppedProjects)
    {
        if (call.Kind != CompilerCallKind.Regular) return false;
        if (call.IsCSharp) return true;

        if (call.ProjectFilePath is { Length: > 0 } projectFilePath)
            droppedProjects.Add(Path.GetFullPath(projectFilePath));

        return false;
    }
}
