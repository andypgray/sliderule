using SlideRule.Cli.Diff;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Settings;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     Runs <c>sliderule install-hook</c>: merges the embedded settings snippet's hook entries and deny rules into
///     <c>.claude/settings.json</c> at the root of the git work tree, or in the working directory outside one.
/// </summary>
/// <remarks>
///     Every refusal comes before the write, which happens only when the merge added something, so a run with
///     nothing to add leaves the file's bytes and modification time as they were.
/// </remarks>
internal sealed class InstallHookRunner(TextWriter output, IEnvironment environment)
{
    /// <summary>The logical name <c>hooks/settings.snippet.json</c> is embedded under.</summary>
    internal const string SnippetResource = "settings.snippet.json";

    private static readonly string SettingsFile = Path.Combine(".claude", "settings.json");

    public async Task<int> RunAsync(InstallHookRequest request, CancellationToken ct)
    {
        string workingDirectory = Path.GetFullPath(request.WorkingDirectory);
        GitWorkTree.Location? workTree = await GitWorkTree.LocateAsync(workingDirectory, ct);
        string root = workTree?.Root ?? workingDirectory;
        string settingsPath = Path.Combine(root, SettingsFile);
        string shown = PathFormat.Relative(workingDirectory, settingsPath);

        string? solution = await RootRelativeAsync(request.Solution, workingDirectory, root, ct);
        string? spec = await RootRelativeAsync(request.Spec, workingDirectory, root, ct);
        string? existing = File.Exists(settingsPath) ? await File.ReadAllTextAsync(settingsPath, ct) : null;
        ClaudeSettingsMerge.Result merged = ClaudeSettingsMerge.Merge(
            existing, EmbeddedResourceText.Load(SnippetResource), solution, spec, request.CommandTree, shown);
        RefuseUnresolvable(solution, spec, root, shown);

        foreach (string line in merged.Lines) await output.WriteLineAsync(line);

        if (!merged.Changed)
        {
            await output.WriteLineAsync($"nothing to change in {shown}");
            return ExitCodes.Clean;
        }

        AtomicFile.WriteAllText(settingsPath, merged.Text);
        await output.WriteLineAsync($"wrote {shown}");
        return ExitCodes.Clean;
    }

    // The path the hook will be handed: relative to the root it runs from, with '/' and the names on disk, as a
    // committed file spells it.
    private static async Task<string?> RootRelativeAsync(
        string? path, string workingDirectory, string root, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string spelled = await GitWorkTree.CommittedSpellingAsync(Path.GetFullPath(path, workingDirectory), ct);
        return PathFormat.Relative(root, spelled);
    }

    // The hook's own resolution, from the root and with the arguments its command carries, so an entry that would
    // find none is refused here rather than written to fail at every turn. Only the solution is resolved: finding the
    // spec by convention takes a workspace load.
    private void RefuseUnresolvable(string? solution, string? spec, string root, string shown)
    {
        InvocationTargets targets = HookRunner.TargetsFor(solution, spec, root, environment);
        try
        {
            ModelPipeline.DiscoverSolution(targets.Solution, root, environment);
        }
        catch (UserErrorException ex)
        {
            throw new UserErrorException(
                $"Nothing was written to {shown}: the hook runs from {root} and would find no solution there. Pass "
                + $"the one it should check: sliderule install-hook <solution>.\n{ex.Message}", ex);
        }
    }
}
