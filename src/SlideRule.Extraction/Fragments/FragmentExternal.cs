namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A type a compilation references but does not declare: a BCL or NuGet type, or, from this input's
///     narrow view, another project's. <see cref="Facts" /> are read from this compilation's metadata symbol,
///     and <see cref="AssemblyName" /> is the assembly the compilation bound the name from, empty where the
///     symbol has no containing assembly; a side fragment records the own assembly instead for a name the own
///     assembly declares.
/// </summary>
/// <remarks>
///     <see cref="PassedDown" /> is what the type passes to its descendants as this compilation reads it, ordinal by
///     constructed name and recorded by name, so it mints no external node. It is read from the symbol alone, so
///     every fragment that references the type records the same list whatever route reached it; it is empty for
///     anything that cannot be a base, which is every type but an unsealed, non-static class.
/// </remarks>
internal sealed record FragmentExternal(
    TypeFacts Facts,
    string AssemblyName,
    IReadOnlyList<FragmentConstruction> PassedDown);
