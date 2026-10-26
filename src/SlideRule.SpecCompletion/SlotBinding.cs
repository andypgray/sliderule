using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SlideRule.SpecCompletion;

/// <summary>
///     Which slot a literal written at a call site fills, decided the way the spec-literal analyzer decides
///     it: from the marker on the parameter the argument carrying that literal binds to.
/// </summary>
/// <remarks>
///     <para>
///         The guards are the spec-literal analyzer's, mirrored, each for the reason its comments there give:
///         the marker is read off the original definition's parameter, an argument the caller did not write is
///         skipped, and the two parameter lists must be the same length. Where the two readers disagreed, an
///         author would be offered names for one slot and warned about another, so <c>SlotNameOn</c> and
///         <c>CarriesValuesThrough</c> are the analyzer's own members copied, which
///         <c>CopiedMemberParityTests</c> holds identical. The analyzer's one further
///         guard, that the declaration sits in the marker's own assembly, changes no answer, because the marker
///         is internal to that assembly; it only spares the analyzer an attribute read on every other call.
///     </para>
///     <para>
///         One operation is asked for, and it is the literal's. A params call expanded at the call site has no
///         argument operation of its own per element, but the literal's operation has parents all the way up,
///         through the array the compiler gathers the elements into, to the one argument carrying them all.
///         Walking up from the literal reaches every element alike, and passes through parentheses and a
///         null-forgiving operator, which have no operation of their own.
///     </para>
///     <para>
///         Refusing is the default at every step. A call that does not bind, a parameter with no marker, a
///         marker naming a slot this assembly does not know: each of them offers nothing rather than guessing,
///         because a name offered for the wrong slot is a name the author accepts and the checker then matches
///         nothing against.
///     </para>
/// </remarks>
internal static class SlotBinding
{
    /// <summary>
    ///     The operation kind a collection expression carries: the spec-literal analyzer's
    ///     <c>CollectionExpressionKind</c>, matched by number for the reason given there.
    /// </summary>
    private const int CollectionExpressionKind = 0x7F;

    /// <summary>
    ///     The slot <paramref name="literal" /> fills, or <see cref="SlotKind.None" /> where nothing here can
    ///     say; <paramref name="marker" /> is the marker type as this compilation resolved it.
    /// </summary>
    internal static SlotKind KindAt(
        LiteralExpressionSyntax literal,
        SemanticModel model,
        INamedTypeSymbol marker,
        CancellationToken cancellationToken)
    {
        if (model.GetOperation(literal, cancellationToken) is not ILiteralOperation written) return SlotKind.None;

        // Up from the literal to the argument it was written in, only through the operations that hand a
        // written value on unchanged. A concatenation is deliberately not one of them: what reaches the slot
        // there is a computed value, and completing one half of it would offer names for a string the author
        // is building rather than for the whole one the slot receives.
        IOperation? ancestor = written.Parent;
        while (ancestor is not null and not IArgumentOperation)
        {
            if (ancestor.ConstantValue.HasValue) return SlotKind.None;
            if (!CarriesValuesThrough(ancestor)) return SlotKind.None;

            ancestor = ancestor.Parent;
        }

        if (ancestor is not IArgumentOperation { Parent: IInvocationOperation call } argument) return SlotKind.None;

        IMethodSymbol target = call.TargetMethod;
        IMethodSymbol declaration = target.OriginalDefinition;
        if (declaration.Parameters.Length != target.Parameters.Length) return SlotKind.None;

        bool authored = argument.ArgumentKind == ArgumentKind.Explicit
                        || argument.ArgumentKind == ArgumentKind.ParamArray;
        if (!authored) return SlotKind.None;

        if (argument.Parameter is not { } parameter) return SlotKind.None;

        int ordinal = parameter.Ordinal;
        if (ordinal < 0 || ordinal >= declaration.Parameters.Length) return SlotKind.None;

        return SpecSlotNames.KindOf(SlotNameOn(declaration.Parameters[ordinal], marker));
    }

    // The same set the spec-literal analyzer names, for the reason given there: a test on the type would also
    // pass a helper's array or a conditional's, and completing inside either would offer names for a string
    // the slot never receives.
    private static bool CarriesValuesThrough(IOperation operation)
    {
        if (operation is IArrayCreationOperation or IArrayInitializerOperation or IConversionOperation)
            return true;

        return (int)operation.Kind == CollectionExpressionKind;
    }

    // Read as the spec-literal analyzer reads it, by symbol rather than by rendered name.
    private static string? SlotNameOn(IParameterSymbol parameter, INamedTypeSymbol marker)
    {
        foreach (AttributeData attribute in parameter.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)) continue;
            if (attribute.ConstructorArguments.Length != 1) continue;
            if (attribute.ConstructorArguments[0].Value is not string name) continue;

            return name;
        }

        return null;
    }
}
