using System.Reflection;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Discovery;

/// <summary>
///     Finds the <see cref="IArchitectureSpec" /> classes an assembly declares and creates one of each.
///     A class is found when it is a non-abstract class visible from outside the assembly: public, or
///     public nested inside a public chain. Each must also declare a public parameterless constructor,
///     since one of each is created here; when a class found has none, no spec is created and a
///     <see cref="SpecDiscoveryException" /> names every such class. Creating a spec runs its
///     constructor, and nothing else in the assembly runs until <c>Define</c> is called.
/// </summary>
// netstandard2.0-safe, so it runs in the Core. Visibility is Type.IsVisible — top-level public, or
// public nested through a public chain — not Type.IsPublic, which is false for any nested type.
// Order is Type.FullName ordinal, because a spec must load predictably (GRAMMAR §9). The
// AssemblyLoadContext that isolates a prebuilt spec DLL is a host concern, not the Core's.
internal static class SpecDiscovery
{
    /// <summary>
    ///     Finds every visible <see cref="IArchitectureSpec" /> class in the assembly and creates one of
    ///     each, ordered by full type name so that the same assembly always yields the same order. Every
    ///     class found is checked for a public parameterless constructor before any is created. Hand the
    ///     result to <c>ArchModelBuilder.Build</c>.
    /// </summary>
    /// <param name="assembly">The assembly to search, usually the compiled spec project.</param>
    /// <returns>One new instance of each spec class found, ordered by full type name.</returns>
    /// <exception cref="SpecDiscoveryException">
    ///     The assembly declares no such class, or a class found has no public parameterless constructor.
    ///     The message names every class without one, ordered by full type name.
    /// </exception>
    public static IReadOnlyList<IArchitectureSpec> FindSpecs(Assembly assembly)
    {
        Guard.NotNull(assembly, nameof(assembly));

        List<Type> specTypes = assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsVisible: true }
                           && typeof(IArchitectureSpec).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        if (specTypes.Count == 0)
            throw new SpecDiscoveryException(
                $"No public {nameof(IArchitectureSpec)} implementations found in assembly '{assembly.GetName().Name}'.");

        // Every class is checked before any is created, so one refusal names them all and no spec's
        // constructor runs in a load that is about to fail.
        List<string> uncreatable = specTypes
            .Where(type => type.GetConstructor(Type.EmptyTypes) is null)
            .Select(type => type.FullName!)
            .ToList();
        if (uncreatable.Count > 0) throw new SpecDiscoveryException(MissingConstructorMessage(uncreatable));

        return specTypes.Select(type => (IArchitectureSpec)Activator.CreateInstance(type)!).ToList();
    }

    private static string MissingConstructorMessage(IReadOnlyList<string> classNames)
    {
        bool one = classNames.Count == 1;
        string names = ProseFormat.JoinQuotedAnd(classNames);
        string subject = one ? $"Spec class {names} has" : $"Spec classes {names} have";
        string remedy = one
            ? "declare a public constructor that takes no arguments, or make the class abstract if it is a base "
              + "for other specs"
            : "declare a public constructor that takes no arguments on each, or make a class abstract if it is a "
              + "base for other specs";

        return $"{subject} no public parameterless constructor. SlideRule creates one instance of each public "
               + $"{nameof(IArchitectureSpec)} class it finds, so {remedy}.";
    }
}
