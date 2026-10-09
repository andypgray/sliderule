namespace SlideRule.Cli.Verbs;

/// <summary>Which <c>baseline</c> subcommand a run is: each one is a mode of the same ratchet pipeline.</summary>
internal enum BaselineMode
{
    /// <summary><c>baseline init</c>: capture each <em>uncaptured</em> ratcheted rule's current violations.</summary>
    Init,

    /// <summary>
    ///     <c>baseline accept-reductions</c>: drop captured entries whose violation no longer occurs, and lower or
    ///     first record site counts; it refuses new entries and rises alike.
    /// </summary>
    AcceptReductions,

    /// <summary><c>baseline add</c>: grandfather exactly one currently observed violation, with attribution.</summary>
    Add,

    /// <summary>
    ///     <c>baseline rename</c>: move a renamed rule's section from its old rule ID to its new one, every entry
    ///     kept and re-sealed; it reads the spec and the baseline files, never the code.
    /// </summary>
    Rename
}
