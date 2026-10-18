namespace SlideRule.Validation;

/// <summary>
///     Marks a parameter of the authoring surface that takes text, one string or a list of them, as the spec
///     slot of the given name, so that a reader can tell from the declaration alone which slot a literal
///     written at that parameter fills.
/// </summary>
/// <remarks>
///     The marker is read from metadata by a compiler extension, out of whatever copy of this assembly
///     the compilation being analysed references rather than out of the copy the extension was built
///     against. It finds the attribute by its full name,
///     <c>SlideRule.Validation.SpecSlotAttribute</c>, and takes the slot from the single
///     constructor argument. That full name and that one string argument are therefore a contract with a
///     reader no search over this assembly can find: renaming the type, moving it to another namespace,
///     or giving the constructor a second parameter leaves the reader matching nothing and every slot
///     silently invisible to it.
///     <para>
///         The constructor is public while the type is internal, which is what makes the arguments
///         readable. A custom-attribute blob names the constructor it was built from, and a reader with
///         no access to that constructor cannot decode the blob, so an internal constructor would hide
///         the very value the marker exists to carry. Keeping the type internal keeps it off the public
///         surface, where it would become something a spec project compiles against.
///     </para>
///     <para>
///         <see cref="Name" /> is the <see cref="SpecSlot.Name" /> of one of the singletons on
///         <see cref="SpecSlot" />, written at the parameter as the <c>nameof</c> of that singleton.
///         Marker and verdict owner share one vocabulary that way, and a name no
///         <see cref="SpecSlot.ByName" /> lookup resolves is a mistake rather than a second naming scheme.
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
