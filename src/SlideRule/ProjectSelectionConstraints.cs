using SlideRule.Fluent;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.Validation;
using static SlideRule.Internal.Guard;

namespace SlideRule;

/// <summary>
///     The verbs that finish a <see cref="ProjectSelection" /> — the packaging rules: what a project
///     targets, what it takes from the package feed, whether its restore is pinned, and whether it
///     ships. Each returns the <see cref="Constraint" /> to hand to <c>Enforce</c> or <c>Migrate</c>.
///     What they read is what MSBuild evaluation answered after SDK defaults and every import, not
///     what the project file's own text says, so a setting a shared props file above the project
///     supplies is a fact these verbs see. A violation is sited at the declaration that won the
///     evaluation, regularly that props file rather than the project itself. Every verb passes a
///     project whose fact nothing evaluated.
/// </summary>
// Negation lives in the verb name, never in a Not() combinator (GRAMMAR §2), and every verb here
// holds §4.10's tri-state honesty rule: a fact nothing evaluated is null, and unknown passes.
public static class ProjectSelectionConstraints
{
    /// <summary>
    ///     States that every selected project must target only these frameworks, such as
    ///     <c>arch.Projects.Packable().MustOnlyTarget("netstandard2.0")</c> or
    ///     <c>MustOnlyTarget("netstandard2.0", "net8.0")</c>. Each framework a project declares outside
    ///     the list is one violation, and under <c>Migrate</c> each is grandfathered on its own, so a
    ///     framework the project adds after the baseline was captured fails the check. Monikers are
    ///     compared case-sensitively against normalized short forms, so a classic project's <c>v4.8</c>
    ///     reads <c>net48</c>. A project whose frameworks nothing evaluated passes. At least one moniker is
    ///     required, and a blank one is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustOnlyTarget(
        this ProjectSelection subject,
        [SpecSlot(nameof(SpecSlot.TargetFramework))]
        string first,
        [SpecSlot(nameof(SpecSlot.TargetFramework))]
        params string[] more)
    {
        IReadOnlyList<string> frameworks = OperandList.OneOrMore(first, more, framework => framework);
        return new MustOnlyTargetConstraint(Subject(subject), frameworks);
    }

    /// <summary>
    ///     States that every selected project must target this framework, among any others it targets, such
    ///     as <c>arch.Projects.Packable().MustTarget("netstandard2.0")</c>: the floor a support promise
    ///     makes, where <c>MustOnlyTarget</c> states the ceiling. A project that does not target it is one
    ///     violation, sited at the declaration of the project's framework list, and under <c>Migrate</c> each
    ///     project is grandfathered on its own. The moniker is compared case-sensitively against normalized
    ///     short forms, so a classic project's <c>v4.8</c> reads <c>net48</c>. A project whose frameworks
    ///     nothing evaluated passes. One framework per rule: to require two, write two rules. A blank moniker
    ///     is reported when the spec is loaded.
    /// </summary>
    public static Constraint MustTarget(
        this ProjectSelection subject,
        [SpecSlot(nameof(SpecSlot.TargetFramework))]
        string framework)
    {
        return new MustTargetConstraint(Subject(subject), NotNull(framework, nameof(framework)));
    }

    /// <summary>
    ///     States that every selected project must declare no <c>PackageReference</c> other than private
    ///     ones, such as <c>arch.Projects.Named("MyApp.Domain").MustReferenceNoPackages()</c>. A private
    ///     reference is one whose <c>PrivateAssets</c> is <c>all</c>, the usual way to declare an analyzer,
    ///     a source generator or a build-time tool: it is neither listed as a dependency of the project's
    ///     own package nor passed on to a project that references it, so it passes.
    ///     <c>IncludeAssets</c> and <c>ExcludeAssets</c> do not make a reference private, and a package
    ///     declared more than once is private only where every declaration is. Every other package a
    ///     project declares is one violation, sited at its own declaration, and under <c>Migrate</c> each
    ///     is grandfathered on its own, so a package the project adds after the baseline was captured fails
    ///     the check. Only what a project declares itself is seen: a package arriving through a project
    ///     reference is not, and the rule's sentence in the generated agent context says so. To forbid
    ///     private references as well, state the strict form with <c>Must</c>:
    ///     <c>arch.Projects.Must(p =&gt; p.PackageReferences.Count == 0, description: "declare no packages")</c>.
    /// </summary>
    public static Constraint MustReferenceNoPackages(this ProjectSelection subject)
    {
        return new MustReferenceNoPackagesConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected project's restore must write a lock file (the evaluated
    ///     <c>RestorePackagesWithLockFile</c>), so a resolved package graph cannot drift between machines.
    ///     A project that evaluates it false fails the check, and a project whose value nothing evaluated
    ///     passes.
    /// </summary>
    public static Constraint MustLockPackages(this ProjectSelection subject)
    {
        return new MustLockPackagesConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that no selected project may produce a NuGet package — the evaluated
    ///     <c>IsPackable</c>. The SDK defaults it on, so this is the verb that makes an internal project's
    ///     opt-out checkable rather than assumed. A project that evaluates it true fails the check, and a
    ///     project whose value nothing evaluated passes.
    /// </summary>
    public static Constraint MustNotBePackable(this ProjectSelection subject)
    {
        return new MustNotBePackableConstraint(Subject(subject));
    }

    /// <summary>
    ///     States that every selected project must satisfy a predicate of your own, for what the project verbs
    ///     cannot say:
    ///     <c>arch.Projects.Must(p =&gt; p.ProjectReferences.Count == 0, description: "reference no projects")</c>.
    ///     The predicate reads the facts on
    ///     <see cref="IProjectInfo" /> and runs against each selected project when the check runs, never
    ///     when the spec is loaded; each project it returns <see langword="false" /> for fails the check,
    ///     reported without a site, because no one declaration in a project is the one a predicate failed
    ///     on. A fact nothing evaluated is <see langword="null" /> there, so a predicate that reads null
    ///     as false asserts something nothing measured. <paramref name="description" /> is required and
    ///     completes the phrase "must ..." as a bare-infinitive verb phrase; it is rendered verbatim in
    ///     the generated agent context and in the check report, and nothing checks that it describes what
    ///     the predicate does. A blank or multi-line description is reported when the spec is loaded.
    /// </summary>
    public static Constraint Must(
        this ProjectSelection subject,
        Func<IProjectInfo, bool> predicate,
        [SpecSlot(nameof(SpecSlot.Description))]
        string description)
    {
        return new ProjectMustConstraint(Subject(subject), NotNull(predicate, nameof(predicate)), NotNull(description, nameof(description)));
    }

    private static ProjectSelection Subject(ProjectSelection subject)
    {
        return NotNull(subject, nameof(subject));
    }
}
