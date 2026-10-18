using SlideRule.Checking;

namespace SlideRule.Rendering;

/// <summary>
///     What a violation says in English, one owner for every surface that words one: the phrase the human
///     check report prints after a site's <c>path:line</c>, and the same phrase inside a build finding's
///     one-line message.
/// </summary>
/// <remarks>
///     Wording only: which site a violation lands at, and how many lines it yields, stays with each caller,
///     because that is where the two surfaces genuinely differ. <c>SarifReportRenderer</c> keeps a twin
///     switch of its own and is not a caller here — each renderer formatting independently is a house
///     convention. Both spell a member through <c>MemberDisplay</c>, because that form is also what
///     <c>baseline add</c> resolves names against.
/// </remarks>
internal static class ViolationWording
{
    /// <summary>The phrase an edge violation reads as: the source, the verb its kind names, and the far end.</summary>
    // What each of the seven edge kinds says, with placement left to the caller.
    internal static string EdgeText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.Reference => $"{violation.Source!.FullName} references {violation.Target!.FullName}",
            ViolationKind.MemberUse => $"{violation.Source!.FullName} uses {MemberDisplay.Of(violation.Member!)}",
            ViolationKind.Construction => $"{violation.Source!.FullName} constructs {violation.Target!.FullName}",
            ViolationKind.Injection => $"{violation.Source!.FullName} injects {violation.Target!.FullName}",
            ViolationKind.Catch => $"{violation.Source!.FullName} catches {violation.Target!.FullName}",
            ViolationKind.Throw => $"{violation.Source!.FullName} throws {violation.Target!.FullName}",
            ViolationKind.Expose => $"{violation.Source!.FullName} exposes {violation.Target!.FullName}",
            _ => string.Empty
        };
    }

    /// <summary>The phrase a shape violation reads as: the type, member or project that failed the verb.</summary>
    // What each of the three subject kinds says, with placement left to the caller. The project
    // arm is the Shape parallel over a project: its name, at whichever declaration carried the fact the verb
    // read — which may be a props file above the project, and may be nothing at all. A per-package or
    // per-framework violation names the item too, in EdgeText's register (no backticks: these lines are jump
    // targets, not prose).
    internal static string SubjectText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.MemberShape => MemberDisplay.Of(violation.SubjectMember!),
            ViolationKind.ProjectShape => violation.Package is { } package
                ? $"{violation.SubjectProject!.Name} references package {package.Name}"
                : violation.Framework is { } framework
                    ? $"{violation.SubjectProject!.Name} targets {framework}"
                    : violation.SubjectProject!.Name,
            _ => violation.Subject!.FullName
        };
    }
}
