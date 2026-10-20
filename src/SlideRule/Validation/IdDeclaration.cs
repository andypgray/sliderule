namespace SlideRule.Validation;

/// <summary>One ID as a spec declared it.</summary>
internal readonly struct IdDeclaration(string id, IdRole role, int tag)
{
    /// <summary>The ID as declared, before any desugaring.</summary>
    internal string Id { get; } = id;

    internal IdRole Role { get; } = role;

    /// <summary>
    ///     The caller's own name for where this was declared, opaque to the collision walk, which only hands it
    ///     back on the verdicts the declaration takes part in.
    /// </summary>
    internal int Tag { get; } = tag;
}
