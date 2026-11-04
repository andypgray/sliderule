using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI composes a narrowing notice: which solution filter narrowed the run, and which
///     declared projects it left unchecked.
/// </summary>
/// <remarks>
///     The wording is <see cref="NarrowedUniverseNotice" />'s, which each factory comes from; what lives here is
///     the CLI's half.
/// </remarks>
internal static class NarrowingNotices
{
    /// <summary>
    ///     Writes the narrowing stamp to <paramref name="output" />, stdout, above a verb's answer, or nothing at
    ///     all when the run was not narrowed.
    /// </summary>
    internal static void Stamp(
        TextWriter output, CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        if (Text(source, factory) is { } stamp) LineBlocks.WriteStamp(output, stamp);
    }

    /// <summary>The narrowing stamp's text, or null when the run was not narrowed.</summary>
    internal static string? Text(CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        return source.Diagnostics.IsNarrowed ? Compose(source, factory) : null;
    }

    /// <summary>
    ///     Writes the narrowing refusal a verb emits to <paramref name="error" />, stderr, instead of an answer.
    ///     Unguarded: the caller decides whether this run refuses, and returns the exit code that goes with it.
    /// </summary>
    internal static void Refusal(
        TextWriter error, CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        string refusal = Compose(source, factory);
        LineBlocks.Write(error, refusal);
    }

    private static string Compose(CodebaseSource source, Func<string, IReadOnlyList<string>, string> factory)
    {
        IReadOnlyList<string> uncheckedProjects = NarrowedUniverseNotice.Relative(
            source.Diagnostics.UncheckedProjects, source.SolutionDirectory);

        return factory(source.SolutionName, uncheckedProjects);
    }
}
