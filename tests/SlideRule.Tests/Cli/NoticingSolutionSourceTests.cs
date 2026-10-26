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

    // Hands back an empty solution and records which kind of acquisition it was asked for.
    private sealed class RecordingSource : ISolutionSource, IDisposable
    {
        private readonly AdhocWorkspace workspace = new();

        public List<string> Calls { get; } = [];

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
