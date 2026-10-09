using SlideRule.Tests.Stubs;

namespace SlideRule.Tests;

// The canonical sample spec, byte-identical to GRAMMAR §12 from the class declaration down and
// compiling as pinned test code. Its rendered sentences and reified model are the spec. The layout
// is essential: the named-argument documentation style (GRAMMAR §10) and the alignment below
// must survive verbatim. The model reifies identically either way, so no model test reds on a
// reformat; QuoteSyncTests does, holding §12's fence to this file line for line. The marker below
// keeps ReSharper cleanup off everything after it, so this file can go through cleanup with all the
// others instead of being remembered as an exception. It sits above the class declaration on
// purpose — GRAMMAR §12's fence starts there, so an unbalanced marker is the only kind that keeps
// the quoted region byte-identical to the doc.
// @formatter:off
public sealed class ArchitectureSpec : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        Layer domain = arch.Layer("Domain", "MyApp.Domain.*").Purpose("Domain holds the order and customer model.");
        Layer web    = arch.Layer("Web",    "MyApp.Web.*");

        arch.Rule("layering/domain-independent")
            .Enforce(domain.MustNotReference(web))
            .Because("Domain is UI-agnostic; transaction boundaries live in services.")
            .Fix("Define an abstraction in Domain and implement it in Web.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*")
                         .MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                from: "Controllers open SqlConnection directly (legacy Active Record style).",
                to: web.WithSuffix("Controller").MustNotReference(typeof(SqlConnection)))
            .Baseline("arch/baseline.json")
            .WhileYoureThere(MigrationPolicy.MigrateIfSmall)
            .Because("Repository pattern for testability — ADR-012.")
            .Fix("Inject the repository.")
            .Exemplar(arch.Types.Named("OrdersRepository"));

        arch.Scope("legacy/billing")
            .Quarantine(arch.Namespace("MyApp.Legacy.Billing.*"))
            .BoundaryOnlyVia(typeof(IBillingFacade), typeof(BillingFacade))
            .Baseline("arch/baseline.json")
            .Dragons("Banker's rounding happens at line-item level, NOT invoice level. " +
                     "Nightly reconciliation depends on this. Do not normalize.")
            .Because("Replacement scheduled (BillingV2, ADR-019); not worth stabilizing.");

        arch.Scope("domain/pricing")
            .Caution(arch.Types.Named("PricingEngine"))
            .Dragons("Discount rules are evaluated in declaration order and the first match wins; " +
                     "the catalogue states exceptions before defaults. Do not sort or de-duplicate the list.")
            .Because("Every checkout path calls PricingEngine directly; a facade would cost more than it guards.");

        arch.Rule("naming/handlers")
            .Enforce(arch.Types.Implementing(typeof(IHandler<>)).MustHaveSuffix("Handler"))
            .Because("Handler discovery is convention-based (see HandlerRegistry).");

        arch.Rule("di/handlers-via-registry")
            .Enforce(arch.Types.Except(arch.Type<HandlerRegistry>())
                         .MustNotConstruct(arch.Types.Implementing(typeof(IHandler<>))))
            .Because("Handlers are resolved through HandlerRegistry; direct construction bypasses discovery.");

        arch.Rule("style/type-name-length")
            .Enforce(arch.Types.InNamespace("MyApp.*")
                         .Must(t => t.Name.Length <= 40,
                               description: "keep type names at or under 40 characters"))
            .Because("Long type names break the generated architecture tables.");
    }
}
