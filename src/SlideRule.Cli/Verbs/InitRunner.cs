using System.ComponentModel;
using System.Text;
using System.Text.Json;
using SlideRule.Cli.Diff;
using SlideRule.Cli.Init;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Settings;
using SlideRule.Cli.SpecLoading;
using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Files;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>init</c> verb: writes a solution's first spec project from the <c>derive_spec</c> recipe's scaffold,
///     adds it to the solution, writes the <c>sliderule.json</c> naming both, builds the spec, and runs the first
///     check.
/// </summary>
/// <remarks>
///     <para>
///         Every refusal comes before the first write, so a refused run leaves the tree as it found it. Once writing
///         has begun, a failed step is reported with the commands that finish the job by hand, because a second init
///         refuses the folder the first one wrote.
///     </para>
///     <para>
///         The settings file goes at the root of the work tree git reports for the solution's directory, where the
///         hook and every command run from that root find it, and beside the solution where there is no work tree.
///         A <c>sliderule.json</c> anywhere between the two would govern the solution instead, so it refuses.
///     </para>
/// </remarks>
internal sealed class InitRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource source,
    IEnvironment environment,
    ISpecProjectCommands commands) : CacheWiredRunner(source, environment)
{
    private static readonly string[] ProjectFilePatterns = ["*.csproj", "*.vbproj", "*.fsproj"];

    public async Task<int> RunAsync(InitRequest request, CancellationToken ct)
    {
        string solutionPath = ModelPipeline.DiscoverSolution(request.Solution, request.WorkingDirectory, Environment);
        if (solutionPath.EndsWith(".slnf", StringComparison.OrdinalIgnoreCase))
            throw new UserErrorException(
                $"{solutionPath} is a solution filter, which dotnet sln add cannot edit. Pass the solution it filters.");

        string solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        GitWorkTree.Location? workTree = await GitWorkTree.LocateAsync(solutionDirectory, ct);
        if (workTree is not null)
        {
            // The settings file is committed, so the paths it records take git's names below the root, never a short
            // name the solution happened to be reached through.
            solutionDirectory = workTree.Directory;
            solutionPath = Path.Join(solutionDirectory, Path.GetFileName(solutionPath));
        }

        Layout layout = Layout.For(solutionPath, solutionDirectory, workTree?.Root ?? solutionDirectory, request);
        RefuseExistingSettings(layout);
        RefuseEnclosingProjects(layout);
        if (Directory.Exists(layout.ProjectDirectory))
            throw new UserErrorException(
                $"{layout.ProjectDirectory} already exists. Remove it, or pass --name or --output to scaffold elsewhere.");

        // The gate graph applies: a project that failed to load is missing from the survey, so neither the framework
        // choice nor a recorded rule could see it.
        (CodebaseSource survey, GraphSummary summary) = await SurveyAsync(
            solutionPath, request.WorkingDirectory, false, request.AllowWorkspaceDiagnostics, ct);
        survey.Dispose();
        RefuseExistingSpec(summary);

        string framework = request.Framework ?? ChosenFramework(summary, layout);
        IReadOnlyList<string> body = request.FromGraph
            ? RecordedRules.Statements(summary, DateOnly.FromDateTime(DateTime.Now))
            : [];

        SpecScaffold scaffold = SpecScaffold.Embedded;
        string? header = FileHeader.For(layout.SpecFile);
        Write(layout.ProjectFile, scaffold.Project(ServerVersion.SemVer, framework), layout);
        Write(layout.SpecFile, scaffold.SpecClass(layout.Name.Namespace, header, body), layout);
        if (request.FromGraph && body.Count == 0)
            await output.WriteLineAsync("no rule recorded: every two product projects are connected one way or the other");

        await RunStepAsync(
            () => commands.AddToSolutionAsync(solutionPath, layout.ProjectFile, ct),
            $"added {layout.Relative(layout.ProjectFile)} to {layout.Relative(solutionPath)}",
            Recovery(layout, solutionPath, true));

        Write(layout.SettingsFile, SettingsJson(layout, solutionPath), layout);

        await RunStepAsync(
            () => commands.BuildAsync(layout.ProjectFile, ct),
            $"built {layout.Relative(layout.ProjectFile)}",
            Recovery(layout, solutionPath, false));

        await output.WriteLineAsync();
        int verdict = await FirstCheckAsync(solutionPath, layout, request, ct);

        await output.WriteLineAsync();
        await output.WriteLineAsync(NextSteps(layout, solutionPath));
        return verdict;
    }

    private static void RefuseExistingSettings(Layout layout)
    {
        foreach (string directory in layout.SolutionDirectoryUpToSettings())
        {
            string candidate = Path.Combine(directory, ProjectSettings.FileName);
            if (File.Exists(candidate))
                throw new UserErrorException(
                    $"{candidate} already exists, and init writes the spec project into a fresh one. Run "
                    + "sliderule check to use the settings you have, or remove the file to scaffold a first spec.");
        }
    }

    // A project in a directory above the spec's folder compiles every source file below it by default, the spec
    // class included, which breaks that project's build.
    private static void RefuseEnclosingProjects(Layout layout)
    {
        foreach (string directory in layout.ProjectDirectoryUpToSettings())
        {
            if (!Directory.Exists(directory)) continue;

            string? enclosing = ProjectFilePatterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern))
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            if (enclosing is not null)
                throw new UserErrorException(
                    $"{enclosing} would compile the spec class under {layout.ProjectDirectory}, because a project "
                    + "takes in every source file below its own folder. Pass --output with a folder no project sits "
                    + "above.");
        }
    }

    // A spec project references the contract package, or a source checkout's contract project, by the contract's name.
    private static void RefuseExistingSpec(GraphSummary summary)
    {
        const string contract = SpecLoadContext.ContractAssemblyName;
        ProjectSummary? spec = summary.Projects.FirstOrDefault(project =>
            project.PackageReferences.Any(package => string.Equals(package.Name, contract, StringComparison.OrdinalIgnoreCase))
            || project.ProjectReferences.Contains(contract, StringComparer.Ordinal));
        if (spec is not null)
            throw new UserErrorException(
                $"The solution already has a spec project, {spec.Name}, which references {contract}. Run sliderule "
                + "check to use it.");
    }

    private static string ChosenFramework(GraphSummary summary, Layout layout)
    {
        List<string> frameworks = summary.Projects.Where(project => project.SolutionMember != false)
            .SelectMany(project => project.TargetFrameworks)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        (int? pin, string? globalJson) = SpecFramework.SdkPin(layout.SolutionDirectory);
        return SpecFramework.Choose(frameworks, pin, globalJson);
    }

    private void Write(string path, string text, Layout layout)
    {
        output.WriteLine(WriteReport.Line(WholeFile.Write(path, text), layout.SettingsDirectory, path));
    }

    private async Task RunStepAsync(Func<Task<ChildProcess.ProcessResult>> step, string done, string recovery)
    {
        ChildProcess.ProcessResult result;
        try
        {
            result = await step();
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException)
        {
            throw new UserErrorException($"{ex.Message}\n{recovery}", ex);
        }

        if (result.ExitCode != 0)
        {
            await error.WriteAsync(result.StandardOutput);
            await error.WriteAsync(result.StandardError);
            throw new UserErrorException($"dotnet exited {result.ExitCode}.\n{recovery}");
        }

        await output.WriteLineAsync(done);
    }

    private static string Recovery(Layout layout, string solutionPath, bool beforeSettings)
    {
        var steps = new StringBuilder();
        steps.Append("init wrote ").Append(layout.Relative(layout.ProjectFile)).Append(" and ")
            .Append(layout.Relative(layout.SpecFile))
            .Append(", so running it again refuses that folder. To finish by hand, fix the error above, then:");
        if (beforeSettings)
        {
            steps.Append("\n  dotnet sln ").Append(Quoted(solutionPath)).Append(" add ").Append(Quoted(layout.ProjectFile));
            steps.Append("\n  write ").Append(layout.SettingsFile).Append(":\n")
                .Append(Indented(SettingsJson(layout, solutionPath)));
        }

        steps.Append("\n  dotnet build ").Append(Quoted(layout.ProjectFile));
        steps.Append("\n  sliderule check");
        return steps.ToString();
    }

    private async Task<int> FirstCheckAsync(string solutionPath, Layout layout, InitRequest request, CancellationToken ct)
    {
        CheckRequest check = new(
            solutionPath, layout.ProjectFile, false, null, request.WorkingDirectory, false, null,
            request.AllowWorkspaceDiagnostics, null, null, DocumentGrain.Full);
        return await new CheckRunner(output, error, SolutionSource, Environment).RunAsync(check, ct);
    }

    private static string NextSteps(Layout layout, string solutionPath)
    {
        return "Next:\n"
               + $"  Write the rules in {layout.Relative(layout.SpecFile)}, rebuild it, and run sliderule check.\n"
               + "  Register the MCP server for Claude Code: claude mcp add --scope project sliderule -- sliderule mcp "
               + layout.Relative(solutionPath) + "\n"
               + "  Check every agent turn: paste the hook entries in "
               + "https://github.com/andypgray/sliderule/blob/main/hooks/settings.snippet.json into "
               + ".claude/settings.json (hooks/README.md there explains them).";
    }

    // Paths relative to the settings file's directory, written with '/' as every committed settings file is.
    private static string SettingsJson(Layout layout, string solutionPath)
    {
        var settings = new SettingsFileJson(layout.Relative(solutionPath), layout.Relative(layout.ProjectFile));
        string document = JsonSerializer.Serialize(settings, SlideRuleJson.Context.SettingsFileJson);
        return SlideRuleJson.Terminated(document);
    }

    private static string Quoted(string path)
    {
        return path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;
    }

    private static string Indented(string text)
    {
        IEnumerable<string> lines = text.TrimEnd('\n').Split('\n').Select(line => "    " + line);
        return string.Join("\n", lines);
    }

    /// <summary>Where init writes, settled from the solution and the request before anything is written.</summary>
    private sealed record Layout(
        SpecProjectName Name,
        string SolutionDirectory,
        string ProjectDirectory,
        string SettingsDirectory)
    {
        public string ProjectFile => Path.Combine(ProjectDirectory, Name.Project + ".csproj");

        public string SpecFile => Path.Combine(ProjectDirectory, "ArchitectureSpec.cs");

        public string SettingsFile => Path.Combine(SettingsDirectory, ProjectSettings.FileName);

        public static Layout For(
            string solutionPath, string solutionDirectory, string settingsDirectory, InitRequest request)
        {
            SpecProjectName name = SpecProjectName.For(solutionPath, request.Name);
            string parent = request.Output is null
                ? Path.Combine(solutionDirectory, "arch")
                : Path.GetFullPath(request.Output, request.WorkingDirectory);
            return new Layout(name, solutionDirectory, Path.Combine(parent, name.Project), settingsDirectory);
        }

        public string Relative(string path)
        {
            return PathFormat.Relative(SettingsDirectory, path);
        }

        public IEnumerable<string> SolutionDirectoryUpToSettings()
        {
            return UpTo(SolutionDirectory);
        }

        public IEnumerable<string> ProjectDirectoryUpToSettings()
        {
            return UpTo(Path.GetDirectoryName(ProjectDirectory)!);
        }

        // From start up to the settings directory, both included; a start outside it yields start's ancestors to
        // the root, since no work-tree boundary bounds what can enclose it.
        private IEnumerable<string> UpTo(string start)
        {
            for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
            {
                yield return directory.FullName;

                if (PathComparison.Comparer.Equals(Path.TrimEndingDirectorySeparator(directory.FullName),
                        Path.TrimEndingDirectorySeparator(SettingsDirectory)))
                    yield break;
            }
        }
    }
}
