using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The persisted extraction cache against a real <c>UseArtifactsOutput</c> tree, with the default layout
///     beside it as the control — the output layout is the only variable. Both arms restore a private copy of
///     the fixture with the real SDK, so what is under test is not only the path derivation but the claim it
///     rests on: that the assets file lands where <see cref="IntermediateOutputTree" /> looks for it.
/// </summary>
/// <remarks>
///     <para>
///         The restore is live because the sibling tests use hand-built trees:
///         <c>Caching/ExtractionCacheStoreTests</c> pins the store's behaviour over an artifacts-shaped tree,
///         and <c>Roslyn/IntermediateOutputTreeTests</c> pins the derivation as arithmetic. Neither can know
///         where the SDK actually writes <c>project.assets.json</c>. A probe watching
///         <c>&lt;projectDirectory&gt;/obj/project.assets.json</c>, a path this layout never creates, would
///         serve a re-restore that changed the model from cache as though nothing had moved.
///     </para>
///     <para>
///         <b>Why the mutation is a byte append.</b> The third run has to load a workspace, so the assets
///         file must stay readable by the SDK; trailing whitespace changes the bytes the store stamps without
///         changing what NuGet parses.
///     </para>
///     <para>
///         Both arms take a dedicated copy: the leased copy resets by pruning anything the fixture source
///         does not contain, which would delete the <c>artifacts/</c> tree the arm exists to produce.
///     </para>
/// </remarks>
public sealed class ArtifactsOutputCacheE2ETests
{
    private const string Web = "MyApp.Web";

    [Fact]
    public async Task ArtifactsLayout_AssetsFileChangedBetweenRuns_IsSeen()
    {
        using TempFixtureWorkspace workspace = RestoredCopy(true);
        using var cache = new TempCacheRoot("artifacts-output-cache");

        // The layout claim, asserted rather than assumed: the assets file is outside the project directory.
        string assets = workspace.PathOf("artifacts", "obj", Web, "project.assets.json");
        File.Exists(assets)
            .ShouldBeTrue();
        File.Exists(workspace.PathOf(Web, "obj", "project.assets.json"))
            .ShouldBeFalse();

        await RunGraphAsync(workspace, cache.Root);
        (await RunGraphAsync(workspace, cache.Root)).ShouldBe(CodebaseSourceOutcome.Hit);

        await File.AppendAllTextAsync(assets, "\n", Ct);

        (await RunGraphAsync(workspace, cache.Root)).ShouldNotBe(CodebaseSourceOutcome.Hit);
    }

    [Fact]
    public async Task DefaultLayout_AssetsFileChangedBetweenRuns_IsSeen()
    {
        using TempFixtureWorkspace workspace = RestoredCopy(false);
        using var cache = new TempCacheRoot("artifacts-output-cache");

        string assets = workspace.PathOf(Web, "obj", "project.assets.json");
        File.Exists(assets)
            .ShouldBeTrue();

        await RunGraphAsync(workspace, cache.Root);
        (await RunGraphAsync(workspace, cache.Root)).ShouldBe(CodebaseSourceOutcome.Hit);

        await File.AppendAllTextAsync(assets, "\n", Ct);

        (await RunGraphAsync(workspace, cache.Root)).ShouldNotBe(CodebaseSourceOutcome.Hit);
    }

    // A private fixture copy, restored under the requested layout. UseArtifactsOutput is only honoured from
    // a Directory.Build.props, which is why the arm writes one rather than setting the property per project.
    private static TempFixtureWorkspace RestoredCopy(bool artifactsLayout)
    {
        TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(restore: false);
        if (artifactsLayout)
            File.WriteAllText(
                workspace.PathOf("Directory.Build.props"),
                "<Project>\n    <PropertyGroup>\n        <UseArtifactsOutput>true</UseArtifactsOutput>\n"
                + "    </PropertyGroup>\n</Project>\n");

        FixtureRestorer.Restore(workspace.SolutionPath);
        return workspace;
    }

    private static async Task<CodebaseSourceOutcome?> RunGraphAsync(TempFixtureWorkspace workspace, string cacheRoot)
    {
        CacheRun run = await CacheRuns.GraphAsync(workspace.SolutionPath, cacheRoot);

        run.Result.ShouldSucceed();
        return run.Outcome;
    }
}
