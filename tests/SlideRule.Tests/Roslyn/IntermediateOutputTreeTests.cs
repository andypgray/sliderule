using Shouldly;
using SlideRule.Roslyn.Solutions;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Where a project's intermediate tree is, and where the restore assets file inside it is, over the
///     output layouts the field has actually produced. Pure strings, no disk and no MSBuild: the derivation
///     is segment arithmetic over two paths from one MSBuild evaluation, and the rows are written rooted at
///     <c>/</c> so they mean the same thing on every platform — <see cref="Path.GetFullPath(string)" /> maps
///     them onto the current drive on Windows, on both sides of each comparison.
/// </summary>
/// <remarks>
///     The assets-file rows rest on a measurement rather than a reading: under <c>UseArtifactsOutput</c> the
///     assets file lands outside the project directory, so a cache probe that assumed the default layout would
///     watch a path that never exists and serve a stale model from cache.
/// </remarks>
public sealed class IntermediateOutputTreeTests
{
    [Theory]
    // The three measured layouts, plus a BaseIntermediateOutputPath redirected to a repository-level obj.
    [InlineData("/r/P/bin/Debug/net10.0/A.dll", "/r/P/obj/Debug/net10.0/A.dll", "/r/P/obj")]
    [InlineData("/r/P/bin/A.dll", "/r/P/obj/Debug/A.dll", "/r/P/obj")]
    [InlineData("/r/artifacts/bin/P/debug/A.dll", "/r/artifacts/obj/P/debug/A.dll", "/r/artifacts/obj")]
    [InlineData("/r/src/P/bin/Debug/net10.0/A.dll", "/r/obj/P/Debug/net10.0/A.dll", "/r/obj")]
    // The degenerate cases one guard collapses: the same path, no path, an empty path, and a sibling file in
    // the very directory the output lives in. None of them adds a directory that could be excluded.
    [InlineData("/r/P/bin/A.dll", "/r/P/bin/A.dll", null)]
    [InlineData("/r/P/bin/A.dll", null, null)]
    [InlineData("/r/P/bin/A.dll", "", null)]
    [InlineData("/r/P/bin/A.dll", "/r/P/bin/B.dll", null)]
    // Two paths that cannot share a tree derive no root. On Windows that is the differing-path-root guard; on
    // a POSIX file system a drive-lettered path is not rooted at all and the rootedness guard answers first.
    [InlineData("C:/a/bin/A.dll", "D:/a/obj/A.dll", null)]
    public void RootOf_DerivesTheRootWithoutNamingObj(
        string evaluatedPath, string? intermediatePath, string? expectedRoot)
    {
        IntermediateOutputTree.RootOf(evaluatedPath, intermediatePath)
            .ShouldBe(expectedRoot is null ? null : Path.GetFullPath(expectedRoot));
    }

    [Theory]
    // The default layout: the assets file sits three levels above the intermediate assembly, so the two
    // pivot directories are stamped too — absent, and that is the point.
    [InlineData(
        "/r/P", "/r/P/bin/Debug/net10.0/A.dll", "/r/P/obj/Debug/net10.0/A.dll",
        "/r/P/obj/project.assets.json;/r/P/obj/Debug/net10.0/project.assets.json;/r/P/obj/Debug/project.assets.json")]
    // UseArtifactsOutput: the assets file is OUTSIDE the project directory entirely, which is the whole
    // defect — the default location leads and is absent, and the real one is third.
    [InlineData(
        "/r/src/P", "/r/artifacts/bin/P/debug/A.dll", "/r/artifacts/obj/P/debug/A.dll",
        "/r/src/P/obj/project.assets.json;/r/artifacts/obj/P/debug/project.assets.json;"
        + "/r/artifacts/obj/P/project.assets.json;/r/artifacts/obj/project.assets.json")]
    // A runtime identifier adds one more pivot, and the walk follows it rather than counting hops.
    [InlineData(
        "/r/P", "/r/P/bin/Debug/net10.0/win-x64/A.dll", "/r/P/obj/Debug/net10.0/win-x64/A.dll",
        "/r/P/obj/project.assets.json;/r/P/obj/Debug/net10.0/win-x64/project.assets.json;"
        + "/r/P/obj/Debug/net10.0/project.assets.json;/r/P/obj/Debug/project.assets.json")]
    // A binlog replay carries no intermediate assembly path, so the set degrades to the default location
    // alone. Same for an output path the workspace never evaluated.
    [InlineData("/r/P", "/r/P/bin/Debug/net10.0/A.dll", null, "/r/P/obj/project.assets.json")]
    [InlineData("/r/P", null, "/r/P/obj/Debug/net10.0/A.dll", "/r/P/obj/project.assets.json")]
    public void AssetsPathsOf_StampsEveryPlaceTheLayoutInForceCouldPutTheAssetsFile(
        string projectDirectory, string? evaluatedOutputPath, string? intermediateAssemblyPath, string expected)
    {
        IReadOnlyList<string> paths = IntermediateOutputTree.AssetsPathsOf(
            projectDirectory, evaluatedOutputPath, intermediateAssemblyPath);

        paths.ShouldBe(expected.Split(';')
            .Select(Path.GetFullPath));
    }

    [Fact]
    public void AssetsPathsOf_DefaultLayout_NeverStampsOnePathTwice()
    {
        // The default location and the intermediate root are the same directory in the default layout, and a
        // duplicated stamp would be a second stat and a second entry in every persisted manifest.
        IReadOnlyList<string> paths = IntermediateOutputTree.AssetsPathsOf(
            "/r/P", "/r/P/bin/Debug/net10.0/A.dll", "/r/P/obj/Debug/net10.0/A.dll");

        paths.Distinct()
            .Count()
            .ShouldBe(paths.Count);
    }
}
