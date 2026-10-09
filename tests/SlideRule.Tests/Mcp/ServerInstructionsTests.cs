using Shouldly;
using Xunit;
using SlideRule.Cli.Mcp.Infrastructure;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Pins the embedded server instructions under the client's truncation cliff, so growth past it
///     turns up as a red here rather than as a silent cut in every session.
/// </summary>
/// <remarks>
///     Claude Code renders MCP server instructions into the session system prompt whole only up to
///     2,048 UTF-16 code units; anything longer is sliced there and suffixed "… [truncated]" —
///     measured: the constant and the slice are in the client binary. <see cref="string.Length" />
///     counts the same units the client slices, so this pin measures exactly what the client
///     measures. When it reds, cut or move content — tool descriptions are the per-fetch channel —
///     never raise the budget. The unbound banner (<see cref="ServerInstructions.For" />) prepends
///     further text ahead of the same cliff, which is why the file keeps its most droppable lines
///     last.
/// </remarks>
public sealed class ServerInstructionsTests
{
    /// <summary>The observed Claude Code cliff: instructions longer than this are cut mid-text.</summary>
    private const int ClientTruncationCliff = 2048;

    /// <summary>
    ///     Ceiling on the unbound banner itself. Everything it spends is spent ahead of the same cliff, so
    ///     it is a direct deduction from how much of <see cref="ServerInstructions.Text" /> an unbound
    ///     server's client ever sees.
    /// </summary>
    /// <remarks>
    ///     The banner carries <see cref="ServerInstructions.UnboundCallCoda" />, so an unbound client buys a
    ///     recovery it can act on at the price of characters of the file's tail — the trade the file's
    ///     ordering was designed to make payable. This is also the coda's only budget: it is inside the
    ///     banner, so bounding the banner bounds it.
    ///     <para>
    ///         The coda spells a pinned version, so the banner's length moves with the version string. The
    ///         ceiling is set clear of the measured length rather than against it, so that a version bump
    ///         alone cannot red this fact from a distance.
    ///     </para>
    /// </remarks>
    private const int BannerBudget = 780;

    [Fact]
    public void Text_FitsUnderTheClientTruncationCliff()
    {
        ServerInstructions.Text.Length.ShouldBeLessThanOrEqualTo(ClientTruncationCliff);
    }

    [Fact]
    public void For_BoundServer_IsTheInstructionsUnchanged()
    {
        // Reference equality, not just equal text: a bound server must pay nothing at all for the
        // unbound path existing.
        ServerInstructions.For(null)
            .ShouldBeSameAs(ServerInstructions.Text);
    }

    [Fact]
    public void For_UnboundServer_LeadsWithTheReasonAndKeepsTheInstructionsWhole()
    {
        const string failure = "Multiple solution files found in C:\\repo: Alpha.sln, Beta.slnx.";

        string instructions = ServerInstructions.For(failure);

        instructions.ShouldStartWith("**This server is running but is not bound to a solution.**");
        // Verbatim: the discovery message names the files and the fix, and re-wording it here would
        // make the handshake and the per-call tool error disagree about what went wrong.
        instructions.ShouldContain(failure);
        // A prefix, never a replacement — the tool surface is described in the same words either way.
        instructions.ShouldEndWith(ServerInstructions.Text);
    }

    [Fact]
    public void For_UnboundServer_NamesBothWaysToNameTheSolution()
    {
        string instructions = ServerInstructions.For("No .sln, .slnf or .slnx file found.");

        instructions.ShouldContain("`args`");
        instructions.ShouldContain("SLIDERULE_SOLUTION_PATH");
    }

    [Fact]
    public void For_UnboundServer_PutsTheInSessionRecoveryBeforeTheConfigRemedies()
    {
        // The point of the ordering: a reader who stops early must meet the thing they can do, not the
        // two things they cannot. "x" keeps the discovery text out of it, so neither index can match inside
        // the quoted failure; Text carries neither marker, so neither can match below the banner either.
        string instructions = ServerInstructions.For("x");

        int recovery = instructions.IndexOf(ServerInstructions.UnboundCallCoda, StringComparison.Ordinal);
        int configRemedies = instructions.IndexOf("`args`", StringComparison.Ordinal);

        recovery.ShouldBeGreaterThanOrEqualTo(0);
        configRemedies.ShouldBeGreaterThan(recovery);
    }

    [Fact]
    public void For_UnboundServer_EmbedsTheCallCodaVerbatim()
    {
        // Verbatim, not merely equivalent: the banner and every tool-call error carry the same constant, so
        // the handshake channel and the per-call channel cannot drift into describing different recoveries.
        ServerInstructions.For("x")
            .ShouldContain(ServerInstructions.UnboundCallCoda);
    }

    [Fact]
    public void UnboundCallCoda_NamesARecoveryTheSessionCanPerform()
    {
        // Lowercase `sliderule` is the command, not the product name Text spells SlideRule — the
        // suite's case-sensitive ShouldContain is what keeps those two apart.
        ServerInstructions.UnboundCallCoda.ShouldContain("sliderule graph <solution>");
        ServerInstructions.UnboundCallCoda.ShouldContain("sliderule check <solution>");

        // And the negative half: a remedy an in-session reader cannot take
        // belongs below the one they can, never inside it. Both of these are client-config edits.
        ServerInstructions.UnboundCallCoda.ShouldNotContain("`args`");
        ServerInstructions.UnboundCallCoda.ShouldNotContain("SLIDERULE_SOLUTION_PATH");
    }

    [Fact]
    public void UnboundCallCoda_AlsoNamesTheRecoveryARegistryManifestSessionCanPerform()
    {
        // The population the coda was written for is the one that cannot run the command above it: the
        // registry manifest has nowhere to put a solution, so its session is unbound by construction, and
        // the dnx launch it prescribes runs the package without installing the tool.
        ServerInstructions.UnboundCallCoda.ShouldContain(
            $"dotnet dnx SlideRule.Cli@{ServerVersion.SemVer} --yes -- graph <solution>");

        // Bare `dnx` is dnx.cmd on Windows and a POSIX shell resolves a bare name to .exe alone, so the
        // short spelling is a command not found in the shell an agent drives. Anchored on the backtick that
        // opens the command, because `dotnet dnx` contains the substring the naive form would match.
        ServerInstructions.UnboundCallCoda.ShouldNotContain("`dnx ");
    }

    [Fact]
    public void UnboundCallCoda_SaysWhereTheRegistryManifestRecoveryRuns()
    {
        // `dotnet dnx` selects its SDK the way every dotnet command does, so inside a repository whose
        // global.json pins an SDK below 10 the verb does not exist, although the dnx script that launched this
        // server ignores the pin. Without the clause, a reader who runs the recovery from the repository root
        // concludes that it is broken.
        ServerInstructions.UnboundCallCoda.ShouldContain(
            "--yes -- graph <solution>`, run from outside any `global.json` that pins an SDK below 10.");
    }

    [Fact]
    public void For_UnboundServer_KeepsTheRecoveryAboveTheTruncationCliff()
    {
        // A generous real failure: the multi-solution refusal on a big repository names candidate paths, so
        // it is the longest thing that can sit between the header and the recovery. Even at 600 characters
        // the recovery ends well clear of the cut, which is what "above the fold" has to mean here.
        string failure = new('x', 600);

        string instructions = ServerInstructions.For(failure);

        int recoveryEnd = instructions.IndexOf(ServerInstructions.UnboundCallCoda, StringComparison.Ordinal)
                          + ServerInstructions.UnboundCallCoda.Length;
        recoveryEnd.ShouldBeLessThanOrEqualTo(ClientTruncationCliff);
    }

    [Fact]
    public void For_UnboundServer_BannerStaysWithinItsBudget()
    {
        // The banner cannot fit under the cliff and leave Text whole — that trade is accepted, which is
        // why the file keeps its most droppable lines last. What must not happen silently is the banner
        // growing and pushing more of Text past the cut, so its own cost is pinned here.
        const string failure = "x";
        int bannerLength = ServerInstructions.For(failure)
            .Length - ServerInstructions.Text.Length - failure.Length;

        bannerLength.ShouldBeLessThanOrEqualTo(BannerBudget);
    }
}
