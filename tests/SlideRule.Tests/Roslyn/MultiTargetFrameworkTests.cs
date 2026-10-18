using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     One project file, two target frameworks, over the real <c>MultiTfm</c> fixture solution
///     (<c>MultiTfm.Core</c> targets <c>netstandard2.0;net10.0</c>; <c>MultiTfm.Web</c> is single-framework
///     and references it). These pin that a multi-framework project has exactly one name everywhere it is
///     read — the model's <see cref="TypeNode.ProjectName" />, the project list, a project reference, and an
///     <c>arch.Project</c> selection — and that the one thing normalization does <em>not</em> cure, the
///     shared types taking one framework's facts, is disclosed rather than silent: as an advisory note, and
///     as the two facts the project node carries for every document that has to render it.
/// </summary>
public sealed class MultiTargetFrameworkTests
{
    private const string Core = "MultiTfm.Core";
    private const string LegacyOnly = "MultiTfm.Core.Legacy.LegacyOnly";
    private const string Web = "MultiTfm.Web";
    private const string Widget = "MultiTfm.Core.Widget";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MultiTfm", "MultiTfm.sln");

    /// <summary>
    ///     The fixture's codebase, extracted once through <see cref="FixtureCodebase" /> for the tests below
    ///     that read it.
    /// </summary>
    private static Task<CodebaseModel> MultiTfmCodebase => FixtureCodebase.Of(SolutionPath);

    [Fact]
    public async Task OpenSolutionAsync_MultiTargetedProject_YieldsRoslynTfmDiscriminatedNames()
    {
        // This pins ROSLYN's behaviour, not SlideRule's — the premise every other test here inverts.
        // MSBuildProjectLoader appends a "(tfm)" discriminator whenever one project file yields more than
        // one Project, which is why the fixture's one csproj arrives under two unusable names. It therefore
        // opens its OWN workspace: WorkspaceLoader normalizes, so loading through it would prove nothing.
        using var workspace = MSBuildWorkspace.Create();

        Solution solution = await workspace.OpenSolutionAsync(SolutionPath, cancellationToken: Ct);

        solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(["MultiTfm.Core(net10.0)", "MultiTfm.Core(netstandard2.0)", "MultiTfm.Web"]);
    }

    [Fact]
    public async Task LoadAsync_MultiTargetedProject_NormalizesEveryNameToItsCsprojName()
    {
        // The load boundary is where the discriminator comes off, so this loads for real rather than
        // through the shared pool: the normalization is the subject.
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(SolutionPath, ct: Ct);

        // Both of the csproj's Projects now answer to the csproj's own name — nothing downstream can read
        // a discriminated one — and the discriminator survives on the side, as the framework map.
        loaded.Solution.Projects
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe([Core, Core, Web]);

        List<ProjectId> coreProjectIds = loaded.Solution.Projects
            .Where(project => project.Name == Core)
            .Select(project => project.Id)
            .ToList();
        IReadOnlyDictionary<ProjectId, string> frameworks = loaded.TargetFrameworks;
        coreProjectIds
            .Select(id => frameworks[id])
            .OrderBy(framework => framework, StringComparer.Ordinal)
            .ShouldBe(["net10.0", "netstandard2.0"]);

        // The single-framework project was never discriminated, so it is not in the map at all.
        ProjectId webProjectId = loaded.Solution.Projects
            .Single(project => project.Name == Web)
            .Id;
        loaded.TargetFrameworks.ContainsKey(webProjectId)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_AttributesItsTypesToTheUndiscriminatedName()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The field report's exact failure: a type attributed to "MultiTfm.Core(net10.0)" is invisible to
        // arch.Project("MultiTfm.Core"), and no spelling of the discriminated name is even typeable.
        model.Type(Widget)
            .ProjectName.ShouldBe(Core);

        // The framework-exclusive type rides in under the same name, so the union really is a union.
        model.Type("MultiTfm.Core.ModernOnly")
            .ProjectName.ShouldBe(Core);

        model.Projects
            .Select(project => project.Name)
            .ShouldBe([Core, Web]);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_ReferenceOnlyTheLosingFrameworkMakes_IsInTheModelWithoutItsMember()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The two axes part company on one field, and this is where that is observable. Widget's
        // netstandard2.0 compilation declares a field of LegacyOnly and net10.0's does not: the reference is
        // netstandard2.0's alone and it IS in the model, because references union across a project's
        // frameworks. The field that makes it is not, because members follow net10.0, the winner. The site is
        // in Widget's partial that carries the #if, which both frameworks compile.
        model.Edge(Widget, LegacyOnly)
            .Sites.Files()
            .ShouldHaveSingleItem()
            .ShouldEndWith("Widget.Legacy.cs");

        model.Type(Widget)
            .Members.ShouldBeEmpty();

        // And the losing framework's exclusive type rides in under the project's one name, like ModernOnly.
        model.Type(LegacyOnly)
            .ProjectName.ShouldBe(Core);
    }

    [Fact]
    public async Task Check_ProjectRuleOverMultiTargetedProject_SelectsItsTypes()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // A rule that must red, and must red NAMING Widget: a green here could otherwise be an
        // empty-subject false negative rather than a selection that worked.
        RuleResult failing = Checker.Run(model, arch =>
                arch.Rule("naming/multi-tfm")
                    .Enforce(arch.Project(Core).MustHavePrefix("Zzz"))
                    .Because("b"))
            .Single();
        // Exhaustive, which is what proves the project rule reaches both frameworks' exclusive types too.
        failing.ShouldHaveFailedWithSubjects([LegacyOnly, "MultiTfm.Core.ModernOnly", Widget]);

        // And a rule that must hold, over the same subject: the selection is non-empty and behaves.
        Checker.Run(model, arch =>
                arch.Rule("layering/multi-tfm")
                    .Enforce(arch.Project(Core).MustNotReference(arch.Namespace("MultiTfm.Web.*")))
                    .Because("b"))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_BanOnAReferenceOnlyTheLosingFrameworkMakes_Reds()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The model fact above read as a verdict: a rule about what the shared surface REFERENCES sees every
        // framework's compilation, so a ban on the legacy branch reds on the edge netstandard2.0 alone made.
        Checker.Run(model, arch =>
                arch.Rule("layering/no-legacy")
                    .Enforce(arch.Project(Core).MustNotReference(arch.Namespace("MultiTfm.Core.Legacy")))
                    .Because("b"))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, Widget, LegacyOnly);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_RecordsNoCrossProjectConflationNote()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // Both compilations of the csproj now carry one project name, so the same-FQN conflation note —
        // which fires only across DIFFERENT project names — never runs. One csproj is never a conflation.
        model.MergeNotes
            .ShouldNotContain(note => note.Contains("each declare", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_RecordsOneMultiFrameworkNoteNamingTheWinningFramework()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The one thing normalization deliberately does not cure: Widget is declared by both frameworks and
        // can only carry one framework's members and hierarchy, while its references are every framework's.
        // net10.0 wins on the extractor's ordinal framework order, not on the csproj's declaration order —
        // which lists netstandard2.0 first, precisely so this pin means something. ModernOnly and LegacyOnly,
        // which one framework each declares, collapsed nothing and add no note.
        model.MergeNotes.ShouldBe([
            "Project 'MultiTfm.Core' targets 'net10.0' and 'netstandard2.0'; the types they share take "
            + "their members and hierarchy from 'net10.0' (the first extracted) but keep every framework's "
            + "references, so a rule about their members or hierarchy is checked against that framework "
            + "alone and a rule about what they reference sees every framework."
        ]);
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_StatesItsFrameworksAndTheWinningOneOnTheProject()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The merge note above says this in prose on one channel; the project node is the queryable form,
        // and it is what the survey renders. The order is extraction's own (ordinal), not the csproj's —
        // which declares netstandard2.0 first — so the first entry is always the one a shared type falls to.
        ProjectNode core = model.Projects.Single(project => project.Name == Core);
        core.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        core.FactsFollow.ShouldBe("net10.0");
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_SingleTargetedProject_StatesItsOneFrameworkAndNoWinner()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // The contrast project inside the same model, and the two facts part company on it: what a project
        // targets is stated whatever it targets, because a rule about it has to be able to fail here too,
        // while the winner is a disclosure about a collapse and this project had none to disclose.
        ProjectNode web = model.Projects.Single(project => project.Name == Web);
        web.TargetFrameworks.ShouldBe(["net10.0"]);
        web.FactsFollow.ShouldBeNull();
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_SingleTargetedConsumer_ResolvesItsProjectReferenceToTheUndiscriminatedName()
    {
        CodebaseModel model = await MultiTfmCodebase;

        // Reference names are resolved through the same Project.Name, so a consumer's ProjectReference to a
        // multi-framework project names it once rather than naming a framework of it.
        model.Projects
            .Single(project => project.Name == Web)
            .ProjectReferences.ShouldBe([Core]);
    }
}
