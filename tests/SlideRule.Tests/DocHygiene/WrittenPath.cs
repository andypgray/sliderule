namespace SlideRule.Tests.DocHygiene;

/// <summary>One write-report line lifted from a fenced code block in a hand-written doc.</summary>
/// <param name="Doc">The repository-relative path of the doc the line was quoted in.</param>
/// <param name="DocLine">The 1-based line in the doc the quote sits on.</param>
/// <param name="Label">The report verb the line opens with (<c>wrote</c> or <c>unchanged</c>).</param>
/// <param name="Path">The quoted path, relative to the solution the verb ran against.</param>
internal sealed record WrittenPath(string Doc, int DocLine, string Label, string Path);
