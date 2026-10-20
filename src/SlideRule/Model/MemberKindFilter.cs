namespace SlideRule.Model;

/// <summary>
///     Which declared members a projection selects (GRAMMAR §4.6), stamped onto a <see cref="MemberSelection" />
///     by its projection property. Distinct from <see cref="SlideRule.MemberKind" />, the actual kind of an
///     inventoried member, by the <see cref="Any" /> case of the unrestricted <c>.Members</c> projection.
/// </summary>
internal enum MemberKindFilter
{
    /// <summary>All member kinds — the <c>.Members</c> projection.</summary>
    Any,

    /// <summary>Methods only — the <c>.Methods</c> projection.</summary>
    Method,

    /// <summary>Properties only — the <c>.Properties</c> projection.</summary>
    Property,

    /// <summary>Fields only — the <c>.Fields</c> projection.</summary>
    Field,

    /// <summary>Events only — the <c>.Events</c> projection.</summary>
    Event
}
