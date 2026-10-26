using Shouldly;
using SlideRule.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The checker over the real MyApp fixture, reusing the shared <see cref="WorkspaceFixture.Model" />
///     (no extra MSBuild load): the <c>layering/domain-independent</c> rule fires with a locatable site. The
///     clean verdicts over the same fixture are rows of <see cref="SlideRule.Tests.Oracle.OracleCaseTableTests" />.
/// </summary>
public sealed class CheckerFixtureIntegrationTests(WorkspaceFixture fixture) : IClassFixture<WorkspaceFixture>
{
    [Fact]
    public void DomainMustNotReferenceWeb_FiresOnOrderServiceWithLocation()
    {
        RuleResult result = Checker.Run(fixture.Model, arch =>
                arch.Rule("layering/domain-independent")
                    .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                    .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                    .Fix("Define an abstraction in Domain and implement it in Web."))
            .Single();

        result.ShouldHaveFailedWithEdgesIncluding(
            ViolationKind.Reference,
            "MyApp.Domain.OrderService -> MyApp.Web.HomeController",
            "MyApp.Domain.OrderService -> MyApp.Web.WebTextExtensions");

        Violation homeController = result.Violations.ShouldHaveSingle(
            v => v.Source!.FullName == "MyApp.Domain.OrderService" && v.Target!.FullName == "MyApp.Web.HomeController",
            v => $"{v.Source?.FullName} -> {v.Target?.FullName}");
        homeController.Sites.Select(site => $"{fixture.RelativePath(site)}:{site.Line}")
            .ShouldContain("MyApp.Domain/OrderService.cs:9");
    }
}
