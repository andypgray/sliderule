namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to <c>sliderule init</c>, which scaffolds a solution's first spec project.</summary>
/// <remarks>
///     <paramref name="Solution" /> is the argument as given, for discovery to resolve, since init never reads a
///     <c>sliderule.json</c>. <paramref name="Name" />, <paramref name="Framework" /> and <paramref name="Output" />
///     override the project name, its target framework and the directory its folder goes in, each null for the
///     default; <paramref name="FromGraph" /> writes the rules recorded from the project graph.
/// </remarks>
internal sealed record InitRequest(
    string? Solution,
    string? Name,
    string? Framework,
    string? Output,
    bool FromGraph,
    bool AllowWorkspaceDiagnostics,
    string WorkingDirectory);
