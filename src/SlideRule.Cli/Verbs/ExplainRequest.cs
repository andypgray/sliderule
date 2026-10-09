namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to an <c>explain</c> run — free of Roslyn types so it crosses the MSBuild gate.</summary>
/// <param name="RuleId">
///     The rule ID to explain (a post-desugar ID, e.g. <c>legacy/billing/containment</c>), or null when the
///     run explains a type or a path instead. Exactly one of this, <paramref name="Type" /> and
///     <paramref name="Path" /> is set; each surface refuses any other combination before building the request.
/// </param>
/// <param name="Solution">
///     The solution to load: the settings file's where one applies, otherwise the positional argument (a file, a
///     directory, or null for discovery).
/// </param>
/// <param name="Spec">
///     The <c>--spec</c> value (a built DLL or a solution-member csproj), else the settings file's spec, else null
///     for the convention.
/// </param>
/// <param name="WorkingDirectory">
///     The directory solution discovery walks up from (unused on the rule form's DLL fast path).
/// </param>
/// <param name="NoCache">
///     Whether to bypass the persisted extraction cache entirely (no read, no write). The rule form fronts it
///     because it reads presence — the rule it found — and a hit spares it the workspace altogether, that
///     form's whole cost; the type and path forms front it as <c>check</c> does. Unused on the rule form's DLL
///     fast path, which opens no solution to cache.
/// </param>
/// <param name="Type">The type to explain, by full or simple name, or null.</param>
/// <param name="Path">The file or directory whose declared types to explain, absolute or solution-relative, or null.</param>
internal sealed record ExplainRequest(
    string? RuleId,
    string? Solution,
    string? Spec,
    string WorkingDirectory,
    bool NoCache,
    string? Type = null,
    string? Path = null);
