using Shouldly;
using SlideRule.Cli.Rendering;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     What <c>render</c> counts against Codex's default budget for <c>AGENTS.md</c> files: the root block on its
///     own, and each card with every file above it. The rule is a function of paths and sizes, so no row touches
///     the disk.
/// </summary>
public sealed class CodexInstructionBudgetTests
{
    private const int Limit = CodexInstructionBudget.DefaultBytes;

    private const string Cure = "raise project_doc_max_bytes in Codex's config.toml";

    // A solution directory spelled for the host. Nothing is ever written here.
    private static readonly string Solution = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "codex-budget", "repo"));

    private static readonly string Root = Path.Combine(Solution, "AGENTS.md");

    [Fact]
    public void Warning_ARootAtTheLimit_SaysNothing()
    {
        CodexInstructionBudget.Warning(Solution, [(Root, Limit)])
            .ShouldBeNull();
    }

    [Fact]
    public void Warning_ARootOneByteOverTheLimit_NamesTheRootItsSizeAndTheCure()
    {
        CodexInstructionBudget.Warning(Solution, [(Root, Limit + 1)])
            .ShouldBe(
                "AGENTS.md is 32,769 bytes, and by default Codex reads only the first 32,768 bytes of AGENTS.md "
                + $"files, so its sessions lose everything after that; {Cure}, or shorten AGENTS.md.");
    }

    [Fact]
    public void Warning_ARootAndCardAtTheLimitTogether_SaysNothing()
    {
        CodexInstructionBudget.Warning(Solution, [(Root, Limit - 100), (Card("Domain"), 100)])
            .ShouldBeNull();
    }

    [Fact]
    public void Warning_ARootAndCardOneByteOverTogether_NamesTheCardAndTheTotal()
    {
        CodexInstructionBudget.Warning(Solution, [(Root, Limit - 100), (Card("Domain"), 101)])
            .ShouldBe(
                "the files from AGENTS.md down to Domain/AGENTS.md total 32,769 bytes, and by default Codex reads "
                + "only the first 32,768 bytes of AGENTS.md files, so a session started in Domain loses everything "
                + $"after that; {Cure}, or shorten those files.");
    }

    [Fact]
    public void Warning_ANestedCard_CountsEveryCardAboveIt()
    {
        // Each pair fits; only the three together cross, so a total that skipped the middle card would say nothing.
        CodexInstructionBudget.Warning(
                Solution, [(Root, Limit - 200), (Card("Core"), 100), (Card("Core", "Model"), 101)])
            .ShouldNotBeNull()
            .ShouldStartWith("the files from AGENTS.md down to Core/Model/AGENTS.md total 32,769 bytes,");
    }

    [Fact]
    public void Warning_SiblingCards_NeverAddToEachOther()
    {
        // Domain and DomainEvents share a prefix but not a directory: a test by string prefix would add them up.
        CodexInstructionBudget.Warning(
                Solution, [(Root, Limit - 200), (Card("Domain"), 200), (Card("DomainEvents"), 200)])
            .ShouldBeNull();
    }

    [Fact]
    public void Warning_ARootOverTheLimitBesideALargerChain_NamesTheRoot()
    {
        CodexInstructionBudget.Warning(Solution, [(Root, Limit + 1), (Card("Domain"), 5_000)])
            .ShouldNotBeNull()
            .ShouldStartWith("AGENTS.md is 32,769 bytes,");
    }

    [Fact]
    public void Warning_TwoCardsOverTheLimit_NamesTheLargerTotal()
    {
        CodexInstructionBudget.Warning(
                Solution, [(Root, Limit - 100), (Card("Web"), 200), (Card("Domain"), 300)])
            .ShouldNotBeNull()
            .ShouldStartWith("the files from AGENTS.md down to Domain/AGENTS.md total 32,968 bytes,");
    }

    private static string Card(params string[] directories)
    {
        return Path.Combine([Solution, .. directories, "AGENTS.md"]);
    }
}
