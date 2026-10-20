namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The properties one evaluation of a project found set by a declaration in the repository, one per name
///     and ordinal by name, beside <paramref name="ProjectFile" />, the first line of the project file that
///     evaluation read, where a property nothing set would be declared.
/// </summary>
/// <remarks>
///     The list's presence is itself the fact that an evaluation ran: a fragment nothing evaluated carries no
///     value here at all, where an evaluated project that sets nothing carries an empty list.
/// </remarks>
internal sealed record FragmentProperties(FragmentSite ProjectFile, IReadOnlyList<FragmentProperty> Declared);
