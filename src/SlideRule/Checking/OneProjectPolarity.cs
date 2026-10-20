namespace SlideRule.Checking;

/// <summary>What a selection does to a verdict where it stands.</summary>
/// <remarks>
///     The axis a one-project reader's error runs along. Membership computed from one compilation can only
///     be a subset of the whole solution's — a type another project also declares is one this compilation
///     cannot know about — so a missed type costs a violation where the selection bans and grants no
///     exemption where it exempts. The first direction under-reports, which is what the build tier is
///     allowed to do; the second over-reports, which is what it may never do.
/// </remarks>
internal enum OneProjectPolarity
{
    /// <summary>A type inside the selection is a violation, so missing one reports less.</summary>
    Banning,

    /// <summary>A type inside the selection is spared, so missing one reports more.</summary>
    Exempting
}
