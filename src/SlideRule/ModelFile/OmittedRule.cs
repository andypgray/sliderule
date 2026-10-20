namespace SlideRule.ModelFile;

/// <summary>A rule the file names but does not carry, and why.</summary>
/// <remarks>
///     Carried in the file rather than left implicit, so a reader can tell "this rule is satisfied" from
///     "this rule is not in the file": the second is not silence, and a reader that treated it as silence
///     would report a clean verdict for a rule it never evaluated.
/// </remarks>
internal sealed class OmittedRule
{
    internal OmittedRule(string id, string reason)
    {
        Id = id;
        Reason = reason;
    }

    internal string Id { get; }

    /// <summary>Why the rule is not carried, as a sentence.</summary>
    internal string Reason { get; }
}
