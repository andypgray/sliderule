namespace SlideRule.Internal;

/// <summary>The guarded, projected operand lists the fluent verbs build from their arguments.</summary>
/// <remarks>
///     Every <see cref="ArgumentException.ParamName" /> a caller sees is the name on the public overload they
///     called: <see cref="OneOrMore{TIn,TOut}" /> guards under <c>nameof(more)</c>, so its parameter is named as
///     every public overload names its array, and under <c>firstName</c> for the leading operand, which an
///     overload whose leading operand has another name passes; <see cref="ZeroOrMore{TIn,TOut}" /> guards under
///     the one name its caller passes.
/// </remarks>
internal static class OperandList
{
    internal static IReadOnlyList<TOut> OneOrMore<TIn, TOut>(
        TIn first, TIn[] more, Func<TIn, TOut> project, string firstName = "first")
        where TIn : class
    {
        // First, because of the .Length below: a caller who wrote the params array out longhand can pass a
        // null one, which would otherwise NRE inside the list allocation rather than naming the argument.
        Guard.NotNull(more, nameof(more));

        var list = new List<TOut>(1 + more.Length) { project(Guard.NotNull(first, firstName)) };
        foreach (TIn item in more) list.Add(project(Guard.NotNull(item, nameof(more))));

        return list;
    }

    internal static IReadOnlyList<T> OneOrMore<T>(T first, T[] more, string firstName = "first")
        where T : class
    {
        return OneOrMore(first, more, static item => item, firstName);
    }

    /// <summary>
    ///     Enumerates <paramref name="operands" /> exactly once, so a lazy sequence runs its query once and a list
    ///     changed after the call does not change the spec. An empty sequence gives an empty list rather than
    ///     throwing: an empty computed list is a spec-author mistake that <c>SpecValidator</c> collects, where a
    ///     null is a programmer error thrown at the call (the <see cref="Guard" /> split).
    /// </summary>
    internal static IReadOnlyList<TOut> ZeroOrMore<TIn, TOut>(IEnumerable<TIn> operands, Func<TIn, TOut> project, string name)
        where TIn : class
    {
        IEnumerable<TIn> sequence = Guard.NotNull(operands, name);

        var list = new List<TOut>();
        foreach (TIn item in sequence) list.Add(project(Guard.NotNull(item, name)));

        return list;
    }

    /// <inheritdoc cref="ZeroOrMore{TIn,TOut}" />
    internal static IReadOnlyList<T> ZeroOrMore<T>(IEnumerable<T> operands, string name)
        where T : class
    {
        return ZeroOrMore(operands, static item => item, name);
    }
}
