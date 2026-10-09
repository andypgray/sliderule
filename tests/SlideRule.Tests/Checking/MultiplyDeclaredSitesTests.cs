using Base;
using Shared;
using Shouldly;
using Xunit;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The evidence half of edge attribution (GRAMMAR §4.1): a violation cites the sites of the owned
///     instances that violate, and nothing else. <see cref="MultiplyDeclaredAttributionTests" /> pins which
///     instances a rule owns; these rows pin which LINES it may print once it owns them, for every verb that
///     mints from an edge's sites — the two filter-aware catch bans, whose evidence is a narrowing of a
///     narrowing, included.
/// </summary>
/// <remarks>
///     <para>
///         The drifted rows read <see cref="DriftedSitesCodebase" />, where two copies of one full name make
///         the same six families of edge from files of their own, so a rule anchored on one declarer has
///         somewhere wrong to point. The linked rows read <see cref="MultiplyDeclaredCodebase" />, where the
///         copies are one source text compiled three times and so share every <c>file:line</c>: there the
///         claim is the other one, that a finding over a linked file prints the same evidence whichever
///         declarer is anchored, from the edge's own list rather than from a filtered copy of it.
///     </para>
///     <para>
///         Site lists are asserted exactly and in order. Order is the extractor's (file, line) order, pinned
///         in its own right, and a violation that cites a superset is the very defect these rows exist for —
///         so <c>ShouldNotBeEmpty</c> would pass over it.
///     </para>
/// </remarks>
public sealed class MultiplyDeclaredSitesTests
{
    // The one Drift.Formatter -> Base.Registry edge's eight sites, parted by the copy that made them: the
    // constructor parameter, the Lookup signature and its `new` in Core's file; the field, the constructor
    // parameter, the Current property, the Refresh signature and its `new` in Satellite's.
    private static readonly string[] CoreReferencesRegistry =
    [
        "Core/Drift/Formatter.cs:5", "Core/Drift/Formatter.cs:7", "Core/Drift/Formatter.cs:9"
    ];

    private static readonly string[] SatelliteReferencesRegistry =
    [
        "Satellite/Drift/Formatter.cs:5", "Satellite/Drift/Formatter.cs:7", "Satellite/Drift/Formatter.cs:12",
        "Satellite/Drift/Formatter.cs:14", "Satellite/Drift/Formatter.cs:18"
    ];

    // The edge's own list is the two runs concatenated, because ordinal file order puts Core's file first.
    private static readonly string[] BothCopiesReferenceRegistry =
        [.. CoreReferencesRegistry, .. SatelliteReferencesRegistry];

    // The same parting for the exception: the catch clause and the throw in each copy.
    private static readonly string[] CoreReferencesRegistryException =
    [
        "Core/Drift/Formatter.cs:15", "Core/Drift/Formatter.cs:20"
    ];

    private static readonly string[] SatelliteReferencesRegistryException =
    [
        "Satellite/Drift/Formatter.cs:20", "Satellite/Drift/Formatter.cs:28"
    ];

    private static readonly string[] BothCopiesReferenceRegistryException =
        [.. CoreReferencesRegistryException, .. SatelliteReferencesRegistryException];

    [Fact]
    public void MustNotReference_DriftedCopies_CiteTheAnchoredDeclarersLinesAlone()
    {
        // The class's claim in one row: one edge, two project-headed bans, and each cites the lines its
        // own copy makes. Charging a declarer with the whole edge points a Satellite finding into a file
        // Satellite never compiles, which is what a rule owning one instance may not do.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/satellite-not-registry")
                    .Enforce(arch.Project("Satellite").MustNotReference(typeof(Registry)))
                    .Because("The satellite is built against its own sources."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Drift.Formatter", "Base.Registry", SatelliteReferencesRegistry);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/core-not-registry")
                    .Enforce(arch.Project("Core").MustNotReference(typeof(Registry)))
                    .Because("The core is built against its own sources."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Drift.Formatter", "Base.Registry", CoreReferencesRegistry);
    }

    [Fact]
    public void MustNotReference_DriftedCopies_AnAttributionInsensitiveSubjectCitesBothCopiesLines()
    {
        // The complement, and the reason the rows above are about ownership rather than about filtering: a head
        // that is not a project owns every instance, so the evidence is the edge's whole list — Core's three
        // lines then Satellite's five, in the extractor's (file, line) order.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/formatter-not-registry")
                    .Enforce(arch.Types.Named("Formatter").MustNotReference(typeof(Registry)))
                    .Because("The formatter is built against its own sources."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Drift.Formatter", "Base.Registry", BothCopiesReferenceRegistry);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/drift-not-registry")
                    .Enforce(arch.Namespace("Drift").MustNotReference(typeof(Registry)))
                    .Because("Formatting is built against its own sources."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Drift.Formatter", "Base.Registry", BothCopiesReferenceRegistry);
    }

    [Fact]
    public void MustNotConstruct_DriftedCopies_CitesTheAnchoredDeclarersOwnNew()
    {
        // The construction family carries its own instances rather than borrowing the reference edge's, so the
        // parting has to hold there too: one `new` per copy, at a line of its own.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("di/satellite-not-new-registry")
                    .Enforce(arch.Project("Satellite").MustNotConstruct(typeof(Registry)))
                    .Because("Registries come from the host."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Construction, "Drift.Formatter", "Base.Registry", ["Satellite/Drift/Formatter.cs:18"]);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("di/core-not-new-registry")
                    .Enforce(arch.Project("Core").MustNotConstruct(typeof(Registry)))
                    .Because("Registries come from the host."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Construction, "Drift.Formatter", "Base.Registry", ["Core/Drift/Formatter.cs:9"]);
    }

    [Fact]
    public void MustNotInject_DriftedCopies_CitesTheAnchoredDeclarersOwnConstructor()
    {
        // The injection family reads the constructor each copy declares, and the two copies declare different
        // ones — Satellite's takes a width beside the registry — so the sites part by declarer as the rest do.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("di/satellite-not-inject-registry")
                    .Enforce(arch.Project("Satellite").MustNotInject(typeof(Registry)))
                    .Because("The registry is resolved, never injected."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Injection, "Drift.Formatter", "Base.Registry", ["Satellite/Drift/Formatter.cs:7"]);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("di/core-not-inject-registry")
                    .Enforce(arch.Project("Core").MustNotInject(typeof(Registry)))
                    .Because("The registry is resolved, never injected."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Injection, "Drift.Formatter", "Base.Registry", ["Core/Drift/Formatter.cs:5"]);
    }

    [Fact]
    public void MustNotExpose_DriftedCopies_CitesTheAnchoredDeclarersOwnSurface()
    {
        // Exposure is the family where the two copies cite different NUMBERS of lines: Satellite's copy puts the
        // registry on a property and a method, Core's on one method, so neither list can stand for the other.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("api/satellite-not-expose-registry")
                    .Enforce(arch.Project("Satellite").MustNotExpose(typeof(Registry)))
                    .Because("The registry stays behind the formatter's surface."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Expose, "Drift.Formatter", "Base.Registry",
                ["Satellite/Drift/Formatter.cs:12", "Satellite/Drift/Formatter.cs:14"]);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("api/core-not-expose-registry")
                    .Enforce(arch.Project("Core").MustNotExpose(typeof(Registry)))
                    .Because("The registry stays behind the formatter's surface."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Expose, "Drift.Formatter", "Base.Registry", ["Core/Drift/Formatter.cs:7"]);
    }

    [Fact]
    public void MustNotThrow_DriftedCopies_CitesTheAnchoredDeclarersOwnThrow()
    {
        // Both copies throw the registry exception from a Fail() of their own, and each ban points at one.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/satellite-not-throw-registry-error")
                    .Enforce(arch.Project("Satellite").MustNotThrow(typeof(RegistryException)))
                    .Because("The satellite reports failures through its result type."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Throw, "Drift.Formatter", "Base.RegistryException", ["Satellite/Drift/Formatter.cs:28"]);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/core-not-throw-registry-error")
                    .Enforce(arch.Project("Core").MustNotThrow(typeof(RegistryException)))
                    .Because("The core reports failures through its result type."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Throw, "Drift.Formatter", "Base.RegistryException", ["Core/Drift/Formatter.cs:20"]);
    }

    [Fact]
    public void MustNotCatch_DriftedCopies_CitesTheAnchoredDeclarersOwnClause()
    {
        // The plain catch ban reads the edge's own sites, which both copies contribute to, and each anchoring
        // cites one clause. The two filter-aware bans below then narrow this evidence per instance.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/satellite-not-catch-registry-error")
                    .Enforce(arch.Project("Satellite").MustNotCatch(typeof(RegistryException)))
                    .Because("A registry failure ends the render."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Catch, "Drift.Formatter", "Base.RegistryException", ["Satellite/Drift/Formatter.cs:20"]);

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/core-not-catch-registry-error")
                    .Enforce(arch.Project("Core").MustNotCatch(typeof(RegistryException)))
                    .Because("A registry failure ends the render."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Catch, "Drift.Formatter", "Base.RegistryException", ["Core/Drift/Formatter.cs:15"]);
    }

    [Fact]
    public void MustNotCatchUnfiltered_DriftedCopies_ReadsTheNarrowingsOwnPerInstanceShare()
    {
        // The narrowing is a fact about the clause each copy spells: Satellite's carries a `when`, so its share
        // of the unfiltered sites is empty and the ban has nothing to mint — green, not a violation with no
        // lines to print. Core's clause is bare, and the ban anchored there cites it.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/satellite-not-catch-registry-error-bare")
                    .Enforce(arch.Project("Satellite").MustNotCatchUnfiltered(typeof(RegistryException)))
                    .Because("A registry failure is handled by kind, never wholesale."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/core-not-catch-registry-error-bare")
                    .Enforce(arch.Project("Core").MustNotCatchUnfiltered(typeof(RegistryException)))
                    .Because("A registry failure is handled by kind, never wholesale."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Catch, "Drift.Formatter", "Base.RegistryException", ["Core/Drift/Formatter.cs:15"]);
    }

    [Fact]
    public void MustNotSwallow_DriftedCopies_ReadsTheNarrowingsOwnPerInstanceShare()
    {
        // The second narrowing, parting the copies the same way: Satellite's clause rethrows and is filtered
        // besides, so nothing survives to it, while Core's holds the failure and is cited alone.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/satellite-not-swallow-registry-error")
                    .Enforce(arch.Project("Satellite").MustNotSwallow(typeof(RegistryException)))
                    .Because("A registry failure is never held quietly."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/core-not-swallow-registry-error")
                    .Enforce(arch.Project("Core").MustNotSwallow(typeof(RegistryException)))
                    .Because("A registry failure is never held quietly."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Catch, "Drift.Formatter", "Base.RegistryException", ["Core/Drift/Formatter.cs:15"]);
    }

    [Fact]
    public void MustOnlyReference_DriftedCopies_CiteTheAnchoredDeclarersLinesOnEveryUnallowedEdge()
    {
        // The outbound allow-list reaches both edges out of the shared name at once, so it is where a rule that
        // cited the whole edge would print two files' lines under a subject naming one project.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/satellite-is-foundation")
                    .Enforce(arch.Project("Satellite").MustOnlyReference(arch.Project("Satellite")))
                    .Because("The satellite builds on nothing else in the solution."))
            .Single()
            .ShouldHaveFailedWithEdgesAtSites(ViolationKind.Reference,
            [
                ("Drift.Formatter -> Base.Registry", SatelliteReferencesRegistry),
                ("Drift.Formatter -> Base.RegistryException", SatelliteReferencesRegistryException)
            ]);
    }

    [Fact]
    public void InboundVerbs_DriftedCopies_CiteTheLinesOfTheInstanceTheyRefuse()
    {
        // The subject sits at the target end here, so it owns both instances and the far end decides which
        // count: the operand names Satellite's copy, so the evidence is Satellite's lines even though Core's
        // instance reached the very same declaration.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/base-not-from-satellite")
                    .Enforce(arch.Project("Base").MustNotBeReferencedBy(arch.Project("Satellite")))
                    .Because("The base is consumed through the core."))
            .Single()
            .ShouldHaveFailedWithEdgesAtSites(ViolationKind.Reference,
            [
                ("Drift.Formatter -> Base.Registry", SatelliteReferencesRegistry),
                ("Drift.Formatter -> Base.RegistryException", SatelliteReferencesRegistryException)
            ]);

        // The allow-list spelling of the same claim: Core's instance is allowed and drops out of the evidence,
        // leaving the refused instance's lines alone rather than the edge's whole list.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/base-only-from-core")
                    .Enforce(arch.Project("Base").MustOnlyBeReferencedBy(arch.Project("Core")))
                    .Because("The base is consumed through the core."))
            .Single()
            .ShouldHaveFailedWithEdgesAtSites(ViolationKind.Reference,
            [
                ("Drift.Formatter -> Base.Registry", SatelliteReferencesRegistry),
                ("Drift.Formatter -> Base.RegistryException", SatelliteReferencesRegistryException)
            ]);
    }

    [Fact]
    public void MustOnlyThrow_DriftedCopies_CitesTheAnchoredDeclarersOwnThrow()
    {
        // The strict throw allow-list mints from the same evidence the forbidden-set verbs do, so the one
        // disallowed thrown type is cited at the anchored copy's line and not at the other copy's.
        Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("ex/satellite-only-invalid-operation")
                    .Enforce(arch.Project("Satellite").MustOnlyThrow(typeof(InvalidOperationException)))
                    .Because("The satellite reports failures as invalid operations."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Throw, "Drift.Formatter", "Base.RegistryException", ["Satellite/Drift/Formatter.cs:28"]);
    }

    [Fact]
    public void MustNotReferenceEachOther_DriftedCopies_MintEachPairOnceCitingEveryCellsEvidence()
    {
        // The family walk judges the shared name once per cell, and both the Core and the Satellite cell own a
        // copy of each pair. A pair reached from several cells is minted ONCE, labelled with the first cell in
        // declaration order, and cites the union of what those cells' evidence holds — in the edge's order, so
        // the finding reads as the edge does rather than as the walk happened to.
        RuleResult result = Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/projects-independent")
                    .Enforce(arch.Each(arch.Projects.Named("Core", "Satellite", "Base")).MustNotReferenceEachOther())
                    .Because("Every project ships on its own."))
            .Single();

        result.ShouldHaveFailedWithEdgesAtSites(ViolationKind.Reference,
        [
            ("Drift.Formatter -> Base.Registry", BothCopiesReferenceRegistry),
            ("Drift.Formatter -> Base.RegistryException", BothCopiesReferenceRegistryException)
        ]);

        result.Violations.Select(violation => violation.Cell)
            .ShouldBe(["Core", "Core"]);
    }

    [Fact]
    public void MustNotReference_ALinkedFile_CitesOneSiteListWhicheverDeclarerIsAnchored()
    {
        // The linked-file claim. The copies of a linked file share every file:line, so a
        // project-anchored finding and an attribution-insensitive one must print identical evidence — and they
        // print it from the edge's own list object, which is what makes the two renderings byte-identical
        // rather than merely equal.
        ReferenceEdge edge = MultiplyDeclaredCodebase.Model.Edge("Shared.Widget", "Shared.WidgetPart");

        Violation anchored = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-widget-part")
                    .Enforce(arch.Project("Tool").MustNotReference(typeof(WidgetPart)))
                    .Because("A widget is assembled elsewhere."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Shared.Widget", "Shared.WidgetPart", ["Shared/Widget.cs:5"]);

        Violation insensitive = Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/widget-not-widget-part")
                    .Enforce(arch.Type(typeof(Widget)).MustNotReference(typeof(WidgetPart)))
                    .Because("A widget is assembled elsewhere."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Shared.Widget", "Shared.WidgetPart", ["Shared/Widget.cs:5"]);

        anchored.Sites.ShouldBeSameAs(edge.Sites);
        insensitive.Sites.ShouldBeSameAs(edge.Sites);
    }

    [Fact]
    public void MustOnlyReferenceItself_OverAFamilyOfTheDeclarers_MintsEachPairOnceCitingEveryCellsEvidence()
    {
        // The allow-list's family walk meets the same repeat the cross-cell ban does: the Core cell and the
        // Satellite cell each own a copy of the pair, and neither cell as declared allows Base. The pair is
        // minted once, labelled with the first cell, citing both cells' lines in the edge's order.
        RuleResult result = Checker.Run(DriftedSitesCodebase.Model, arch =>
                arch.Rule("layering/each-copy-stays-home")
                    .Enforce(arch.Each(arch.Projects.Named("Core", "Satellite")).MustOnlyReferenceItself())
                    .Because("Each copy of the formatter builds on its own project alone."))
            .Single();

        result.ShouldHaveFailedWithEdgesAtSites(ViolationKind.Reference,
        [
            ("Drift.Formatter -> Base.Registry", BothCopiesReferenceRegistry),
            ("Drift.Formatter -> Base.RegistryException", BothCopiesReferenceRegistryException)
        ]);

        result.Violations.Select(violation => violation.Cell)
            .ShouldBe(["Core", "Core"]);
    }

    [Fact]
    public void MustNotReference_ALinkedFile_SubjectNamingTwoOfThreeDeclarers_CitesTheUnionOfTheirShares()
    {
        // The arm between "one instance counts" and "every instance counts": a union subject naming two of the
        // linked file's three declarers owns two of the edge's three instances, and their shares are merged in
        // the edge's order. For a linked file every share is the edge's own list, so the merge hands that list
        // back and the finding still reads as the one line.
        ReferenceEdge edge = MultiplyDeclaredCodebase.Model.Edge("Shared.Widget", "Shared.WidgetPart");

        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-and-stub-not-widget-part")
                    .Enforce(arch.AnyOf(arch.Project("Tool"), arch.Project("Stub")).MustNotReference(typeof(WidgetPart)))
                    .Because("A widget is assembled elsewhere."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "Shared.Widget", "Shared.WidgetPart", ["Shared/Widget.cs:5"])
            .Sites.ShouldBeSameAs(edge.Sites);
    }
}
