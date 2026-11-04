using System.Text.RegularExpressions;
using SlideRule.Cli.Init;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.Init;

/// <summary>
///     A scripted <see cref="ISpecProjectCommands" />: records each command <c>init</c> runs, and answers with the
///     results it is given, or runs the real command where <see cref="Real" /> built it.
/// </summary>
internal sealed class ScriptedSpecProjectCommands : ISpecProjectCommands
{
    private static readonly ChildProcess.ProcessResult Succeeded = new(0, "", "");

    private readonly Func<string, string, CancellationToken, Task<ChildProcess.ProcessResult>> _add;

    private readonly Func<string, CancellationToken, Task<ChildProcess.ProcessResult>> _build;

    private ScriptedSpecProjectCommands(
        Func<string, string, CancellationToken, Task<ChildProcess.ProcessResult>> add,
        Func<string, CancellationToken, Task<ChildProcess.ProcessResult>> build)
    {
        _add = add;
        _build = build;
    }

    /// <summary>
    ///     Each command run, in order: <c>add &lt;solution&gt; &lt;project&gt;</c> or <c>build &lt;project&gt;</c>.
    /// </summary>
    public List<string> Calls { get; } = [];

    /// <summary>Answers both commands with the given results, running nothing.</summary>
    public static ScriptedSpecProjectCommands Answering(
        ChildProcess.ProcessResult? add = null, ChildProcess.ProcessResult? build = null)
    {
        return new ScriptedSpecProjectCommands(
            (_, _, _) => Task.FromResult(add ?? Succeeded),
            (_, _) => Task.FromResult(build ?? Succeeded));
    }

    /// <summary>
    ///     The real commands, except that the build first points the written csproj's <c>SlideRule</c> package
    ///     reference at the contract assembly this test process loads.
    /// </summary>
    /// <remarks>
    ///     The suite has no feed serving <c>SlideRule</c> at the running version, which is what the package
    ///     reference names. A <c>HintPath</c> reference to the same <c>SlideRule.dll</c> is still the reference
    ///     spec discovery looks for, so the swap changes where the contract comes from and nothing the check reads.
    /// </remarks>
    public static ScriptedSpecProjectCommands Real()
    {
        DotnetSpecProjectCommands real = new();
        return new ScriptedSpecProjectCommands(
            real.AddToSolutionAsync,
            (project, ct) =>
            {
                PointAtThisContract(project);
                return real.BuildAsync(project, ct);
            });
    }

    public Task<ChildProcess.ProcessResult> AddToSolutionAsync(string solution, string project, CancellationToken ct)
    {
        Calls.Add($"add {solution} {project}");
        return _add(solution, project, ct);
    }

    public Task<ChildProcess.ProcessResult> BuildAsync(string project, CancellationToken ct)
    {
        Calls.Add($"build {project}");
        return _build(project, ct);
    }

    private static void PointAtThisContract(string project)
    {
        string contract = typeof(Arch).Assembly.Location;
        string text = File.ReadAllText(project);
        string swapped = Regex.Replace(
            text,
            "<PackageReference Include=\"SlideRule\" Version=\"[^\"]*\" />",
            $"<Reference Include=\"SlideRule\" HintPath=\"{contract}\" />");
        if (swapped == text)
            throw new InvalidOperationException($"{project} carries no SlideRule package reference to swap.");

        File.WriteAllText(project, swapped);
    }
}
