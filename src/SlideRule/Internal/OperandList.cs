namespace SlideRule.Internal;

/// <summary>
///     The one <c>(first, params more)</c> operand-list builder behind the fluent surface. Every verb
///     overload that takes at least one operand differs only in what it projects each operand into, and
///     the shape that makes a zero-operand call uncompilable is the same in all of them.
/// </summary>
/// <remarks>
///     The guards spell <c>nameof(more)</c>, which is why that parameter is named as every public
///     overload names its array, and <c>firstName</c> for the leading operand, which defaults to
///     <c>first</c> and is passed by an overload whose leading operand has another name. So every
///     <see cref="ArgumentException.ParamName" /> a caller sees is the name on the public overload
///     they actually called.
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
}
