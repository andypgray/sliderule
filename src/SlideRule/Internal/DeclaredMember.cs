using System.Reflection;

namespace SlideRule.Internal;

/// <summary>
///     The one "what counts as a declared member" policy: the members declared on the type's generic
///     definition alone, since a member anchor is definition-level (GRAMMAR §4.5).
/// </summary>
/// <remarks>
///     Validation asks it whether an anchor's member exists (<see cref="Validation.SpecValidator" />) and
///     prose whether it renders a trailing <c>()</c> (<see cref="Member.IsMethod" />); the two must agree, or
///     a spec that validates clean renders a member the check never found.
/// </remarks>
internal static class DeclaredMember
{
    private const BindingFlags Flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    ///     Empty when <paramref name="name" /> is blank, typo'd, or inherited rather than declared. A C# 14 extension
    ///     property counts as declared on the static class whose block declares it, and the static method emitted
    ///     for its accessor does not, since a use of that method is a use of the property.
    /// </summary>
    internal static MemberInfo[] Of(Type type, string name)
    {
        Type definition = Generics.Definition(type);
        MemberInfo[] declared = definition.GetMember(name, Flags)
            .Where(member => member is not MethodInfo method || ExtensionGroupings.PropertyImplementedBy(method) is null)
            .ToArray();

        return declared.Length > 0 ? declared : ExtensionGroupings.PropertiesNamed(definition, name).Cast<MemberInfo>().ToArray();
    }
}
