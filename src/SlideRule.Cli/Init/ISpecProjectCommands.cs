using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Init;

/// <summary>
///     The two <c>dotnet</c> commands <c>init</c> runs over the spec project it wrote: adding it to the solution, and
///     building it.
/// </summary>
// A deliberate seam: the build restores SlideRule at the running tool's version, which only a packed feed serves, so
// the suite drives init through a scripted double and CI drives the real commands from the packed tool.
internal interface ISpecProjectCommands
{
    /// <summary>Runs <c>dotnet sln <paramref name="solution" /> add <paramref name="project" /></c>.</summary>
    Task<ChildProcess.ProcessResult> AddToSolutionAsync(string solution, string project, CancellationToken ct);

    /// <summary>Runs <c>dotnet build <paramref name="project" /></c>.</summary>
    Task<ChildProcess.ProcessResult> BuildAsync(string project, CancellationToken ct);
}

/// <summary>The real commands, through the SDK on <c>PATH</c>.</summary>
internal sealed class DotnetSpecProjectCommands : ISpecProjectCommands
{
    // A first restore downloads the contract package, and the build behind it compiles nothing else, so ten minutes
    // only trips on a wedge.
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(10);

    public Task<ChildProcess.ProcessResult> AddToSolutionAsync(string solution, string project, CancellationToken ct)
    {
        string directory = Path.GetDirectoryName(solution)!;
        return ChildProcess.RunAsync(
            DotnetCommand.StartInfo(["sln", solution, "add", project], directory), ct: ct);
    }

    public Task<ChildProcess.ProcessResult> BuildAsync(string project, CancellationToken ct)
    {
        string directory = Path.GetDirectoryName(project)!;
        return ChildProcess.RunAsync(
            DotnetCommand.StartInfo(["build", project, "--disable-build-servers"], directory), BuildTimeout, ct: ct);
    }
}
