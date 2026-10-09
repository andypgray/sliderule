using Xunit;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     A family whose cells are layers defined by projects that each compile a source-only package (GRAMMAR §4.1,
///     §5.1), over the real <c>DriftedApp</c> fixture solution: <c>DriftedApp.Json</c> and
///     <c>DriftedApp.Testing</c> target netstandard2.1 and net10.0, each compile a copy of <c>Polyfills.Guard</c>
///     and <c>Polyfills.MD5Polyfill</c> of their own, and neither references the other. So each shim name is one
///     type both projects declare, and each project's call to the guard reaches its own copy.
/// </summary>
/// <remarks>
///     <para>
///         <b>The field shape.</b> A family of two layers, each defined by one of those projects, holds the two
///         projects' copies of every shim in two different cells. Cells overlap only where both hold one copy, so
///         the family is judged rather than refused, and it gives the verdicts the family of the two projects gives.
///     </para>
///     <para>
///         <b>Why the red rows stand beside the green ones.</b> Green on its own would prove nothing: a model that
///         never extracted the shims, or a family nobody judged, is green too. The client's genuine reference into
///         the core reds a family of four such layers, so project-defined layers are judged over this model. And
///         taking the generated shims out of both layer definitions reds both calls to the guard, so the copies
///         and their edges are in the model, and each green row passes with the shims in its cells.
///     </para>
///     <para>
///         The bed is read in place through <see cref="WarmWorkspacePool" />, as
///         <see cref="DriftedCopiesCheckTests" /> reads it and for the same reasons.
///     </para>
/// </remarks>
public sealed class SourcePackageShimCheckTests
{
    private const string Client = "DriftedApp.Client";
    private const string Core = "DriftedApp.Core";
    private const string Json = "DriftedApp.Json";
    private const string Testing = "DriftedApp.Testing";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "DriftedApp", "DriftedApp.slnx");

    private static Task<CodebaseModel> DriftedCodebase => FixtureCodebase.Of(SolutionPath);

    [Fact]
    public async Task Check_LayersDefinedByTheImporters_MustNotReferenceEachOther_Passes()
    {
        CodebaseModel model = await DriftedCodebase;

        // The field's rule. Each layer holds its own project's copy of every shim and no copy sits in both, so the
        // family stands, and neither project references the other.
        Checker.Run(model, arch =>
            {
                Layer json = arch.Layer("Json", arch.Project(Json));
                Layer testing = arch.Layer("Testing", arch.Project(Testing));
                arch.Rule("layering/extensions-independent")
                    .Enforce(arch.Each(json, testing).MustNotReferenceEachOther())
                    .Because("Each extension ships on its own.");
            })
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_AFamilyOfFourProjectLayers_FailsOnTheGenuineCrossProjectEdgeAlone()
    {
        CodebaseModel model = await DriftedCodebase;

        // The control. The client's reference to Cell crosses from one cell into another; the core's own copy of
        // StringExtensions reaches Cell inside its cell, and each importer's call reaches the guard inside its own.
        Checker.Run(model, arch =>
            {
                Layer client = arch.Layer("Client", arch.Project(Client));
                Layer core = arch.Layer("Core", arch.Project(Core));
                Layer json = arch.Layer("Json", arch.Project(Json));
                Layer testing = arch.Layer("Testing", arch.Project(Testing));
                arch.Rule("layering/independent")
                    .Enforce(arch.Each(client, core, json, testing).MustNotReferenceEachOther())
                    .Because("Every project ships on its own.");
            })
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "DriftedApp.Client.Renderer", "DriftedApp.Cell");
    }

    [Fact]
    public async Task Check_LayersDefinedByTheImporters_MustOnlyReferenceItself_Passes()
    {
        CodebaseModel model = await DriftedCodebase;

        // Each layer's self is its cell as declared, which holds that project's copy of the guard, so the call each
        // importer makes is allowed.
        Checker.Run(model, arch =>
            {
                Layer json = arch.Layer("Json", arch.Project(Json));
                Layer testing = arch.Layer("Testing", arch.Project(Testing));
                arch.Rule("layering/extensions-self-contained")
                    .Enforce(arch.Each(json, testing).MustOnlyReferenceItself())
                    .Because("Each extension builds on nothing else in the solution.");
            })
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_AuthoredLayerDefinitions_MustOnlyReferenceItself_FailsOnEachCallToTheGuard()
    {
        CodebaseModel model = await DriftedCodebase;

        // The shims open with the generated banner, so Authored() inside a layer's definition takes them out of the
        // layer, and out of its self with them: each importer's call to its own copy of the guard now leaves every
        // cell. The same adjective on the family does not do this, which the next row pins.
        Checker.Run(model, arch =>
            {
                Layer json = arch.Layer("Json", arch.Project(Json).Authored());
                Layer testing = arch.Layer("Testing", arch.Project(Testing).Authored());
                arch.Rule("layering/extensions-self-contained")
                    .Enforce(arch.Each(json, testing).MustOnlyReferenceItself())
                    .Because("Each extension builds on nothing else in the solution.");
            })
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference,
            [
                "DriftedApp.Json.JsonText -> Polyfills.Guard",
                "DriftedApp.Testing.TestConsole -> Polyfills.Guard"
            ]);
    }

    [Fact]
    public async Task Check_AnAuthoredFamily_MustOnlyReferenceItself_Passes()
    {
        CodebaseModel model = await DriftedCodebase;

        // On the family, Authored() narrows the subject and nothing else. No shim is judged, and each cell as declared
        // still holds its project's copy of the guard, so both calls stay allowed.
        Checker.Run(model, arch =>
            {
                Layer json = arch.Layer("Json", arch.Project(Json));
                Layer testing = arch.Layer("Testing", arch.Project(Testing));
                arch.Rule("layering/extensions-self-contained")
                    .Enforce(arch.Each(json, testing).Authored().MustOnlyReferenceItself())
                    .Because("Each extension builds on nothing else in the solution.");
            })
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_LayersDefinedByTheImporters_MustNotHaveCircularReferences_Passes()
    {
        CodebaseModel model = await DriftedCodebase;

        // No arrow runs between the two cells in either direction, because each call to the guard stays in the cell
        // that makes it.
        Checker.Run(model, arch =>
            {
                Layer json = arch.Layer("Json", arch.Project(Json));
                Layer testing = arch.Layer("Testing", arch.Project(Testing));
                arch.Rule("layering/extensions-acyclic")
                    .Enforce(arch.Each(json, testing).MustNotHaveCircularReferences())
                    .Because("The extensions can be built in some order.");
            })
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public async Task Check_TheProjectFamilyOverTheImporters_GivesTheLayerFamilysVerdicts()
    {
        CodebaseModel model = await DriftedCodebase;

        // The same two laws over project cells. Each project is its own cell and names a shared type only at itself,
        // so the partition holds by construction, and each verdict matches the one its layer spelling gets above.
        Checker.Run(model, arch =>
                arch.Rule("layering/extensions-independent")
                    .Enforce(arch.Each(arch.Projects.Named(Json, Testing)).MustNotReferenceEachOther())
                    .Because("Each extension ships on its own."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(model, arch =>
                arch.Rule("layering/extensions-self-contained")
                    .Enforce(arch.Each(arch.Projects.Named(Json, Testing)).MustOnlyReferenceItself())
                    .Because("Each extension builds on nothing else in the solution."))
            .Single()
            .ShouldHavePassedClean();
    }
}
