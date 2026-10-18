using SlideRule.Baselines;
using SlideRule.Codebase;

namespace SlideRule.Checking;

/// <summary>
///     One concrete way a rule was broken: a reference between two types, a use of a banned member, a
///     type, member or project of the wrong shape, a subject that matched nothing, or an error that
///     stopped the rule being evaluated. <see cref="Kind" /> says which, and therefore which of the
///     properties below carry a value and which are null; <see cref="Sites" /> carries the source
///     positions that evidence it. A rule's failing violations are in
///     <see cref="RuleResult.Violations" /> and its tolerated ones in
///     <see cref="RuleResult.Grandfathered" />.
/// </summary>
// The constructor takes only what every violation has — its kind and its evidence — and each factory
// below names the slots its own kind populates, through an object initializer. That is why the slots
// carry a private setter rather than being get-only: init is unavailable on this target framework (no
// IsExternalInit), and every factory is a member of this class, so the setters reach exactly as far
// as they must and a violation is read-only to every consumer. A new slot therefore touches no
// existing factory, and no factory threads a run of nulls past slots of the same type where a
// transposition would compile.
internal sealed class Violation
{
    private Violation(ViolationKind kind, IReadOnlyList<SourceLocation> sites)
    {
        Kind = kind;
        Sites = sites;
    }

    /// <summary>Gets what kind of violation this is, and so which of the properties below carry a value.</summary>
    public ViolationKind Kind { get; }

    /// <summary>
    ///     Gets the type at the near end of the offending edge: the one that references, constructs,
    ///     injects, catches, throws or exposes, depending on <see cref="Kind" />. This is where the edit
    ///     goes, for the inbound verbs as much as the outbound ones. Null for every kind that is not an
    ///     edge.
    /// </summary>
    public TypeNode? Source { get; private set; }

    /// <summary>
    ///     Gets the type at the far end of the offending edge: the referenced type, the constructed type,
    ///     the injected parameter type, the caught or the thrown exception type, or the exposed type,
    ///     depending on <see cref="Kind" />. Null for every kind that is not an edge.
    /// </summary>
    public TypeNode? Target { get; private set; }

    /// <summary>
    ///     Gets the type that failed a verb about the type itself (its shape, its name, what it inherits
    ///     or is attributed with, where it lives, or a <c>Must</c> predicate); null for every other kind.
    /// </summary>
    public TypeNode? Subject { get; private set; }

    /// <summary>Gets the banned member the source used; null for every kind but a member use.</summary>
    public MemberReference? Member { get; private set; }

    /// <summary>Gets the declared member that failed a member rule; null for every other kind.</summary>
    public MemberNode? SubjectMember { get; private set; }

    /// <summary>Gets the project that failed a packaging or targeting rule; null for every other kind.</summary>
    public ProjectNode? SubjectProject { get; private set; }

    /// <summary>
    ///     Gets the declared package reference that counts against the rule. Populated only by the
    ///     per-package violations of <c>MustReferenceNoPackages</c>, <c>MustOnlyReferencePackages</c> and
    ///     <c>MustNotReferencePackages</c>, one for each package the project declares that the rule objects to,
    ///     and null on every other project violation and every other kind: its presence is what parts "this
    ///     project is wrong" from "this project declares this package".
    /// </summary>
    public PackageReference? Package { get; private set; }

    /// <summary>
    ///     Gets the target framework, as a short moniker, that counts against the rule. Populated only by the
    ///     per-framework violations of <c>MustOnlyTarget</c>, one for each framework the project targets that the
    ///     rule does not permit, and by those of <c>MustTarget</c>, naming the framework the rule requires and the
    ///     project does not target (<see cref="FrameworkMissing" />). A <c>MustSetProperty</c> violation fills it
    ///     too, with the framework whose build was judged. Null on every other project violation and every other
    ///     kind.
    /// </summary>
    public string? Framework { get; private set; }

    /// <summary>
    ///     Gets whether <see cref="Framework" /> names a framework the project lacks, as on a <c>MustTarget</c>
    ///     violation, rather than one it targets. False on every other violation.
    /// </summary>
    // A flag beside the moniker rather than a slot of its own: both verbs key the same framework:{moniker} item,
    // so the identity, the JSON key and the baseline matcher read one field, and only the wording forks.
    public bool FrameworkMissing { get; private set; }

    /// <summary>
    ///     Gets the MSBuild property a <c>MustSetProperty</c> violation is about, named as the rule names it, and
    ///     null on every other violation. <see cref="Framework" /> names the framework whose build was judged, or
    ///     is null where that build told no framework apart, and <see cref="PropertyValue" /> what it holds.
    /// </summary>
    public string? Property { get; private set; }

    /// <summary>
    ///     Gets the value the judged build evaluated for <see cref="Property" />, or null where nothing in the
    ///     solution set it there; null on every other violation.
    /// </summary>
    public string? PropertyValue { get; private set; }

    /// <summary>
    ///     Gets the package, framework or property a per-item project violation names, in the form its baseline
    ///     identity keys it by: <c>package:</c>, <c>framework:</c> or <c>property:</c> followed by the package
    ///     identifier as declared, the short moniker, or the property as the rule names it with <c>@</c> and the
    ///     framework of the build judged. Null on every violation that names no item.
    /// </summary>
    internal string? ProjectItem { get; private set; }

    /// <summary>
    ///     The bare name of <see cref="ProjectItem" />: the package identifier, the property with its framework, or
    ///     the moniker, untagged.
    /// </summary>
    internal string? ProjectItemName { get; private set; }

    private const string PackageItemPrefix = "package:";
    private const string FrameworkItemPrefix = "framework:";
    private const string PropertyItemPrefix = "property:";

    /// <summary>
    ///     Gets the source positions that evidence the violation: the sites of the offending edge, or the
    ///     declarations of the offending type, member or project fact. For a project fact that is the
    ///     declaration that won the build's evaluation, regularly a props file above the project file, and
    ///     the project file itself where nothing declared the property. Empty when the subject matched
    ///     nothing and when the rule errored, neither of which has anywhere to point.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites { get; }

    /// <summary>
    ///     Gets the explanatory text of a violation that has no code to point at: why the subject matched
    ///     nothing, or what stopped the rule being evaluated. On a reference violation from
    ///     <c>MustNotHaveCircularReferences</c> it instead names the circle the pair lies on ("circular
    ///     references among the A and B layers"), which <c>--json</c> output alone prints. Null otherwise.
    /// </summary>
    public string? Detail { get; private set; }

    /// <summary>
    ///     Gets what to change so the rule's subject matches something, on a violation that is about the
    ///     rule rather than about the code: the selection's own semantics, and what to check first. Present
    ///     on every violation raised because the subject selected nothing, and null on every other kind,
    ///     whose fix is a change to the code the violation names. The check report prints it on a line
    ///     under the violation, and <c>--json</c> output carries it as <c>hint</c>.
    /// </summary>
    public string? Hint { get; private set; }

    /// <summary>
    ///     Gets the name of the family cell the violation is attributed to: the layer or the project the rule
    ///     was speaking about when the violation was found. Populated only by the verbs of a rule whose
    ///     subject is a family (<c>arch.Each</c>), and null on every violation of every other rule. Where a
    ///     verb constrains what may reference the subject, the cell is the one the referenced type sits in,
    ///     not the referencing type's, because that is the cell the rule was judging. The burndown printed
    ///     by <c>sliderule status</c> groups a family rule's grandfathered violations by it, and it is no
    ///     part of what a baseline file records.
    /// </summary>
    public string? Cell { get; private set; }

    /// <summary>
    ///     This violation's deterministic within-rule report order key: (Source|Subject FullName, Target
    ///     FullName, Member SymbolId, Target|Subject ProjectName), compared ordinal by the checker. A
    ///     MemberUse mirrors Reference's (source, target) as (source FullName, member SymbolId); a
    ///     MemberShape mirrors Shape's subject as (declaring-type FullName, member SymbolId); a
    ///     ProjectShape mirrors it as (project Name, package Name or framework moniker), the item standing
    ///     where a Target would, so one project's per-item violations sort by the item they name.
    /// </summary>
    /// <remarks>
    ///     The fourth slot exists because the first three do not separate every pair of violations: a full
    ///     name a project declares and a referenced assembly also supplies denotes two nodes (GRAMMAR §4.1),
    ///     and one source type can reach both where it is itself compiled into two projects. Without it the
    ///     sort ties, and a stable sort then falls through to the order the subject set was walked in — a
    ///     reference-keyed hash set, which is to say no order at all, differing run to run. Project name is
    ///     the discriminator because it is exactly what parts the two: the external half carries the
    ///     supplying assembly's name.
    /// </remarks>
    internal (string Primary, string Secondary, string Tertiary, string Quaternary) OrderKey
    {
        get
        {
            // A MemberShape's declaring-type FullName and a ProjectShape's project Name are the only
            // primary keys that are not a Source or a Subject; every other kind leaves both null and never
            // reaches them.
            string primary = (Source ?? Subject)?.FullName
                             ?? SubjectProject?.Name
                             ?? (SubjectMember is { } member ? member.DeclaringTypeFullName : string.Empty);
            string secondary = Target?.FullName ?? ProjectItemName ?? string.Empty;
            string tertiary = Member?.SymbolId ?? SubjectMember?.SymbolId ?? string.Empty;
            string quaternary = (Target ?? Subject)?.ProjectName ?? string.Empty;
            return (primary, secondary, tertiary, quaternary);
        }
    }

    /// <summary>
    ///     Returns the entry that identifies this violation in a baseline file — what decides whether a
    ///     captured baseline grandfathers it — or null for a violation no baseline can ever hold.
    /// </summary>
    /// <remarks>
    ///     An edge key for the edge kinds, pairing <see cref="Source" />'s symbol ID with
    ///     <see cref="Target" />'s, or with <see cref="Member" />'s for a banned member use. A subject key
    ///     otherwise: <see cref="Subject" />'s symbol ID for a type, <see cref="SubjectMember" />'s for a
    ///     member, and <see cref="SubjectProject" />'s for a project. A per-item project violation keys an
    ///     edge instead, from the project's identity to <see cref="ProjectItem" />, so a package or framework
    ///     added after the baseline was captured is a new identity and fails, as a different forbidden target
    ///     from a grandfathered source does. Every overload, parameter, catch clause, throw and signature
    ///     position of one pair rides under one identity, its sites being evidence rather than identity; an edge entry also
    ///     records how many sites it covered when it was captured, and a pair that has since grown past that
    ///     count fails the rule instead of being grandfathered. Null for a subject that matched nothing and
    ///     for a rule that errored: those have no stable identity.
    /// </remarks>
    public BaselineEntry? BaselineIdentity()
    {
        return Kind switch
        {
            ViolationKind.Reference or ViolationKind.Construction or ViolationKind.Injection
                or ViolationKind.Catch or ViolationKind.Throw or ViolationKind.Expose =>
                BaselineEntry.ForEdge(Source!.SymbolId, Target!.SymbolId),
            ViolationKind.MemberUse => BaselineEntry.ForEdge(Source!.SymbolId, Member!.SymbolId),
            ViolationKind.Shape => BaselineEntry.ForSubject(Subject!.SymbolId),
            ViolationKind.MemberShape => BaselineEntry.ForSubject(SubjectMember!.SymbolId),
            // A list-valued verb keys each item apart, so a migrated rule still reds on the next package or
            // framework; a verb about one fact of the project keys the project alone.
            ViolationKind.ProjectShape => ProjectItem is { } item
                ? BaselineEntry.ForEdge(SubjectProject!.SymbolId, item)
                : BaselineEntry.ForSubject(SubjectProject!.SymbolId),
            _ => null
        };
    }

    internal static Violation Reference(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Reference, source, target, sites);
    }

    /// <summary>
    ///     A reference violation carrying the circle its pair lies on — what
    ///     <c>MustNotHaveCircularReferences</c> mints (GRAMMAR §5.3). Identical to
    ///     <see cref="Reference(TypeNode, TypeNode, IReadOnlyList{SourceLocation})" /> in kind, identity and
    ///     order key, so the baseline, the human report and SARIF are untouched; <see cref="Detail" /> is
    ///     the JSON channel's addition alone.
    /// </summary>
    internal static Violation Reference(
        TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites, string detail)
    {
        return new Violation(ViolationKind.Reference, sites) { Source = source, Target = target, Detail = detail };
    }

    /// <summary>
    ///     Labels a just-minted violation with the family cell whose law it breaks, and hands it back so a
    ///     per-cell walk can annotate at the mint site. A null <paramref name="cell" /> labels nothing, which
    ///     is what the same walks passing through for a plain subject want.
    /// </summary>
    /// <remarks>
    ///     A step of its own rather than a parameter on each factory, because the cell is orthogonal to the
    ///     kind and would arrive beside a string the mint already takes — the cycle gate's circle detail — and
    ///     two strings in a row is the transposition the factories above are shaped to prevent. Mutating here
    ///     is safe for the same reason the private setters are: only this class can reach them, and a walk
    ///     labels the instance it has just made, before any consumer has seen it.
    /// </remarks>
    internal Violation InCell(string? cell)
    {
        Cell = cell;
        return this;
    }

    internal static Violation Construction(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Construction, source, target, sites);
    }

    internal static Violation Injection(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Injection, source, target, sites);
    }

    internal static Violation Catch(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Catch, source, target, sites);
    }

    internal static Violation Throw(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Throw, source, target, sites);
    }

    internal static Violation Expose(TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return Edge(ViolationKind.Expose, source, target, sites);
    }

    internal static Violation MemberUse(TypeNode source, MemberReference member, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.MemberUse, sites) { Source = source, Member = member };
    }

    internal static Violation Shape(TypeNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.Shape, sites) { Subject = subject };
    }

    internal static Violation MemberShape(MemberNode subjectMember, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.MemberShape, sites) { SubjectMember = subjectMember };
    }

    /// <summary>
    ///     A subject project failing a packaging or escape verb (GRAMMAR §4.10), evidenced by the offending
    ///     fact's declaration sites — which may be empty, because a fact nothing declared has nowhere to
    ///     point and an unlocated finding is more honest than a made-up one.
    /// </summary>
    internal static Violation ProjectShape(ProjectNode subject, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.ProjectShape, sites) { SubjectProject = subject };
    }

    /// <summary>
    ///     One declared package reference counting against a package verb, sited at the reference's own
    ///     declaration — which may be a props file above the project. Shares <see cref="ProjectShape" />'s
    ///     kind; <see cref="Package" /> is what tells the two apart, and what keys its identity per package.
    /// </summary>
    internal static Violation ProjectPackage(ProjectNode subject, PackageReference package)
    {
        return new Violation(ViolationKind.ProjectShape, [package.Site])
        {
            SubjectProject = subject,
            Package = package,
            ProjectItemName = package.Name,
            ProjectItem = PackageItemPrefix + package.Name
        };
    }

    /// <summary>
    ///     One target framework counting against <c>MustOnlyTarget</c>, as its short moniker, sited at the
    ///     declaration of the project's framework list. Shares <see cref="ProjectShape" />'s kind;
    ///     <see cref="Framework" /> keys its identity per framework.
    /// </summary>
    internal static Violation ProjectFramework(ProjectNode subject, string framework, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.ProjectShape, sites)
        {
            SubjectProject = subject,
            Framework = framework,
            ProjectItemName = framework,
            ProjectItem = FrameworkItemPrefix + framework
        };
    }

    /// <summary>
    ///     The target framework a <c>MustTarget</c> rule requires and the project does not target, as its short
    ///     moniker, sited at the declaration of the project's framework list. Keyed per framework as
    ///     <see cref="ProjectFramework" /> is; <see cref="FrameworkMissing" /> is what the wording reads to say the
    ///     project lacks it.
    /// </summary>
    internal static Violation ProjectMissingFramework(
        ProjectNode subject, string framework, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(ViolationKind.ProjectShape, sites)
        {
            SubjectProject = subject,
            Framework = framework,
            FrameworkMissing = true,
            ProjectItemName = framework,
            ProjectItem = FrameworkItemPrefix + framework
        };
    }

    /// <summary>
    ///     One evaluation of a project in which <paramref name="property" /> is unset or holds another value than a
    ///     <c>MustSetProperty</c> rule asks, sited at the definition that won, or at the project file where nothing
    ///     set it. Shares <see cref="ProjectShape" />'s kind; <see cref="Property" /> with the evaluation's
    ///     framework keys its identity.
    /// </summary>
    internal static Violation ProjectProperty(
        ProjectNode subject, string property, ProjectEvaluation evaluation, EvaluatedProperty? found)
    {
        SourceLocation site = found?.Site ?? evaluation.ProjectFile;
        // "Nullable@net8.0", or the property alone for a build no framework was told apart in. The framework is part
        // of the item, not evidence: two frameworks that both lack a property share the project-file site, so an
        // identity that left the framework out would let a migrated rule pass the next framework that loses the
        // property.
        string item = evaluation.TargetFramework is { } framework ? $"{property}@{framework}" : property;
        return new Violation(ViolationKind.ProjectShape, [site])
        {
            SubjectProject = subject,
            Property = property,
            Framework = evaluation.TargetFramework,
            PropertyValue = found?.Value,
            ProjectItemName = item,
            ProjectItem = PropertyItemPrefix + item
        };
    }

    internal static Violation EmptySubject(string detail, string hint)
    {
        return new Violation(ViolationKind.EmptySubject, Array.Empty<SourceLocation>())
        {
            Detail = detail, Hint = hint
        };
    }

    internal static Violation RuleError(string detail)
    {
        return new Violation(ViolationKind.RuleError, Array.Empty<SourceLocation>()) { Detail = detail };
    }

    // The one mint the six edge factories share: an edge violation is a (source, target) pair with its
    // sites, and every other slot empty.
    private static Violation Edge(
        ViolationKind kind, TypeNode source, TypeNode target, IReadOnlyList<SourceLocation> sites)
    {
        return new Violation(kind, sites) { Source = source, Target = target };
    }
}
