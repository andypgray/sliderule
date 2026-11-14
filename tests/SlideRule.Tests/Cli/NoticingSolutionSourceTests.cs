using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Replay;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <see cref="NoticingSolutionSource" /> warns once, at the first acquisition of either kind, and hands a closure
///     request to the source it wraps, so a run that falls back from an invalid capture still loads only a closure.
/// </summary>
public sealed class NoticingSolutionSourceTests
{
    [Fact]
    public async Task AcquireClosureThenWhole_WarnsOnceAndForwardsTheClosureRequest()
    {
        using var inner = new RecordingSource();
        var error = new StringWriter();
        var source = new NoticingSolutionSource("the capture is stale", error, inner);

        using SolutionHandle closure = await source.AcquireClosureAsync("App.sln", ["A.csproj"], Ct);
        using SolutionHandle whole = await source.AcquireAsync("App.sln", Ct);

        error.ToString()
            .ShouldBe($"warning: the capture is stale{Environment.NewLine}");
        inner.Calls.ShouldBe(["closure A.csproj", "whole"]);
        closure.IsClosure.ShouldBeTrue();
        whole.IsClosure.ShouldBeFalse();
    }

    [Fact]
    public void HoldsLoaded_AnswersAsTheWrappedSourceDoes_WithoutANotice()
    {
        // A wrapped warm source that holds the solution must still read as holding it, or a built-DLL explain
        // over it loses the stale-spec verdict the warm session can give; asking acquires nothing.
        using var inner = new RecordingSource { Loaded = "App.sln" };
        var error = new StringWriter();
        ISolutionSource source = new NoticingSolutionSource("the capture is stale", error, inner);

        source.HoldsLoaded("App.sln").ShouldBeTrue();
        source.HoldsLoaded("Other.sln").ShouldBeFalse();
        error.ToString().ShouldBeEmpty();
        inner.Calls.ShouldBeEmpty();
    }

    // Hands back an empty solution and records which kind of acquisition it was asked for.
    private sealed class RecordingSource : ISolutionSource, IDisposable
    {
        private readonly AdhocWorkspace workspace = new();

        public List<string> Calls { get; } = [];

        /// <summary>The one solution path this source reports holding loaded, or null for none.</summary>
        public string? Loaded { get; init; }

        public bool HoldsLoaded(string solutionPath)
        {
            return solutionPath == Loaded;
        }

        public void Dispose()
        {
            workspace.Dispose();
        }

        public Task<SolutionHandle> AcquireAsync(string solutionPath, CancellationToken ct)
        {
            Calls.Add("whole");
            return Task.FromResult(Handle(false));
        }

        public Task<SolutionHandle> AcquireClosureAsync(
            string solutionPath, IReadOnlyCollection<string> projectFiles, CancellationToken ct)
        {
            Calls.Add($"closure {string.Join(",", projectFiles)}");
            return Task.FromResult(Handle(true));
        }

        private SolutionHandle Handle(bool isClosure)
        {
            return new SolutionHandle(
                workspace.CurrentSolution, WorkspaceDiagnostics.None, null,
                CanonicalProjectFiles.Of(workspace.CurrentSolution), isClosure: isClosure);
        }
    }
}
