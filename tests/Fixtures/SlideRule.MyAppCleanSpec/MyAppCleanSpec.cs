namespace SlideRule.MyAppCleanSpec;

/// <summary>
///     A spec for the MyApp fixture whose rules all hold, so <c>check</c> exits 0. The Migrate rule's two
///     <c>DataTable</c> edges (HomeController and InvoiceController) are both grandfathered by
///     <c>arch/clean-baseline.json</c>, so a Migrate rule with all its debt captured still passes.
/// </summary>
public sealed class MyAppCleanSpec : IArchitectureSpec
{
    /// <inheritdoc />
    public void Define(Arch arch)
    {
        arch.Rule("layering/billing-independent")
            .Enforce(arch.Namespace("MyApp.Legacy.Billing.*").MustNotReference(arch.Namespace("MyApp.Web.*")))
            .Because("Billing must not reach up into the web layer.");

        arch.Rule("naming/interfaces")
            .Enforce(arch.Types.OfKind(TypeKind.Interface).InNamespace("MyApp.*").MustHavePrefix("I"))
            .Because("House naming convention; agents grep by I-prefix.");

        arch.Rule("data-access/no-inline-sql")
            .Migrate(
                "Controllers build DataTables inline (legacy Active Record style).",
                arch.Namespace("MyApp.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("System.Data.*")))
            .Baseline("arch/clean-baseline.json")
            .Because("Repository pattern for testability.");
    }
}
