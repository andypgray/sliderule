namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The properties one evaluation of a project found set in the repository, ordinal by name, beside the
///     project file that evaluation read.
/// </summary>
/// <remarks>
///     The project file travels with the list because it is where a property nothing set would be declared, so
///     a rule about a missing property has a line to point at. The list's presence is itself the fact that an
///     evaluation ran: a fragment nothing evaluated carries no value here at all, where an evaluated project
///     that sets nothing carries an empty list.
/// </remarks>
/// <param name="ProjectFile">The project file's first line.</param>
/// <param name="Declared">The properties set by a declaration in the repository, one per name.</param>
internal sealed record FragmentProperties(FragmentSite ProjectFile, IReadOnlyList<FragmentProperty> Declared);
