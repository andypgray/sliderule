using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.Cli;

namespace SlideRule.Tests.Legacy;

/// <summary>
///     The whole of a legacy CLI run, formatted for a Shouldly <c>customMessage</c>: the MSBuild this
///     process registered, then the run's <see cref="CliResultAssertions.Transcript" />.
/// </summary>
/// <remarks>
///     The legacy tests are the ones that fail on somebody else's machine — a CI runner with a different
///     Visual Studio, or none. In human mode a workspace-load diagnostic goes only to stderr, so an
///     assertion on the exit code alone throws away the runner's own account of what went wrong and leaves
///     a bare number to reason from. <see cref="MsBuildBootstrap.LastSelection" /> is read through the
///     quarantine's sanctioned boundary type, so this reaches past nothing the spec protects.
/// </remarks>
internal static class LegacyRunContext
{
    /// <summary>The MSBuild the process registered, for a message that has no run to report.</summary>
    internal static string MsBuildSelection => MsBuildBootstrap.LastSelection ?? "(none registered)";

    /// <summary>Formats one run. <paramref name="label" /> names it when a message carries more than one.</summary>
    internal static string DescribeLegacyRun(this CliResult result, string? label = null)
    {
        string heading = label is null ? string.Empty : $"[{label}] ";
        string transcript = result.Transcript();

        return $"{heading}MSBuild: {MsBuildSelection}{Environment.NewLine}{transcript}";
    }
}
