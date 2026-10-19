using Shouldly;
using SlideRule.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>explain --type</c> and <c>explain --path</c> through the real command tree, over the MyApp
///     fixture and its violated spec.
/// </summary>
public sealed class ExplainClaimsCommandTests
{
    [Fact]
    public async Task ExplainType_PrintsTheHeaderAndEveryClaimant()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--type", "MyApp.Domain.OrderService", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        // The whole answer: what a reader learns here is the layout, so the pin is every line of it. The lone
        // positional after --type is the solution, not a rule ID.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe(
                """
                    MyApp.Domain.OrderService (class)
                      project: MyApp.Domain
                      file: MyApp.Domain/OrderService.cs
                      layers:
                        Domain
                          namespace `MyApp.Domain` matches `MyApp.Domain.*`
                      rules:
                        layering/domain-independent: The Domain layer
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        layering/projects-independent: Each of the projects matching `MyApp.*`
                          declared by project `MyApp.Domain`
                        layering/projects-reached-only-by-themselves: Each of the projects matching `MyApp.*`
                          declared by project `MyApp.Domain`
                        di/handlers-via-registry: Types, except `HandlerRegistry`
                          outside the except: `HandlerRegistry`
                        layering/no-ghost: Types in `MyApp.Domain.*`
                          namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        exceptions/domain-throws-domain: The Domain layer
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        exceptions/no-unfiltered-catch: The Web or Domain layers
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        exceptions/no-swallowed-catch: The Web or Domain layers
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        exceptions/no-bare-bcl-throw: The Domain layer
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                        layering/services-in-web: Types named `*Service`
                          its name `OrderService` ends with `Service`
                        layering/services-behind-contracts: Types named `*Service`
                          its name `OrderService` ends with `Service`
                        layering/no-ungoverned-types: Types in `MyApp.*`
                          namespace `MyApp.Domain` matches `MyApp.*`
                        domain/values-immutable: Properties of the Domain layer
                          in the Domain layer
                            namespace `MyApp.Domain` matches `MyApp.Domain.*`
                    """.NormalizedTrimmed());
    }

    [Fact]
    public async Task ExplainType_BySimpleName_ExplainsTheTypeWearingIt()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--type", "OrderService", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed("MyApp.Domain.OrderService (class)");
    }

    [Fact]
    public async Task ExplainPath_OfOneHalfOfAPartialType_ExplainsTheTypeAndNamesBothFiles()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--path", "MyApp.Domain/Order.Validation.cs", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldStartWith(
                "MyApp.Domain.Order (class)\n  project: MyApp.Domain\n"
                + "  files: MyApp.Domain/Order.Validation.cs, MyApp.Domain/Order.cs\n");
    }

    [Fact]
    public async Task ExplainPath_OfADirectory_ExplainsEveryTypeUnderIt_InNameOrder()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--path", "MyApp.Legacy.Billing", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed();
        string answer = result.Out.NormalizedTrimmed();
        answer.ShouldContain("  scopes:\n    legacy/billing (quarantine): Types in `MyApp.Legacy.Billing.*`\n");
        answer.ShouldContain(
            "  excepted by:\n    rule legacy/billing/containment: Types in `MyApp.Legacy.Billing.*`, except `IBillingFacade` or "
            + "`BillingFacade`\n");
        answer
            .Split('\n')
            .Where(line => line.Length > 0 && line[0] != ' ')
            .ShouldBe(
            [
                "MyApp.Legacy.Billing.BillingCalculator (class)",
                "MyApp.Legacy.Billing.BillingFacade (class)",
                "MyApp.Legacy.Billing.IBillingFacade (interface)",
                "MyApp.Legacy.Billing.RoundingMode (enum)"
            ]);
    }

    [Fact]
    public async Task ExplainPath_DeclaringNoType_AnswersThatItDeclaresNone()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--path", "MyApp.Web/NoSuchFile.cs", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe("No type the solution declares is in 'MyApp.Web/NoSuchFile.cs'.");
    }

    [Fact]
    public async Task ExplainType_Unknown_RefusesWithTheClosestFullNames()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--type", "OrderServ", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldRefuseWith("Unknown type 'OrderServ'. Closest full names:", "  MyApp.Domain.OrderService");
    }

    [Fact]
    public async Task ExplainType_AsAStringifiedArray_RefusesOnTheShape()
    {
        CliResult result = await CliRunner.InvokeAsync(
            "explain", "--type", "[\"Order\"]", CliRunner.MyAppSolution, "--spec", CliRunner.ViolatedSpecDll);

        result.ShouldRefuseWith(
            "Cannot explain type '[\"Order\"]'. That is a JSON array written as text; pass one type name: 'Order'.");
    }

    [Theory]
    [InlineData("--spec", "SPEC")]
    [InlineData("layering/domain-independent", "SOLUTION", "--type", "Order", "--spec", "SPEC")]
    [InlineData("--type", "Order", "--path", "MyApp.Web", "--spec", "SPEC")]
    public async Task Explain_WithOtherThanExactlyOneForm_Refuses(params string[] args)
    {
        string[] resolved = args
            .Select(arg => arg switch
            {
                "SPEC" => CliRunner.ViolatedSpecDll,
                "SOLUTION" => CliRunner.MyAppSolution,
                _ => arg
            })
            .Prepend("explain")
            .ToArray();

        CliResult result = await CliRunner.InvokeAsync(resolved);

        result.ShouldRefuseWith(CommandFactory.ExplainFormsRefusal);
    }
}
