namespace SlideRule.Rendering;

/// <summary>
///     What a <see cref="BuildFinding" /> is about. A reader deciding how loudly to report a finding asks
///     this and nothing else.
/// </summary>
internal enum BuildFindingKind
{
    /// <summary>A rule that is law was broken: an <c>Enforce</c> rule, or a quarantine's containment rule.</summary>
    BrokenRule,

    /// <summary>
    ///     A <c>Migrate</c> rule found the old pattern somewhere its baseline does not grandfather: debt
    ///     that has just grown, rather than the debt already agreed.
    /// </summary>
    NewDebt
}
