using Shouldly;
using SlideRule.Rendering;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The shape gate over the four example solutions' committed <c>ARCHITECTURE.md</c> drawings. It reads
///     the committed files and nothing else, so it runs everywhere and names the failures a byte comparison
///     would only report after somebody re-rendered: a missing fence, a caption naming the wrong spec, or a
///     survey that drew the spec project's own package references.
/// </summary>
/// <remarks>
///     <para>
///         Byte-currency belongs to CI's examples job, which builds each example and runs
///         <c>render --diagram … --check</c> over it. That job is ubuntu-only, so a Windows checkout gets no
///         signal from it at all.
///     </para>
///     <para>
///         DocHygiene rather than Dogfood, because this needs no workspace and does not belong in
///         <c>SelfSpecTests</c>. That class excludes <c>examples/</c> from its render
///         and orphan gates on purpose, since those blocks belong to other specs; a file-shape assertion
///         over a committed drawing is not that gate and does not want its exclusion.
///     </para>
/// </remarks>
public sealed class ExampleArchitectureDocTests
{
    /// <summary>
    ///     The four committed drawings, each with the solution its survey fence is captioned from and
    ///     the spec assembly its law fence is captioned from.
    /// </summary>
    private static readonly (string Path, string Solution, string Spec)[] Pinned =
    [
        ("examples/Meridian/ARCHITECTURE.md", "Meridian.slnx", "Meridian.Architecture"),
        ("examples/Meridian.Quoting/ARCHITECTURE.md", "Meridian.Quoting.slnx", "Meridian.Quoting.Architecture"),
        ("examples/Meridian.Operations/ARCHITECTURE.md", "Meridian.Operations.slnx", "Meridian.Operations.Architecture"),
        ("examples/Meridian.Interchange/ARCHITECTURE.md", "Meridian.Interchange.slnx", "Meridian.Interchange.Architecture")
    ];

    public static TheoryData<string, string, string> ExampleDocs => [.. Pinned];

    [Theory]
    [MemberData(nameof(ExampleDocs))]
    public void ExampleArchitectureDoc_CarriesBothFencesAroundHandWrittenProse(
        string relativePath, string solutionFileName, string specName)
    {
        // Arrange
        string committed = RepoRoot.ReadText(relativePath);

        // Act
        string body = committed.ShouldCarryOneManagedBlock($"{relativePath} must carry exactly one managed block.");

        // Assert
        body.Occurrences("```mermaid")
            .ShouldBe(2, $"{relativePath} must carry both drawings.");
        body.ShouldContain(
            $"accTitle: Codebase survey: {solutionFileName}",
            $"{relativePath}'s survey fence must be captioned from {solutionFileName}.");
        body.ShouldContain(
            $"accTitle: Architecture law: {specName}",
            $"{relativePath}'s law fence must be captioned from {specName}, not another example's spec.");
        Prose(committed, body)
            .ShouldNotBeNullOrWhiteSpace($"{relativePath} carries no hand-written prose around its block.");
    }

    /// <summary>
    ///     The gate on the survey fence's declared-members default, read straight off the committed
    ///     artifacts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each example's spec project references the SlideRule packages it is written against, so
    ///         the workspace loads this repository's projects on every example render; only the fence's
    ///         membership filter keeps them off a page captioned "Projects in this solution". The render
    ///         lines carry no scope flag, which is the point — so if that default regressed there would be
    ///         nothing else between the passengers and the four committed drawings.
    ///     </para>
    ///     <para>
    ///         The second half closes the same hole from the other side: whatever narrows the fence could
    ///         narrow it too far and drop a project the example owns, and reading the drawing
    ///         alone cannot say which it did. The solution file can.
    ///     </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(ExampleDocs))]
    public void ExampleArchitectureDoc_SurveysThisExamplesProjectsAndNoOthers(
        string relativePath, string solutionFileName, string specName)
    {
        // Arrange
        string body = ManagedBlock.ExtractBody(RepoRoot.ReadText(relativePath))!;
        string solution = relativePath[..(relativePath.LastIndexOf('/') + 1)] + solutionFileName;

        // Act: a survey labels a node with the csproj's base name, and the names are read from the solution
        // rather than listed here, so a new project in an example reds this rather than going silently undrawn.
        List<string> missing = SolutionInventory.DeclaredProjectPaths(solution)
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .Where(name => !body.Contains($"[\"{name}\"]", StringComparison.Ordinal))
            .ToList();

        // Assert
        body.ShouldNotContain(
            "SlideRule",
            $"{relativePath} draws this repository's own projects, which {solutionFileName} does not "
            + "declare; the survey fence's declared-members default has regressed.");
        missing.ShouldReportNothing(
            $"{relativePath} omits project(s) {solutionFileName} declares, so the survey fence narrowed "
            + $"further than declared membership (spec {specName})");
    }

    /// <summary>
    ///     The inventory: a fifth example cannot ship a drawing that no case above covers, and so cannot
    ///     ship one whose currency nothing gates.
    /// </summary>
    [Fact]
    public void EveryTrackedExampleArchitectureDoc_IsOneOfTheFourPinned()
    {
        // Arrange
        List<string> pinned = Pinned.Select(static entry => entry.Path)
            .ToList();

        // Act
        List<string> uncovered = TrackedFiles.All
            .Where(static path => path.StartsWith("examples/", StringComparison.Ordinal))
            .Where(static path => Path.GetFileName(path) == "ARCHITECTURE.md")
            .Where(path => !pinned.Contains(path))
            .ToList();

        // Assert
        uncovered.ShouldReportNothing("Example drawing(s) outside the pinned set");
    }

    // Everything outside the managed block: the hand-written preamble and the trailing prose.
    private static string Prose(string committed, string body)
    {
        return committed.Replace(body, string.Empty, StringComparison.Ordinal)
            .Replace(ManagedBlock.BeginMarker, string.Empty, StringComparison.Ordinal)
            .Replace(ManagedBlock.EndMarker, string.Empty, StringComparison.Ordinal);
    }
}
