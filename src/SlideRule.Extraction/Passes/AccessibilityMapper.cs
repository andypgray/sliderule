using Microsoft.CodeAnalysis;
using CoreAccessibility = SlideRule.Accessibility;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Maps Roslyn's <see cref="RoslynAccessibility" /> onto Core's <see cref="CoreAccessibility" />, a
///     separate enum so that Roslyn never enters Core.
/// </summary>
/// <remarks>
///     <c>NotApplicable</c> has no C# declaration meaning, and the two entry points split on it by path.
///     GRAMMAR §4.6 holds every declared member of a solution-declared type to a real declared accessibility,
///     so the member inventory keeps <see cref="Map(ISymbol)" />: a member with no keyword meaning is a broken
///     inventory, not input to tolerate. A type fact can be minted for an error symbol a partially-loaded
///     workspace yields, which the external-mint path reaches through base types, interfaces and attribute
///     classes; such a type is an external, never a rule subject (§4.1), so the type path uses
///     <see cref="TryMap" /> and falls back rather than crashing a survey that has no spec to answer to.
/// </remarks>
internal static class AccessibilityMapper
{
    /// <summary>
    ///     Maps the declared accessibility of an inventoried member symbol (GRAMMAR §4.6), private included,
    ///     and throws on <c>NotApplicable</c>.
    /// </summary>
    public static CoreAccessibility Map(ISymbol symbol)
    {
        if (TryMap(symbol, out CoreAccessibility accessibility)) return accessibility;

        throw new InvalidOperationException(
            $"Symbol '{symbol.ToDisplayString()}' reports accessibility '{symbol.DeclaredAccessibility}', which has no C# declaration meaning here.");
    }

    /// <summary>
    ///     The same mapping, returning <see langword="false" /> for <c>NotApplicable</c> instead of throwing, so
    ///     a caller holding an unresolved symbol can choose a fallback.
    /// </summary>
    public static bool TryMap(ISymbol symbol, out CoreAccessibility accessibility)
    {
        switch (symbol.DeclaredAccessibility)
        {
            case RoslynAccessibility.Public:
                accessibility = CoreAccessibility.Public;
                return true;
            case RoslynAccessibility.Internal:
                accessibility = CoreAccessibility.Internal;
                return true;
            case RoslynAccessibility.Protected:
                accessibility = CoreAccessibility.Protected;
                return true;
            case RoslynAccessibility.ProtectedOrInternal:
                accessibility = CoreAccessibility.ProtectedInternal;
                return true;
            case RoslynAccessibility.ProtectedAndInternal:
                accessibility = CoreAccessibility.PrivateProtected;
                return true;
            case RoslynAccessibility.Private:
                accessibility = CoreAccessibility.Private;
                return true;
            default:
                accessibility = default;
                return false;
        }
    }
}
