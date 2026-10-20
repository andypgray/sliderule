namespace SlideRule.Validation;

/// <summary>
///     Marks a parameter of the authoring surface that takes text, one string or a list of them, as the spec
///     slot of the given name, so that a reader can tell from the declaration alone which slot a literal
///     written at that parameter fills.
/// </summary>
/// <remarks>
///     A compiler extension reads the marker from metadata, out of whichever copy of this assembly the
///     analysed compilation references, finding it by its full name, <c>SlideRule.Validation.SpecSlotAttribute</c>,
///     and taking the slot from the single constructor argument. That name and that one string argument are a
///     contract with a reader no search over this assembly finds: renaming the type, moving its namespace or
///     adding a constructor parameter leaves every slot silently invisible to it.
///     <para>
///         The constructor is public while the type is internal: a custom-attribute blob names the
///         constructor it was built from, and a reader with no access to that constructor cannot decode it.
///         The type stays internal so no spec project compiles against it.
///     </para>
///     <para>
///         A <see cref="Name" /> no <see cref="SpecSlot.ByName" /> lookup resolves is a mistake, not a second
///         naming scheme: marker and verdict owner share one vocabulary.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class SpecSlotAttribute : Attribute
{
    /// <summary>Marks the parameter as the slot called <paramref name="name" />.</summary>
    /// <param name="name">The slot's <see cref="SpecSlot.Name" />, written as the <c>nameof</c> of its singleton.</param>
    public SpecSlotAttribute(string name)
    {
        Name = name;
    }

    /// <summary>The name of the slot this parameter fills.</summary>
    internal string Name { get; }
}
