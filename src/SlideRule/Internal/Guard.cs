using System.Globalization;

namespace SlideRule.Internal;

/// <summary>
///     Minimal argument guards for the public API boundary, standing in for the throw helpers netstandard2.0
///     lacks (<c>ArgumentNullException.ThrowIfNull</c>, <c>ArgumentException.ThrowIfNullOrWhiteSpace</c>).
/// </summary>
/// <remarks>
///     On the surface a spec chains off <c>arch</c> the split is by kind, not by argument: a null or an
///     undefined enum value is a programmer error and throws at the call; a blank or malformed string is a
///     spec-author mistake that <c>SpecValidator</c> collects, so every error in the spec is reported at once.
///     So <c>arch.Namespace(null!)</c> throws and <c>arch.Namespace("")</c> does not. <see cref="Lifetime" />
///     is the exception and stays with the catalog's <c>UndefinedLifetime</c>, whose message names the rule
///     that used the value; that code is pinned by GRAMMAR §8 and its tests. The <c>NamespacePattern</c> and
///     <c>TypeNamePattern</c> constructors, though they sit in <c>SlideRule.Fluent</c>, throw on a blank.
/// </remarks>
internal static class Guard
{
    internal static T NotNull<T>(T? value, string paramName)
        where T : class
    {
        return value ?? throw new ArgumentNullException(paramName);
    }

    internal static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (value is null) throw new ArgumentNullException(paramName);

        if (value.Trim().Length == 0) throw new ArgumentException("Value must not be blank.", paramName);

        return value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentOutOfRangeException" /> when <paramref name="value" /> is not a declared
    ///     member of its enum, naming every member by its qualified name so a caller who cast an <c>int</c>
    ///     reads the spelling to use.
    /// </summary>
    internal static TEnum Defined<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum
    {
        if (Enum.IsDefined(typeof(TEnum), value)) return value;

        string name = typeof(TEnum).Name;
        var numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        // Comma-joined after a phrase that needs no final conjunction.
        string members = string.Join(", ", Enum.GetNames(typeof(TEnum)).Select(member => name + "." + member));
        throw new ArgumentOutOfRangeException(paramName, value,
            $"'({name}){numeric.ToString(CultureInfo.InvariantCulture)}' is not a defined {name}; "
            + $"the defined values are {members}.");
    }
}
