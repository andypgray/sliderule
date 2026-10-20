namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed throw edge inside one fragment (GRAMMAR §4.8): source and thrown type by FQN, and the sites
///     of the <c>throw</c> statements and throw expressions. The thrown type is the thrown expression's static
///     type; a bare rethrow (<c>throw;</c>) records nothing. The pure-data counterpart of
///     <see cref="SlideRule.Codebase.ThrowEdge" />.
/// </summary>
internal sealed record FragmentThrowEdge(
    string SourceFullName,
    string ThrownFullName,
    IReadOnlyList<FragmentSite> Sites);
