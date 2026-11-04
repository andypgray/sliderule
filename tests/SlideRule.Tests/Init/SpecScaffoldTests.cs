using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary>
///     <see cref="SpecScaffold" />: the scaffold <c>init</c> writes is the one the committed recipe publishes, with
///     only the version, the framework, the namespace and the rules changed.
/// </summary>
public sealed class SpecScaffoldTests
{
    [Fact]
    public void Embedded_IsTheCommittedRecipesTwoFences()
    {
        // Arrange
        QuotedFence project = QuotedFence.ShouldFindOne(
            DocPaths.DeriveSpecPrompt, SpecScaffold.ProjectMarker, "where the scaffold's spec csproj should be exactly one");
        QuotedFence specClass = QuotedFence.ShouldFindOne(
            DocPaths.DeriveSpecPrompt, "public sealed class ArchitectureSpec : IArchitectureSpec",
            "where the scaffold's spec class should be exactly one");

        // Act
        SpecScaffold scaffold = SpecScaffold.Embedded;

        // Assert
        scaffold.ProjectTemplate.ShouldBe(Joined(project));
        scaffold.SpecClassTemplate.ShouldBe(Joined(specClass));
    }

    [Fact]
    public void Project_SubstitutesTheVersionAndTheFrameworkAndNothingElse()
    {
        SpecScaffold scaffold = SpecScaffold.Embedded;

        string project = scaffold.Project("1.2.3", "net48");

        project.ShouldBe(scaffold.ProjectTemplate
            .Replace("Version=\"...\"", "Version=\"1.2.3\"", StringComparison.Ordinal)
            .Replace(SpecScaffold.FrameworkLiteral, "<TargetFrameworks>net48</TargetFrameworks>", StringComparison.Ordinal));
    }

    [Fact]
    public void SpecClass_WithAHeaderAndRules_PutsTheHeaderFirstAndTheRulesInsideDefine()
    {
        SpecScaffold scaffold = SpecScaffold.Embedded;

        string specClass = scaffold.SpecClass("Acme.Architecture", "// Copyright Acme.\n\n", ["// one", "", "// two"]);

        specClass.ShouldBe(
            """
                // Copyright Acme.

                using SlideRule;

                namespace Acme.Architecture;

                public sealed class ArchitectureSpec : IArchitectureSpec
                {
                    public void Define(Arch arch)
                    {
                        // one

                        // two
                    }
                }

                """.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void SpecClass_WithNoHeaderAndNoRules_IsTheRecipesClassInTheNamespace()
    {
        SpecScaffold scaffold = SpecScaffold.Embedded;

        string specClass = scaffold.SpecClass("Acme.Architecture", null, []);

        specClass.ShouldBe(scaffold.SpecClassTemplate.Replace(
            SpecScaffold.NamespaceLiteral, "namespace Acme.Architecture;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Version=\"...\"")]
    [InlineData(SpecScaffold.FrameworkLiteral)]
    [InlineData(SpecScaffold.NamespaceLiteral)]
    [InlineData("    {\n    }")]
    public void FromRecipe_ARecipeMissingALiteralInitReplaces_FailsRatherThanShipTheTemplate(string literal)
    {
        // Arrange
        string recipe = RepoRoot.ReadText(DocPaths.DeriveSpecPrompt).ReplaceLineEndings("\n");
        string broken = recipe.Replace(literal, "", StringComparison.Ordinal);
        broken.ShouldNotBe(recipe, "the arrange should have removed the literal from the recipe");

        // Act
        var failure = Should.Throw<InvalidOperationException>(() => SpecScaffold.FromRecipe(broken));

        // Assert
        failure.Message.ShouldStartWith("The embedded derive-spec recipe");
    }

    private static string Joined(QuotedFence fence)
    {
        return string.Concat(fence.Lines.Select(static line => line + "\n"));
    }
}
