using System.Text;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The Claude Code instruction files that decide whether its sessions read the root managed block. By
///     default Claude Code reads a <c>CLAUDE.md</c>, <c>.claude/CLAUDE.md</c> or <c>CLAUDE.local.md</c> found in
///     the directory it starts in or any directory above it, and then reads no <c>AGENTS.md</c> at all, so a
///     block written beside such a file reaches its sessions only when one of those files imports it.
///     <see cref="Warning" /> is what <c>render</c> prints when none does.
/// </summary>
/// <remarks>
///     Every rule here mirrors the client's own and moves with it (last verified 2026-09-24, on Claude Code
///     2.1.280): the three names in the order it looks for them, the walk up to the last directory below the
///     filesystem root, the user memory file that never counts, and what it reads as an import. The client's
///     settings are never read: an option that loads both files is one developer's machine, and the warning
///     is for everyone working in the repository.
/// </remarks>
internal static class ClaudeInstructionFiles
{
    private const string PlainName = "CLAUDE.md";

    // The names the client weighs against AGENTS.md in each directory, in the order it looks for them.
    private static readonly string[] Names = [PlainName, Path.Combine(".claude", PlainName), "CLAUDE.local.md"];

    /// <summary>
    ///     The warning <c>render</c> prints after its own, without the <c>warning:</c> prefix, or <c>null</c>
    ///     when nothing stands between Claude Code and the root block: no CLAUDE file was found, or one that was
    ///     imports the block's file.
    /// </summary>
    /// <param name="solutionDirectory">The directory the root block is written into.</param>
    /// <param name="environment">The seam the home and configuration directories are read through.</param>
    internal static string? Warning(string solutionDirectory, IEnvironment environment)
    {
        const string agents = ContextFileComposer.FileName;

        string? home = Home(environment);
        string blockFile = Path.GetFullPath(Path.Combine(solutionDirectory, agents));
        IReadOnlyList<string> found = Find(solutionDirectory, UserMemoryFiles(environment));
        if (found.Count == 0 || found.Any(file => ImportsBlockFile(file, blockFile, home))) return null;

        // The nearest file is the one named: a plain CLAUDE.md beside the block takes the import itself, and
        // any other file is cured by a CLAUDE.md beside the block that holds it.
        string nearest = PathFormat.Relative(solutionDirectory, found[0]);
        string cure = string.Equals(nearest, PlainName, StringComparison.Ordinal)
            ? $"add the line @{agents} to {PlainName}"
            : $"add a {PlainName} beside {agents} holding the line @{agents}";

        return $"by default Claude Code reads {nearest} instead of {agents}, so its sessions miss the managed "
               + $"block; {cure}.";
    }

    /// <summary>
    ///     Every CLAUDE file that counts against <c>AGENTS.md</c> for a session started in
    ///     <paramref name="solutionDirectory" />, nearest first: that directory and each one above it, stopping
    ///     below the filesystem root, and in each directory the three names in the client's order.
    /// </summary>
    /// <param name="solutionDirectory">The directory the walk starts in.</param>
    /// <param name="excludedPaths">Full paths that never count, however the walk reaches them.</param>
    internal static IReadOnlyList<string> Find(string solutionDirectory, IReadOnlySet<string> excludedPaths)
    {
        var found = new List<string>();
        for (DirectoryInfo? directory = new(Path.GetFullPath(solutionDirectory));
             directory?.Parent is not null;
             directory = directory.Parent)
            foreach (string name in Names)
            {
                string candidate = Path.Combine(directory.FullName, name);
                if (File.Exists(candidate) && !excludedPaths.Contains(candidate)) found.Add(candidate);
            }

        return found;
    }

    /// <summary>
    ///     The user memory files, which never count against <c>AGENTS.md</c>: <c>.claude/CLAUDE.md</c> under the
    ///     home directory, and <c>CLAUDE.md</c> under <c>CLAUDE_CONFIG_DIR</c> when that is set.
    /// </summary>
    /// <param name="environment">The seam the two directories are read through.</param>
    internal static IReadOnlySet<string> UserMemoryFiles(IEnvironment environment)
    {
        var files = new HashSet<string>(PathComparison.Comparer);
        if (Home(environment) is { } home) files.Add(Path.GetFullPath(Path.Combine(home, ".claude", PlainName)));

        // Kept beside the home file rather than replacing it, although the client then reads only this one as
        // the user's own: a home file it would count goes unreported, which errs toward saying nothing.
        if (environment.NonBlankVariable(SlideRuleEnvVars.ClaudeConfigDirectory) is { } configDirectory)
            files.Add(Path.GetFullPath(Path.Combine(configDirectory, PlainName)));

        return files;
    }

    /// <summary>
    ///     Whether <paramref name="text" />, read from a file in <paramref name="fileDirectory" />, imports
    ///     <paramref name="blockFile" /> the way Claude Code reads an import: an <c>@</c> starting the text or
    ///     following whitespace, then a path running to the next whitespace, resolved against the file's own
    ///     directory, or against <paramref name="home" /> when it starts <c>~/</c>. Nothing inside a fenced code
    ///     block, an HTML comment or an inline code span is an import.
    /// </summary>
    /// <remarks>
    ///     A function of its arguments alone, so every shape the client does and does not read as an import can
    ///     be driven by a test with no file on disk.
    /// </remarks>
    internal static bool Imports(string text, string fileDirectory, string blockFile, string? home)
    {
        string target = Path.GetFullPath(blockFile);
        string readable = WithoutCodeSpans(WithoutComments(WithoutFencedBlocks(text)));

        return readable.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 1 && word[0] == '@')
            .Any(word => NamesBlockFile(word.Substring(1), fileDirectory, home, target));
    }

    /// <summary>
    ///     Whether a found CLAUDE file brings the block in: a link to the block's file, or text that imports it.
    ///     A file that cannot be read is still a file the client finds, and one this run cannot show imports
    ///     anything.
    /// </summary>
    internal static bool ImportsBlockFile(string file, string blockFile, string? home)
    {
        try
        {
            if (new FileInfo(file).ResolveLinkTarget(returnFinalTarget: true) is { } target
                && string.Equals(
                    PathCanonicalizer.Resolve(target.FullName),
                    PathCanonicalizer.Resolve(blockFile),
                    PathComparison.Comparison))
                return true;

            string text = File.ReadAllText(file);
            return Imports(text, Path.GetDirectoryName(file)!, blockFile, home);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Where the client finds the home directory: USERPROFILE on Windows and HOME elsewhere, as Node reads it.
    private static string? Home(IEnvironment environment)
    {
        string variable = OperatingSystem.IsWindows() ? SlideRuleEnvVars.UserProfile : SlideRuleEnvVars.Home;
        return environment.NonBlankVariable(variable);
    }

    // One import names the block's file when it resolves to it and ends in the same name. The name is compared
    // on its own because Windows drops a trailing dot from a full path, and "@AGENTS.md." names another file. A
    // null character, which only a binary file could hold, is no path, and resolving it would throw.
    private static bool NamesBlockFile(string path, string fileDirectory, string? home, string target)
    {
        if (path.Contains('\0')) return false;
        if (!string.Equals(PathFormat.Leaf(path), Path.GetFileName(target), PathComparison.Comparison)) return false;

        string? resolved = path.StartsWith("~/", StringComparison.Ordinal)
            ? home is null ? null : Path.Combine(home, path.Substring(2))
            : Path.Combine(fileDirectory, path);

        return resolved is not null && string.Equals(Path.GetFullPath(resolved), target, PathComparison.Comparison);
    }

    // Drops every line from an opening fence of three or more backticks or tildes through the line that closes
    // it: the same character, at least as many, and nothing else. A fence never closed runs to the end.
    private static string WithoutFencedBlocks(string text)
    {
        var kept = new StringBuilder(text.Length);
        string? fence = null;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (fence is not null)
            {
                if (ClosesFence(trimmed, fence)) fence = null;
            }
            else if (FenceOpening(trimmed) is { } opening)
            {
                fence = opening;
            }
            else
            {
                kept.Append(line);
            }

            kept.Append('\n');
        }

        return kept.ToString();
    }

    private static string? FenceOpening(string trimmed)
    {
        bool opens = trimmed.StartsWith("```", StringComparison.Ordinal)
                     || trimmed.StartsWith("~~~", StringComparison.Ordinal);

        return opens ? trimmed.Substring(0, RunLength(trimmed, 0, trimmed[0])) : null;
    }

    private static bool ClosesFence(string trimmed, string fence)
    {
        return trimmed.Length >= fence.Length && RunLength(trimmed, 0, fence[0]) == trimmed.Length;
    }

    // Replaces each HTML comment, across lines if it runs across them, with a space. One never closed runs to
    // the end.
    private static string WithoutComments(string text)
    {
        var kept = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            int open = text.IndexOf("<!--", position, StringComparison.Ordinal);
            if (open < 0)
            {
                kept.Append(text, position, text.Length - position);
                break;
            }

            kept.Append(text, position, open - position).Append(' ');
            int close = text.IndexOf("-->", open + 4, StringComparison.Ordinal);
            position = close < 0 ? text.Length : close + 3;
        }

        return kept.ToString();
    }

    // Replaces each inline code span with a space: a run of backticks through the next run of exactly as many.
    // A run with no partner is literal text.
    private static string WithoutCodeSpans(string text)
    {
        var kept = new StringBuilder(text.Length);
        var position = 0;
        while (position < text.Length)
        {
            if (text[position] != '`')
            {
                kept.Append(text[position]);
                position++;
                continue;
            }

            int run = RunLength(text, position, '`');
            int close = ClosingRun(text, position + run, run);
            if (close < 0)
            {
                kept.Append(text, position, run);
                position += run;
                continue;
            }

            kept.Append(' ');
            position = close + run;
        }

        return kept.ToString();
    }

    private static int ClosingRun(string text, int from, int length)
    {
        int position = from;
        while (position < text.Length)
        {
            if (text[position] != '`')
            {
                position++;
                continue;
            }

            int run = RunLength(text, position, '`');
            if (run == length) return position;

            position += run;
        }

        return -1;
    }

    private static int RunLength(string text, int from, char repeated)
    {
        int end = from;
        while (end < text.Length && text[end] == repeated) end++;

        return end - from;
    }
}
