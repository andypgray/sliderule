using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The check report's subject-coverage pair end to end, over the <c>RazorApp</c> fixture: a rule written
///     the way a rule author reaches for a web tier — <c>arch.Project("RazorApp.Web")</c> — states how much
///     of what it swept nobody wrote, and the same rule narrowed with <c>.Authored()</c> states nothing at
///     all.
/// </summary>
/// <remarks>
///     The pair is measured on the materialized subject set rather than on the selection that produced it,
///     which is what makes it self-extinguishing by construction: there is no rule anywhere about when to
///     suppress the statement, because a subject with no generated types has nothing to report. The two
///     specs below differ by exactly one call, so what changes between the two documents is attributable to
///     that call alone.
/// </remarks>
public sealed class GeneratedSubjectCheckE2ETests
{
    private const string RuleId = "naming/web-types";

    private const string ProjectSubjectSpec = """
                                              using SlideRule;

                                              namespace RazorAppSpecs
                                              {
                                                  public sealed class ProjectSubjectSpec : IArchitectureSpec
                                                  {
                                                      public void Define(Arch arch)
                                                      {
                                                          arch.Rule("naming/web-types")
                                                              .Enforce(arch.Project("RazorApp.Web").MustHaveSuffix("Controller"))
                                                              .Because("Web types are named for what they are.");
                                                      }
                                                  }
                                              }
                                              """;

    private const string AuthoredSubjectSpec = """
                                               using SlideRule;

                                               namespace RazorAppSpecs
                                               {
                                                   public sealed class AuthoredSubjectSpec : IArchitectureSpec
                                                   {
                                                       public void Define(Arch arch)
                                                       {
                                                           arch.Rule("naming/web-types")
                                                               .Enforce(arch.Project("RazorApp.Web").Authored().MustHaveSuffix("Controller"))
                                                               .Because("Web types are named for what they are.");
                                                       }
                                                   }
                                               }
                                               """;

    [Fact]
    public async Task Check_ProjectSubjectSweepingGeneratedTypes_StatesBothNumbers()
    {
        // Arrange
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("generated-subject", "ProjectSubject", ProjectSubjectSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", FixtureBeds.RazorAppSolution, "--spec", specDll, "--json");

        // Assert — both numbers, because either alone is unreadable: "2 generated" says nothing without the
        // 4 it is 2 of, and the denominator appears nowhere else in the document. Here the pair says the
        // rule is aimed at a subject half of which nobody can act on.
        result.Out.ShouldHaveSubjectCoverage(RuleId, 4, 2);
    }

    [Fact]
    public async Task Check_SubjectNarrowedToAuthored_ReportsNeitherKey()
    {
        // Arrange — the same rule, one call different.
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("generated-subject", "AuthoredSubject", AuthoredSubjectSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync(
            "check", FixtureBeds.RazorAppSolution, "--spec", specDll, "--json");

        // Assert — the statement extinguishes itself. Both keys go, never one.
        result.Out.ShouldReportNoSubjectCoverage(RuleId);
    }

    [Fact]
    public async Task Check_HumanReport_CarriesTheSubjectLineForAFailingRule()
    {
        // Arrange
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("generated-subject", "ProjectSubjectHuman", ProjectSubjectSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync("check", FixtureBeds.RazorAppSolution, "--spec", specDll);

        // Assert — the terminal reader gets the same fact, sited between the rule's own framing and its list
        // of violations, so it reads as scope rather than as one more finding.
        result.Out.ShouldContain("  subject: 4 types, 2 generated");
    }

    [Fact]
    public async Task Check_HumanReport_NarrowedToAuthored_SaysNothingAboutTheSubject()
    {
        // Arrange
        string specDll = SpecAssemblyCompiler.EmitSpecDllToFreshTemp("generated-subject", "AuthoredSubjectHuman", AuthoredSubjectSpec);

        // Act
        CliResult result = await CliRunner.InvokeAsync("check", FixtureBeds.RazorAppSolution, "--spec", specDll);

        // Assert — no line at all rather than a line reading zero. A rule aimed squarely at code someone
        // wrote has nothing to say here, and its silence is what makes the line's presence a finding.
        result.Out.ShouldNotContain("subject:");
    }
}
