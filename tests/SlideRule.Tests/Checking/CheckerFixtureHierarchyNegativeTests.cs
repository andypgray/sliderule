using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The negative hierarchy verbs over the real MyApp fixture (GRAMMAR §5.3), reusing the shared
///     <see cref="WorkspaceFixture.Model" /> (no extra MSBuild load). The reds already live in the committed
///     fixture, so each verb pins its known violator (red) and a scoped-out non-violator (green). The
///     interface and attribute anchors are fully-qualified strings (GRAMMAR §5.2), which is what lets this
///     class name MyApp types without a reflectable stub declaring them: correspondence to the extracted
///     model is by full name either way, and a string names the correspondence instead of impersonating it.
///     <c>System.Exception</c> is the one external (BCL) base anchor, and the one that still wants a
///     <c>typeof</c> — the spec side of this test compiles against the BCL already.
/// </summary>
public sealed class CheckerFixtureHierarchyNegativeTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    [Fact]
    public void MustNotImplement_OpenGeneric_RedsInvoiceCreatedHandler_PassesHomeController()
    {
        // InvoiceCreatedHandler : IHandler<InvoiceCreated> — the open-generic construction match reds it.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("InvoiceCreatedHandler")
                .MustNotImplement("MyApp.Web.IHandler<T>"))
            .ShouldHaveFailedWithSubjects(["MyApp.Web.InvoiceCreatedHandler"]);

        // HomeController does not implement IHandler — the ban silently passes.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("HomeController")
                .MustNotImplement("MyApp.Web.IHandler<T>"))
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotDeriveFrom_ExternalBaseException_RedsOrderRuleViolation_PassesOrderApproval()
    {
        // OrderRuleViolation : System.Exception — the direct external base is recorded, so the ban reds it.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("OrderRuleViolation")
                .MustNotDeriveFrom(typeof(Exception)))
            .ShouldHaveFailedWithSubjects(["MyApp.Domain.OrderRuleViolation"]);

        // OrderApproval derives from no BCL exception — the ban silently passes.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("OrderApproval")
                .MustNotDeriveFrom(typeof(Exception)))
            .ShouldHavePassed();
    }

    [Fact]
    public void MustNotBeAttributedWith_WebRoute_RedsHomeController_PassesInvoiceCreatedHandler()
    {
        // HomeController carries [WebRoute("/home")] — the declared attribute reds it.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("HomeController")
                .MustNotBeAttributedWith("MyApp.Web.WebRouteAttribute"))
            .ShouldHaveFailedWithSubjects(["MyApp.Web.HomeController"]);

        // InvoiceCreatedHandler carries no attribute — the ban silently passes.
        Checker.Verdict(fixture.Model, arch => arch.Types.WithPrefix("InvoiceCreatedHandler")
                .MustNotBeAttributedWith("MyApp.Web.WebRouteAttribute"))
            .ShouldHavePassed();
    }
}
