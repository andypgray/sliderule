using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary><c>.Methods.MustHaveSuffix("Async")</c> → "must be named `*Async`" (GRAMMAR §5.7; reuses the §5.3 fragment).</summary>
internal sealed class MemberMustHaveSuffixConstraint(MemberSelection subject, string suffix) : MemberConstraint(subject)
{
    internal string Suffix { get; } = suffix;

    internal override string VerbPhrase => "must be named " + ProseFormat.Backtick("*" + Suffix);
}
