namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One property an evaluation found set by a declaration in the repository, as pure data: its name, its
///     evaluated value, and the <c>file:line</c> of the definition that won. The serializable counterpart of
///     <see cref="SlideRule.Codebase.EvaluatedProperty" />, which adds the framework the evaluation was for.
/// </summary>
/// <remarks>
///     The value is the evaluated one, so a property a props file defines in terms of another arrives
///     expanded, and the site is the definition MSBuild kept, which is regularly a props file rather than the
///     project file.
/// </remarks>
internal readonly record struct FragmentProperty(string Name, string Value, FragmentSite Site);
