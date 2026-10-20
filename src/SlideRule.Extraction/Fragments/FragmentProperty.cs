namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One property an evaluation found set by a declaration in the repository: its name, its evaluated value,
///     so a property defined in terms of another arrives expanded, and the <c>file:line</c> of the definition
///     MSBuild kept, regularly a props file rather than the project file. The serializable counterpart of
///     <see cref="SlideRule.Codebase.EvaluatedProperty" />, which adds the framework the evaluation was for.
/// </summary>
internal readonly record struct FragmentProperty(string Name, string Value, FragmentSite Site);
