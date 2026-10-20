using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustHaveExactlyOneCounterpart(among:, named:)</c> → "must have exactly one counterpart named
///     {template} among {list}" (GRAMMAR §5.3). The correspondence verb: per subject the template
///     derives a name — every <c>{Name}</c> replaced by the subject's simple name — and exactly one
///     type in the among selection must carry it; zero counterparts and several are both red.
/// </summary>
internal sealed class MustHaveExactlyOneCounterpartConstraint(Selection subject, IReadOnlyList<Selection> among, string template)
    : OperandConstraint(subject, among)
{
    /// <summary>The one selection naming where a counterpart may live.</summary>
    internal IReadOnlyList<Selection> Among => Operands;

    internal string Template { get; } = template;

    /// <summary>The placeholder a template must carry, and the only run <see cref="Derive" /> replaces.</summary>
    // Spelled once because two surfaces must agree on it exactly: checking substitutes it, and spec build
    // refuses a template without it. A placeholder the validator accepted and the checker did not recognize
    // would green every spec and derive a constant name for every subject.
    internal const string NamePlaceholder = "{Name}";

    // netstandard2.0's two-string Replace is ordinal by definition — there is no StringComparison overload
    // to spell it with — which is the same match the spec-build placeholder check runs, so a '{name}' typo
    // fails at build rather than deriving a constant name at check time.
    internal string Derive(string simpleName)
    {
        return Template.Replace(NamePlaceholder, simpleName);
    }

    internal override string VerbPhrase =>
        "must have exactly one counterpart named " + ProseFormat.Backtick(Template) + " among " + Among.TargetListProse();
}
