namespace SlideRule.Checking;

/// <summary>
///     Whether one rule's verdict follows from a single project's compilation, and — when it does not —
///     the sentence saying what stands in the way.
/// </summary>
internal sealed class OneProjectVerdict
{
    /// <summary>Builds the verdict from what stands in the way, or from null when nothing does.</summary>
    /// <param name="reason">Why the rule takes the whole solution, or null for a decidable rule.</param>
    internal OneProjectVerdict(string? reason)
    {
        Reason = reason;
    }

    /// <summary>Whether the rule's verdict is a function of one project's own compilation.</summary>
    internal bool Decidable => Reason is null;

    /// <summary>Why the rule takes the whole solution, or null when it does not.</summary>
    internal string? Reason { get; }
}
