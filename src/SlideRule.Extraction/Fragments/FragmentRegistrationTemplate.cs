namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The registrations an in-solution wrapper over <c>IServiceCollection</c> makes, written in terms of its own
///     type parameters and <c>ServiceLifetime</c> parameters (GRAMMAR §4.7). The merge expands it at every
///     <see cref="FragmentWrapperCall" /> that names <see cref="Wrapper" />, in whichever project the call sits.
/// </summary>
internal sealed record FragmentRegistrationTemplate(
    string Wrapper,
    IReadOnlyList<FragmentLifetimeDefault> LifetimeDefaults,
    IReadOnlyList<FragmentRegistrationLeaf> Registrations,
    IReadOnlyList<FragmentWrapperCall> Forwards);

/// <summary>
///     A call to a wrapper that may have a <see cref="FragmentRegistrationTemplate" />: the wrapper's key, its type
///     arguments by ordinal, and the <c>ServiceLifetime</c> arguments the call passes, by parameter name. Inside a
///     template, as a forward, its slots may name the enclosing wrapper's parameters and its site list is empty.
/// </summary>
internal sealed record FragmentWrapperCall(
    string Wrapper,
    IReadOnlyList<FragmentRegistrationType> TypeArguments,
    IReadOnlyList<FragmentLifetimeArgument> LifetimeArguments,
    IReadOnlyList<FragmentSite> Sites);

/// <summary>One registration a template's body makes; a null implementation names none.</summary>
internal sealed record FragmentRegistrationLeaf(
    FragmentRegistrationLifetime Lifetime,
    FragmentRegistrationType Service,
    FragmentRegistrationType? Implementation);

/// <summary>
///     A type slot: a definition-level full name, or the ordinal of the enclosing wrapper's type parameter.
///     Neither set means the source spelled a type that cannot be read, so whatever depends on it records nothing.
/// </summary>
internal sealed record FragmentRegistrationType(string? FullName = null, int? TypeParameter = null);

/// <summary>
///     A lifetime slot: a literal, or the name of the enclosing wrapper's <c>ServiceLifetime</c> parameter.
///     Neither set means a lifetime that is not a literal, so whatever depends on it records nothing.
/// </summary>
internal sealed record FragmentRegistrationLifetime(Lifetime? Literal = null, string? Parameter = null);

/// <summary>The lifetime a call passes for one of the wrapper's <c>ServiceLifetime</c> parameters.</summary>
internal sealed record FragmentLifetimeArgument(string Parameter, FragmentRegistrationLifetime Lifetime);

/// <summary>The declared default of one of a wrapper's <c>ServiceLifetime</c> parameters.</summary>
internal sealed record FragmentLifetimeDefault(string Parameter, Lifetime Lifetime);
