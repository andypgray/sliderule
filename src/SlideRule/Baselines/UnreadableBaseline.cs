namespace SlideRule.Baselines;

/// <summary>
///     A baseline file the orphan scan could not read: the reader refused its text, or the file could not be
///     opened. The scan names it and carries on past it.
/// </summary>
internal sealed class UnreadableBaseline
{
    internal UnreadableBaseline(string path, string reason)
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>Gets the baseline file, solution-relative with forward slashes.</summary>
    internal string Path { get; }

    /// <summary>Gets why the file could not be read, as one sentence naming neither the file nor a recovery.</summary>
    internal string Reason { get; }
}
