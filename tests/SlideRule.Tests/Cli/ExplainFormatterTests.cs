using Shouldly;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The <c>explain</c> field dump, pinned over the canonical sample's rule shapes in-process:
///     the <c>&lt;id&gt; (&lt;posture&gt;)</c> header (with a scope posture's role), each present field once,
///     the posture payloads (Migrate <c>from</c>/<c>policy</c>/<c>baseline</c>; a scope's scope/boundary/
///     baseline/dragons), and the tripwire's sentence-less, boundary-less form under both scope postures.
///     <c>Fix</c> renders here even though it stays out of the always-on block.
/// </summary>
public sealed class ExplainFormatterTests
{
    private static string Dump(string id)
    {
        return Dump(Checker.Canonical, id);
    }

    /// <summary>The same dump over a model the row built for itself.</summary>
    private static string Dump(ArchitectureModel model, string id)
    {
        return string.Join("\n", ExplainFormatter.Lines(model.Rule(id)));
    }

    [Fact]
    public void Enforce_WithFix_DumpsHeaderSentenceBecauseFix()
    {
        Dump("layering/domain-independent")
            .ShouldBe(
                "layering/domain-independent (enforce)\n" +
                "  sentence: The Domain layer must not reference the Web layer.\n" +
                "  because: Domain is UI-agnostic; transaction boundaries live in services.\n" +
                "  fix: Define an abstraction in Domain and implement it in Web.");
    }

    [Fact]
    public void Enforce_WithCitation_DumpsCitationLineAfterBecause()
    {
        // The canonical sample cites nothing, so the citing rule is built inline: the dump keeps the
        // field order the check renderer uses — the reason, the page it rests on, then the remediation.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("http/reuse-httpclient")
                .Enforce(arch.Namespace("MyApp.*").MustHaveSuffix("Client"))
                .Because("A new client per call exhausts sockets.")
                .Citation("https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines")
                .Fix("Inject IHttpClientFactory."));

        Dump(model, "http/reuse-httpclient")
            .ShouldBe(
                "http/reuse-httpclient (enforce)\n" +
                "  sentence: Types in `MyApp.*` must be named `*Client`.\n" +
                "  because: A new client per call exhausts sockets.\n" +
                "  citation: https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines\n" +
                "  fix: Inject IHttpClientFactory.");
    }

    [Fact]
    public void Enforce_WithExemplar_DumpsTheExemplarAsTheSpecNamesItAfterTheFix()
    {
        // Explaining a rule never extracts the solution, so the exemplar is the spec's phrase with no site;
        // it follows the fix it points at.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("data-access/no-inline-sql")
                .Enforce(arch.Namespace("MyApp.Web.*").MustHaveSuffix("Controller"))
                .Because("Repository pattern for testability.")
                .Fix("Move the SQL into a repository.")
                .Exemplar(arch.Types.Named("BookingRepository")));

        Dump(model, "data-access/no-inline-sql")
            .ShouldBe(
                "data-access/no-inline-sql (enforce)\n" +
                "  sentence: Types in `MyApp.Web.*` must be named `*Controller`.\n" +
                "  because: Repository pattern for testability.\n" +
                "  fix: Move the SQL into a repository.\n" +
                "  exemplar: types named `BookingRepository`");
    }

    [Fact]
    public void Migrate_WithExemplar_DumpsTheExemplarBetweenTheFixAndThePayload()
    {
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("data-access/no-inline-sql")
                .Migrate(
                    "Controllers open SqlConnection directly.",
                    arch.Namespace("MyApp.Web.*").MustHaveSuffix("Controller"))
                .Because("Repository pattern for testability.")
                .Fix("Inject the repository.")
                .Exemplar(arch.Types.Named("OrdersRepository"))
                .Baseline("arch/baseline.json"));

        Dump(model, "data-access/no-inline-sql")
            .ShouldBe(
                "data-access/no-inline-sql (migrate)\n" +
                "  sentence: Types in `MyApp.Web.*` must be named `*Controller`.\n" +
                "  because: Repository pattern for testability.\n" +
                "  fix: Inject the repository.\n" +
                "  exemplar: types named `OrdersRepository`\n" +
                "  from: Controllers open SqlConnection directly.\n" +
                "  policy: MigrateIfSmall\n" +
                "  baseline: arch/baseline.json");
    }

    [Fact]
    public void Enforce_WithoutExemplar_OmitsTheExemplarLine()
    {
        Dump("layering/domain-independent")
            .ShouldNotContain("exemplar:");
    }

    [Fact]
    public void Enforce_WithoutCitation_OmitsTheCitationLine()
    {
        Dump("layering/domain-independent")
            .ShouldNotContain("citation:");
    }

    [Fact]
    public void Enforce_WithoutFix_OmitsTheFixLine()
    {
        Dump("naming/interfaces")
            .ShouldBe(
                "naming/interfaces (enforce)\n" +
                "  sentence: Interfaces in `MyApp.*` must be named `I*`.\n" +
                "  because: House naming convention; agents grep by I-prefix.");
    }

    [Fact]
    public void Migrate_DumpsFromPolicyAndBaseline()
    {
        Dump("data-access/no-inline-sql")
            .ShouldBe(
                "data-access/no-inline-sql (migrate)\n" +
                "  sentence: Types in the Web layer named `*Controller` must not reference `SqlConnection`.\n" +
                "  because: Repository pattern for testability — ADR-012.\n" +
                "  fix: Inject the repository.\n" +
                "  exemplar: types named `OrdersRepository`\n" +
                "  from: Controllers open SqlConnection directly (legacy Active Record style).\n" +
                "  policy: MigrateIfSmall\n" +
                "  baseline: arch/baseline.json");
    }

    [Fact]
    public void QuarantineContainment_DumpsRoleScopeBoundaryBaselineAndDragons()
    {
        Dump("legacy/billing/containment")
            .ShouldBe(
                "legacy/billing/containment (quarantine/containment)\n" +
                "  sentence: Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or `BillingFacade`, " +
                "must be referenced only by types in `MyApp.Legacy.Billing.*`, `IBillingFacade` or `BillingFacade`.\n" +
                "  because: Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
                "  fix: use `IBillingFacade`\n" +
                "  scope: legacy/billing\n" +
                "  boundary: `IBillingFacade`, `BillingFacade`\n" +
                "  baseline: arch/baseline.json\n" +
                "  dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
                "Nightly reconciliation depends on this. Do not normalize.");
    }

    [Fact]
    public void QuarantineTripwire_DumpsRoleScopeAndDragons_NoSentenceOrBoundary()
    {
        Dump("legacy/billing/tripwire")
            .ShouldBe(
                "legacy/billing/tripwire (quarantine/tripwire)\n" +
                "  because: Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.\n" +
                "  scope: legacy/billing\n" +
                "  dragons: Banker's rounding happens at line-item level, NOT invoice level. " +
                "Nightly reconciliation depends on this. Do not normalize.");
    }

    [Fact]
    public void CautionTripwire_DumpsPostureRoleScopeAndDragons_NoBoundaryOrBaseline()
    {
        // The whole dump as one string rather than as a header pin plus three absences: the body arm is
        // shared with Quarantine and every line in it self-gates on presence, so what needs proving is that
        // a caution's four lines are all of them.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Scope("shared/utilities")
                .Caution(arch.Namespace("MyApp.Shared.*"))
                .Dragons("Argument order is structural: every caller passes them positionally.")
                .Because("The helpers are public API for the whole solution."));

        Dump(model, "shared/utilities/tripwire")
            .ShouldBe(
                "shared/utilities/tripwire (caution/tripwire)\n" +
                "  because: The helpers are public API for the whole solution.\n" +
                "  scope: shared/utilities\n" +
                "  dragons: Argument order is structural: every caller passes them positionally.");
    }

    [Fact]
    public void QuarantineWithASelectionBoundary_PrintsTheSurfaceAsProseNotTypeNames()
    {
        // The boundary line reads the pre-rendered surface, so a no-load operand arrives already worded
        // and a CLI that cannot reach Core's prose helpers never has to word one itself.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .BoundaryOnlyVia(arch.Types.Named("BillingFacade"), arch.Namespace("MyApp.Legacy.Billing.Contracts.*"))
                .Dragons("Rounding is structural.")
                .Because("Replacement scheduled."));
        string dump = Dump(model, "legacy/billing/containment");

        dump.ShouldContain("  boundary: types named `BillingFacade`, types in `MyApp.Legacy.Billing.Contracts.*`");
        dump.ShouldContain("  fix: use types named `BillingFacade`");
    }

    [Fact]
    public void QuarantineWithDragonsDoc_PrintsThePathOnly_NotDragonsOrBoundary()
    {
        // A hermetic quarantined scope documented via a linked file rather than inline dragons prose.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Scope("legacy/billing")
                .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
                .DragonsDoc("arch/billing-dragons.md")
                .Because("Replacement scheduled; see the linked doc."));
        string dump = Dump(model, "legacy/billing/containment");

        dump.ShouldContain("  dragons-doc: arch/billing-dragons.md");
        dump.ShouldNotContain("  dragons:");
        dump.ShouldNotContain("  boundary:"); // hermetic quarantine — no sanctioned surface
    }
}
