using System.Text.Json;
using Shouldly;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end <c>graph</c> against the DriftedApp fixture — the solution in the suite where two projects
///     declare one full name in <em>separate</em> files whose members differ: <c>DriftedApp.Core</c> and
///     <c>DriftedApp.Core.Ansi</c> each declare <c>DriftedApp.StringExtensions</c>, neither references the
///     other, and only the core's copy reaches <c>DriftedApp.Cell</c>. Beside them, <c>DriftedApp.Json</c> and
///     <c>DriftedApp.Testing</c> each compile a source-only package's <c>Polyfills</c> types into themselves, on
///     two frameworks each.
/// </summary>
/// <remarks>
///     <para>
///         The bed holds two ways to draw a phantom edge through a shared name. Charging every declarer with
///         every reference any copy made would render the core copy's reference to <c>Cell</c> as
///         <c>DriftedApp.Core.Ansi -&gt; DriftedApp.Core</c>. Crediting a reference into the shared name to the
///         declarer whose facts the node carries would render <c>DriftedApp.Markup</c>, which references the
///         satellite alone, as reaching the core.
///     </para>
///     <para>
///         <b>Why the edge list is asserted whole.</b> The satellite declares no project reference at all, so
///         any edge out of it is a phantom, and the markup project can see the core through nothing. The
///         client's genuine reference into the core is the control that must survive. The two package importers
///         reference only their own copies of the shims, so they add no edge at all.
///     </para>
///     <para>
///         <see cref="LinkedSourceGraphE2ETests" /> carries the linked-file shape, where every declarer compiles
///         one source text; this carries the drifted one, where they do not.
///     </para>
/// </remarks>
public sealed class DriftedCopiesGraphE2ETests
{
    // Read in place rather than leased: graph writes nothing into the tree, and the startup sweep has already
    // restored every TestSolutions bed in the output directory.
    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "DriftedApp", "DriftedApp.slnx");

    [Fact]
    public async Task Graph_DriftedCopiesOfOneName_RenderTheDeclaredEdgesAndStillStateTheSharedName()
    {
        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", SolutionPath, "--json");

        // Assert — the two edges the project files declare and no other: nothing between the two declarers in
        // either direction, and the markup project's reference at the copy it bound.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        GraphSurveyJson.ProjectEdges(document)
            .ShouldBe([
                ("DriftedApp.Client", "DriftedApp.Core", 1),
                ("DriftedApp.Markup", "DriftedApp.Core.Ansi", 1)
            ]);

        // Each shared name IS declared twice, so the coverage statement keeps both declarers: only an edge drawn
        // through a shared name has to be credited to the copy that made or bound it. An importer compiles the
        // shims on two frameworks and is still one declarer of each.
        MultiplyDeclaredTypes(document)
            .ShouldBe([
                ("DriftedApp.StringExtensions", "DriftedApp.Core, DriftedApp.Core.Ansi"),
                ("Polyfills.Guard", "DriftedApp.Json, DriftedApp.Testing"),
                ("Polyfills.MD5Polyfill", "DriftedApp.Json, DriftedApp.Testing")
            ]);
        document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .Single(entry => entry.GetProperty("type")
                .GetString() == "DriftedApp.StringExtensions")
            .GetProperty("factsFollow")
            .GetString()
            .ShouldBe("DriftedApp.Core");
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // Each entry as its name and its declarers joined in the order the document lists them, so a red shows the whole
    // roster rather than the first project that differed.
    private static IReadOnlyList<(string Type, string DeclaredBy)> MultiplyDeclaredTypes(JsonDocument document)
    {
        return document.RootElement.GetProperty("multiplyDeclaredTypes")
            .EnumerateArray()
            .Select(entry => (
                entry.GetProperty("type")
                    .GetString()!,
                string.Join(", ", GraphSurveyJson.Strings(entry, "declaredBy"))))
            .ToList();
    }
}
