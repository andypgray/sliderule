using System.Runtime.CompilerServices;
using Shouldly;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The text and bookkeeping of <see cref="WorkspaceLoader" />, pinned without a workspace wherever the subject
///     allows one to be left out.
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
///     <para>
///         The load warnings the workspace itself never reports, each driven through a real load: an <c>Analyzer</c>
///         item naming a file that does not exist leaves an unresolved reference for the load boundary to strip, and a
///         <c>Reference</c> whose HintPath is missing is dropped by the build before the compiler sees it. The legacy
///         row also holds the framework references the build resolves without a HintPath out of the warning.
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

    [Fact]
    public async Task LoadAsync_AnAnalyzerItemNamingAMissingFile_LogsTheStrippedAnalyzerNotice()
    {
        using TempDirectory temp = TestTempRoot.Fresh("loader-stripped-analyzer");
        temp.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Analyzer Include="analyzers/Missing.Analyzer.dll" />
              </ItemGroup>
            </Project>
            """);
        temp.WriteFile(["App", "Widget.cs"], "namespace App { public sealed class Widget { } }\n");
        string solution = temp.WriteFile(["App.slnx"], "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
        var diagnostics = new List<string>();

        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solution, diagnostics.Add, Ct);

        diagnostics.ShouldBe(
        [
            "Load stripped 1 unresolved analyzer reference; an analyzer or source generator the build names was "
            + "missing from disk, so any code it generates is absent from the model."
        ]);
    }

    [Fact]
    public async Task LoadAsync_AnSdkProjectReferenceWhoseHintPathIsMissing_LogsTheUnresolvedReference()
    {
        using TempDirectory temp = TestTempRoot.Fresh("loader-unresolved-reference");
        string csproj = temp.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="Vendor.Missing">
                  <HintPath>lib/Vendor.Missing.dll</HintPath>
                </Reference>
              </ItemGroup>
            </Project>
            """);
        temp.WriteFile(["App", "Widget.cs"], "namespace App { public sealed class Widget { } }\n");
        string solution = temp.WriteFile(["App.slnx"], "<Solution>\n  <Project Path=\"App/App.csproj\" />\n</Solution>\n");
        var diagnostics = new List<string>();

        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solution, diagnostics.Add, Ct);

        diagnostics.ShouldBe([UnresolvedReferences.Message(csproj, ["Vendor.Missing"])]);
    }

    [Fact]
    public async Task LoadAsync_ALegacyProjectReferenceWhoseHintPathIsMissing_LogsItAloneBesideTheFrameworkReferences()
    {
        CliRunner.SkipUnlessLoadable(CliRunner.ClassicAppSolution);
        using var fixture = new TempFixtureWorkspace("LegacySolutions/ClassicApp", "ClassicApp.sln", false);
        string csproj = fixture.PathOf("Classic.Billing", "Classic.Billing.csproj");
        FixtureEdits.EditOnDisk(
            csproj,
            content => content.Replace(
                "<Reference Include=\"System.Data\" />",
                "<Reference Include=\"System.Data\" />"
                + "<Reference Include=\"Vendor.Missing\"><HintPath>lib\\Vendor.Missing.dll</HintPath></Reference>",
                StringComparison.Ordinal));
        var diagnostics = new List<string>();

        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(fixture.SolutionPath, diagnostics.Add, Ct);

        diagnostics.ShouldBe([UnresolvedReferences.Message(csproj, ["Vendor.Missing"])]);
    }

    private static async Task<long> ShouldFailLoadingOnceAsync(string missingSolution)
    {
        StrongBox<long> inner = WorkspaceLoader.CountLoadsOnThisFlow();

        await Should.ThrowAsync<FileNotFoundException>(() => WorkspaceLoader.LoadAsync(missingSolution, ct: Ct));

        return inner.Value;
    }
}
