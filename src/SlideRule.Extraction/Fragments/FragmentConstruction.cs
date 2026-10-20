namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A <see cref="SlideRule.Codebase.TypeConstruction" /> by name: the definition's full name
///     (<c>N.IHandler&lt;T&gt;</c>) and the name as constructed (<c>N.IHandler&lt;N.Order&gt;</c>), which
///     coincide for a non-generic type.
/// </summary>
internal sealed record FragmentConstruction(string DefinitionFullName, string ConstructedName);
