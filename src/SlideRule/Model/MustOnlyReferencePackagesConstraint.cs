using SlideRule.Fluent;
using SlideRule.Prose;

namespace SlideRule.Model;

/// <summary>
///     <c>.MustOnlyReferencePackages(glob, …)</c> → "must reference only NuGet packages matching {list}, other than
///     private ones, whose `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen)"
///     (GRAMMAR §4.10): <see cref="MustReferenceNoPackagesConstraint" /> with an allow-list. Both of that verb's
///     boundaries ride in this sentence for the reasons given there, the privacy clause set off by commas so it
///     reads as the exemption it is rather than as a narrowing of the list.
/// </summary>
internal sealed class MustOnlyReferencePackagesConstraint(ProjectSelection subject, IReadOnlyList<string> patterns)
    : ProjectConstraint(subject)
{
    /// <summary>The package identifier globs a non-private reference must match one of, in authoring order.</summary>
    internal IReadOnlyList<string> Patterns { get; } = patterns;

    internal override string VerbPhrase =>
        "must reference only NuGet packages matching "
        + ProseFormat.BacktickedList(Patterns)
        + ", other than private ones, whose `PrivateAssets` is `all` "
        + "(declared references only; transitive dependencies are not seen)";
}
