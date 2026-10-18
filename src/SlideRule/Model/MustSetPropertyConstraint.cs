using SlideRule.Fluent;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustSetProperty(name, value)</c> → "must set {name} to {value}" (GRAMMAR §4.10). No caveat rides the
///     sentence: "set" already reads as declared by somebody, which is the boundary the verb keeps, a value the
///     SDK defaults counting for nothing. The verb is judged once per evaluation, so a multi-targeted project
///     answers for each framework apart.
/// </summary>
internal sealed class MustSetPropertyConstraint(ProjectSelection subject, string name, string value)
    : ProjectConstraint(subject)
{
    /// <summary>The MSBuild property's name, as authored; matched ignoring case.</summary>
    internal string Name { get; } = name;

    /// <summary>The value the property must hold, as authored; compared ignoring case and surrounding whitespace.</summary>
    internal string Value { get; } = value;

    internal override string VerbPhrase =>
        "must set " + ProseFormat.Backtick(Name) + " to " + ProseFormat.Backtick(Value);
}
