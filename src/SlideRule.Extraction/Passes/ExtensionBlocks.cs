using System.Reflection;
using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     C# 14 extension blocks (<c>extension(IEnumerable&lt;int&gt; s) { … }</c>), which the compiler reports as
///     unnamed types nested in the static class that declares them. A block is never a modelled type: its
///     members are attributed to that static class, as a classic extension method is.
/// </summary>
/// <remarks>
///     The compiler API floor this assembly builds against predates extension blocks, so neither the kind nor
///     the link from a block method to its implementation can be named here. Both are read from the running
///     compiler: the kind as its numeric value, the link by reflection.
/// </remarks>
internal static class ExtensionBlocks
{
    // TypeKind.Extension in the compiler versions that have it.
    private const int ExtensionTypeKind = 14;

    // IMethodSymbol.AssociatedExtensionImplementation, or null on a compiler without it. Safe to read as
    // optional: a type of the extension kind exists only in a compiler that also has this property, so a null
    // accessor never meets a block method.
    private static readonly Func<IMethodSymbol, IMethodSymbol?>? AssociatedImplementation = ImplementationAccessor();

    /// <summary>Whether <paramref name="type" /> is an extension block.</summary>
    internal static bool IsExtensionBlock(this INamedTypeSymbol type)
    {
        return (int)type.TypeKind == ExtensionTypeKind;
    }

    /// <summary>The extension blocks <paramref name="type" /> declares.</summary>
    internal static IEnumerable<INamedTypeSymbol> BlocksOf(INamedTypeSymbol type)
    {
        return type.GetTypeMembers().Where(IsExtensionBlock);
    }

    /// <summary>
    ///     The definition of the static method the compiler emits for <paramref name="method" />, a method
    ///     declared in an extension block: static, with the receiver, if any, as its first parameter. Null where
    ///     there is none. Read from the method's definition and returned as a definition, so every construction of a
    ///     generic block method answers the one ID the member inventory records.
    /// </summary>
    internal static IMethodSymbol? ImplementationOf(IMethodSymbol method)
    {
        return AssociatedImplementation?.Invoke(method.OriginalDefinition)?.OriginalDefinition;
    }

    /// <summary>
    ///     The extension-block property that <paramref name="method" /> is the emitted static method of an accessor
    ///     for, such as the <c>get_Name</c> a caller may spell in place of <c>Name</c>, an extension indexer included,
    ///     as its definition; null for any other method.
    /// </summary>
    internal static IPropertySymbol? PropertyImplementedBy(IMethodSymbol method)
    {
        IMethodSymbol definition = method.OriginalDefinition;

        // A screen ahead of the block walk, which a use of every static method would otherwise pay: an emitted
        // accessor is a static method of a static class, named as its accessor is. The match itself is by symbol.
        if (AssociatedImplementation is null || definition is not { IsStatic: true, ContainingType.IsStatic: true }) return null;
        if (!definition.Name.StartsWith("get_", StringComparison.Ordinal) && !definition.Name.StartsWith("set_", StringComparison.Ordinal))
            return null;

        return BlocksOf(definition.ContainingType)
            .SelectMany(block => block.GetMembers().OfType<IPropertySymbol>())
            .FirstOrDefault(property => Implements(property.GetMethod, definition) || Implements(property.SetMethod, definition));
    }

    private static bool Implements(IMethodSymbol? accessor, IMethodSymbol definition)
    {
        return accessor is not null && SymbolEqualityComparer.Default.Equals(ImplementationOf(accessor), definition);
    }

    private static Func<IMethodSymbol, IMethodSymbol?>? ImplementationAccessor()
    {
        MethodInfo? getter = typeof(IMethodSymbol).GetProperty("AssociatedExtensionImplementation")?.GetMethod;

        return getter is null
            ? null
            : (Func<IMethodSymbol, IMethodSymbol?>)getter.CreateDelegate(typeof(Func<IMethodSymbol, IMethodSymbol?>));
    }
}
