using System.Reflection;
using System.Runtime.CompilerServices;

namespace SlideRule.Internal;

/// <summary>
///     The reflection view of a C# 14 extension block. The compiler emits each block as a nested grouping type of
///     the static class that declares it, carrying the block's properties, and emits every accessor as a static
///     method of the class itself, taking the receiver first when the property has one.
/// </summary>
internal static class ExtensionGroupings
{
    private const BindingFlags Flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;

    /// <summary>
    ///     The extension properties named <paramref name="name" /> that <paramref name="type" />'s blocks declare,
    ///     indexers left out.
    /// </summary>
    internal static PropertyInfo[] PropertiesNamed(Type type, string name)
    {
        return GroupingsOf(type)
            .SelectMany(grouping => grouping.GetProperties(Flags))
            .Where(property => property.Name == name && property.GetIndexParameters().Length == 0)
            .ToArray();
    }

    /// <summary>
    ///     The extension property one of whose accessors <paramref name="method" /> is the emitted static method
    ///     for, such as the <c>get_Name</c> a caller may spell in place of <c>Name</c>, an extension indexer
    ///     included; null for any other method.
    /// </summary>
    internal static PropertyInfo? PropertyImplementedBy(MethodInfo method)
    {
        if (!method.IsStatic || method.DeclaringType is not { } type) return null;

        MethodInfo definition = method.IsGenericMethod ? method.GetGenericMethodDefinition() : method;
        foreach (Type grouping in GroupingsOf(type))
        {
            Type[]? receiver = ReceiverOf(grouping);
            foreach (PropertyInfo property in grouping.GetProperties(Flags))
                if (IsImplementedBy(property.GetGetMethod(true), receiver, definition)
                    || IsImplementedBy(property.GetSetMethod(true), receiver, definition))
                    return property;
        }

        return null;
    }

    // A grouping type is a special-name nested type carrying [Extension], which only the compiler can apply to a type.
    private static IEnumerable<Type> GroupingsOf(Type type)
    {
        if (!type.IsAbstract || !type.IsSealed) return [];

        return type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(nested => nested.IsSpecialName && nested.IsDefined(typeof(ExtensionAttribute), false));
    }

    // The receiver's type, read from the one parameter of the method in the special-name marker type each grouping
    // type nests; null when the grouping type has none.
    private static Type[]? ReceiverOf(Type grouping)
    {
        return grouping.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(marker => marker.IsSpecialName)
            .SelectMany(marker => marker.GetMethods(Flags))
            .Select(marker => marker.GetParameters())
            .Where(parameters => parameters.Length == 1)
            .Select(parameters => new[] { parameters[0].ParameterType })
            .FirstOrDefault();
    }

    // The emitted method carries the accessor's name and, generic over the block's type parameters, the accessor's
    // parameters behind the receiver an instance accessor takes. C# lets no other method of the class share both,
    // so the name and that signature identify it.
    private static bool IsImplementedBy(MethodInfo? accessor, Type[]? receiver, MethodInfo method)
    {
        if (accessor is null || accessor.Name != method.Name) return false;

        Type[] expected = accessor.IsStatic ? [] : receiver ?? [];
        Type[] parameters = expected
            .Concat(accessor.GetParameters().Select(parameter => parameter.ParameterType))
            .ToArray();
        Type[] actual = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        int arity = accessor.DeclaringType!.IsGenericTypeDefinition ? accessor.DeclaringType.GetGenericArguments().Length : 0;

        return method.GetGenericArguments().Length == arity
               && parameters.Length == actual.Length
               && parameters.Zip(actual, SameShape).All(same => same);
    }

    // Type equality with each generic parameter compared by position, since the grouping type declares the block's
    // type parameters on itself and the emitted method declares them on the method.
    private static bool SameShape(Type left, Type right)
    {
        if (left.IsGenericParameter || right.IsGenericParameter)
            return left.IsGenericParameter && right.IsGenericParameter && left.GenericParameterPosition == right.GenericParameterPosition;

        if (left.HasElementType || right.HasElementType)
            return left.HasElementType && right.HasElementType
                                       && left.IsArray == right.IsArray && left.IsByRef == right.IsByRef && left.IsPointer == right.IsPointer
                                       && (!left.IsArray || left.GetArrayRank() == right.GetArrayRank())
                                       && SameShape(left.GetElementType()!, right.GetElementType()!);

        if (!left.IsGenericType || !right.IsGenericType) return left == right;

        return left.GetGenericTypeDefinition() == right.GetGenericTypeDefinition()
               && left.GetGenericArguments().Zip(right.GetGenericArguments(), SameShape).All(same => same);
    }
}
