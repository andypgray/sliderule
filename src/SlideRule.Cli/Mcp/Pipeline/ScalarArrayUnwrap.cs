using System.Text.Json;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The one-element-array unwrap the scalar coercers share. Models routinely wrap a single value in an
///     array (<c>["A"]</c>, <c>[true]</c>) where a scalar is advertised, and every coercer forgives it the
///     same way: read one element, insist the array ends there, and refuse an array holding more.
/// </summary>
/// <remarks>
///     Only the protocol lives here. What counts as a readable scalar, what the empty array means, and the wording
///     of every refusal stay with the coercer they belong to: the coercers differ on all three, and their messages
///     are pinned text. Hand-rolled rather than <c>JsonSerializer.Deserialize&lt;T&gt;</c> for the element, which
///     would recurse straight back through the coercer's own factory.
/// </remarks>
internal static class ScalarArrayUnwrap
{
    /// <summary>
    ///     Reads one scalar of <typeparamref name="T" /> from the token <paramref name="reader" /> is positioned at,
    ///     taken by <see langword="ref" /> as no <see cref="Func{T,TResult}" /> can take it.
    ///     <paramref name="insideArray" /> chooses the coercer's wording only: an error naming an "array element"
    ///     tells the caller the coercer did look inside their array and still could not use what it found there.
    /// </summary>
    internal delegate T ScalarReader<out T>(ref Utf8JsonReader reader, bool insideArray);

    /// <summary>
    ///     Collapses the array <paramref name="reader" /> has opened, positioned at its
    ///     <see cref="JsonTokenType.StartArray" />, to the single scalar it holds, read by the coercer's own
    ///     <paramref name="readScalar" />; <paramref name="onEmptyArray" /> answers <c>[]</c> with a value or a
    ///     refusal.
    /// </summary>
    /// <exception cref="JsonException">The JSON ended inside the array.</exception>
    /// <exception cref="UserErrorException">The array holds more than one element.</exception>
    internal static T Read<T>(
        ref Utf8JsonReader reader,
        ScalarReader<T> readScalar,
        Func<T> onEmptyArray,
        string multipleElementsMessage)
    {
        if (!reader.Read()) throw new JsonException("Unexpected end of JSON while reading array.");

        if (reader.TokenType == JsonTokenType.EndArray) return onEmptyArray();

        T value = readScalar(ref reader, insideArray: true);

        if (!reader.Read()) throw new JsonException("Unexpected end of JSON while reading array.");

        if (reader.TokenType != JsonTokenType.EndArray) throw new UserErrorException(multipleElementsMessage);

        return value;
    }
}
