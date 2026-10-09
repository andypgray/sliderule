using Microsoft.CodeAnalysis;
using CoreTypeKind = SlideRule.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Maps Roslyn's <see cref="RoslynTypeKind" /> onto SlideRule's Core <see cref="CoreTypeKind" />
///     (the two enums are deliberately distinct — Roslyn must not enter Core). The alias is required
///     because <c>TypeKind</c> is otherwise ambiguous here.
/// </summary>
/// <remarks>
///     Class → Class (records and static classes are classes at the symbol level), Interface,
///     Struct (record structs included), Enum, Delegate. Everything else — Error, TypeParameter,
///     Array, Pointer, Dynamic, Module, Submission, FunctionPointer — is unmappable.
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
    internal static bool IsModelledType(INamedTypeSymbol type)
    {
        return !type.IsImplicitlyDeclared && type.CanBeReferencedByName && TryMap(type, out _);
    }
}
