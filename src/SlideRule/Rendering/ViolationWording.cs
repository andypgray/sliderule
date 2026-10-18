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
    // targets, not prose), and a framework the project lacks reads as one it does not target.
    internal static string SubjectText(Violation violation)
    {
        return violation.Kind switch
        {
            ViolationKind.MemberShape => MemberDisplay.Of(violation.SubjectMember!),
            ViolationKind.ProjectShape => ProjectText(violation),
            _ => violation.Subject!.FullName
        };
    }

    // The project arm on its own, which the SARIF renderer's twin switch mirrors line for line. A property violation
    // says what the judged build holds, and names the framework only where the build told one apart.
    private static string ProjectText(Violation violation)
    {
        string project = violation.SubjectProject!.Name;
        if (violation.Package is { } package) return $"{project} references package {package.Name}";
        if (violation.Property is { } property) return project + PropertyText(property, violation);
        if (violation.Framework is not { } framework) return project;

        return violation.FrameworkMissing ? $"{project} does not target {framework}" : $"{project} targets {framework}";
    }

    private static string PropertyText(string property, Violation violation)
    {
        string build = violation.Framework is { } framework ? $" for {framework}" : "";
        return violation.PropertyValue switch
        {
            null => $" does not set {property}{build}",
            { } value when value.Trim().Length == 0 => $" sets {property} to an empty value{build}",
            { } value => $" sets {property} to {value}{build}"
        };
    }
}
