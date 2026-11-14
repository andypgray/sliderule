using Microsoft.CodeAnalysis;
using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     One compilation's registration wrappers (GRAMMAR §4.7): the template of each wrapper it declares, and every
///     call it makes to a wrapper with arguments of its own, each materialized in a canonical order.
/// </summary>
/// <remarks>
///     A registration or call inside a wrapper's body that reads one of the wrapper's own parameters, bare or
///     inside a constructed type, belongs to its template; one that reads none is concrete where it stands and is
///     recorded at its own site, so a fact a body spells out is never also expanded at every call.
/// </remarks>
internal sealed class RegistrationTemplates(RegistrationRecognizer recognizer, Func<INamedTypeSymbol, string> fullName)
{
    private readonly SortedDictionary<string, FragmentWrapperCall> _callShapes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedSet<FragmentSite>> _callSites = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, Template> _templates = new(StringComparer.Ordinal);

    /// <summary>
    ///     Adds the registration to <paramref name="wrapper" />'s template when it reads one of the wrapper's
    ///     parameters, bare or inside a constructed type, and returns whether it reads one.
    /// </summary>
    public bool TryAddRegistration(IMethodSymbol wrapper, RecognizedRegistration registration)
    {
        FragmentRegistrationType service = TypeSlot(registration.Service, wrapper);
        FragmentRegistrationType? implementation =
            registration.Implementation is { } type ? TypeSlot(type, wrapper) : null;
        bool reads = ReadsWrapper(service)
                     || (implementation is { } implementationSlot && ReadsWrapper(implementationSlot))
                     || registration.Lifetime.Parameter is not null;
        if (!reads) return false;

        var leaf = new FragmentRegistrationLeaf(registration.Lifetime, service, implementation);
        if (TemplateOf(wrapper) is { } template) template.Registrations[LeafKey(leaf)] = leaf;
        return true;
    }

    public void AddCall(IMethodSymbol? wrapper, RecognizedWrapperCall call, FragmentSite site)
    {
        var shape = new FragmentWrapperCall(
            call.Wrapper,
            call.TypeArguments.Select(argument => TypeSlot(argument, wrapper)).ToList(),
            call.LifetimeArguments
                .Select(argument => new FragmentLifetimeArgument(argument.Parameter, argument.Lifetime ?? new FragmentRegistrationLifetime()))
                .OrderBy(argument => argument.Parameter, StringComparer.Ordinal)
                .ToList(),
            []);
        string key = CallKey(shape);

        if (wrapper is not null
            && (shape.TypeArguments.Any(ReadsWrapper)
                || call.LifetimeArguments.Any(argument => argument.Lifetime?.Parameter is not null)))
        {
            if (TemplateOf(wrapper) is { } template) template.Forwards[key] = shape;
            return;
        }

        if (!_callShapes.ContainsKey(key)) _callShapes[key] = shape;
        _callSites.SitesFor(key).Add(site);
    }

    /// <summary>The templates in wrapper-key order; null when there are none.</summary>
    public IReadOnlyList<FragmentRegistrationTemplate>? Templates()
    {
        List<FragmentRegistrationTemplate> templates = _templates
            .Select(entry => new FragmentRegistrationTemplate(
                entry.Key,
                recognizer.LifetimeDefaults(entry.Value.Wrapper)
                    .Select(lifetimeDefault => new FragmentLifetimeDefault(lifetimeDefault.Parameter, lifetimeDefault.Lifetime))
                    .OrderBy(lifetimeDefault => lifetimeDefault.Parameter, StringComparer.Ordinal)
                    .ToList(),
                entry.Value.Registrations.Values.ToList(),
                entry.Value.Forwards.Values.ToList()))
            .ToList();
        return templates.Count == 0 ? null : templates;
    }

    /// <summary>The calls with their sites, in call-key order; null when there are none.</summary>
    public IReadOnlyList<FragmentWrapperCall>? Calls()
    {
        List<FragmentWrapperCall> calls = _callShapes
            .Select(entry => entry.Value with { Sites = _callSites[entry.Key].ToList() })
            .ToList();
        return calls.Count == 0 ? null : calls;
    }

    // Null for a wrapper with no documentation ID, whose calls could never name it; what it reads is dropped.
    private Template? TemplateOf(IMethodSymbol wrapper)
    {
        if (RegistrationRecognizer.WrapperKey(wrapper) is not { } key) return null;

        if (!_templates.TryGetValue(key, out Template? template))
        {
            template = new Template(wrapper.OriginalDefinition);
            _templates[key] = template;
        }

        return template;
    }

    // Whether a slot reads a wrapper parameter: TypeSlot sets TypeParameter for a bare type parameter and Mentions
    // for a constructed type built over the wrapper's own.
    private static bool ReadsWrapper(FragmentRegistrationType slot)
    {
        return slot.TypeParameter is not null || slot.Mentions is not null;
    }

    // The sorted, distinct ordinals of the wrapper's own type parameters a constructed type is built over, through
    // type arguments, containing types, array elements and pointees; null when there are none. An open generic
    // such as typeof(IRepo<>) mentions nothing.
    private static IReadOnlyList<int>? MentionedWrapperTypeParameters(ITypeSymbol? type, IMethodSymbol? wrapper)
    {
        if (type is not INamedTypeSymbol || wrapper is null) return null;

        var ordinals = new SortedSet<int>();
        Visit(type);
        return ordinals.Count == 0 ? null : ordinals.ToList();

        void Visit(ITypeSymbol current)
        {
            switch (current)
            {
                case ITypeParameterSymbol parameter:
                    if (RegistrationRecognizer.WrapperTypeParameter(parameter, wrapper) is { } owned) ordinals.Add(owned.Ordinal);
                    break;
                case INamedTypeSymbol named:
                    for (INamedTypeSymbol? level = named; level is not null; level = level.ContainingType)
                        foreach (ITypeSymbol argument in level.TypeArguments)
                            Visit(argument);
                    break;
                case IArrayTypeSymbol array:
                    Visit(array.ElementType);
                    break;
                case IPointerTypeSymbol pointer:
                    Visit(pointer.PointedAtType);
                    break;
            }
        }
    }

    private FragmentRegistrationType TypeSlot(ITypeSymbol? type, IMethodSymbol? wrapper)
    {
        return type switch
        {
            INamedTypeSymbol named => new FragmentRegistrationType(fullName(named), Mentions: MentionedWrapperTypeParameters(named, wrapper)),
            ITypeParameterSymbol parameter => new FragmentRegistrationType(TypeParameter: parameter.Ordinal),
            _ => new FragmentRegistrationType()
        };
    }

    private static string LeafKey(FragmentRegistrationLeaf leaf)
    {
        return $"{LifetimeKey(leaf.Lifetime)}|{TypeKey(leaf.Service)}|{(leaf.Implementation is { } impl ? TypeKey(impl) : "")}";
    }

    private static string CallKey(FragmentWrapperCall call)
    {
        IEnumerable<string> types = call.TypeArguments.Select(TypeKey);
        IEnumerable<string> lifetimes = call.LifetimeArguments.Select(argument => $"{argument.Parameter}={LifetimeKey(argument.Lifetime)}");
        return $"{call.Wrapper}|{string.Join(",", types)}|{string.Join(",", lifetimes)}";
    }

    private static string TypeKey(FragmentRegistrationType type)
    {
        if (type.FullName is { } name)
            return type.Mentions is { } mentions ? $"{name}<{string.Join(",", mentions.Select(ordinal => $"`{ordinal}"))}>" : name;

        return type.TypeParameter is { } parameter ? $"`{parameter}" : "?";
    }

    private static string LifetimeKey(FragmentRegistrationLifetime lifetime)
    {
        return lifetime.Literal?.ToString() ?? (lifetime.Parameter is { } parameter ? $"@{parameter}" : "?");
    }

    // Keyed so a body that spells one registration twice stores it once, in a canonical order.
    private sealed class Template(IMethodSymbol wrapper)
    {
        internal IMethodSymbol Wrapper { get; } = wrapper;

        internal SortedDictionary<string, FragmentRegistrationLeaf> Registrations { get; } = new(StringComparer.Ordinal);
        internal SortedDictionary<string, FragmentWrapperCall> Forwards { get; } = new(StringComparer.Ordinal);
    }
}
