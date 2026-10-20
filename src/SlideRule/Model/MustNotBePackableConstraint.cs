namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotBePackable()</c> → "must not be packable" (GRAMMAR §4.10) — the law that keeps an
///     internal project off the feed, the SDK defaulting <c>IsPackable</c> on.
/// </summary>
internal sealed class MustNotBePackableConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    internal override string VerbPhrase => "must not be packable";
}
