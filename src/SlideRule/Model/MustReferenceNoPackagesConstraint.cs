
namespace SlideRule.Model;

/// <summary>
///     <c>.MustReferenceNoPackages()</c> → "must reference no NuGet packages other than private ones, whose
///     `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen)" (GRAMMAR §4.10).
///     Both boundaries ride in the sentence. The first defines "private" where it is used, because the word alone
///     also means a package from a private feed, and says "other than" rather than "except" so it cannot read as
///     a second <c>, except</c> clause. The parenthetical is the honesty boundary, pinned exactly as
///     <see cref="MustOnlyReferenceConstraint" />'s is: the model holds what a project declares, so a project that
///     takes its whole dependency surface transitively through a project reference satisfies this verb and the
///     sentence says so rather than letting a reader assume otherwise.
/// </summary>
internal sealed class MustReferenceNoPackagesConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    internal override string VerbPhrase =>
        "must reference no NuGet packages other than private ones, whose `PrivateAssets` is `all` "
        + "(declared references only; transitive dependencies are not seen)";
}
