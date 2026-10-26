using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI composes an unsupported-projects stamp: which declared projects the run did not
///     reach, and why.
/// </summary>
/// <remarks>
///     The wording is <see cref="UnsupportedProjectsNotice" />'s, which the factory comes from; what lives here is
///     the CLI's half. This line and the verb's <c>--json</c> document spell each path and its reason alike,
///     because both compose each entry through <see cref="UnsupportedProjectsNotice.Entry" /> and
///     <see cref="UnsupportedProjectsNotice.Reason" /> over a solution-relative path.
/// </remarks>
internal static class UnsupportedProjectsNotices
{
    /// <summary>
    ///     Writes the stamp to <paramref name="output" />, stdout, above a verb's answer, or nothing at all when
    ///     the run reached every declared project.
    /// </summary>
    internal static void Stamp(
        TextWriter output, CodebaseSource source, Func<IReadOnlyList<string>, string> factory)
    {
        if (source.Diagnostics.UnsupportedProjects.Count == 0) return;

        IReadOnlyList<string> entries = UnsupportedProjectsNotice.Relative(
            source.Diagnostics.UnsupportedProjects, source.SolutionDirectory);

        LineBlocks.WriteStamp(output, factory(entries));
    }
}
