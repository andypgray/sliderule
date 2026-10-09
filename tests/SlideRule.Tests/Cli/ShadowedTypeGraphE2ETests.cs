using System.Text.Json;
using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The survey's package-facing coverage statements end to end, over the <c>ShadowedApp</c> fixture: a
///     project declaring a full name that a referenced package also supplies, and the private-reference key
///     beside it — the product project declares its one package with <c>PrivateAssets="all"</c> and the test
///     project declares none. <c>Roslyn.ShadowedPackageNameTests</c> pins the model and the summary; these pin
///     the two documents a reader actually holds, because a key that is computed correctly and rendered
///     nowhere is not a statement.
/// </summary>
/// <remarks>
///     One class for both statements because the fixture lease is keyed by the calling source file: a second
///     file over this fixture would take a copy, a restore and a cold extraction of its own to ask the same
///     <c>graph --json</c> question.
/// </remarks>
public sealed class ShadowedTypeGraphE2ETests
{
    private const string ShadowedHeading = "Type names a referenced assembly also supplies:";
    private const string Shadowed = "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
    private const string Package = "Microsoft.Extensions.DependencyInjection.Abstractions";

    [Fact]
    public async Task Graph_ShadowedType_IsReadableFromTheDocumentAlone()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/ShadowedApp", "ShadowedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — everything a rule author needs before writing a rule about the name, without reaching for
        // the source: that it means two types, which project wrote one, which assembly supplies the other,
        // and who binds the assembly rather than the declaration.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement entry = document.RootElement.GetProperty("shadowedTypes")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        entry.ShouldSatisfyAllConditions(
            () => entry.GetProperty("type")
                .GetString()
                .ShouldBe(Shadowed),
            () => entry.GetProperty("declaredBy")
                .GetString()
                .ShouldBe("ShadowedApp.Product.Tests"),
            () => GraphSurveyJson.Strings(entry, "suppliedBy")
                .ShouldBe([Package]),
            () => GraphSurveyJson.Strings(entry, "boundFromAssemblyBy")
                .ShouldBe(["ShadowedApp.Product"]));
    }

    [Fact]
    public async Task Graph_ShadowedType_ElidesToACountAtSkeletonGrain()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/ShadowedApp", "ShadowedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json", "--grain", "skeleton");

        // Assert — the count is what tells elision from absence. A section that simply vanished at the
        // coarsest grain would read as the good state, which for a coverage statement is the opposite of
        // the truth.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        document.RootElement.TryGetProperty("shadowedTypes", out _)
            .ShouldBeFalse();
        document.RootElement.GetProperty("shadowedTypeCount")
            .GetInt32()
            .ShouldBe(1);
    }

    [Fact]
    public async Task Graph_HumanSurvey_CarriesTheShadowedSectionBesideTheMultiplyDeclaredOne()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/ShadowedApp", "ShadowedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath);

        // Assert — one line per name, carrying the same fact the document does, so a terminal reader is not
        // sent to --json for it.
        result.ShouldSucceed();
        Section(result.Out, ShadowedHeading)
            .ShouldBe([
                $"  {Shadowed} — declared by ShadowedApp.Product.Tests; "
                + $"also supplied by {Package}, which ShadowedApp.Product binds"
            ]);
    }

    [Fact]
    public async Task Graph_PrivateAssetsReference_IsNamedInBothKeysOnTheProductRowAlone()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace("TestSolutions/ShadowedApp", "ShadowedApp.slnx");

        // Act
        CliResult result = await CliRunner.InvokeAsync("graph", fixture.SolutionPath, "--json");

        // Assert — the product's row lists the reference among what it declares and names it again as
        // private; the test project's row, which declares nothing, carries no private key rather than an
        // empty one.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement product = GraphSurveyJson.Project(document, "ShadowedApp.Product");
        JsonElement tests = GraphSurveyJson.Project(document, "ShadowedApp.Product.Tests");

        product.ShouldSatisfyAllConditions(
            () => GraphSurveyJson.Strings(product, "packageReferences")
                .ShouldBe([Package]),
            () => GraphSurveyJson.Strings(product, "privatePackageReferences")
                .ShouldBe([Package]));
        tests.ShouldSatisfyAllConditions(
            () => GraphSurveyJson.Strings(tests, "packageReferences")
                .ShouldBeEmpty(),
            () => tests.TryGetProperty("privatePackageReferences", out _)
                .ShouldBeFalse());
    }

    // The lines under one heading, up to the blank line closing the section.
    private static IReadOnlyList<string> Section(string output, string heading)
    {
        List<string> lines = output.NormalizedLines()
            .Split('\n')
            .ToList();
        int start = lines.FindIndex(line => line.Trim() == heading);
        start.ShouldBeGreaterThanOrEqualTo(0, $"no '{heading}' section in:\n{output}");

        return lines.Skip(start + 1)
            .TakeWhile(line => line.Trim()
                .Length > 0)
            .ToList();
    }
}
