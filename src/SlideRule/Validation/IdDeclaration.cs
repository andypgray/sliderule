namespace SlideRule.Validation;

/// <summary>
///     One ID as a spec declared it: the text, the anchor it was written on, and the caller's own tag for
///     where that was.
/// </summary>
/// <remarks>
///     <see cref="Tag" /> is opaque to the collision walk, which only hands it back on the verdicts a
///     declaration takes part in. A caller holding registrations passes their positions; a caller holding
///     source positions passes those. Declarations are supplied in the order the spec wrote them, because
///     the catalog reports a collision at the first occurrence of the ID.
/// </remarks>
internal readonly struct IdDeclaration(string id, IdRole role, int tag)
{
    /// <summary>The ID as declared, before any desugaring.</summary>
    internal string Id { get; } = id;

    /// <summary>Whether a rule or a scope declared it.</summary>
    internal IdRole Role { get; } = role;

    /// <summary>The caller's own name for where this was declared.</summary>
    internal int Tag { get; } = tag;
}
