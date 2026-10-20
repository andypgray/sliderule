namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A type a compilation references but does not declare: a BCL or NuGet type, or, from this input's
///     narrow view, another project's. <see cref="Facts" /> are read from this compilation's metadata symbol,
///     and <see cref="AssemblyName" /> is the assembly the compilation bound the name from, empty where the
///     symbol has no containing assembly; a side fragment records the own assembly instead for a name the own
///     assembly declares.
/// </summary>
internal sealed record FragmentExternal(TypeFacts Facts, string AssemblyName);
