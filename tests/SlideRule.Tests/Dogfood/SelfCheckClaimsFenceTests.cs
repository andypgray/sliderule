using Xunit;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     The self-check page's <c>explain --type</c> excerpt is a run of this repository's spec over this
///     repository: every line it quotes is a line of the answer for <c>CommandEntryPoint</c>, in order.
/// </summary>
public sealed class SelfCheckClaimsFenceTests
{
    private const string TypeName = "SlideRule.Cli.CommandEntryPoint";

    [Fact]
    public async Task ClaimsFence_IsAnExcerptOfWhatExplainPrintsForTheShell()
    {
        // Arrange: the answer as explain --type composes it, over the model the shared self-spec gates read.
        CodebaseModel codebase = await SelfSpecTests.WholeCodebase.Value;
        TypeNode type = codebase.Types.Single(candidate => candidate.FullName == TypeName && !candidate.IsExternal);
        TypeClaims claims = TypeClaims.Of(SelfSpecTests.SelfModel, new ClaimTracer(codebase), type);

        // Act
        string answer = string.Join("\n", ClaimsFormatter.Lines(claims, RepoRoot.Directory));

        // Assert
        CapturedFences.SelfCheckClaims.ShouldBeAnInOrderExcerptOf(answer, $"The answer for {TypeName}:\n{answer}");
    }
}
