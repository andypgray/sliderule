using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Expands every <see cref="FragmentWrapperCall" /> in a merge through the
///     <see cref="FragmentRegistrationTemplate" /> of the wrapper it names (GRAMMAR §4.7), whichever fragment
///     declares it, into the registrations the call makes, each at the call's own sites.
/// </summary>
/// <remarks>
///     A template forwards to other wrappers, so expansion follows the chain with each wrapper's arguments
///     substituted, and stops at a wrapper already on the chain. A slot that stays unbound (a type the call did
///     not spell readably, a lifetime that is not a literal and has no default) drops the registration that reads
///     it, or for an implementation leaves it unnamed, as a direct call would. A call whose wrapper has no
///     template (one compiled into a package) expands to nothing. Several fragments carry one wrapper's template
///     when its project builds for several target frameworks, and <c>#if</c> can make them differ. A call expands
///     through the template of its own fragment's framework, as each framework's direct registrations are recorded
///     from that framework alone, and through every one when none is of its framework, since which of them a
///     consumer compiles against is not recorded.
/// </remarks>
internal static class WrapperCallExpander
{
    public static IEnumerable<FragmentServiceRegistration> Expand(IReadOnlyList<CodebaseFragment> fragments)
    {
        var templates = new Dictionary<string, List<FrameworkTemplate>>(StringComparer.Ordinal);
        foreach (CodebaseFragment fragment in fragments)
        foreach (FragmentRegistrationTemplate template in fragment.RegistrationTemplates ?? [])
        {
            if (!templates.TryGetValue(template.Wrapper, out List<FrameworkTemplate>? declared))
                templates[template.Wrapper] = declared = [];

            declared.Add(new FrameworkTemplate(fragment.TargetFramework, template));
        }

        if (templates.Count == 0) yield break;

        foreach (CodebaseFragment fragment in fragments)
        foreach (FragmentWrapperCall call in fragment.WrapperCalls ?? [])
        {
            var registrations = new HashSet<(Lifetime, string, string?)>();
            List<string?> types = call.TypeArguments
                .Select(type => type.FullName)
                .ToList();
            Dictionary<string, Lifetime?> lifetimes = call.LifetimeArguments
                .ToDictionary(argument => argument.Parameter, argument => argument.Lifetime.Literal, StringComparer.Ordinal);

            var chain = new HashSet<string>(StringComparer.Ordinal);
            ExpandInto(registrations, templates, fragment.TargetFramework, call.Wrapper, types, lifetimes, chain);

            foreach ((Lifetime lifetime, string service, string? implementation) in registrations)
                yield return new FragmentServiceRegistration(lifetime, service, implementation, call.Sites);
        }
    }

    private static void ExpandInto(
        HashSet<(Lifetime, string, string?)> registrations, Dictionary<string, List<FrameworkTemplate>> templates,
        string? framework, string wrapper, List<string?> types, Dictionary<string, Lifetime?> lifetimes, HashSet<string> chain)
    {
        if (!templates.TryGetValue(wrapper, out List<FrameworkTemplate>? declared) || !chain.Add(wrapper)) return;

        List<FrameworkTemplate> own = declared
            .Where(candidate => string.Equals(candidate.Framework, framework, StringComparison.Ordinal))
            .ToList();
        foreach (FrameworkTemplate candidate in own.Count > 0 ? own : declared)
            ExpandTemplate(registrations, templates, framework, candidate.Template, types, lifetimes, chain);

        chain.Remove(wrapper);
    }

    private static void ExpandTemplate(
        HashSet<(Lifetime, string, string?)> registrations, Dictionary<string, List<FrameworkTemplate>> templates,
        string? framework, FragmentRegistrationTemplate template, List<string?> types, Dictionary<string, Lifetime?> lifetimes,
        HashSet<string> chain)
    {
        foreach (FragmentRegistrationLeaf leaf in template.Registrations)
        {
            if (LifetimeOf(leaf.Lifetime) is not { } lifetime || TypeOf(leaf.Service) is not { } service) continue;

            string? implementation = leaf.Implementation is { } slot ? TypeOf(slot) : null;
            registrations.Add((lifetime, service, implementation));
        }

        foreach (FragmentWrapperCall forward in template.Forwards)
        {
            List<string?> forwardTypes = forward.TypeArguments
                .Select(TypeOf)
                .ToList();
            Dictionary<string, Lifetime?> forwardLifetimes = forward.LifetimeArguments
                .ToDictionary(argument => argument.Parameter, argument => LifetimeOf(argument.Lifetime), StringComparer.Ordinal);
            ExpandInto(registrations, templates, framework, forward.Wrapper, forwardTypes, forwardLifetimes, chain);
        }

        return;

        string? TypeOf(FragmentRegistrationType slot)
        {
            if (slot.FullName is { } fullName) return fullName;
            return slot.TypeParameter is { } ordinal && ordinal < types.Count ? types[ordinal] : null;
        }

        Lifetime? LifetimeOf(FragmentRegistrationLifetime slot)
        {
            if (slot.Literal is { } literal) return literal;
            if (slot.Parameter is not { } parameter) return null;
            if (lifetimes.TryGetValue(parameter, out Lifetime? passed)) return passed;

            return template.LifetimeDefaults.FirstOrDefault(lifetimeDefault => lifetimeDefault.Parameter == parameter)?.Lifetime;
        }
    }
}

// A wrapper's template with the target framework of the fragment that carries it, null for a single-framework project.
internal readonly record struct FrameworkTemplate(string? Framework, FragmentRegistrationTemplate Template);
