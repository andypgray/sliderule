using System.Text;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The MSBuild tier of edge attribution where two projects declare one full name in separate files
///     (GRAMMAR §4.1), over the real <c>DriftedApp</c> fixture solution: <c>DriftedApp.Core</c> and
///     <c>DriftedApp.Core.Ansi</c> each declare <c>DriftedApp.StringExtensions</c> with different members, and
///     only the core's copy reaches <c>DriftedApp.Cell</c>. The fast-path bed in
///     <c>Checking.MultiplyDeclaredAttributionTests</c> stages the same shape from source strings; this pins the
///     half only a real workspace can, that two csproj files compiling two files under one name reach the merge
///     as the shape those rows reason over.
/// </summary>
/// <remarks>
///     <para>
///         <b>The checker's half of the fixture.</b> <c>Cli.DriftedCopiesGraphE2ETests</c> drives the
///         <c>graph</c> verb over the same solution through the CLI. This class carries the verdicts a spec
///         author reading that survey would write about the drifted copies, and
///         <see cref="SourcePackageShimCheckTests" /> the ones about the package shims beside them.
///     </para>
///     <para>
///         <b>Why the red rows stand beside the green one.</b> Green on its own would prove nothing — an empty
///         subject, an empty target, or a model that never extracted is green too. The genuine client edge reds
///         the same verb, and the markup project's reference reds a ban on the satellite, so each green is
///         reachable only over a model that really loaded both copies.
///     </para>
///     <para>
///         The bed is read in place through <see cref="WarmWorkspacePool" /> rather than leased through
///         <see cref="TempFixtureWorkspace" />: nothing here writes to the tree, and
///         <see cref="FixtureRestorer" />'s startup sweep has already restored every <c>TestSolutions</c>
///         bed in the output directory, so the projects load with their references resolved.
///     </para>
/// </remarks>
public sealed class DriftedCopiesCheckTests
{
    private const string Core = "DriftedApp.Core";
    private const string Ansi = "DriftedApp.Core.Ansi";
    private const string Client = "DriftedApp.Client";
    private const string Markup = "DriftedApp.Markup";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "DriftedApp", "DriftedApp.slnx");

    private static readonly string FixtureRoot = Path.GetDirectoryName(SolutionPath)!;

    private static Task<CodebaseModel> DriftedCodebase => FixtureCodebase.Of(SolutionPath);

    [Fact]
    public async Task Check_SatelliteMustOnlyReferenceItself_PassesOverTheRealWorkspace()
    {
        CodebaseModel model = await DriftedCodebase;

        // The field's rule. The satellite's copy of StringExtensions reaches nothing else in the solution; the
        // reference to Cell sits in the core's copy, in a file the satellite never compiles. The satellite's copy
        // also reaches System.Text.StringBuilder, an external type, which the allow-list verb exempts.
        Checker.Run(model, arch =>
                arch.Rule("layering/ansi-is-foundation")
                    .Enforce(arch.Project(Ansi).MustOnlyReference(arch.Project(Ansi)))
                    .Because("The ANSI layer builds on nothing else in the solution."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_ClientMustNotReferenceCore_FailsOnTheGenuineCrossProjectEdge()
    {
        CodebaseModel model = await DriftedCodebase;

        // The control: a real ProjectReference and a type only the core declares.
        Checker.Run(model, arch =>
                arch.Rule("layering/client-must-not-reference-core")
                    .Enforce(arch.Project(Client).MustNotReference(arch.Project(Core)))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "DriftedApp.Client.Renderer", "DriftedApp.Cell");
    }

    [Fact]
    public async Task Check_MarkupReferencingTheSharedName_CountsAgainstTheCopyItBound()
    {
        CodebaseModel model = await DriftedCodebase;

        // The markup project references the satellite alone, so its reference reached the satellite's copy: a ban
        // on the satellite reds on it, and a ban on the core, whose facts the node carries, does not.
        Checker.Run(model, arch =>
                arch.Rule("layering/markup-must-not-reference-ansi")
                    .Enforce(arch.Project(Markup).MustNotReference(arch.Project(Ansi)))
                    .Because("Markup is rendered without the ANSI layer."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "DriftedApp.Markup.Page", "DriftedApp.StringExtensions");

        Checker.Run(model, arch =>
                arch.Rule("layering/markup-must-not-reference-core")
                    .Enforce(arch.Project(Markup).MustNotReference(arch.Project(Core)))
                    .Because("Markup ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_EachCopysReferenceToASharedExternalType_CitesThatCopysOwnFile()
    {
        CodebaseModel model = await DriftedCodebase;

        // The one edge both copies make. Each reaches System.Text.StringBuilder from a file of its own, so the
        // model's single edge stands for two instances whose sites lie in different files, and a ban anchored on
        // either project has somewhere wrong to point. Both rows are red, so neither green rides on an empty
        // subject, and what each asserts is the evidence: the anchored copy's lines, exactly and in order.
        Checker.Run(model, arch =>
                arch.Rule("perf/ansi-must-not-reference-string-builder")
                    .Enforce(arch.Project(Ansi).MustNotReference(typeof(StringBuilder)))
                    .Because("The ANSI layer writes through the console's own buffer."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "DriftedApp.StringExtensions", "System.Text.StringBuilder",
                [SiteInCopy(Ansi, "Utilities", 14), SiteInCopy(Ansi, "Utilities", 15)]);

        Checker.Run(model, arch =>
                arch.Rule("perf/core-must-not-reference-string-builder")
                    .Enforce(arch.Project(Core).MustNotReference(typeof(StringBuilder)))
                    .Because("The core writes through the console's own buffer."))
            .Single()
            .ShouldHaveFailedWithEdgeAtSites(
                ViolationKind.Reference, "DriftedApp.StringExtensions", "System.Text.StringBuilder",
                [SiteInCopy(Core, "Extensions", 15), SiteInCopy(Core, "Extensions", 16)]);
    }

    // One site in one declarer's copy of StringExtensions, rendered the way a violation renders it. The bed is
    // read in place, so the path is the fixture's own absolute one and no test may spell it as a literal.
    private static string SiteInCopy(string project, string folder, int line)
    {
        return $"{Path.Combine(FixtureRoot, project, folder, "StringExtensions.cs")}:{line}";
    }
}
