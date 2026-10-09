namespace SlideRule.Tests.DocHygiene;

/// <summary>One grandfathered count lifted from a fenced code block in a hand-written doc.</summary>
/// <param name="Doc">The repository-relative path of the doc the count was quoted in.</param>
/// <param name="DocLine">The 1-based line in the doc the count sits on.</param>
/// <param name="RuleId">
///     The rule the count belongs to, or <see cref="GrandfatheredCounts.RootTotal" /> for a
///     <c>Burndown:</c> line, which totals every baseline under the solution.
/// </param>
/// <param name="Count">The quoted count of currently grandfathered violations.</param>
/// <param name="Stale">
///     The quoted count of baseline entries that no longer match anything ("fixed awaiting acceptance"),
///     or <see langword="null" /> when the quoted shape does not carry one.
/// </param>
internal sealed record GrandfatheredCount(string Doc, int DocLine, string RuleId, int Count, int? Stale);
