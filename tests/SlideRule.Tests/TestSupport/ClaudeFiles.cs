using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Rendering;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The precondition every "render says nothing about Claude Code" row rests on.
/// </summary>
internal static class ClaudeFiles
{
    /// <summary>
    ///     Asserts that render would find no CLAUDE file for a session started in <paramref name="directory" />:
    ///     none in it or in any directory above it short of the filesystem root, the user memory file apart.
    /// </summary>
    /// <remarks>
    ///     The walk, the names and the user-memory exemption are render's own
    ///     (<see cref="ClaudeInstructionFiles.Find" />), so the guard reds on exactly the files that would make
    ///     render warn and void the row. The user memory file is the machine's own and never counts, and it is
    ///     read off the real environment because that is the one the row's render reads.
    ///     <paramref name="consequence" /> completes the sentence <c>Stray CLAUDE file '&lt;path&gt;' …</c>, so
    ///     each caller says what one would cost it.
    /// </remarks>
    internal static void ShouldHaveNoClaudeFileInAnyAncestor(this string directory, string consequence)
    {
        IReadOnlySet<string> userMemory = ClaudeInstructionFiles.UserMemoryFiles(new SystemEnvironment());
        IReadOnlyList<string> stray = ClaudeInstructionFiles.Find(directory, userMemory);

        stray.ShouldBeEmpty($"Stray CLAUDE file '{stray.FirstOrDefault()}' {consequence}");
    }
}
