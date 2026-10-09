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
///         <b>The guards are the analyzer's, mirrored.</b> A marker lives on the original definition's
///         parameter, because a constructed generic's parameters are substitutions carrying no attribute
///         blob of their own; an argument the caller did not write has no literal to complete in; an
///         ordinal only means the same thing on both lists while the lists are the same length. Where the
///         two readers disagreed, an author would be offered names for one slot and warned about another.
///     </para>
///     <para>
///         <b>One operation is asked for, and it is the literal's.</b> A params call expanded at the call
///         site has no argument operation of its own per element, but the literal's operation has parents
///         all the way up, through the array the compiler gathers the elements into, to the one argument
///         carrying them all. Walking up from the literal reaches every element alike, and passes through
///         parentheses and a null-forgiving operator, which have no operation of their own.
///     </para>
///     <para>
///         <b>Refusing is the default at every step.</b> A call that does not bind, a parameter with no
///         marker, a marker naming a slot this assembly does not know: each of them offers nothing rather
///         than guessing, because a name offered for the wrong slot is a name the author accepts and the
///         checker then matches nothing against.
///     </para>
/// </remarks>
internal static class SlotBinding
{
    /// <summary>
    ///     The operation kind a collection expression carries, and the same number the spec-literal analyzer
    ///     matches it by. The member naming it was added to the compiler API after the version this assembly
    ///     is built against, so the kind cannot be written here and is matched by its number instead. A
    ///     compiler old enough not to name it is also old enough never to produce one, so the number reaches
    ///     nothing on those.
    /// </summary>
    internal const int CollectionExpressionOperationKind = 0x7F;

    /// <summary>
    ///     The slot the literal fills, or <see cref="SlotKind.None" /> where nothing here can say.
    /// </summary>
    /// <param name="literal">The literal expression the caret is inside.</param>
    /// <param name="model">The semantic model of the document the literal is in.</param>
    /// <param name="marker">The marker type as this compilation resolved it.</param>
    /// <param name="cancellationToken">The token the one operation lookup runs under.</param>
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

    // The operations that hand on a value written inside them, named one by one. A test on the type would
    // be wider than that and wrong: a helper returning an array, a method called on a constant receiver
    // and a conditional choosing between two arrays are all array-typed, and each computes its value
    // rather than carrying a written one — so completing inside any of them would offer names for a
    // string the slot never receives.
    private static bool CarriesValuesThrough(IOperation operation)
    {
        if (operation is IArrayCreationOperation or IArrayInitializerOperation or IConversionOperation)
            return true;

        return (int)operation.Kind == CollectionExpressionOperationKind;
    }

    // The marker as it sits in metadata: one attribute of the class this compilation resolved, carrying
    // one string. Compared by symbol rather than by rendered name, so a type of the same name from
    // somewhere else is not read as the marker.
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
