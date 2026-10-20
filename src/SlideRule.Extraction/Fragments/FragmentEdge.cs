namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed reference edge inside one fragment: source and target by FQN, and the <c>file:line</c>
///     sites where the reference occurs.
/// </summary>
internal sealed record FragmentEdge(
    string SourceFullName,
    string TargetFullName,
    IReadOnlyList<FragmentSite> Sites);
