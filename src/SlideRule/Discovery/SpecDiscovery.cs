using System.Reflection;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Discovery;

/// <summary>
///     Finds the <see cref="IArchitectureSpec" /> classes an assembly declares and creates one of each.
///     Creating a spec runs its constructor, and nothing else in the assembly runs until <c>Define</c> is
///     called.
/// </summary>
// Visibility is Type.IsVisible — top-level public, or public nested through a public chain — not
// Type.IsPublic, which is false for any nested type. Order is Type.FullName ordinal, because a spec must
// load predictably (GRAMMAR §9).
internal static class SpecDiscovery
{
    /// <summary>
    ///     Creates one instance of each non-abstract <see cref="IArchitectureSpec" /> class visible from outside
    ///     <paramref name="assembly" />, ordered by full type name.
    /// </summary>
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
