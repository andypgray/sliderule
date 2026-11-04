using SlideRule.Cli.SpecLoading;
using SlideRule.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     A <see cref="StaleSpec" /> as the <c>staleSpec</c> key of the check and status documents: the spec project and
///     the differing file, solution-relative and forward-slashed, with the <see cref="StaleReason" /> and
///     <see cref="SpecComparison" />.
/// </summary>
internal sealed record StaleSpecStamp(string Project, string File, StaleReason Reason, SpecComparison Comparison)
{
    /// <summary>The stamp for <paramref name="stale" />, or null when the spec matches its build.</summary>
    internal static StaleSpecStamp? From(StaleSpec? stale, string solutionDirectory)
    {
        if (stale is null) return null;

        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        return new StaleSpecStamp(
            relativizer.Relative(stale.ProjectFilePath),
            relativizer.Relative(stale.DifferingFile),
            stale.Reason,
            stale.Comparison);
    }
}

/// <summary>The one place the CLI tells a reader that the spec it ran is older than the spec's sources.</summary>
/// <remarks>
///     The line goes to stderr, with the <c>warning:</c> prefix unless the run writes a document, where the
///     <c>staleSpec</c> key carries the fact and stderr is its unadorned echo, as for the workspace diagnostics.
///     <see cref="Stamp" /> also writes it to stdout, above an answer whose reader never sees stderr.
/// </remarks>
internal static class StaleSpecNotice
{
    /// <summary>
    ///     Writes the line for <paramref name="stale" /> to <paramref name="error" />; a no-op when it is null.
    /// </summary>
    internal static void Render(TextWriter error, StaleSpec? stale, string solutionDirectory, bool json = false)
    {
        if (stale is null) return;

        string text = Text(stale, solutionDirectory);
        error.WriteLine(json ? text : $"warning: {text}");
    }

    /// <summary>
    ///     Writes the sentence for <paramref name="stale" /> to <paramref name="output" />, stdout, above an answer
    ///     with no stderr to read, then the blank line that separates the two; a no-op when it is null.
    /// </summary>
    internal static void Stamp(TextWriter output, StaleSpec? stale, string solutionDirectory)
    {
        if (stale is null) return;

        LineBlocks.WriteStamp(output, Text(stale, solutionDirectory));
    }

    /// <summary>The sentence, paths solution-relative.</summary>
    internal static string Text(StaleSpec stale, string solutionDirectory)
    {
        var relativizer = new PathFormat.Relativizer(solutionDirectory);
        string file = relativizer.Relative(stale.DifferingFile);
        string project = relativizer.Relative(stale.ProjectFilePath);

        string change = stale.Reason switch
        {
            StaleReason.Changed => $"'{file}' has changed since it was built",
            StaleReason.Missing => $"'{file}' no longer exists",
            StaleReason.Added => $"'{file}' was added after it was built",
            StaleReason.Newer => $"'{file}' was written after it was built",
            _ => throw new ArgumentOutOfRangeException(nameof(stale), stale.Reason, "Unknown stale reason.")
        };

        return $"the spec project '{stale.ProjectName}' differs from its last build ({change}); "
               + $"this run used the rules that build holds. Rebuild it: dotnet build {project}";
    }
}
