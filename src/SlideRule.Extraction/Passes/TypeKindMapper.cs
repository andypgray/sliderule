using Microsoft.CodeAnalysis;
using CoreTypeKind = SlideRule.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Maps Roslyn's <see cref="RoslynTypeKind" /> onto Core's <see cref="CoreTypeKind" />, a separate enum
///     so that Roslyn never enters Core.
/// </summary>
/// <remarks>
///     Records and static classes are classes at the symbol level, and record structs are structs. Every other
///     kind (Error, TypeParameter, Array, Pointer, Dynamic, Module, Submission, FunctionPointer) is unmappable.
/// </remarks>
internal static class TypeKindMapper
{
    public static bool TryMap(INamedTypeSymbol symbol, out CoreTypeKind kind)
    {
        switch (symbol.TypeKind)
        {
            case RoslynTypeKind.Class:
                kind = CoreTypeKind.Class;
                return true;
            case RoslynTypeKind.Interface:
                kind = CoreTypeKind.Interface;
                return true;
            case RoslynTypeKind.Struct:
                kind = CoreTypeKind.Struct;
                return true;
            case RoslynTypeKind.Enum:
                kind = CoreTypeKind.Enum;
                return true;
            case RoslynTypeKind.Delegate:
                kind = CoreTypeKind.Delegate;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    /// <summary>
    ///     Whether a type is one the model records: not implicitly declared, nameable from source, and of a
    ///     kind <see cref="TryMap" /> maps.
    /// </summary>
    internal static bool IsModelledType(this INamedTypeSymbol type)
    {
        return !type.IsImplicitlyDeclared && type.CanBeReferencedByName && TryMap(type, out _);
    }
}
