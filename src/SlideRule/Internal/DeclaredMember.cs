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

    /// <summary>Empty when <paramref name="name" /> is blank, typo'd, or inherited rather than declared.</summary>
    internal static MemberInfo[] Of(Type type, string name)
    {
        Type definition = Generics.Definition(type);
        return definition.GetMember(name, Flags);
    }
}
