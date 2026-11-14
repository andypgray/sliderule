using System.Globalization;
using SlideRule.Rendering;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Codex's budget for the <c>AGENTS.md</c> files it reads. A session reads every one from the repository root
///     down to the directory it starts in, root first, and stops at <see cref="DefaultBytes" /> of them combined
///     unless its settings raise the limit: the file that crosses the limit is cut short and every file after it
///     is skipped. <see cref="Warning" /> is what <c>render</c> prints when the files it writes would cross it.
/// </summary>
/// <remarks>
///     The walk and the budget mirror the client's and move with it (last verified 2026-11-05, on the main branch
///     of openai/codex). Only the files render writes are counted, at the bytes it writes: a hand-written
///     <c>AGENTS.md</c> above the solution directory, an <c>AGENTS.override.md</c> and the client's settings are
///     not read.
/// </remarks>
internal static class CodexInstructionBudget
{
    // Codex's default for project_doc_max_bytes, the setting that raises it.
    internal const int DefaultBytes = 32 * 1024;

    private const string Cure = "raise project_doc_max_bytes in Codex's config.toml";

    private static readonly string Limit =
        $"and by default Codex reads only the first {Count(DefaultBytes)} bytes of AGENTS.md files";

    /// <summary>
    ///     The warning <c>render</c> prints after its own, without the <c>warning:</c> prefix, or <c>null</c> when
    ///     every file in <paramref name="files" /> reaches a session whole. A file's total is its own bytes and
    ///     those of every file in a directory above it, which is what a session started in its directory reads.
    ///     The root block is named whenever it crosses the limit on its own, since every session then loses its
    ///     end; otherwise the card with the largest total over the limit is.
    /// </summary>
    internal static string? Warning(string solutionDirectory, IReadOnlyList<(string Path, int Bytes)> files)
    {
        string[] solution = PathFormat.Segments(Path.GetFullPath(solutionDirectory));
        List<PlacedFile> placed = files
            .Select(file => new PlacedFile(file.Path, DirectorySegments(file.Path), file.Bytes))
            .ToList();

        PlacedFile? root = placed.FirstOrDefault(file => file.Directory.Length == solution.Length
                                                         && PathFormat.IsWithin(file.Directory, solution));
        if (root is not null && root.Bytes > DefaultBytes)
        {
            string rootName = PathFormat.Relative(solutionDirectory, root.Path);
            return $"{rootName} is {Count(root.Bytes)} bytes, {Limit}, so its sessions lose everything after that; "
                   + $"{Cure}, or shorten {rootName}.";
        }

        (PlacedFile? File, int Total) largest = placed
            .Select(file => ((PlacedFile?)file, Total(file, placed)))
            .OrderByDescending(entry => entry.Item2)
            .FirstOrDefault();
        if (largest.File is null || largest.Total <= DefaultBytes) return null;

        string top = PathFormat.Relative(solutionDirectory, (root ?? largest.File).Path);
        string card = PathFormat.Relative(solutionDirectory, largest.File.Path);
        string directory = PathFormat.Relative(solutionDirectory, Path.GetDirectoryName(largest.File.Path)!);
        return $"the files from {top} down to {card} total {Count(largest.Total)} bytes, {Limit}, so a session "
               + $"started in {directory} loses everything after that; {Cure}, or shorten those files.";
    }

    // What a session started in the file's directory reads: every file in that directory or one above it.
    private static int Total(PlacedFile file, IEnumerable<PlacedFile> placed)
    {
        return placed
            .Where(other => PathFormat.IsWithin(other.Directory, file.Directory))
            .Sum(other => other.Bytes);
    }

    private static string[] DirectorySegments(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        return PathFormat.Segments(directory);
    }

    private static string Count(int bytes)
    {
        return bytes.ToString("N0", CultureInfo.InvariantCulture);
    }

    private sealed record PlacedFile(string Path, string[] Directory, int Bytes);
}
