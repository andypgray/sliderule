namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed construction edge inside one fragment (GRAMMAR §4.5): source and constructed type by FQN,
///     and the sites of the object creations. The pure-data counterpart of
///     <see cref="SlideRule.Codebase.ConstructorEdge" />.
/// </summary>
internal sealed record FragmentConstructorEdge(
    string SourceFullName,
    string ConstructedFullName,
    IReadOnlyList<FragmentSite> Sites);
