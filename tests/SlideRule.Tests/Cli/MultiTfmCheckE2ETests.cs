using System.Text.Json;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The check report's <c>multiTargetedProjects</c> stamp end to end, over the <c>MultiTfm</c> fixture.
///     The stamp says what a verdict over a project that compiles twice covers: the members and hierarchy of
///     the types its frameworks share are one framework's, so a member, base type or interface another
///     framework's <c>#if</c> guards was never in the model to violate anything, while the references every
///     framework makes are all in the model, so a ban on a reference only the losing framework makes reds.
/// </summary>
/// <remarks>
///     The stamp is a trust statement rather than a finding, so both halves of the claim are asserted here:
///     the multi-targeted project is named with its frameworks and its winner, and a single-framework
///     solution's report carries no such key at all — which is what keeps every existing report byte-identical.
/// </remarks>
public sealed class MultiTfmCheckE2ETests
{
    private const string Core = "MultiTfm.Core";
    private const string LegacyOnly = "MultiTfm.Core.Legacy.LegacyOnly";

    // The 1-based line of the #if !NET field in Widget.Legacy.cs, the partial of Widget that carries the
    // framework-conditional region. The violation must be sited there — a file BOTH frameworks compile,
    // declaring a type both frameworks share — because that is what says the edge came off the losing
    // compilation of a shared type rather than off a type only one framework declares.
    private const int LegacyFieldLine = 11;
    private const string NoLegacyRuleId = "layering/no-legacy";
    private const string RuleId = "layering/core-independent";
    private const string Widget = "MultiTfm.Core.Widget";

    // Pattern-only, so the spec assembly needs no reference to the fixture at all — and passing, because a
    // clean verdict is exactly the reading the stamp qualifies.
    private const string CoreIndependentSpec = """
                                               using SlideRule;

                                               namespace MultiTfmSpecs
                                               {
                                                   public sealed class CoreIndependentSpec : IArchitectureSpec
                                                   {
                                                       public void Define(Arch arch)
                                                       {
                                                           arch.Rule("layering/core-independent")
                                                               .Enforce(arch.Project("MultiTfm.Core").MustNotReference(arch.Namespace("MultiTfm.Web.*")))
                                                               .Because("The core must not know about the web tier.");
                                                       }
                                                   }
                                               }
                                               """;

    // The other half of the stamp's claim, and it has to red: a ban on the branch only netstandard2.0
    // compiles, which the shared surface reaches from a field net10.0 never sees.
    private const string NoLegacySpec = """
                                        using SlideRule;

                                        namespace MultiTfmSpecs
                                        {
                                            public sealed class NoLegacySpec : IArchitectureSpec
                                            {
                                                public void Define(Arch arch)
                                                {
                                                    arch.Rule("layering/no-legacy")
                                                        .Enforce(arch.Project("MultiTfm.Core").MustNotReference(arch.Namespace("MultiTfm.Core.Legacy")))
                                                        .Because("The shared surface must not reach the legacy-only branch.");
                                                }
                                            }
                                        }
                                        """;

    [Fact]
    public async Task Check_MultiTargetedProject_IsStampedWithItsFrameworksAndTheWinningOne()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("multi-tfm", "CoreIndependent", CoreIndependentSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the rule passed, and the stamp says what that pass covers. Both are read off the one
        // document, because the point of the stamp is precisely that it qualifies this verdict.
        result.ShouldSucceed();
        result.Out.ShouldHavePassed(RuleId);

        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement stamped = Stamps(document)
            .Single();

        stamped.GetProperty("project")
            .GetString()
            .ShouldBe(Core);
        stamped.GetProperty("targetFrameworks")
            .EnumerateArray()
            .Select(framework => framework.GetString())
            .ShouldBe(["net10.0", "netstandard2.0"]);
        stamped.GetProperty("factsFollow")
            .GetString()
            .ShouldBe("net10.0");
    }

    [Fact]
    public async Task Check_MultiTargetedProject_LeavesTheVerdictWhole()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("multi-tfm", "CoreIndependentWhole", CoreIndependentSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the stamp scopes the verdict, it never invalidates it: the model is complete, every rule
        // ran and answered, and nothing here is a load failure. Stamping modelIncomplete would tell a client
        // to distrust an answer that is entirely trustworthy about the framework it was measured against.
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.RootElement.TryGetProperty("modelIncomplete", out _)
            .ShouldBeFalse();
        document.RootElement.GetProperty("summary")
            .GetProperty("rulesChecked")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Check_SingleTargetedSolution_OmitsTheStampEntirely()
    {
        // Act — the MyApp bed, every project of which targets one framework, checked with the spec that
        // holds. This is the shape of every report the suite's goldens carry.
        CliResult result = await CliRunner.InvokeAsync(
            "check", CliRunner.MyAppSolution, "--spec", CliRunner.CleanSpecDll, "--json");

        // Assert — absent, not an empty array: a solution with nothing to say about frameworks renders the
        // document it rendered before the key existed.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();

        document.RootElement.TryGetProperty("multiTargetedProjects", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Check_BanOnAReferenceOnlyTheLosingFrameworkMakes_RedsWithTheSiteInTheSharedFile()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("multi-tfm", "NoLegacy", NoLegacySpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", fixture.SolutionPath, "--spec", specDll, "--json");

        // Assert — the half of the stamp a clean verdict cannot show. Widget's field of LegacyOnly is
        // netstandard2.0's alone and never reaches the model, because members follow net10.0; the reference
        // that field makes does, because references union across a project's frameworks. So the ban reds, and
        // it reds at the field itself, in the partial of Widget that both frameworks compile.
        result.ShouldReportViolations();
        result.Out.ShouldHaveSingleViolationOnEdge(NoLegacyRuleId, "reference", Widget, LegacyOnly);

        JsonElement site = CheckJson.Violations(result.Out, NoLegacyRuleId)
            .Single()
            .GetProperty("sites")
            .EnumerateArray()
            .First();
        site.GetProperty("file")
            .GetString()
            .ShouldNotBeNull()
            .ShouldEndWith("Widget.Legacy.cs");
        site.GetProperty("line")
            .GetInt32()
            .ShouldBe(LegacyFieldLine);
    }

    private static IEnumerable<JsonElement> Stamps(JsonDocument document)
    {
        return document.RootElement.GetProperty("multiTargetedProjects")
            .EnumerateArray();
    }
}
