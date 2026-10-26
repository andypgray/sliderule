using Shouldly;
using SlideRule.Fluent;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Prose;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Stubs;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The reified read model: the rules a spec builds, in post-desugar order, with their postures,
///     Migrate defaults (GRAMMAR §4.4) and scope desugarings (GRAMMAR §7), and the node each verb reifies
///     to, with the walk hooks it fills.
/// </summary>
public class ModelReificationTests
{
    private const string DragonsProse =
        "Banker's rounding happens at line-item level, NOT invoice level. " +
        "Nightly reconciliation depends on this. Do not normalize.";

    // A quarantined scope documented via .DragonsDoc(...) rather than inline .Dragons(...).
    private static readonly IArchitectureSpec DragonsDocScopeSpec = new InlineSpec(arch =>
        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade))
            .DragonsDoc("arch/billing-dragons.md")
            .Because("Replacement scheduled; see the linked doc."));

    // A cautioned scope carrying both dragons forms, so its one child's payload can be pinned whole.
    private static readonly IArchitectureSpec CautionScopeSpec = new InlineSpec(arch =>
        arch.Scope("shared/utilities")
            .Caution(arch.Namespace("MyApp.Shared.*"))
            .Dragons(DragonsProse)
            .DragonsDoc("arch/utilities-dragons.md")
            .Because("The helpers are public API for the whole solution."));

    // One Enforce rule and one Migrate rule that cite their page — the two stage types the trailer sits on.
    private static readonly IArchitectureSpec CitedRuleSpec = new InlineSpec(arch =>
    {
        arch.Rule("http/reuse-httpclient")
            .Enforce(arch.Types.MustNotConstruct(typeof(SqlConnection)))
            .Because("A new client per call exhausts sockets.")
            .Citation("https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
        arch.Rule("async/no-blocking-waits")
            .Migrate(
                from: "Handlers block on the task.",
                to: arch.Types.MustNotConstruct(typeof(SqlConnection)))
            .Because("Blocking a pooled thread on an incomplete task deadlocks under load.")
            .Citation("https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios");
    });

    private static ArchRule Rule(string id)
    {
        return Checker.Canonical
            .Rule(id);
    }

    // The containment child of a one-scope spec whose sanctioned surface the caller spells.
    private static ArchRule BoundedContainment(Func<Arch, IQuarantinedScope, IQuarantinedScope> boundary)
    {
        return Checker.Model(arch =>
            {
                IQuarantinedScope scope = arch.Scope("legacy/billing")
                    .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"));

                boundary(arch, scope)
                    .Dragons(DragonsProse)
                    .Because("Replacement scheduled.");
            })
            .Rule("legacy/billing/containment");
    }

    [Fact]
    public void Build_CanonicalSample_ReifiesNineRulesInPinnedOrder()
    {
        // Nine post-desugar rules from eight statements: the quarantine mints two children, the caution one.
        Checker.Canonical
            .Rules.Select(rule => rule.Id)
            .ShouldBe(
            [
                "layering/domain-independent",
                "naming/interfaces",
                "data-access/no-inline-sql",
                "legacy/billing/containment",
                "legacy/billing/tripwire",
                "domain/pricing/tripwire",
                "naming/handlers",
                "di/handlers-via-registry",
                "style/type-name-length"
            ]);
    }

    [Theory]
    [InlineData("layering/domain-independent", nameof(Posture.Enforce))]
    [InlineData("naming/interfaces", nameof(Posture.Enforce))]
    [InlineData("data-access/no-inline-sql", nameof(Posture.Migrate))]
    [InlineData("legacy/billing/containment", nameof(Posture.Quarantine))]
    [InlineData("legacy/billing/tripwire", nameof(Posture.Quarantine))]
    [InlineData("domain/pricing/tripwire", nameof(Posture.Caution))]
    [InlineData("naming/handlers", nameof(Posture.Enforce))]
    [InlineData("di/handlers-via-registry", nameof(Posture.Enforce))]
    [InlineData("style/type-name-length", nameof(Posture.Enforce))]
    public void Build_CanonicalSample_AssignsPostures(string id, string postureName)
    {
        // By name, because Posture is internal and a public theory cannot take it as a parameter.
        var posture = Enum.Parse<Posture>(postureName);

        Rule(id)
            .Posture.ShouldBe(posture);
    }

    [Fact]
    public void EnforceRule_CarriesBecauseFixAndNoPostureData()
    {
        ArchRule rule = Rule("layering/domain-independent");

        rule.Because.ShouldBe("Domain is UI-agnostic; transaction boundaries live in services.");
        rule.Fix.ShouldBe("Define an abstraction in Domain and implement it in Web.");
        rule.Constraint.ShouldNotBeNull();
        rule.Migrate.ShouldBeNull();
        rule.Scope.ShouldBeNull();
    }

    [Fact]
    public void EnforceRule_WithoutFix_LeavesFixNull()
    {
        Rule("naming/interfaces")
            .Fix.ShouldBeNull();
    }

    [Fact]
    public void EnforceRule_WithCitation_CarriesTheUrl()
    {
        ArchModelBuilder.Build(CitedRuleSpec)
            .Rule("http/reuse-httpclient")
            .Citation.ShouldBe("https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
    }

    [Fact]
    public void MigrateRule_WithCitation_CarriesTheUrl()
    {
        ArchModelBuilder.Build(CitedRuleSpec)
            .Rule("async/no-blocking-waits")
            .Citation.ShouldBe("https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/async-scenarios");
    }

    [Fact]
    public void Rule_WithoutCitation_LeavesCitationNull()
    {
        Rule("naming/interfaces")
            .Citation.ShouldBeNull();
    }

    [Fact]
    public void MigrateRule_ReifiesFromToBaselineAndPolicy()
    {
        ArchRule rule = Rule("data-access/no-inline-sql");

        MigrateData migrate = rule.Migrate.ShouldNotBeNull();
        migrate.From.ShouldBe("Controllers open SqlConnection directly (legacy Active Record style).");
        migrate.ToSentence.ShouldBe("Types in the Web layer named `*Controller` must not reference `SqlConnection`.");
        migrate.ToSentence.ShouldBe(rule.Sentence);
        migrate.BaselinePath.ShouldBe("arch/baseline.json");
        migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void MigrateRule_WithoutOptions_DefaultsBaselineToConventionalPathAndPolicyMigrateIfSmall()
    {
        // A Migrate rule with neither .Baseline(...) nor .WhileYoureThere(...) — exercises the defaults.
        ArchRule rule = Checker.Model(arch =>
            {
                Layer web = arch.Layer("Web", "MyApp.Web.*");
                arch.Rule("data-access/no-inline-sql")
                    .Migrate(
                        "Controllers open SqlConnection directly.",
                        web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
                    .Because("Repository pattern for testability.");
            })
            .Rules.ShouldHaveSingleItem();

        rule.Posture.ShouldBe(Posture.Migrate);
        MigrateData migrate = rule.Migrate.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the rule ID (GRAMMAR §4.4).
        migrate.BaselinePath.ShouldBe("arch/baselines/data-access/no-inline-sql.json");
        migrate.Policy.ShouldBe(MigrationPolicy.MigrateIfSmall);
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoContainmentCarryingBoundaryBaselineDragonsAndAutoFix()
    {
        ArchRule containment = Rule("legacy/billing/containment");

        ScopeData quarantine = containment.Scope.ShouldNotBeNull();
        quarantine.Role.ShouldBe(ScopeRole.Containment);
        quarantine.Surface.ShouldBe(["`IBillingFacade`", "`BillingFacade`"]);
        quarantine.BaselinePath.ShouldBe("arch/baseline.json");
        quarantine.Dragons.ShouldBe(DragonsProse);
        quarantine.ScopeId.ShouldBe("legacy/billing");
        containment.Constraint.ShouldNotBeNull();
        // Auto-derived fix from the first BoundaryOnlyVia type (GRAMMAR §5.5).
        containment.Fix.ShouldBe("use `IBillingFacade`");
        // The raw quarantined selection rides on the containment child so the renderer can place it.
        quarantine.Scoped.ShouldNotBeNull();
    }

    [Fact]
    public void QuarantineScope_DesugarsIntoTripwireCarryingDragonsButNoConstraint()
    {
        ArchRule tripwire = Rule("legacy/billing/tripwire");

        ScopeData quarantine = tripwire.Scope.ShouldNotBeNull();
        quarantine.Role.ShouldBe(ScopeRole.Tripwire);
        quarantine.Dragons.ShouldBe(DragonsProse);
        quarantine.ScopeId.ShouldBe("legacy/billing");
        // Boundary and baseline are containment concerns; the tripwire has no closed-vocabulary law.
        quarantine.Surface.ShouldBeEmpty();
        quarantine.BaselinePath.ShouldBeNull();
        // The quarantined selection rides on the tripwire too so its diff-touch can map changed files.
        quarantine.Scoped.ShouldNotBeNull();
        tripwire.Constraint.ShouldBeNull();
        tripwire.Sentence.ShouldBe(string.Empty);
    }

    [Fact]
    public void CautionScope_DesugarsIntoTheTripwireAlone()
    {
        // One rule, not two with an inert half: a caution declares no containment law, so there is nothing
        // for a second child to hold.
        ArchModelBuilder.Build(CautionScopeSpec)
            .Rules.Select(rule => rule.Id)
            .ShouldBe(["shared/utilities/tripwire"]);
    }

    [Fact]
    public void CautionScope_TripwireCarriesTheScopePayloadAndNoLaw()
    {
        ArchRule tripwire = ArchModelBuilder.Build(CautionScopeSpec)
            .Rule("shared/utilities/tripwire");

        tripwire.Posture.ShouldBe(Posture.Caution);
        tripwire.Because.ShouldBe("The helpers are public API for the whole solution.");
        tripwire.Constraint.ShouldBeNull();
        tripwire.Sentence.ShouldBe(string.Empty);
        tripwire.Fix.ShouldBeNull();
        tripwire.BaselinePath.ShouldBeNull();

        ScopeData caution = tripwire.Scope.ShouldNotBeNull();
        caution.Role.ShouldBe(ScopeRole.Tripwire);
        caution.ScopeId.ShouldBe("shared/utilities");
        caution.Dragons.ShouldBe(DragonsProse);
        caution.DragonsDoc.ShouldBe("arch/utilities-dragons.md");
        // Boundary and baseline are containment concerns, and a caution has no containment at all.
        caution.Surface.ShouldBeEmpty();
        caution.BaselinePath.ShouldBeNull();
        // The scoped selection rides on the tripwire so the renderer can place the card and the diff-touch
        // can map changed files — the same two jobs it does on a quarantine's tripwire.
        caution.Scoped.ShouldNotBeNull();
    }

    [Fact]
    public void QuarantineScope_NamespaceBoundary_SanctionsARegionAndNamesItInTheFix()
    {
        ArchRule containment = BoundedContainment((arch, scope) =>
            scope.BoundaryOnlyVia(arch.Namespace("MyApp.Legacy.Billing.Contracts.*")));

        containment.Scope.ShouldNotBeNull()
            .Surface.ShouldBe(["types in `MyApp.Legacy.Billing.Contracts.*`"]);
        containment.Fix.ShouldBe("use types in `MyApp.Legacy.Billing.Contracts.*`");
    }

    [Fact]
    public void QuarantineScope_NamedBoundary_SanctionsASurfaceTheSpecNeverLoads()
    {
        // The no-load spelling (GRAMMAR §7): no typeof, so no reference to the product assembly and no
        // [InternalsVisibleTo] for an internal facade.
        ArchRule containment = BoundedContainment((arch, scope) => scope.BoundaryOnlyVia(arch.Types.Named("BillingFacade")));

        containment.Scope.ShouldNotBeNull()
            .Surface.ShouldBe(["types named `BillingFacade`"]);
        containment.Fix.ShouldBe("use types named `BillingFacade`");
    }

    [Fact]
    public void QuarantineScope_TypeBoundary_IsSugarForTheSameSelectionModel()
    {
        ArchRule sugar = BoundedContainment((_, scope) => scope.BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade)));
        ArchRule spelled = BoundedContainment((arch, scope) =>
            scope.BoundaryOnlyVia(arch.Type<IBillingFacade>(), arch.Type<BillingFacade>()));

        // Identical model, identical prose — the whole of what "sugar" claims (GRAMMAR §3.3).
        sugar.Sentence.ShouldBe(spelled.Sentence);
        sugar.Fix.ShouldBe(spelled.Fix);
        sugar.Scope.ShouldNotBeNull()
            .Surface.ShouldBe(spelled.Scope.ShouldNotBeNull().Surface);
        Checker.ShouldReifyAlike(sugar.Constraint!, spelled.Constraint!);
    }

    [Fact]
    public void QuarantineScope_MixedBoundary_WrapsTheTypeBesideTheSelection()
    {
        ArchRule containment = BoundedContainment((arch, scope) =>
            scope.BoundaryOnlyVia(arch.Types.Named("BillingFacade"), arch.Type<IBillingFacade>()));

        containment.Scope.ShouldNotBeNull()
            .Surface.ShouldBe(["types named `BillingFacade`", "`IBillingFacade`"]);
    }

    [Fact]
    public void QuarantineScope_SharesBecauseAcrossBothChildren()
    {
        const string because = "Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.";
        Rule("legacy/billing/containment")
            .Because.ShouldBe(because);
        Rule("legacy/billing/tripwire")
            .Because.ShouldBe(because);
    }

    [Fact]
    public void QuarantineScope_WithDragonsDoc_ReifiesLinkedDocPathOnBothChildren()
    {
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        ScopeData containment = model.Rule("legacy/billing/containment")
            .Scope.ShouldNotBeNull();
        containment.DragonsDoc.ShouldBe("arch/billing-dragons.md");
        containment.Dragons.ShouldBeNull();
        model.Rule("legacy/billing/tripwire")
            .Scope.ShouldNotBeNull()
            .DragonsDoc.ShouldBe("arch/billing-dragons.md");
    }

    [Fact]
    public void QuarantineScope_WithoutBaseline_DefaultsContainmentToConventionalPath()
    {
        // DragonsDocScopeSpec omits .Baseline, so the containment child falls back to the default.
        ArchitectureModel model = ArchModelBuilder.Build(DragonsDocScopeSpec);

        ScopeData containment = model.Rule("legacy/billing/containment")
            .Scope.ShouldNotBeNull();
        // .Baseline omitted ⇒ conventional default derived from the containment rule ID (GRAMMAR §4.4/§7).
        containment.BaselinePath.ShouldBe("arch/baselines/legacy/billing/containment.json");
        // The tripwire's baseline stays null — grandfathering is a containment concern.
        model.Rule("legacy/billing/tripwire")
            .Scope.ShouldNotBeNull()
            .BaselinePath.ShouldBeNull();
    }

    [Fact]
    public void MustNotUseRule_ReifiesToWalkableMemberConstraint()
    {
        // The flagship member-ban rule, for the walkable-model pins (Migrate posture, real members).
        ArchRule rule = Checker.Model(arch => arch.Rule("time/inject-clock")
                .Migrate(
                    "Code reads the ambient clock directly.",
                    arch.Types.MustNotUse(
                        arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                        arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Wall-clock reads are untestable; inject IClock — ADR-nnn."))
            .Rule("time/inject-clock");

        rule.Posture.ShouldBe(Posture.Migrate);
        var constraint = rule.Constraint.ShouldBeOfType<MustNotUseConstraint>();

        // Members in authoring order, each with its authored declaring type + name (GRAMMAR §4.5).
        constraint.MemberOperands.Select(member => (member.DeclaringType, member.Name))
            .ShouldBe([(typeof(DateTime), "Now"), (typeof(DateTime), "UtcNow")]);
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotConstructRule_ReifiesToWalkableConstructConstraint()
    {
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotConstruct(typeof(SqlConnection)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotConstructConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void DependencyVerbConstraint_HasEmptyMemberOperands()
    {
        // The member-operands walk hook is empty for the dependency verbs (GRAMMAR §4.5, §8 items 11–13).
        Rule("layering/domain-independent")
            .Constraint.ShouldNotBeNull()
            .MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void MemberSubjectRule_ReifiesToWalkableMemberConstraint()
    {
        // The flagship member-subject rule, for the walkable-model pins (GRAMMAR §4.6).
        ArchRule rule = Checker.OneRule(arch => arch.Namespace("MyApp.Web.*").Methods.Returning(typeof(Task))
            .MustHaveSuffix("Async"));

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MemberMustHaveSuffixConstraint>();

        // The member subject: the Methods projection carrying one Returning adjective (GRAMMAR §4.6).
        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Method);
        constraint.MemberSubject.Adjectives.OfType<ReturningAdjective>()
            .ShouldHaveSingleItem();

        // The inherited Subject is the underlying TYPE selection (Subject => MemberSubject.Source), so
        // foreign walks and Quarantine desugaring keep working on the type side.
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.Subject!.Noun.ShouldBeOfType<NamespaceNoun>();

        // Member subjects never populate MemberOperands (that hook is the MustNotUse target list).
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void PropertySubjectRule_ReifiesToWalkableMemberConstraint()
    {
        // The .Properties projection mints a PropertySelection, which is what carries the kind-only verb —
        // the reified node is the same walkable MemberConstraint shape every other member verb reifies to.
        var constraint = ShouldReifyTo<MemberMustBeGetOnlyConstraint>(arch => arch.Namespace("MyApp.Domain.*").Properties
            .MustBeGetOnly());

        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Property);
        constraint.MemberSubject.ShouldBeOfType<PropertySelection>();
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void FieldSubjectRule_ReifiesToWalkableMemberConstraint()
    {
        var constraint = ShouldReifyTo<MemberMustBeReadonlyConstraint>(arch => arch.Namespace("MyApp.Web.*").Fields
            .MustBeReadonly());

        constraint.MemberSubject.Kind.ShouldBe(MemberKindFilter.Field);
        constraint.MemberSubject.ShouldBeOfType<FieldSelection>();
        constraint.Subject.ShouldBeSameAs(constraint.MemberSubject.Source);
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void StaticAdjective_ReifiesOntoTheMemberSelection_KeepingTheConcreteType()
    {
        // The adjective lands in the member selection's adjective list, and Rebuild returns the concrete
        // type — which is what keeps the kind-only verb reachable after it, in any order.
        var constraint = ShouldReifyTo<MemberMustBeReadonlyConstraint>(arch => arch.Namespace("MyApp.Web.*").Fields.ThatAreStatic()
            .MustBeReadonly());

        constraint.MemberSubject.Adjectives.OfType<MemberThatAreStaticAdjective>()
            .ShouldHaveSingleItem();
        constraint.MemberSubject.ShouldBeOfType<FieldSelection>();
    }

    [Fact]
    public void Under_ReifiesItsGlobsAsWritten()
    {
        // No normalization: the model carries what the author wrote, case and all, for every reader to match.
        var constraint = ShouldReifyTo<MustBeInternalConstraint>(arch => arch.Types.Under("src/Console/Internal", "src/*/Generated")
            .MustBeInternal());

        constraint.Subject!.Adjectives.OfType<UnderAdjective>()
            .ShouldHaveSingleItem()
            .Globs.ShouldBe(["src/Console/Internal", "src/*/Generated"]);
    }

    [Fact]
    public void MemberExcept_ReifiesItsPayloadsAsMemberSelections_InAuthoringOrder()
    {
        // No union node on the member stratum: the adjective holds the payload list itself, each payload a
        // member selection over its own source, so a payload's kind survives for the kind-clash check.
        var constraint = ShouldReifyTo<MemberMustHaveSuffixConstraint>(arch => arch.Namespace("MyApp.*").Methods
            .Except(arch.Types.Named("Program").Methods.Named("Main"), arch.Types.Named("Startup").Members)
            .MustHaveSuffix("Async"));

        MemberExceptAdjective except = constraint.MemberSubject.Adjectives.OfType<MemberExceptAdjective>()
            .ShouldHaveSingleItem();
        except.Payloads.Select(payload => payload.Kind)
            .ShouldBe([MemberKindFilter.Method, MemberKindFilter.Any]);
        constraint.MemberSubject.ShouldBeOfType<MethodSelection>();
    }

    [Fact]
    public void AccessibilityAdjective_ReifiesOntoTheMemberSelection_CarryingItsLevel()
    {
        // One node carries all six levels, the member twin of the type-side accessibility node.
        var constraint = ShouldReifyTo<MemberMustBeProtectedConstraint>(arch => arch.Namespace("MyApp.Web.*").Fields.ThatArePrivate()
            .MustBeProtected());

        constraint.MemberSubject.Adjectives.OfType<MemberAccessibilityAdjective>()
            .ShouldHaveSingleItem()
            .Accessibility.ShouldBe(Accessibility.Private);
        constraint.MemberSubject.ShouldBeOfType<FieldSelection>();
    }

    [Fact]
    public void ProjectSubjectRule_ReifiesToWalkableProjectConstraintWithNoTypeSubject()
    {
        // The third subject stratum (GRAMMAR §4.10). Its ProjectSubject carries the selection, and the
        // inherited type Subject is NULL — there is no underlying type selection to hand up, which is the
        // whole point of departure from a member constraint. Every type-side walk therefore reaches nothing
        // here, and the nullable-flow analysis is what audits that each of them dispatched first.
        var constraint = ShouldReifyTo<MustNotBePackableConstraint>(arch => arch.Projects.Matching("Zphil.*")
            .MustNotBePackable());

        constraint.Subject.ShouldBeNull();
        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        constraint.ProjectSubject.Adjectives.OfType<ProjectMatchingAdjective>()
            .ShouldHaveSingleItem()
            .Globs.ShouldBe(["Zphil.*"]);
    }

    [Fact]
    public void ProjectAdjectives_ReifyOntoTheProjectSelectionInAuthoringOrder()
    {
        // Each adjective appends and clones, so the list is the chain in authoring order — the same
        // immutable-value shape the type and member selections take.
        var constraint = ShouldReifyTo<MustLockPackagesConstraint>(arch => arch.Projects.Packable()
            .Named("SlideRule", "SlideRule.Cli")
            .Except(arch.Projects.Matching("*.Tests"))
            .MustLockPackages());

        constraint.ProjectSubject.Adjectives.Select(adjective => adjective.GetType()
                .Name)
            .ShouldBe(["ProjectPackableAdjective", "ProjectNamedAdjective", "ProjectExceptAdjective"]);
    }

    [Fact]
    public void ProjectVerbOperands_ReifyOntoTheirOwnConstraintNodes()
    {
        // The two operand-carrying project verbs publish their operands as their own domain-named lists —
        // never through the type-side Operands hook, which stays empty because a project verb names no
        // type selection at all.
        var target = ShouldReifyTo<MustOnlyTargetConstraint>(arch => arch.Projects.Named("SlideRule")
            .MustOnlyTarget("netstandard2.0", "net8.0"));

        target.Frameworks.ShouldBe(["netstandard2.0", "net8.0"]);
        target.Operands.ShouldBeEmpty();

        var floor = ShouldReifyTo<MustTargetConstraint>(arch => arch.Projects.Packable().MustTarget("netstandard2.0"));

        floor.Framework.ShouldBe("netstandard2.0");
        floor.Operands.ShouldBeEmpty();

        var allowed = ShouldReifyTo<MustOnlyReferencePackagesConstraint>(arch => arch.Projects.Named("SlideRule")
            .MustOnlyReferencePackages("Microsoft.Bcl.*", "System.Memory"));

        allowed.Patterns.ShouldBe(["Microsoft.Bcl.*", "System.Memory"]);
        allowed.Operands.ShouldBeEmpty();

        var banned = ShouldReifyTo<MustNotReferencePackagesConstraint>(arch => arch.Projects.Named("SlideRule")
            .MustNotReferencePackages("GreenPipes"));

        banned.Patterns.ShouldBe(["GreenPipes"]);
        banned.Operands.ShouldBeEmpty();

        var property = ShouldReifyTo<MustSetPropertyConstraint>(arch => arch.Projects.Named("SlideRule")
            .MustSetProperty("Nullable", "enable"));

        property.Name.ShouldBe("Nullable");
        property.Value.ShouldBe("enable");
        property.Operands.ShouldBeEmpty();

        var must = ShouldReifyTo<ProjectMustConstraint>(arch => arch.Projects.Named("SlideRule")
            .Must(project => project.IsPackable == true, description: "produce a package"));

        must.Description.ShouldBe("produce a package");
        must.Subject.ShouldBeNull();
    }

    [Fact]
    public void RegisteredNoun_WithLifetime_ReifiesToInjectConstraintCarryingLifetimes()
    {
        // arch.Registered(Lifetime.X) reifies to a RegisteredNoun carrying that lifetime; MustNotInject
        // reifies to a MustNotInjectConstraint whose Operands mirror its Targets (GRAMMAR §4.7).
        // The captive-dependency flagship: singleton-registered types must not inject scoped/transient ones.
        var constraint = ShouldReifyTo<MustNotInjectConstraint>(arch => arch.Registered(Lifetime.Singleton)
            .MustNotInject(arch.Registered(Lifetime.Scoped), arch.Registered(Lifetime.Transient)));

        constraint.Subject!.Noun.ShouldBeOfType<RegisteredNoun>()
            .Lifetime.ShouldBe(Lifetime.Singleton);
        constraint.Operands.ShouldBe(constraint.Targets);
        constraint.Targets.Select(target => ((RegisteredNoun)target.Noun).Lifetime)
            .ShouldBe([Lifetime.Scoped, Lifetime.Transient]);
        // MustNotInject is a dependency-shape verb (overrides Operands, not MemberOperands).
        constraint.MemberOperands.ShouldBeEmpty();
    }

    [Fact]
    public void RegisteredNoun_NoArg_ReifiesWithNullLifetime()
    {
        // arch.Registered() reifies to a RegisteredNoun with a null lifetime (any lifetime).
        var constraint = ShouldReifyTo<MustNotInjectConstraint>(arch => arch.Registered()
            .MustNotInject(arch.Registered(Lifetime.Scoped)));
        constraint.Subject!.Noun.ShouldBeOfType<RegisteredNoun>()
            .Lifetime.ShouldBeNull();
    }

    [Fact]
    public void MustNotInject_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotInject(typeof(SqlConnection)),
            arch => arch.Types.MustNotInject(arch.Type(typeof(SqlConnection))));
    }

    [Fact]
    public void MustNotCatchRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatch-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotCatch(typeof(InvalidOperationException)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotCatchConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotCatchUnfilteredRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotCatchUnfiltered-rule spec, for the dependency-verb reification + empty-member-hook
        // pins. The filter condition lives in the verb, so the node's shape is the plain catch verb's — no extra
        // operand carries it, and the member hook stays empty.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotCatchUnfiltered(typeof(Exception)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotCatchUnfilteredConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotSwallowRule_ReifiesToWalkableCatchConstraint()
    {
        // A single MustNotSwallow-rule spec, for the dependency-verb reification + empty-member-hook pins. Both
        // the filter condition and the rethrow condition live in the verb, so the node's shape is the plain catch
        // verb's — no extra operand carries either, and the member hook stays empty.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotSwallow(typeof(Exception)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotSwallowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustNotThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotThrow(typeof(Exception)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotThrowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustOnlyThrowRule_ReifiesToWalkableThrowConstraint()
    {
        // A single MustOnlyThrow-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustOnlyThrow(typeof(InvalidOperationException)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustOnlyThrowConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotCatch_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotCatch(typeof(InvalidOperationException)),
            arch => arch.Types.MustNotCatch(arch.Type(typeof(InvalidOperationException))));
    }

    [Fact]
    public void MustNotCatchUnfiltered_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotCatchUnfiltered(typeof(Exception)),
            arch => arch.Types.MustNotCatchUnfiltered(arch.Type(typeof(Exception))));
    }

    [Fact]
    public void MustNotSwallow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotSwallow(typeof(Exception)),
            arch => arch.Types.MustNotSwallow(arch.Type(typeof(Exception))));
    }

    [Fact]
    public void MustNotThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotThrow(typeof(Exception)),
            arch => arch.Types.MustNotThrow(arch.Type(typeof(Exception))));
    }

    [Fact]
    public void MustOnlyThrow_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustOnlyThrow(typeof(InvalidOperationException)),
            arch => arch.Types.MustOnlyThrow(arch.Type(typeof(InvalidOperationException))));
    }

    [Fact]
    public void MustNotExposeRule_ReifiesToWalkableExposeConstraint()
    {
        // A single MustNotExpose-rule spec, for the dependency-verb reification + empty-member-hook pins.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustNotExpose(typeof(SqlConnection)));

        rule.ShouldReifyToWalkableDependencyConstraint<MustNotExposeConstraint>(constraint => constraint.Targets);
    }

    [Fact]
    public void MustNotExpose_TypeSugar_ReifiesIdenticallyToWrappedSelection()
    {
        Checker.ShouldReifyAlike(
            arch => arch.Types.MustNotExpose(typeof(SqlConnection)),
            arch => arch.Types.MustNotExpose(arch.Type(typeof(SqlConnection))));
    }

    // ---- Membership and coverage verbs (GRAMMAR §5.3, §4.1, §4.7): one operand-carrying node, one
    //      string-carrying node and one nullary node ----

    [Fact]
    public void MustBelongToRule_ReifiesToWalkableMembershipConstraint()
    {
        // The coverage verb stores its memberships on the shared operand list, so the generic walks reach
        // them with no special-casing, and Memberships is a domain-named alias over that one list rather
        // than a second copy — which is what asserting Operands against it holds.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustBelongTo(arch.Namespace("MyApp.Domain.*")));

        rule.ShouldReifyToWalkableDependencyConstraint<MustBelongToConstraint>(constraint => constraint.Memberships);
    }

    [Fact]
    public void MustBelongTo_ExplicitTypeMembership_RoundTripsThroughTheOperandList()
    {
        // The verb has no bare-Type sugar twin (GRAMMAR §5.3: a bare typeof membership would read as "must
        // be that type"), so a type membership is written out as a selection. That spelling is legal and
        // reifies like any other membership, which is what this row pins.
        var constraint = ShouldReifyTo<MustBelongToConstraint>(arch => arch.Types
            .MustBelongTo(arch.Type(typeof(IBillingFacade)), arch.Namespace("MyApp.Domain.*")));

        // Memberships in authoring order, each carrying its own noun.
        constraint.Memberships.Count.ShouldBe(2);
        constraint.Memberships[0]
            .Noun.ShouldBeOfType<TypeNoun>()
            .Anchor.Type.ShouldBe(typeof(IBillingFacade));
        constraint.Memberships[1]
            .Noun.ShouldBeOfType<NamespaceNoun>()
            .Glob.ShouldBe("MyApp.Domain.*");
        constraint.Operands.ShouldBe(constraint.Memberships);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpartRule_ReifiesToWalkableCorrespondenceConstraint()
    {
        // The correspondence verb stores its one among selection on the shared operand list, exactly as the
        // coverage verb stores its memberships — so the generic walks (foreign-Arch validation, the pattern
        // walk, Quarantine desugaring) reach it with no arm of their own, and the template rides the node.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustHaveExactlyOneCounterpart(
            among: arch.Namespace("MyApp.Contracts.*"), named: "I{Name}"));

        rule.ShouldReifyToWalkableDependencyConstraint<MustHaveExactlyOneCounterpartConstraint>(constraint => constraint.Among);
    }

    [Fact]
    public void MustHaveExactlyOneCounterpart_AmongAndTemplate_RoundTripThroughTheOperandList()
    {
        // One among selection, never a params list (GRAMMAR §5.3): candidate homes are unioned with
        // arch.AnyOf, which reifies as one operand, so Among is asserted against Operands rather than beside
        // them. The template is stored verbatim: substitution happens at check time, never at mint.
        var constraint = ShouldReifyTo<MustHaveExactlyOneCounterpartConstraint>(arch => arch.Types
            .MustHaveExactlyOneCounterpart(among: arch.Namespace("MyApp.Contracts.*"), named: "I{Name}"));

        constraint.Among.ShouldHaveSingleItem()
            .Noun.ShouldBeOfType<NamespaceNoun>()
            .Glob.ShouldBe("MyApp.Contracts.*");
        constraint.Template.ShouldBe("I{Name}");
        constraint.Operands.ShouldBe(constraint.Among);
    }

    [Fact]
    public void MustResideInProjectRule_ReifiesToWalkableProjectConstraint()
    {
        // A string-carrying shape verb: the project name rides on the node itself, so the rule names no
        // selection beyond its subject and both walk hooks stay empty.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustResideInProject("MyApp.Web"));

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustResideInProjectConstraint>();

        constraint.ProjectName.ShouldBe("MyApp.Web");
        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void MustBeRegisteredRule_ReifiesToWalkableNullaryConstraint()
    {
        // The nullary shape verb: its membership is an extracted fact rather than an authored operand, so
        // there is nothing on the node for a walk to reach and the subject is the only selection the rule
        // names — the node's whole payload is which verb it is.
        ArchRule rule = Checker.OneRule(arch => arch.Types.MustBeRegistered());

        rule.Posture.ShouldBe(Posture.Enforce);
        var constraint = rule.Constraint.ShouldBeOfType<MustBeRegisteredConstraint>();

        constraint.Operands.ShouldBeEmpty();
        constraint.MemberOperands.ShouldBeEmpty();
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>();
        constraint.Subject.Adjectives.ShouldBeEmpty();
    }

    // ---- Surface union: arch.AnyOf reification (GRAMMAR §5.1) ----

    [Fact]
    public void AnyOf_NestedUnion_FlattensAtMint()
    {
        // Prose and evaluation both read one leaf list: AnyOf(AnyOf(a, b), c) ≡ AnyOf(a, b, c), operand
        // order preserved.
        var arch = new Arch();
        var union = arch.AnyOf(arch.AnyOf(arch.Project("A"), arch.Project("B")), arch.Project("C"))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Select(part => part.Noun.ShouldBeOfType<ProjectNoun>()
                .Name)
            .ShouldBe(["A", "B", "C"]);
    }

    [Fact]
    public void AnyOf_NestedUnionCarryingAdjectives_StaysALeaf()
    {
        // An inner union with adjectives is a narrowed set of its own — flattening it would lose the
        // narrowing, so it survives as one operand.
        var arch = new Arch();
        Selection inner = arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(arch.Type(typeof(SqlConnection)));
        var outer = arch.AnyOf(inner, arch.Project("C"))
            .ShouldBeOfType<UnionSelection>();

        outer.Parts.Count.ShouldBe(2);
        outer.Parts[0]
            .ShouldBeOfType<UnionSelection>()
            .Parts.Count.ShouldBe(2);
    }

    [Fact]
    public void AnyOf_NestedEmptyUnion_StaysALeaf()
    {
        // Flattened, an empty inner union would vanish into its parent and leave the validator nothing to report.
        var arch = new Arch();
        Selection empty = arch.AnyOf(new List<Selection>());
        var outer = arch.AnyOf(empty, arch.Project("C"))
            .ShouldBeOfType<UnionSelection>();

        outer.Parts.Count.ShouldBe(2);
        outer.Parts[0]
            .ShouldBeOfType<UnionSelection>()
            .Parts.ShouldBeEmpty();
    }

    [Fact]
    public void AnyOf_Adjective_IsOwnedByTheUnionNotDistributedThroughIt()
    {
        // (a ∪ b) − c, not (a − c) ∪ (b − c): the union keeps its two operands and grows its own adjective.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B")).Except(arch.Type(typeof(SqlConnection)))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<ExceptAdjective>();
    }

    [Fact]
    public void Authored_AppendsOneAuthoredAdjective()
    {
        // The adjective carries no payload — the whole statement is the placement and the fragment — so
        // the model pin is that one lands, on the union as well as on a plain selection.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"), arch.Project("B")).Authored()
            .ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<AuthoredAdjective>();
    }

    [Fact]
    public void Authored_Repeated_RendersOnceButReifiesTwice()
    {
        // The sentence reads a repeated adjective once while the model keeps both, so ShouldReifyAlike reds on the
        // model text alone.
        var arch = new Arch();
        Constraint once = arch.Types.InNamespace("MyApp.*").Authored().MustBeSealed();
        Constraint twice = arch.Types.InNamespace("MyApp.*").Authored().Authored().MustBeSealed();

        SentenceRenderer.Sentence(twice)
            .ShouldBe(SentenceRenderer.Sentence(once));
        Should.Throw<ShouldAssertException>(() => Checker.ShouldReifyAlike(twice, once));
    }

    [Fact]
    public void AnyOf_SingleOperand_StaysAUnionInTheModel()
    {
        // Legal and an identity (§2 principle 5): a loop that yields one operand must not become an error,
        // and the node stays a union so the shape does not depend on how many times the loop ran.
        var arch = new Arch();
        var union = arch.AnyOf(arch.Project("A"))
            .ShouldBeOfType<UnionSelection>();

        union.Parts.ShouldHaveSingleItem();
        union.Adjectives.ShouldBeEmpty();
    }

    // ---- Except over one or more operands (GRAMMAR §5.1, §5.2) ----

    [Fact]
    public void Except_OneOperand_IsThePayloadItself()
    {
        // One operand passes through unchanged rather than becoming a one-part union.
        var arch = new Arch();
        Selection exclusion = arch.Type(typeof(SqlConnection));
        var except = arch.Types.Except(exclusion)
            .Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<ExceptAdjective>();

        except.Payload.ShouldBeSameAs(exclusion);
    }

    [Fact]
    public void Except_SeveralOperands_MintTheUnionAnyOfWould()
    {
        // Several operands are the union arch.AnyOf mints — the operands as parts, no adjectives of its own
        // — so nothing downstream needs a second multi-payload shape to understand.
        var arch = new Arch();
        var except = arch.Types.Except(arch.Project("A"), arch.Project("B"))
            .Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<ExceptAdjective>();
        var union = except.Payload.ShouldBeOfType<UnionSelection>();

        union.Parts.Count.ShouldBe(2);
        union.Adjectives.ShouldBeEmpty();
    }

    [Fact]
    public void Except_TypeSugar_WrapsEachTypeAsABareTypeNoun()
    {
        var arch = new Arch();
        var except = arch.Types.Except(typeof(SqlConnection), typeof(SqlCommand))
            .Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<ExceptAdjective>();
        var union = except.Payload.ShouldBeOfType<UnionSelection>();

        union.Parts.Select(part => part.Noun.ShouldBeOfType<TypeNoun>()
                .Anchor.Type)
            .ShouldBe([typeof(SqlConnection), typeof(SqlCommand)]);
        union.Parts.ShouldAllBe(part => part.Adjectives.Count == 0);
    }

    [Fact]
    public void Named_AppendsOneNamedAdjectiveCarryingTheNamesInOrder()
    {
        // Authoring order is the render order, so the model keeps the list as written.
        var arch = new Arch();
        var named = arch.Types.Named("A", "B", "C")
            .Adjectives.ShouldHaveSingleItem()
            .ShouldBeOfType<NamedAdjective>();

        named.Names.ShouldBe(["A", "B", "C"]);
    }

    // The constraint a one-rule spec enforcing `constraint` reifies to, held to being a TConstraint.
    private static TConstraint ShouldReifyTo<TConstraint>(Func<Arch, Constraint> constraint)
        where TConstraint : Constraint
    {
        return Checker.OneRule(constraint)
            .Constraint
            .ShouldBeOfType<TConstraint>();
    }
}

/// <summary>
///     The shared claim the operand-carrying-verb rows above make about a reified <see cref="ArchRule" />, as
///     an extension so the rule each row already arranged reads as the sentence's subject.
/// </summary>
/// <remarks>
///     <para>
///         <c>file</c>-scoped because it encodes one suite's claim about the dependency-verb walk hook rather
///         than reusable vocabulary — a top-level class would put it in every test file's extension lookup for
///         a helper only these rows want.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
file static class ArchRuleReificationAssertions
{
    /// <summary>
    ///     Asserts <paramref name="rule" /> reified to the walkable shape every operand-carrying verb shares —
    ///     the dependency verbs and the coverage verb alike: an Enforce <typeparamref name="TConstraint" />
    ///     carrying one operand, with the generic walk reaching that operand through <c>Operands</c>, an empty
    ///     member hook, and the subject selection intact.
    /// </summary>
    /// <remarks>
    ///     <paramref name="targets" /> is a parameter because each verb declares its own domain-named list
    ///     (<c>Targets</c>, <c>Memberships</c>) rather than inheriting one from
    ///     <see cref="OperandConstraint" /> — reading it through the concrete type is what keeps every row
    ///     pinning the property its own verb publishes.
    /// </remarks>
    internal static void ShouldReifyToWalkableDependencyConstraint<TConstraint>(
        this ArchRule rule, Func<TConstraint, IReadOnlyList<Selection>> targets)
        where TConstraint : OperandConstraint
    {
        string report = Describe(rule);
        rule.Posture.ShouldBe(Posture.Enforce, report);
        var constraint = rule.Constraint.ShouldBeOfType<TConstraint>(report);
        IReadOnlyList<Selection> declared = targets(constraint);

        // Operands in authoring order; the verb's own list is an alias over them (the operand walk hook, NOT
        // MemberOperands).
        declared.ShouldHaveSingleItem(report);
        constraint.Operands.ShouldBe(declared, report);
        // An operand-carrying verb overrides Operands, not MemberOperands — its member hook is empty.
        constraint.MemberOperands.ShouldBeEmpty(report);
        constraint.Subject!.Noun.ShouldBeOfType<TypesNoun>(report);
        constraint.Subject.Adjectives.ShouldBeEmpty(report);
    }

    /// <summary>The rule's identity, posture and the constraint type it actually reified to.</summary>
    private static string Describe(ArchRule rule)
    {
        string reified = rule.Constraint?.GetType()
            .Name ?? "(no constraint)";
        return $"Rule '{rule.Id}' ({rule.Posture}) reified {reified}.";
    }
}
