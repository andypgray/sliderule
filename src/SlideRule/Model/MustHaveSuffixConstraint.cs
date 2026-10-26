using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.MustHaveSuffix("Handler")</c> → "must be named `*Handler`" (GRAMMAR §5.3).</summary>
internal sealed class MustHaveSuffixConstraint(Selection subject, string suffix) : Constraint(subject)
{
    internal string Suffix { get; } = suffix;

    internal override string VerbPhrase => "must be named " + ProseFormat.SuffixGlob(Suffix);
}
