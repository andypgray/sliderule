namespace SlideRule.Baselines;

/// <summary>
///     A baseline file the orphan scan could not read: the reader refused its text, or the file could not be
///     opened. The scan names it and carries on past it.
/// </summary>
// A class rather than a record, because this target framework declares no IsExternalInit.
internal sealed class UnreadableBaseline
{
    /// <summary>The file at <paramref name="path" />, refused for <paramref name="reason" />.</summary>
    /// <param name="path">The baseline file, solution-relative with forward slashes.</param>
    /// <param name="reason">Why it could not be read, as one sentence naming neither the file nor a recovery.</param>
    internal UnreadableBaseline(string path, string reason)
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>Gets the baseline file, solution-relative with forward slashes.</summary>
    internal string Path { get; }

    /// <summary>Gets why the file could not be read.</summary>
    internal string Reason { get; }
}
