namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A container-registration fact inside one fragment (GRAMMAR §4.7): the lifetime, the service's FQN, the
///     implementation's FQN (null for a factory/instance form or the <c>AddHostedService</c>
///     unresolvable-service fallback), and the sites of the recognized registration calls. The pure-data
///     counterpart of <see cref="SlideRule.Codebase.ServiceRegistration" />, and string-side as it is.
/// </summary>
internal sealed record FragmentServiceRegistration(
    Lifetime Lifetime,
    string ServiceFullName,
    string? ImplementationFullName,
    IReadOnlyList<FragmentSite> Sites);
