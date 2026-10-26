using System.Runtime.CompilerServices;
using Shouldly;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The pieces of <see cref="WorkspaceLoader" /> that pin without loading a workspace.
/// </summary>
/// <remarks>
///     <para>
///         The refusal a filter that cannot be read reaches the user as. Roslyn signals every filter fault the
///         same way — a bare exception carrying "Failed to load solution filter" and nothing else — so this
///         sentence is the entire difference between a stack trace and an actionable answer, and it has to name
///         both the file and what a well-formed filter looks like. The end-to-end route to it is
///         <see cref="SlideRule.Tests.Cli.FilteredSolutionE2ETests" />'s malformed-filter row.
///     </para>
///     <para>
///         The per-flow load count, which the load probe and <c>CliResult</c> both install around the same
///         call. A counter installed further out keeps counting through one installed inside it, and two calls
///         running at once never count into each other's counter. The load is counted before the workspace
///         opens anything, so a missing solution is enough to drive it, and the assertions read only the
///         per-flow counters, never the process-wide total that the rest of the suite moves.
///     </para>
///     <para>
///         The closure load's refusals: a subset the solution cannot express as a filter answers null, so the caller
///         loads the whole solution instead, and it answers before any workspace opens.
///     </para>
/// </remarks>
public sealed class WorkspaceLoaderTests
{
    [Fact]
    public void UnreadableFilterMessage_AFilterPath_NamesTheFileThenWhatAWellFormedFilterIs()
    {
        string message = WorkspaceLoader.UnreadableFilterMessage("/repo/Broken.slnf");

        message.ShouldBe(
            "Could not read the solution filter '/repo/Broken.slnf'.\n"
            + "A filter must be well-formed JSON whose 'solution' path resolves to a .sln or .slnx, and "
            + "every project it lists must be a member of that solution.");
    }

    [Fact]
    public async Task CountLoadsOnThisFlow_TwoConcurrentInnerCounters_EachCountsItsOwnLoadAndTheOuterCountsBoth()
    {
        string missingSolution = Path.Combine(TestTempRoot.For("loader-flow"), "Missing.sln");
        StrongBox<long> outer = WorkspaceLoader.CountLoadsOnThisFlow();

        long[] inner = await Task.WhenAll(
            ShouldFailLoadingOnceAsync(missingSolution), ShouldFailLoadingOnceAsync(missingSolution));

        inner.ShouldBe([1L, 1L]);
        outer.Value.ShouldBe(2);
    }

    [Fact]
    public async Task LoadClosureAsync_AProjectTheSolutionDoesNotDeclare_ReturnsNullWithoutLoading()
    {
        using TempDirectory temp = TestTempRoot.Fresh("loader-closure");
        string solution = temp.WriteFile(["App.slnx"], "<Solution>\n  <Project Path=\"A/A.csproj\" />\n</Solution>\n");
        StrongBox<long> loads = WorkspaceLoader.CountLoadsOnThisFlow();

        LoadedSolution? loaded = await WorkspaceLoader.LoadClosureAsync(solution, [temp.PathOf("B", "B.csproj")], ct: Ct);

        loaded.ShouldBeNull();
        loads.Value.ShouldBe(0);
    }

    [Fact]
    public async Task LoadClosureAsync_ASolutionThatDoesNotParse_ReturnsNullRatherThanThrowing()
    {
        using TempDirectory temp = TestTempRoot.Fresh("loader-closure");
        string solution = temp.WriteFile(["App.slnx"], "<Solution>\n  <Project Path=\"A/A.csproj\"\n");

        LoadedSolution? loaded = await WorkspaceLoader.LoadClosureAsync(solution, [temp.PathOf("A", "A.csproj")], ct: Ct);

        loaded.ShouldBeNull();
    }

    private static async Task<long> ShouldFailLoadingOnceAsync(string missingSolution)
    {
        StrongBox<long> inner = WorkspaceLoader.CountLoadsOnThisFlow();

        await Should.ThrowAsync<FileNotFoundException>(() => WorkspaceLoader.LoadAsync(missingSolution, ct: Ct));

        return inner.Value;
    }
}
