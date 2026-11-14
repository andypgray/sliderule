using System.CommandLine;

namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to <c>sliderule install-hook</c>, which merges the hook settings into a repository.</summary>
/// <remarks>
///     <paramref name="Solution" /> and <paramref name="Spec" /> are the arguments as given, relative to
///     <paramref name="WorkingDirectory" />, each null to leave it out of the commands written.
///     <paramref name="CommandTree" /> is the tree the run was parsed by, which an existing entry's command line is
///     read against.
/// </remarks>
internal sealed record InstallHookRequest(string? Solution, string? Spec, string WorkingDirectory, Command CommandTree);
