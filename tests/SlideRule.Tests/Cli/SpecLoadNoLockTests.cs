using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     Loading a spec must not leave the spec project's build output locked.
/// </summary>
/// <remarks>
///     The model <see cref="ModelPipeline.LoadModel" /> returns roots the spec's <c>Type</c> references, so
///     the collectible load context is never actually collected while that model lives. In the long-lived MCP
///     server a path load would therefore pin the spec DLL and its dependencies for the whole host lifetime,
///     and every <c>dotnet build</c> of the spec project would fail to overwrite them until the server was
///     killed, which for a stdio server ends the session's architecture queries: the client never reconnects
///     one. <c>SpecLoadContext</c> loads from bytes to keep that from happening; this pins it.
/// </remarks>
public sealed class SpecLoadNoLockTests
{
    [Fact]
    public void LoadModel_AfterLoadingASpecSuccessfully_LeavesEveryAssemblyBesideItReplaceable()
    {
        // Arrange: an intact staged copy, so taking an exclusive handle below cannot contend with the
        // real fixture output other tests read.
        string stagedSpec = SpecOutputStager.StageWithout(CliRunner.CleanSpecDll);

        // Act
        ArchitectureModel model = ModelPipeline.LoadModel(stagedSpec);

        // Assert: a real model came back, so the assertion below cannot pass by never having loaded.
        model.Rules.ShouldNotBeEmpty();

        string stagedDirectory = Path.GetDirectoryName(stagedSpec)!;
        foreach (string assemblyPath in Directory.GetFiles(stagedDirectory, "*.dll"))
            Should.NotThrow(
                () => OpenWithNoSharing(assemblyPath),
                $"'{Path.GetFileName(assemblyPath)}' is still locked after the spec load, so a build "
                + "of the spec project would fail to overwrite it.");
    }

    /// <summary>
    ///     Opens the file such that the call fails if any other handle is open on it — the cheapest
    ///     read-only stand-in for the overwrite MSBuild performs when it copies a fresh build over.
    /// </summary>
    private static void OpenWithNoSharing(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
    }
}
