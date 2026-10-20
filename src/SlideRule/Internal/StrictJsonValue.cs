using System.Globalization;

namespace SlideRule.Internal;

/// <summary>
///     One value of a document read by <see cref="StrictJson" />, and where it starts in the text. The
///     accessors mirror <c>System.Text.Json.JsonElement</c>'s, and an object's members and an array's
///     elements keep the order the text declares them.
/// </summary>
/// <remarks>
///     <para>
///         The whole document is materialized, so a value stays usable for as long as the caller holds it,
///         unlike <c>JsonElement</c>, which is a window onto a buffer.
///     </para>
///     <para>
///         Asking a value for something its kind does not have is a caller bug, not content: it throws
///         <see cref="InvalidOperationException" /> rather than returning a default, because a text that
///         parsed cannot make it happen. Content problems were all settled during the parse, so nothing here
///         refuses anything but <see cref="TryGetInt32" />'s integer too large for the type.
///     </para>
///     <para>
///         Only the parser constructs these, so the constructors carry no guards: the kind and the
///         payload are made to agree by the one place that pairs them.
///     </para>
/// </remarks>
internal sealed class StrictJsonValue
{
    private readonly IReadOnlyList<StrictJsonValue>? _elements;
    private readonly IReadOnlyList<StrictJsonProperty>? _members;
    private readonly Dictionary<string, StrictJsonValue>? _byName;

    // A string's resolved text, or a number exactly as it was written. Holding the number as text is
    // what lets an integer of any length read as a Number while TryGetInt32 still answers
    // whether it fits.
    private readonly string? _text;

    /// <summary>A literal: <see cref="StrictJsonKind.True" /> or <see cref="StrictJsonKind.False" />.</summary>
    internal StrictJsonValue(StrictJsonKind kind, JsonPosition position)
    {
        ValueKind = kind;
        Position = position;
    }

    /// <summary>A <see cref="StrictJsonKind.String" /> or a <see cref="StrictJsonKind.Number" />.</summary>
    internal StrictJsonValue(StrictJsonKind kind, string text, JsonPosition position)
        : this(kind, position)
    {
        _text = text;
    }

    /// <summary>An object, taking over <paramref name="byName" />, the parser's ordinal index of its members.</summary>
    internal StrictJsonValue(
        IReadOnlyList<StrictJsonProperty> members, Dictionary<string, StrictJsonValue> byName, JsonPosition position)
        : this(StrictJsonKind.Object, position)
    {
        _members = members;
        _byName = byName;
    }

    internal StrictJsonValue(IReadOnlyList<StrictJsonValue> elements, JsonPosition position)
        : this(StrictJsonKind.Array, position)
    {
        _elements = elements;
    }

    internal StrictJsonKind ValueKind { get; }

    /// <summary>Where the value's first character sits in the text that was parsed.</summary>
    internal JsonPosition Position { get; }

    /// <summary>Matches <paramref name="name" /> ordinally.</summary>
    internal bool TryGetProperty(string name, out StrictJsonValue? value)
    {
        Require(StrictJsonKind.Object, "Object");
        Guard.NotNull(name, nameof(name));

        return _byName!.TryGetValue(name, out value);
    }

    /// <summary>
    ///     Throws <see cref="KeyNotFoundException" /> when the object has no member named
    ///     <paramref name="name" />: the form for a required field.
    /// </summary>
    internal StrictJsonValue GetProperty(string name)
    {
        bool found = TryGetProperty(name, out StrictJsonValue? value);
        if (found) return value!;

        string quoted = JsonText.Quote(name);
        throw new KeyNotFoundException("The object has no member named " + quoted + ".");
    }

    internal IEnumerable<StrictJsonProperty> EnumerateObject()
    {
        Require(StrictJsonKind.Object, "Object");
        return _members!;
    }

    internal IEnumerable<StrictJsonValue> EnumerateArray()
    {
        Require(StrictJsonKind.Array, "Array");
        return _elements!;
    }

    internal int GetArrayLength()
    {
        Require(StrictJsonKind.Array, "Array");
        return _elements!.Count;
    }

    internal string GetString()
    {
        Require(StrictJsonKind.String, "String");
        return _text!;
    }

    /// <summary>
    ///     False when the number does not fit an <see cref="int" />: a file may carry an integer of any length,
    ///     and one too large is content, not a caller bug, so it is reported rather than thrown.
    /// </summary>
    internal bool TryGetInt32(out int value)
    {
        Require(StrictJsonKind.Number, "Number");
        return int.TryParse(_text!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    internal bool GetBoolean()
    {
        bool isLiteral = ValueKind == StrictJsonKind.True || ValueKind == StrictJsonKind.False;
        if (!isLiteral) throw WrongKind("True or False");

        return ValueKind == StrictJsonKind.True;
    }

    private void Require(StrictJsonKind kind, string asked)
    {
        if (ValueKind != kind) throw WrongKind(asked);
    }

    private InvalidOperationException WrongKind(string asked)
    {
        return new InvalidOperationException(
            "The requested operation requires a value of kind " + asked
                                                                + ", but this value has kind " + ValueKind + ".");
    }
}
