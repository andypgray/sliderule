using System.Globalization;

namespace SlideRule.Internal;

/// <summary>
///     Minimal argument guards for the public API boundary. netstandard2.0 lacks the
///     <c>ArgumentNullException.ThrowIfNull</c> / <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>
///     throw helpers, so these stand in for programmer-error checks only.
/// </summary>
/// <remarks>
///     The split is by kind, not by argument: a null, an arity error or an undefined enum value is a
///     programmer error and throws here, at the call; a blank or malformed string is a spec-author
///     mistake and is collected by <c>SpecValidator</c> so every error in the spec is reported at once.
///     So <c>arch.Namespace(null!)</c> throws and <c>arch.Namespace("")</c> does not.
///     <para>
///         <see cref="Lifetime" /> is the one undefined-enum value that stays with the catalog, as
///         <c>UndefinedLifetime</c>, rather than moving to <see cref="Defined{TEnum}" />. That is
///         deliberate and not an oversight to tidy: the catalog message also names the rule the value was
///         used by, which an <see cref="ArgumentOutOfRangeException" /> at
///         <c>arch.Registered((Lifetime)7)</c> cannot, and the code is pinned by GRAMMAR §8 and its tests.
///     </para>
/// </remarks>
internal static class Guard
{
    /// <summary>Throws <see cref="ArgumentNullException" /> when <paramref name="value" /> is null.</summary>
    internal static T NotNull<T>(T? value, string paramName)
        where T : class
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    /// <summary>Throws when <paramref name="value" /> is null, empty, or all whitespace.</summary>
    internal static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (value is null) throw new ArgumentNullException(paramName);

        if (value.Trim().Length == 0) throw new ArgumentException("Value must not be blank.", paramName);

        return value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> when <paramref name="value" /> is not a
    ///     declared member of its enum. The message names every member by its qualified name rather than
    ///     stating a numeric range, so a caller who reached this by casting an <c>int</c> reads the
    ///     spelling to use instead; the members are read off the enum, so one added later needs no edit
    ///     here.
    /// </summary>
    internal static TEnum Defined<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum
    {
        if (Enum.IsDefined(typeof(TEnum), value)) return value;

        string name = typeof(TEnum).Name;
        var numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        // Comma-joined, introduced by a phrase that needs no final conjunction. Writing "A, B, or
        // C" would mean branching on how many members there are, and every arm but one would be code no
        // enum on this surface can reach.
        string members = string.Join(", ", Enum.GetNames(typeof(TEnum)).Select(member => name + "." + member));
        throw new ArgumentOutOfRangeException(paramName, value,
            $"'({name}){numeric.ToString(CultureInfo.InvariantCulture)}' is not a defined {name}; "
            + $"the defined values are {members}.");
    }
}
