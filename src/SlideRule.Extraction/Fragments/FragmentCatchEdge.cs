namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed catch edge inside one fragment (GRAMMAR §4.8): source and caught type by FQN, the sites of
///     the <c>catch</c> clauses, and two nested subsets of them: <see cref="UnfilteredSites" />, the clauses
///     that spell no <c>when</c> filter, and within those <see cref="SwallowingSites" />, the ones whose block
///     does not end in a <c>throw</c>. A bare <c>catch</c> records <c>System.Exception</c> as the caught type
///     and counts as unfiltered. The pure-data counterpart of <see cref="SlideRule.Codebase.CatchEdge" />.
/// </summary>
internal sealed record FragmentCatchEdge(
    string SourceFullName,
    string CaughtFullName,
    IReadOnlyList<FragmentSite> Sites,
    IReadOnlyList<FragmentSite> UnfilteredSites,
    IReadOnlyList<FragmentSite> SwallowingSites);
