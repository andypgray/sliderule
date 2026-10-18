namespace SlideRule.SpecCompletion;

/// <summary>
///     Which of the slots this provider offers names in a literal fills, or <see cref="None" /> for every
///     other slot and for a parameter carrying no marker at all.
/// </summary>
/// <remarks>
///     Seven of the authoring surface's slots name something the compilation can enumerate — one names
///     namespaces and six name types — and those are the seven that complete. Every other slot holds
///     prose, an ID, a glob over names nothing declares yet, or a value with no universe to draw from, and
///     a list offered there would be a list of wrong answers.
/// </remarks>
internal enum SlotKind
{
    /// <summary>Not a slot this provider offers names in.</summary>
    None,

    /// <summary>A namespace glob, which completes segment by segment and offers the subtree form.</summary>
    NamespacePattern,

    /// <summary>One exact type name, which is a simple name and never a dotted one.</summary>
    ExactTypeName,

    /// <summary>An interface anchor, spelled as a definition's full name.</summary>
    InterfaceName,

    /// <summary>A base-type anchor, spelled as a definition's full name.</summary>
    BaseTypeName,

    /// <summary>An attribute anchor, spelled as a definition's full name.</summary>
    AttributeName,

    /// <summary>A member's return-type anchor, spelled as a definition's full name.</summary>
    ReturnTypeName,

    /// <summary>A parameter-type anchor, spelled as a definition's full name.</summary>
    ParameterTypeName
}

/// <summary>
///     The marker this assembly finds slots by, and the seven slot names it acts on, as constants.
/// </summary>
/// <remarks>
///     <para>
///         Every name here is a mirror, held by a test and by nothing else. This assembly references
///         no project in this repository, so the compiler cannot check one of these spellings against the
///         contract that owns it. What holds them equal is an agreement arm in the test project, which sees
///         both sides: the marker's full name against the attribute type itself, and each constant against
///         the contract's own constant of the same name. A spelling that drifted would leave this provider
///         matching nothing and offering nothing, silently, in every editor.
///     </para>
/// </remarks>
internal static class SpecSlotNames
{
    /// <summary>
    ///     The marker's full name, which is how this assembly finds a marker in a compilation it holds no
    ///     reference into.
    /// </summary>
    internal const string MarkerMetadataName = "SlideRule.Validation.SpecSlotAttribute";

    /// <summary>A namespace glob: <c>arch.Namespace</c>, <c>InNamespace</c>, <c>MustResideInNamespace</c>, a layer's region.</summary>
    internal const string NamespacePattern = nameof(NamespacePattern);

    /// <summary>One exact type name on <c>Named</c>.</summary>
    internal const string ExactTypeName = nameof(ExactTypeName);

    /// <summary>An interface anchor spelled as a definition's full name.</summary>
    internal const string InterfaceName = nameof(InterfaceName);

    /// <summary>A base-type anchor spelled as a definition's full name.</summary>
    internal const string BaseTypeName = nameof(BaseTypeName);

    /// <summary>An attribute anchor spelled as a definition's full name.</summary>
    internal const string AttributeName = nameof(AttributeName);

    /// <summary>A member <c>Returning</c> anchor spelled as a definition's full name.</summary>
    internal const string ReturnTypeName = nameof(ReturnTypeName);

    /// <summary>A <c>MustAcceptParameter</c> anchor spelled as a definition's full name.</summary>
    internal const string ParameterTypeName = nameof(ParameterTypeName);

    /// <summary>
    ///     The kind the slot of this name completes as, or <see cref="SlotKind.None" /> for a name this
    ///     provider offers nothing in and for a parameter that carried no marker.
    /// </summary>
    /// <remarks>
    ///     An unknown name maps to <see cref="SlotKind.None" /> rather than throwing, which is what lets a
    ///     spec project compile against a newer contract than this assembly was built beside: a slot added
    ///     to the contract completes nothing here until this list grows.
    /// </remarks>
    /// <param name="slotName">The name the marker carried, or null where no marker was found.</param>
    internal static SlotKind KindOf(string? slotName)
    {
        return slotName switch
        {
            NamespacePattern => SlotKind.NamespacePattern,
            ExactTypeName => SlotKind.ExactTypeName,
            InterfaceName => SlotKind.InterfaceName,
            BaseTypeName => SlotKind.BaseTypeName,
            AttributeName => SlotKind.AttributeName,
            ReturnTypeName => SlotKind.ReturnTypeName,
            ParameterTypeName => SlotKind.ParameterTypeName,
            _ => SlotKind.None
        };
    }
}
