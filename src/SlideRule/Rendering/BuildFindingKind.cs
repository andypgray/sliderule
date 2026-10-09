namespace SlideRule.Rendering;

/// <summary>
///     What a <see cref="BuildFinding" /> is about: a law that was broken, or debt that has grown past what
///     its baseline records. A reader deciding how loudly to report a finding asks this and nothing else.
/// </summary>
internal enum BuildFindingKind
{
    /// <summary>
    ///     A rule that is law was broken. An <c>Enforce</c> rule, and a quarantine's containment rule —
    ///     which ratchets, but states a boundary rather than a migration, so a new crossing of it is not
    ///     scheduled debt.
    /// </summary>
    BrokenRule,

    /// <summary>
    ///     A <c>Migrate</c> rule found the old pattern somewhere its baseline does not grandfather: debt
    ///     that has just grown, rather than the debt already agreed.
    /// </summary>
    NewDebt
}
