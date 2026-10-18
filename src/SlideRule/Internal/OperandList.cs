namespace SlideRule.Internal;

/// <summary>
///     The operand-list builders behind the fluent surface. Every verb overload that takes at least one
///     operand differs only in what it projects each operand into, and the shape that makes a zero-operand
///     call uncompilable is the same in all of them; the sequence overloads beside a few of them share the
///     other builder, which takes a list computed at run time.
/// </summary>
/// <remarks>
///     <see cref="OneOrMore{TIn,TOut}" />'s guards spell <c>nameof(more)</c>, which is why that parameter is
///     named as every public overload names its array, and <c>firstName</c> for the leading operand, which
///     defaults to <c>first</c> and is passed by an overload whose leading operand has another name.
///     <see cref="ZeroOrMore{TIn,TOut}" /> guards the sequence and each element under the one name its
///     caller passes. So every <see cref="ArgumentException.ParamName" /> a caller sees is the name on the
///     public overload they actually called.
///     <para>
///         <see cref="ZeroOrMore{TIn,TOut}" /> returns an empty list for an empty sequence rather than
///         throwing: a computed list that came out empty is a spec-author mistake, collected by
///         <c>SpecValidator</c> with the rest of the spec's errors, where a null is a programmer error
///         thrown at the call (the <see cref="Guard" /> split).
///     </para>
/// </remarks>
internal static class OperandList
{
    /// <summary>
    ///     The guarded, projected operand list: <paramref name="first" /> then each of
    ///     <paramref name="more" />, null-checked by parameter name and passed through
    ///     <paramref name="project" />.
    /// </summary>
    internal static IReadOnlyList<TOut> OneOrMore<TIn, TOut>(
        TIn first, TIn[] more, Func<TIn, TOut> project, string firstName = "first")
        where TIn : class
    {
        // Before the .Length below, which is the whole reason this guard is the first statement: a caller
        // who wrote the params array out longhand can pass a null one, and every verb built on this method
        // would otherwise NRE inside the list allocation rather than naming the argument.
        Guard.NotNull(more, nameof(more));

        var list = new List<TOut>(1 + more.Length) { project(Guard.NotNull(first, firstName)) };
        foreach (TIn item in more) list.Add(project(Guard.NotNull(item, nameof(more))));

        return list;
    }

    /// <summary>
    ///     The guarded, projected operand list of a sequence overload: each element of
    ///     <paramref name="operands" />, null-checked under <paramref name="name" /> and passed through
    ///     <paramref name="project" />. The sequence is enumerated exactly once, so a lazy one runs its
    ///     query once and a list changed after the call does not change the spec.
    /// </summary>
    internal static IReadOnlyList<TOut> ZeroOrMore<TIn, TOut>(IEnumerable<TIn> operands, Func<TIn, TOut> project, string name)
        where TIn : class
    {
        IEnumerable<TIn> sequence = Guard.NotNull(operands, name);

        var list = new List<TOut>();
        foreach (TIn item in sequence) list.Add(project(Guard.NotNull(item, name)));

        return list;
    }
}
