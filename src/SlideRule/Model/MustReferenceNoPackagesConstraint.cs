namespace SlideRule.Model;

/// <summary>
///     <c>.MustReferenceNoPackages()</c> → "must reference no NuGet packages other than private ones, whose
///     `PrivateAssets` is `all` (declared references only; transitive dependencies are not seen)" (GRAMMAR §4.10).
/// </summary>
/// <remarks>
///     Both boundaries ride in the sentence. The first defines "private" where it is used, because the word alone
///     also means a package from a private feed, and says "other than" rather than "except" so it cannot read as
///     a second <c>, except</c> clause. The parenthetical is the honesty boundary, pinned exactly as
///     <see cref="MustOnlyReferenceConstraint" />'s is: the model holds what a project declares, so a project that
///     takes its whole dependency surface transitively through a project reference satisfies this verb and the
///     sentence says so rather than letting a reader assume otherwise.
/// </remarks>
internal sealed class MustReferenceNoPackagesConstraint(ProjectSelection subject) : ProjectConstraint(subject)
{
    /// <summary>
    ///     The honesty boundary every package verb's sentence closes with, held in one place so a correction to
    ///     what the model sees cannot land on one verb and miss another.
    /// </summary>
    internal const string DeclaredOnlyCaveat = " (declared references only; transitive dependencies are not seen)";

    /// <summary>
    ///     The privacy exemption the allow-list package verbs state, held in one place so a correction to what
    ///     "private" means cannot land on one verb and miss another.
    /// </summary>
    internal const string PrivateExemption = "other than private ones, whose `PrivateAssets` is `all`";

    internal override string VerbPhrase => "must reference no NuGet packages " + PrivateExemption + DeclaredOnlyCaveat;
}
