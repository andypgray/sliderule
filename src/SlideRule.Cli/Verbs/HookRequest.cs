namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to a <c>hook</c> run, before anything about the tree it fires for is known.</summary>
/// <remarks>
///     <paramref name="Solution" /> (a file or a directory to search) and <paramref name="Spec" /> are relative to the
///     working tree the hook fires for and still unresolved; null leaves the choice to the settings file, then to the
///     walk up from that tree or the spec convention. <paramref name="MaxRounds" /> caps the consecutive
///     continuations one prompt may get.
/// </remarks>
internal sealed record HookRequest(
    string? Solution,
    string? Spec,
    string DiffBase,
    int MaxRounds,
    string WorkingDirectory);
