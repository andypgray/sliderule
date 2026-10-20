using Microsoft.CodeAnalysis;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Extraction's symbol-tolerance policy, held in one place: the symbol a binding resolved to, or — when
///     it resolved to none — the compiler's first candidate; and for a type the compiler handed back as an
///     error type, that error type's first candidate.
/// </summary>
/// <remarks>
///     A name the compiler could only guess at still contributes its edge rather than vanishing from the
///     model, and the downstream gates (<see cref="TypeKindMapper" /> on the type channels, the recognizer's
///     own symbol-first gate on the registration channel) are what stop a wrong guess from minting something
///     false. Every site that binds a name for extraction goes through here, so the tolerance cannot be granted
///     on one channel and silently withheld on another. The injection and exposure channels are the one
///     exception: they read a parameter's or a signature's declared type and leave an error type as the
///     compiler gave it.
/// </remarks>
internal static class SymbolInfoExtensions
{
    internal static ISymbol? BestCandidate(this SymbolInfo info)
    {
        return info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
    }

    /// <summary>
    ///     The same policy for a type the compiler bound while binding something else, such as an invoked
    ///     method's type argument, which no <see cref="SymbolInfo" /> of its own reaches. An error type carries
    ///     the candidates a bind of its written name reports, so this and a bind of that name agree.
    /// </summary>
    internal static ISymbol? BestCandidate(this ITypeSymbol type)
    {
        return type is IErrorTypeSymbol error ? error.CandidateSymbols.FirstOrDefault() : type;
    }
}
