namespace SlideRule.Cli.Verbs;

/// <summary>
///     The parsed inputs to a <c>render</c> run, with the settings file's render targets already applied.
/// </summary>
/// <remarks>
///     <paramref name="AllowWorkspaceDiagnostics" /> opts out of
///     <see cref="SlideRule.Roslyn.Diagnostics.IncompleteModelGate" />. <paramref name="DiagramOnly" /> and
///     <paramref name="DiagramExclude" /> are project-name glob lists that scope the <paramref name="Diagram" />
///     target.
/// </remarks>
internal sealed record RenderRequest(
    string? Solution,
    string? Spec,
    string WorkingDirectory,
    bool NoCache,
    bool AllowWorkspaceDiagnostics,
    string? Diagram = null,
    string? DiagramOnly = null,
    string? DiagramExclude = null,
    bool Model = false,
    bool Check = false);
