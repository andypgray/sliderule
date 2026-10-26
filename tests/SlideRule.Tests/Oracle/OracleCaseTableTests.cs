using ArchUnitNET.Fluent;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Oracle;

/// <summary>
///     The differential-testing oracle. SlideRule's checker builds its
///     dependency model from Roslyn source; ArchUnitNET builds its model from compiled IL
///     (Mono.Cecil). Each row below expresses the same architecture rule on both substrates over the same
///     <c>MyApp</c> fixture and asserts they reach the same verdict — the set of type FullNames
///     that violate. Every row pins that set against a hand-derived expected truth and asserts the
///     two substrates equal each other: agreeing with each other alone could mean both are wrong the same
///     way, so the pinned truth is the third leg.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is deliberately raw-constraint, verdict-level, type granularity. The
///         following SlideRule behaviour is out of oracle scope — a documented boundary, not a
///         silent omission:
///     </para>
///     <list type="bullet">
///         <item>
///             Escape hatches (<c>.Where(...)</c> / <c>.Must(...)</c>): arbitrary C# predicates that
///             ArchUnitNET cannot see, so there is no analog to compare against.
///         </item>
///         <item>
///             Posture machinery: Migrate/Quarantine baselines, ratchet grandfathering, and the Quarantine
///             tripwire's diff-aware touch check. ArchUnitNET has no posture concept; only the raw
///             constraint a posture reduces to (e.g. Quarantine → <c>MustOnlyBeReferencedBy</c>, row 7) is
///             compared.
///         </item>
///         <item>
///             SlideRule-specific verdict rules: the inert-target warning (a matched-nothing
///             forbidden set) and the empty-subject fail-by-default. These are asserted directly in the
///             checker's own tests; the oracle rows are all non-empty-subject, non-inert by construction.
///         </item>
///         <item>
///             <c>file:line</c> locations: ArchUnitNET carries none. This is why the
///             comparison is at type granularity — SlideRule's per-site locations have nothing to compare
///             against.
///         </item>
///         <item>
///             Correspondence counting (<c>MustHaveExactlyOneCounterpart</c>): ArchUnitNET asserts facts
///             about a type itself and has no form that counts the other types a name template derives — there is
///             nothing to build a row against, only a wrong one.
///         </item>
///     </list>
/// </remarks>
public sealed class OracleCaseTableTests(WorkspaceFixture workspace, OracleArchitecture oracle)
    : IClassFixture<OracleArchitecture>, IClassFixture<WorkspaceFixture>
{
    // Row 1: Domain must not reference Web. Only MyApp.Domain.OrderService reaches into Web
    // (new HomeController(), typeof(HomeController), and the WebTextExtensions extension call).
    [Fact]
    public void Row1_DomainMustNotReferenceWeb()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleReferenceViolators(arch =>
            arch.Rule("oracle/domain-not-web")
                .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                .Because("Oracle row 1: Domain must not reference Web."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInNamespace("MyApp.Domain")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespace("MyApp.Web");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Domain.OrderService");
    }

    // Row 2: Web must not reference Domain. No Web type reaches down into Domain — clean on both.
    [Fact]
    public void Row2_WebMustNotReferenceDomain()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleReferenceViolators(arch =>
            arch.Rule("oracle/web-not-domain")
                .Enforce(arch.Layer("Web", "MyApp.Web.*").MustNotReference(arch.Layer("Domain", "MyApp.Domain.*")))
                .Because("Oracle row 2: Web must not reference Domain."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInNamespace("MyApp.Web")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespace("MyApp.Domain");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit);
    }

    // Row 3: Billing must not reference Web. Billing depends only downward/internally — clean on both.
    [Fact]
    public void Row3_BillingMustNotReferenceWeb()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleReferenceViolators(arch =>
            arch.Rule("oracle/billing-not-web")
                .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
                .Because("Oracle row 3: Billing must not reference Web."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInNamespace("MyApp.Legacy.Billing")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespace("MyApp.Web");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit);
    }

    // Row 4: Web *Controller types must not reference System.Data (an EXTERNAL target, kept on both
    // sides for MustNot*). Both controllers return/new a System.Data.DataTable.
    [Fact]
    public void Row4_ControllersMustNotReferenceSystemData()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleReferenceViolators(arch =>
            arch.Rule("oracle/controllers-no-system-data")
                .Enforce(arch.Namespace("MyApp.Web.*").WithSuffix("Controller")
                    .MustNotReference(arch.Namespace("System.Data.*")))
                .Because("Oracle row 4: controllers must not touch System.Data."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInNamespace("MyApp.Web")
            .And()
            .HaveNameEndingWith("Controller")
            .Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespace("System.Data");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.HomeController", "MyApp.Web.InvoiceController");
    }

    // Row 5: interfaces under MyApp.* must be I-prefixed. All four MyApp interfaces are — clean.
    [Fact]
    public void Row5_InterfacesMustHaveIPrefix()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleShapeViolators(arch =>
            arch.Rule("oracle/interface-prefix")
                .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
                .Because("Oracle row 5: interfaces are I-prefixed."));

        IArchRule rule = ArchRuleDefinition.Interfaces()
            .That()
            .ResideInAssembly(oracle.Domain, oracle.Web, oracle.Billing)
            .Should()
            .HaveNameStartingWith("I");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit);
    }

    // Row 6: types implementing IHandler<T> must be *Handler-suffixed. RefundProcessor implements
    // IHandler<InvoiceCreated> but lacks the suffix; InvoiceCreatedHandler has it. HIGHEST-RISK row:
    // open-generic / transitive interface matching across the two substrates.
    //
    // The SlideRule side anchors by fully-qualified string (GRAMMAR §5.2), which is how this class holds to
    // its doctrine without a stub: naming MyApp.Web.IHandler<T> is not referencing it, so the oracle still
    // never sees MyApp by CLR identity, and nothing here declares a type MyApp already declares. A string
    // names the DEFINITION, so it carries the open-generic typeof anchor's semantics exactly.
    [Fact]
    public void Row6_HandlerImplementorsMustHaveHandlerSuffix()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleShapeViolators(arch =>
            arch.Rule("oracle/handler-suffix")
                .Enforce(arch.Types.Implementing("MyApp.Web.IHandler<T>").MustHaveSuffix("Handler"))
                .Because("Oracle row 6: handler implementors carry the Handler suffix."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ImplementInterface(oracle.HandlerInterface())
            .Should()
            .HaveNameEndingWith("Handler");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.RefundProcessor");
    }

    // Row 7: raw quarantined-containment (what a Quarantine scope desugars to). The quarantined interior
    // {BillingCalculator, RoundingMode} may be referenced only from within Billing or via the facade;
    // InvoiceController reaches the interior directly, HomeController rides the IBillingFacade facade.
    [Fact]
    public void Row7_QuarantinedInteriorContainment()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleReferenceViolators(arch =>
        {
            Selection quarantined = arch.Namespace("MyApp.Legacy.Billing.*");
            Selection facadeImpl = arch.Types.WithNameMatching("BillingFacade");
            Selection facadeIface = arch.Types.WithNameMatching("IBillingFacade");
            arch.Rule("oracle/quarantined-containment")
                .Enforce(quarantined.Except(facadeImpl).Except(facadeIface)
                    .MustOnlyBeReferencedBy(quarantined, facadeImpl, facadeIface))
                .Because("Oracle row 7: quarantined billing interior is facade-only.");
        });

        // The interior is the ArchUnitNET analog of quarantined.Except(facadeImpl).Except(facadeIface).
        oracle.QuarantinedInterior()
            .Select(type => type.FullName)
            .ShouldBe(
                ["MyApp.Legacy.Billing.BillingCalculator", "MyApp.Legacy.Billing.RoundingMode"], true);

        // Equivalent to inbound containment: no outsider (a MyApp type outside Billing) may depend on
        // the interior. Only MyApp.Web.InvoiceController does.
        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInAssembly(oracle.Domain, oracle.Web)
            .Should()
            .NotDependOnAny(oracle.QuarantinedInterior());
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.InvoiceController");
    }

    // Row 8 (member-use, best-effort): the ambient-clock ban at caller-type granularity. SlideRule's
    // MustNotUse(DateTime.Now, DateTime.UtcNow) flags the using TYPE (GRAMMAR §4.5); ArchUnitNET sees the IL
    // getter calls (a property read compiles to get_Now()/get_UtcNow()). ArchUnitNET has no fluent member-call
    // predicate, so its dependency model is queried directly (OracleArchitecture) — same substrate, verdict-level.
    // Only HomeController reads the clock.
    [Fact]
    public void Row8_AmbientClockReadsAtCallerTypeGranularity()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleMemberUseViolators(arch =>
            arch.Rule("oracle/no-ambient-clock")
                .Enforce(arch.Types.MustNotUse(
                    arch.Member(typeof(DateTime), nameof(DateTime.Now)),
                    arch.Member(typeof(DateTime), nameof(DateTime.UtcNow))))
                .Because("Oracle row 8: no ambient-clock reads."));

        IReadOnlySet<string> archUnit = oracle.TypesReadingAmbientClock();

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.HomeController");
    }

    // Row 9 (member-subject, best-effort): "methods returning Task must be named *Async" at
    // DECLARING-TYPE granularity. SlideRule's memberShape violation names the offending member (an M: DocId,
    // GRAMMAR §4.6); ArchUnitNET has method members with a ReturnType but no return-type-at-definition fluent
    // predicate, so its model is queried directly (OracleArchitecture) — same substrate, verdict-level, reduced
    // to the declaring type (the bridge, exactly as row 8's caller-type reduction). Only HomeController declares
    // unsuffixed Task-returning methods (Save returning Task, Load returning Task<int>).
    [Fact]
    public void Row9_TaskReturningMethodsMustHaveAsyncSuffixAtDeclaringTypeGranularity()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleMemberShapeViolators(arch =>
            arch.Rule("oracle/async-suffix")
                .Enforce(arch.Namespace("MyApp.Web.*").Methods.Returning(typeof(Task), typeof(Task<>))
                    .MustHaveSuffix("Async"))
                .Because("Oracle row 9: Task-returning methods carry the Async suffix."));

        IReadOnlySet<string> archUnit = oracle.TypesDeclaringUnsuffixedTaskReturningMethods();

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.HomeController");
    }

    // Row 10 (residence twin): *Service types must reside in the Web project. The first Should()-position
    // residence row: SlideRule's MustResideInProject is declarer membership (any declaring project
    // satisfies), ArchUnitNET's ResideInAssembly is assembly membership — on this single-targeted fixture
    // the two coincide. OrderService is declared by MyApp.Domain, red on both substrates; InvoiceService
    // resides where the rule says and is green.
    [Fact]
    public void Row10_ServiceTypesMustResideInWebProject()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleShapeViolators(arch =>
            arch.Rule("oracle/services-in-web")
                .Enforce(arch.Types.WithSuffix("Service").MustResideInProject("MyApp.Web"))
                .Because("Oracle row 10: Service types reside in the Web project."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .HaveNameEndingWith("Service")
            .Should()
            .ResideInAssembly(oracle.Web);
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Domain.OrderService");
    }

    // Row 11 (membership twin): every MyApp.* type must belong to the Domain layer or the Web layer.
    // SlideRule's MustBelongTo resolves its layer operands in subject position and tests plain
    // containment; ArchUnitNET says the same thing as a Should()/OrShould() disjunction of exact-namespace
    // residences (MyApp's namespaces are flat, so exact match and the subtree glob coincide — the same
    // mapping rows 1–4 pin). The subject is namespace-matched on BOTH sides so compiler-emitted
    // global-namespace types fall out of both substrates. The four Billing types are outside both layers.
    [Fact]
    public void Row11_EveryMyAppTypeMustBelongToDomainOrWeb()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleShapeViolators(arch =>
            arch.Rule("oracle/no-ungoverned-types")
                .Enforce(arch.Namespace("MyApp.*")
                    .MustBelongTo(arch.Layer("Domain", "MyApp.Domain.*"), arch.Layer("Web", "MyApp.Web.*")))
                .Because("Oracle row 11: every MyApp type belongs to a declared layer."));

        IArchRule rule = ArchRuleDefinition.Types()
            .That()
            .ResideInNamespaceMatching("MyApp.*")
            .Should()
            .ResideInNamespace("MyApp.Domain")
            .OrShould()
            .ResideInNamespace("MyApp.Web");
        IReadOnlySet<string> archUnit = oracle.FailingTypeNames(rule);

        AssertOracleAgreement(
            slideRule, archUnit,
            "MyApp.Legacy.Billing.BillingCalculator",
            "MyApp.Legacy.Billing.BillingFacade",
            "MyApp.Legacy.Billing.IBillingFacade",
            "MyApp.Legacy.Billing.RoundingMode");
    }

    // Row 12 (member-shape twin): the Domain layer's properties must declare no setter. A strict twin rather
    // than a reduction — ArchUnitNET's NotHaveSetter() is SetterVisibility == NotAccessible, and its loader
    // gives an `init` accessor a real set method, so a `{ get; init; }` property reds on BOTH substrates,
    // which is exactly SlideRule's semantics: get-only is a claim about the declaration (GRAMMAR §5.7).
    // Money is a positional readonly record struct, so its generated Amount is the init carrier; Order's
    // Reference and Total are plain `{ get; set; }`. Two scoping facts of the pinned 0.13.4 package shape the
    // ArchUnitNET side: it has no AreDeclaredInTypesThat(), so the declaring-type scope goes through the
    // AreDeclaredIn(IObjectProvider<IType>) overload over the same namespace predicate rows 1-4 use; and a
    // nested type carries no namespace of its own in IL, so Order.Line sits in the empty namespace there and
    // its two get-only properties are outside the ArchUnitNET subject while SlideRule keeps them in. That
    // last difference is not observable here, because those two are green on both readings.
    [Fact]
    public void Row12_DomainPropertiesMustBeGetOnlyIncludingInitOnlySetters()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleMemberShapeViolators(arch =>
            arch.Rule("oracle/domain-values-immutable")
                .Enforce(arch.Namespace("MyApp.Domain.*").Properties.MustBeGetOnly())
                .Because("Oracle row 12: Domain properties declare no setter."));

        IArchRule rule = ArchRuleDefinition.PropertyMembers()
            .That()
            .AreDeclaredIn(ArchRuleDefinition.Types()
                .That()
                .ResideInNamespace("MyApp.Domain"))
            .Should()
            .NotHaveSetter();
        IReadOnlySet<string> archUnit = oracle.FailingMemberDeclaringTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Domain.Money", "MyApp.Domain.Order");
    }

    // Row 13 (member-shape twin at type granularity, with one stated divergence): the Web layer's STATIC
    // fields must be readonly. ArchUnitNET's BeReadOnly() is Writability == ReadOnly, and its field loader
    // maps Writability from Cecil's IsInitOnly alone — so a `const` field reads Writable there and reds,
    // where SlideRule greens it because const is readonly's superset (GRAMMAR §5.7). 0.13.4 cannot express
    // const-satisfies at all: FieldMember carries no IsConst. At member granularity the two therefore diverge
    // on ReportBudget.MaxRows. The oracle's contract is verdict-level at type granularity (the class remarks'
    // documented boundary), and at that granularity the divergence is not observable: both substrates reduce
    // to { MyApp.Web.ReportBudget }, which declares a static field that is not readonly on either
    // reading. const-satisfies is pinned by MustBeReadonlyVerbTests.MustBeReadonly_ConstField_Passes, not here.
    [Fact]
    public void Row13_WebStaticFieldsMustBeReadonlyAtDeclaringTypeGranularity()
    {
        IReadOnlySet<string> slideRule = ShouldHaveSlideRuleMemberShapeViolators(arch =>
            arch.Rule("oracle/no-static-mutable")
                .Enforce(arch.Namespace("MyApp.Web.*").Fields.ThatAreStatic().MustBeReadonly())
                .Because("Oracle row 13: static fields of the Web layer are readonly."));

        IArchRule rule = ArchRuleDefinition.FieldMembers()
            .That()
            .AreStatic()
            .And()
            .AreDeclaredIn(ArchRuleDefinition.Types()
                .That()
                .ResideInNamespace("MyApp.Web"))
            .Should()
            .BeReadOnly();
        IReadOnlySet<string> archUnit = oracle.FailingMemberDeclaringTypeNames(rule);

        AssertOracleAgreement(slideRule, archUnit, "MyApp.Web.ReportBudget");
    }

    /// <summary>
    ///     The oracle assertion: both substrates equal the pinned expected set, which catches a blind spot they
    ///     share and makes them equal to each other. Sets are compared order-insensitively.
    /// </summary>
    private static void AssertOracleAgreement(
        IReadOnlySet<string> slideRule, IReadOnlySet<string> archUnit, params string[] expected)
    {
        slideRule.ShouldBe(expected, true);
        archUnit.ShouldBe(expected, true);
    }

    // A reader keeps one violation kind, so without this guard a rule that failed as an empty subject or a
    // rule error would leave its set empty and read as clean agreement.
    private RuleResult ShouldReachAVerdict(Action<Arch> define)
    {
        RuleResult result = Checker.Run(workspace.Model, define)
            .Single();

        result.Violations.ShouldNotContain(violation =>
            violation.Kind == ViolationKind.EmptySubject || violation.Kind == ViolationKind.RuleError);
        return result;
    }

    // The four readers below stay raw rather than stating their sets through the RuleResult outcome verbs:
    // each projects a slot of its own choosing — a source rather than an edge, a declaring type rather than the
    // member — and the value has to survive as a set for AssertOracleAgreement to compare it three ways.
    private IReadOnlySet<string> ShouldHaveSlideRuleReferenceViolators(Action<Arch> define)
    {
        return ShouldReachAVerdict(define)
            .Violators(ViolationKind.Reference, violation => violation.Source!.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }

    private IReadOnlySet<string> ShouldHaveSlideRuleShapeViolators(Action<Arch> define)
    {
        return ShouldReachAVerdict(define)
            .ShapeSubjects()
            .ToHashSet(StringComparer.Ordinal);
    }

    private IReadOnlySet<string> ShouldHaveSlideRuleMemberUseViolators(Action<Arch> define)
    {
        return ShouldReachAVerdict(define)
            .Violators(ViolationKind.MemberUse, violation => violation.Source!.FullName)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A member-shape rule's violators reduced to DECLARING-TYPE FullNames — the bridge to the oracle's
    // type-granularity compare (rows 9, 12 and 13). SlideRule keys the specific member (M: DocId); the
    // oracle agrees on which types own an offending member, exactly as row 8 agrees on which types read the
    // clock.
    private IReadOnlySet<string> ShouldHaveSlideRuleMemberShapeViolators(Action<Arch> define)
    {
        return ShouldReachAVerdict(define)
            .Violators(ViolationKind.MemberShape, violation => ((TypeNode)violation.SubjectMember!.DeclaringType).FullName)
            .ToHashSet(StringComparer.Ordinal);
    }
}
