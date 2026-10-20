namespace SlideRule.Checking;

/// <summary>
///     Whether one rule's verdict follows from a single project's compilation, and — when it does not —
///     the sentence saying what stands in the way.
/// </summary>
internal sealed class OneProjectVerdict
{
    internal OneProjectVerdict(string? reason)
    {
        Reason = reason;
    }

    internal bool Decidable => Reason is null;

    /// <summary>Why the rule takes the whole solution, or null when it does not.</summary>
    internal string? Reason { get; }
}
