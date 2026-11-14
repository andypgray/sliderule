namespace SlideRule.Tests.Cli;

/// <summary>
///     The lines <c>check --timings</c> writes to stderr, parsed: each row's name and seconds in the order written,
///     <c>total</c> included, and the cache line, null when the block has none.
/// </summary>
internal sealed record TimingsBlock(IReadOnlyList<(string Name, double Seconds)> Rows, string? Cache)
{
    /// <summary>The row names in the order written.</summary>
    public IReadOnlyList<string> Names => [.. Rows.Select(row => row.Name)];
}
