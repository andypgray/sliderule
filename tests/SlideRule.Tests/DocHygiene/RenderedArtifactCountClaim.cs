namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     One prose claim about how much this repository's own render writes, lifted from a hand-written
///     doc: how many artifacts it produces, or how many of those are per-directory cards.
/// </summary>
/// <param name="Doc">The repository-relative path of the doc the claim was written in.</param>
/// <param name="DocLine">The 1-based line in the doc the claim sits on.</param>
/// <param name="Kind">Which of the two committed sets the claim counts.</param>
/// <param name="Written">The count exactly as the prose spells it, word or numeral.</param>
internal sealed record RenderedArtifactCountClaim(
    string Doc,
    int DocLine,
    RenderedArtifactCounts.ClaimKind Kind,
    string Written);
