using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustNotReferencePackages(glob, …)</c> → "must not reference NuGet packages matching {list} (declared
///     references only; transitive dependencies are not seen)" (GRAMMAR §4.10). The honesty boundary rides in the
///     sentence as it does on <see cref="MustReferenceNoPackagesConstraint" />. No privacy clause does, because
///     privacy exempts nothing here: a banned package is banned however it is declared.
/// </summary>
internal sealed class MustNotReferencePackagesConstraint(ProjectSelection subject, IReadOnlyList<string> patterns)
    : ProjectConstraint(subject)
{
    /// <summary>The package identifier globs no declared reference may match, in authoring order.</summary>
    internal IReadOnlyList<string> Patterns { get; } = patterns;

    internal override string VerbPhrase =>
        "must not reference NuGet packages matching "
        + ProseFormat.BacktickedList(Patterns)
        + MustReferenceNoPackagesConstraint.DeclaredOnlyCaveat;
}
