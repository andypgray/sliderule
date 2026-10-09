using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Extraction's symbol-tolerance policy, held in one place: the symbol a binding resolved to, or — when
///     it resolved to none — the compiler's first candidate; and for a type the compiler handed back as an
///     error type, that error type's first candidate.
/// </summary>
/// <remarks>
///     The policy is a product decision, not a convenience. SlideRule's subject is a messy legacy codebase
///     that may not fully compile against the references at hand, so a name the compiler could only guess at
///     still contributes its edge rather than vanishing from the model. Falling back to
///     <see cref="SymbolInfo.CandidateSymbols" /> is how that guess is honoured, and the downstream gates
///     (<see cref="TypeKindMapper" /> on the type channels, the recognizer's own symbol-first gate on the
///     registration channel) are what stop a wrong guess from minting something false. Every site that binds
///     a name for extraction goes through here, so the tolerance cannot be granted on one channel and
///     silently withheld on another. The injection and exposure channels are the one deliberate exception:
///     they read a parameter's or a signature's declared type and leave an error type as the compiler gave it.
/// </remarks>
internal static class SymbolInfoExtensions
{
    /// <summary>The bound symbol, else the first candidate, else <see langword="null" />.</summary>
    internal static ISymbol? BestCandidate(this SymbolInfo info)
    {
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    /// <summary>
    ///     The type itself, or, where the compiler could not bind it exactly and handed back an error type, that
    ///     error type's first candidate, else <see langword="null" />.
    /// </summary>
    /// <remarks>
    ///     The same policy for a type the compiler bound while binding something else, such as an invoked method's
    ///     type argument, which no <see cref="SymbolInfo" /> of its own reaches. An error type carries the
    ///     candidates a bind of its written name reports, so this and a bind of that name agree.
    /// </remarks>
    internal static ISymbol? BestCandidate(this ITypeSymbol type)
    {
        return type is IErrorTypeSymbol error ? error.CandidateSymbols.FirstOrDefault() : type;
    }
}
