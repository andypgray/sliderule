namespace SlideRule.Cli.Verbs;

/// <summary>
///     The parsed inputs to an <c>arch_context</c> run: <paramref name="Path" />, a file or directory, absolute or
///     solution-relative, against the server's bound <paramref name="Solution" /> and <paramref name="Spec" />.
/// </summary>
internal sealed record ContextRequest(string Path, string? Solution, string? Spec, string WorkingDirectory);
