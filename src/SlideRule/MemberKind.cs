namespace SlideRule;

/// <summary>
///     Which kind of member an <see cref="IMemberInfo" /> describes, read from its <see cref="IMemberInfo.Kind" />
///     in a member predicate. An accessor is never a member of its own: a property's or an event's accessors
///     belong to the property or event, so <c>obj.P</c> and <c>obj.P = x</c> are both a use of one property.
/// </summary>
// SlideRule's own enum, never Roslyn's SymbolKind: Core is netstandard2.0 and references no Roslyn. It sits in
// the root namespace because IMemberInfo.Kind returns it; the extracted model's member references use it too.
// The accessor fold happens at extraction (GRAMMAR §4.5), so no reader has to redo it.
public enum MemberKind
{
    /// <summary>A method, an extension method included.</summary>
    Method,

    /// <summary>A property, with both of its accessors.</summary>
    Property,

    /// <summary>A field, an enum member included.</summary>
    Field,

    /// <summary>An event, with both of its accessors.</summary>
    Event
}
