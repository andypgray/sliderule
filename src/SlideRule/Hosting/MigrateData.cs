namespace SlideRule.Hosting;

/// <summary>What a <c>Migrate</c> rule carries beyond an ordinary <see cref="ArchRule" />.</summary>
internal sealed class MigrateData
{
    internal MigrateData(string from, string toSentence, string baselinePath, MigrationPolicy policy)
    {
        From = from;
        ToSentence = toSentence;
        BaselinePath = baselinePath;
        Policy = policy;
    }

    /// <summary>
    ///     Gets the description of the pattern the code still follows, in the spec's own words — the
    ///     <c>from</c> the migration was declared with.
    /// </summary>
    public string From { get; }

    /// <summary>
    ///     Gets the target constraint as one English sentence: what the code is moving toward, and what a
    ///     violation outside the baseline is judged against.
    /// </summary>
    public string ToSentence { get; }

    /// <summary>
    ///     Gets the file recording the rule's grandfathered violations: the path the spec named, or the
    ///     <see cref="SlideRule.Baselines.BaselineConventions.DefaultPath" /> default. Never null, stored with forward
    ///     slashes and resolved against the solution directory.
    /// </summary>
    public string BaselinePath { get; }

    /// <summary>
    ///     Gets the policy the spec set with <c>WhileYoureThere</c>, or <see cref="MigrationPolicy.MigrateIfSmall" />
    ///     where it set none.
    /// </summary>
    public MigrationPolicy Policy { get; }
}
