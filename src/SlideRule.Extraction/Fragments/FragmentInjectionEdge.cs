namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed constructor-injection edge inside one fragment (GRAMMAR §4.7): source and injected type by
///     FQN, and the sites of the injected constructor parameters. The pure-data counterpart of
///     <see cref="SlideRule.Codebase.InjectionEdge" />.
/// </summary>
internal sealed record FragmentInjectionEdge(
    string SourceFullName,
    string InjectedFullName,
    IReadOnlyList<FragmentSite> Sites);
