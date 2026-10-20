namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed exposure edge inside one fragment (GRAMMAR §4.9): source and exposed type by FQN, and the
///     declaration sites of the exposing members. An exposure edge is minted from every public signature
///     position (return, parameter, property/field/event type) of an effectively-public member. The pure-data
///     counterpart of <see cref="SlideRule.Codebase.ExposureEdge" />.
/// </summary>
internal sealed record FragmentExposureEdge(
    string SourceFullName,
    string ExposedFullName,
    IReadOnlyList<FragmentSite> Sites);
