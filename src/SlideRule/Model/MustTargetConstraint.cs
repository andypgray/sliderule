using SlideRule.Fluent;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustTarget(tfm)</c> → "must target {tfm}" (GRAMMAR §4.10): the floor <see cref="MustOnlyTargetConstraint" />
///     is the ceiling of. Single-operand by design (§10): a list of required frameworks reads as all-of and
///     any-of alike, so a second required framework is a second rule. No caveat rides the sentence, for the same
///     reason the ceiling carries none: a project's frameworks are a closed set.
/// </summary>
internal sealed class MustTargetConstraint(ProjectSelection subject, string framework) : ProjectConstraint(subject)
{
    /// <summary>The required target framework moniker, as authored.</summary>
    internal string Framework { get; } = framework;

    internal override string VerbPhrase => "must target " + ProseFormat.Backtick(Framework);
}
