using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Rendering;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The precondition every "render says nothing about Claude Code" row rests on.
/// </summary>
internal static class ClaudeFiles
{
    private static readonly string[] Names = ["CLAUDE.md", Path.Combine(".claude", "CLAUDE.md"), "CLAUDE.local.md"];

    /// <summary>
    ///     Asserts that neither <paramref name="directory" /> nor any directory above it holds a CLAUDE file, the
    ///     user memory file apart.
    /// </summary>
    /// <remarks>
    ///     Render walks from the solution directory to the filesystem root, so a stray file in any ancestor of a
    ///     temp root would make it warn and quietly void the row. The user memory file is the machine's own and
    ///     never counts, and it is read off the real environment because that is the one the row's render reads.
    ///     <paramref name="consequence" /> completes the sentence <c>Stray CLAUDE file '&lt;path&gt;' …</c>, so
    ///     each caller says what one would cost it.
    /// </remarks>
    internal static void ShouldHaveNoClaudeFileInAnyAncestor(string directory, string consequence)
    {
        IReadOnlySet<string> userMemory = ClaudeInstructionFiles.UserMemoryFiles(new SystemEnvironment());
        for (DirectoryInfo? dir = new(directory); dir is not null; dir = dir.Parent)
            foreach (string name in Names)
            {
                string candidate = Path.Combine(dir.FullName, name);
                if (userMemory.Contains(candidate)) continue;

                File.Exists(candidate)
                    .ShouldBeFalse($"Stray CLAUDE file '{candidate}' {consequence}");
            }
    }
}
