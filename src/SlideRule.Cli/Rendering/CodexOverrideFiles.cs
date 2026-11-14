using SlideRule.Rendering;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The <c>AGENTS.override.md</c> files that hide what <c>render</c> writes from Codex. In each directory on its
///     walk Codex reads the first of <c>AGENTS.override.md</c> and <c>AGENTS.md</c> that exists as a file, so an
///     override beside the root block or a card stands in for that file in every session that reaches the
///     directory. <see cref="Warnings" /> is what <c>render</c> prints for each one.
/// </summary>
/// <remarks>
///     The lookup mirrors the client's and moves with it (last verified 2026-11-06, codex-rs
///     <c>core/src/agents_md.rs</c> at 49f2fc2c1). The client drops an empty file only after it has chosen it, so
///     an override's text is never read: a blank one hides the file beside it all the same. The user's own
///     <c>~/.codex/AGENTS.override.md</c> is never read either, because the client reads it apart from the project
///     files and it shadows none of them.
/// </remarks>
internal static class CodexOverrideFiles
{
    private const string OverrideName = "AGENTS.override.md";

    /// <summary>
    ///     One warning per file in <paramref name="writtenPaths" /> with an override beside it, in the order given,
    ///     each without the <c>warning:</c> prefix; empty when no override stands beside any of them.
    /// </summary>
    internal static IReadOnlyList<string> Warnings(string solutionDirectory, IEnumerable<string> writtenPaths)
    {
        string solution = Path.GetFullPath(solutionDirectory);

        return writtenPaths
            .Select(Path.GetFullPath)
            .Where(path => File.Exists(OverrideBeside(path)))
            .Select(path => Warning(solution, path))
            .ToList();
    }

    private static string Warning(string solution, string written)
    {
        string directory = Path.GetDirectoryName(written)!;
        string file = PathFormat.Relative(solution, written);
        string shadow = PathFormat.Relative(solution, OverrideBeside(written));
        var cure = $"move the override's text into {file} outside the managed block, or remove {shadow}.";

        string where = PathFormat.Relative(solution, directory);
        string missed = where == "."
            ? "its sessions miss the managed block"
            : $"sessions started in {where} or below it miss that card";

        return $"Codex reads {shadow} in place of {file}, so {missed}; {cure}";
    }

    private static string OverrideBeside(string written)
    {
        return Path.Combine(Path.GetDirectoryName(written)!, OverrideName);
    }
}
