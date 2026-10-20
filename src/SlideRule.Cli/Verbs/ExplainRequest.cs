namespace SlideRule.Cli.Verbs;

/// <summary>The parsed inputs to an <c>explain</c> run.</summary>
/// <remarks>
///     Exactly one of <paramref name="RuleId" /> (a post-desugar ID, e.g. <c>legacy/billing/containment</c>),
///     <paramref name="Type" /> (a full or simple type name) and <paramref name="Path" /> (a file or directory,
///     absolute or solution-relative) is set; each surface refuses any other combination before building the request.
/// </remarks>
internal sealed record ExplainRequest(
    string? RuleId,
    string? Solution,
    string? Spec,
    string WorkingDirectory,
    bool NoCache,
    string? Type = null,
    string? Path = null);
