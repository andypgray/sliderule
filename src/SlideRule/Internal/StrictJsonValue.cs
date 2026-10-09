using System.Globalization;

namespace SlideRule.Internal;

/// <summary>
///     One value of a document read by <see cref="StrictJson" />, and where it starts in the text. The
///     accessors mirror <c>System.Text.Json.JsonElement</c>'s.
/// </summary>
/// <remarks>
///     <para>
///         The whole document is materialized, so a value stays usable for as long as the caller holds
///         it — unlike <c>JsonElement</c>, which is a window onto a buffer. That is the one behavioural
///         difference a caller moving between the two has to know about, and it only ever makes code
///         that worked keep working.
///     </para>
///     <para>
///         Asking a value for something its kind does not have is a caller bug, not content: it throws
///         <see cref="InvalidOperationException" /> rather than returning a default, because a text that
///         parsed cannot make it happen. Content problems were all settled during the parse, which is
///         why nothing here refuses anything. The one exception is <see cref="TryGetInt32" />, where an
///         integer too large for the type is the file's business and not the caller's.
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
    // what lets an integer of any length read as a Number while TryGetInt32 still answers honestly
    // about whether it fits.
    private readonly string? _text;

    /// <summary>A literal: <see cref="StrictJsonKind.True" /> or <see cref="StrictJsonKind.False" />.</summary>
    internal StrictJsonValue(StrictJsonKind kind, JsonPosition position)
    {
        ValueKind = kind;
        Position = position;
    }

    /// <summary>
    ///     A <see cref="StrictJsonKind.String" /> with its escapes resolved, or a
    ///     <see cref="StrictJsonKind.Number" /> exactly as it was written.
    /// </summary>
    internal StrictJsonValue(StrictJsonKind kind, string text, JsonPosition position)
        : this(kind, position)
    {
        _text = text;
    }

    /// <summary>
    ///     An object, from its members in file order and the parser's ordinal index of them by name, which
    ///     the value takes over.
    /// </summary>
    internal StrictJsonValue(
        IReadOnlyList<StrictJsonProperty> members, Dictionary<string, StrictJsonValue> byName, JsonPosition position)
        : this(StrictJsonKind.Object, position)
    {
        _members = members;
        _byName = byName;
    }

    /// <summary>An array, from its elements in file order.</summary>
    internal StrictJsonValue(IReadOnlyList<StrictJsonValue> elements, JsonPosition position)
        : this(StrictJsonKind.Array, position)
    {
        _elements = elements;
    }

    /// <summary>What this value is.</summary>
    internal StrictJsonKind ValueKind { get; }

    /// <summary>Where the value's first character sits in the text that was parsed.</summary>
    internal JsonPosition Position { get; }

    /// <summary>
    ///     The member named <paramref name="name" />, matched ordinally and case-sensitively. False and a
    ///     null value when the object has no such member.
    /// </summary>
    internal bool TryGetProperty(string name, out StrictJsonValue? value)
    {
        Require(StrictJsonKind.Object, "Object");
        Guard.NotNull(name, nameof(name));

        return _byName!.TryGetValue(name, out value);
    }

    /// <summary>
    ///     The member named <paramref name="name" />, matched ordinally and case-sensitively. Throws
    ///     <see cref="KeyNotFoundException" /> when the object has no such member, so this is the form to
    ///     use where the field is required.
    /// </summary>
    internal StrictJsonValue GetProperty(string name)
    {
        bool found = TryGetProperty(name, out StrictJsonValue? value);
        if (found) return value!;

        string quoted = JsonText.Quote(name);
        throw new KeyNotFoundException("The object has no member named " + quoted + ".");
    }

    /// <summary>The object's members, in the order the text declares them.</summary>
    internal IEnumerable<StrictJsonProperty> EnumerateObject()
    {
        Require(StrictJsonKind.Object, "Object");
        return _members!;
    }

    /// <summary>The array's elements, in the order the text declares them.</summary>
    internal IEnumerable<StrictJsonValue> EnumerateArray()
    {
        Require(StrictJsonKind.Array, "Array");
        return _elements!;
    }

    /// <summary>How many elements the array has.</summary>
    internal int GetArrayLength()
    {
        Require(StrictJsonKind.Array, "Array");
        return _elements!.Count;
    }

    /// <summary>The string's text, with every escape already resolved.</summary>
    internal string GetString()
    {
        Require(StrictJsonKind.String, "String");
        return _text!;
    }

    /// <summary>
    ///     The number as an <see cref="int" />, or false when it does not fit. An integer of any length
    ///     reads as a number, so a file may legitimately carry one this cannot return — that is content,
    ///     not a caller bug, and it is reported rather than thrown.
    /// </summary>
    internal bool TryGetInt32(out int value)
    {
        Require(StrictJsonKind.Number, "Number");
        return int.TryParse(_text!, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Whether the literal is <c>true</c>.</summary>
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
