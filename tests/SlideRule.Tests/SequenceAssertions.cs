using System.Runtime.CompilerServices;
using Shouldly;

namespace SlideRule.Tests;

/// <summary>Assertions over any sequence, for the lookups no subject's own assertion file covers.</summary>
/// <remarks>
///     Attributed <c>[ShouldlyMethods]</c>, as <see cref="SlideRule.Tests.DocHygiene.DocGateAssertions" /> is: the
///     asserted list is built from the parameter, so the attribute is what makes a red quote the caller's sequence
///     rather than this file's local. Shouldly finds that quote by searching the caller's line for this method's name,
///     so the method keeps a frame of its own in every build, where an optimized tail call into Shouldly would
///     otherwise drop it. The message carries the rest: the predicate as the caller wrote it and every item there was.
/// </remarks>
[ShouldlyMethods]
internal static class SequenceAssertions
{
    /// <summary>
    ///     Asserts exactly one item of <paramref name="items" /> satisfies <paramref name="predicate" />, and returns
    ///     it. A miss or a tie reds with the predicate's source text and every item, each written by
    ///     <paramref name="describe" />, or by its <c>ToString</c> when none is given.
    /// </summary>
    /// <remarks>Pass <paramref name="describe" /> for an item type whose <c>ToString</c> names only the type.</remarks>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    internal static T ShouldHaveSingle<T>(
        this IEnumerable<T> items, Func<T, bool> predicate, Func<T, string>? describe = null,
        [CallerArgumentExpression(nameof(predicate))]
        string sought = "")
    {
        List<T> all = items.ToList();
        IEnumerable<string> described = all.Select(describe ?? (static item => item?.ToString() ?? "null"));
        string present = all.Count == 0 ? "(none)" : string.Join(", ", described);

        return all.Where(predicate)
            .ToList()
            .ShouldHaveSingleItem($"Looking for the one item where {sought}; there are: {present}.");
    }
}
