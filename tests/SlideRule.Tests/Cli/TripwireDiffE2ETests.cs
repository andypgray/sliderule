using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     End-to-end scope tripwires against a real git repo (<see cref="TempGitRepo" />).
///     <c>check --diff-base HEAD</c> warns for changed files inside a scope and
///     never gates on those warnings: an untracked new file in dragon territory (the agent-hook case,
///     found via <c>git ls-files --others</c>) warns and exits 0, while a tracked change combined with a
///     new interior reference exits 1 — the exit code is containment-driven, the tripwire only warns.
///     Both postures are here because they warn in different words about different questions, and a
///     Caution has no containment at all: its rows touch a Domain file neither quarantine covers, so the
///     exit 0 they report is the whole verdict rather than a red rule declining to fire.
/// </summary>
/// <remarks>
///     The hook-mode rows are the other end of that same warning: a rule that only ever warns has only exit 0
///     to travel on, and the rendering the <c>hook</c> verb asks of the check is how it reaches the agent. They
///     run the check in process from a request, which is how the verb runs it. They belong here rather than
///     with the rest of the check e2e rows because they need what this class already builds — a real
///     repository with a real touched file in a real quarantined scope — and a mode that shapes the exit-0
///     channel cannot be measured on a run that has nothing to say.
/// </remarks>
public sealed class TripwireDiffE2ETests
{
    // The caution rows all touch the same tracked file and read back the same two lines, on three channels.
    // Held as constants because the claim is that the three channels carry one message, and three inline
    // copies would let two of them drift apart while every row stayed green.
    private const string CautionTouch = "\n// touched by the caution tripwire test\n";

    private const string CautionWarning =
        "Changed file 'MyApp.Domain/RetryPolicy.cs' is inside cautioned scope 'domain/retry-budget' — " +
        "read the dragons before editing: sliderule explain domain/retry-budget/tripwire.";

    private const string CautionDragons =
        "  dragons: RetryPolicy's broad catch is filtered on purpose: the `when` clause is what keeps it " +
        "green under the unfiltered-catch rule, and it is the fixture's one sanctioned broad handler. " +
        "Keep the filter; add cases beside it, never inside it.";

    // The Domain tree before and after its one touch: the untouched tree read once for the hook, then the
    // touched file every caution row runs against, read on three channels. The rows differ only in the output
    // they ask for and none of them mutates further, so the leased tree, its reset and its git commit are the
    // class's rather than each row's. The repo is disposed inside the lambda, so the lease is released before
    // the class's other rows ask for one.
    private static readonly Lazy<Task<(CliResult Clean, CliResult Human, CliResult Json, CliResult Hook)>> Caution =
        new(async () =>
        {
            using var repo = new TempGitRepo();
            CliResult clean = await CheckForTheHookAsync(repo);
            File.AppendAllText(repo.PathOf("MyApp.Domain", "RetryPolicy.cs"), CautionTouch);

            string[] args =
                ["check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD"];
            return (
                clean,
                await CliRunner.InvokeAsync(args),
                await CliRunner.InvokeAsync([.. args, "--json"]),
                await CheckForTheHookAsync(repo));
        });

    private const string QuarantineWarning =
        "Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing'";

    private const string QuarantineDragons =
        "  dragons: Banker's rounding happens at line-item level, NOT invoice level. Do not normalize.";

    // One untracked file in dragon territory, read on two channels and once more through a request naming a
    // different file. The rows differ only in the output they ask for and none of them mutates further, so the
    // leased tree — and the wholesale workspace reload a new source file forces on it — is paid once for the three
    // rather than once per row.
    private static readonly Lazy<Task<(CliResult Human, CliResult Hook, CliResult GivenChangedFiles)>>
        Untracked = new(async () =>
        {
            using var repo = new TempGitRepo();
            // A brand-new, still-untracked file in the quarantined billing project — SDK globs compile it in.
            repo.WriteQuarantineNote();

            string[] args =
                ["check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD"];
            return (
                await CliRunner.InvokeAsync(args),
                await CheckForTheHookAsync(repo),
                // A new file git would list as changed, and a request naming instead a tracked file nobody
                // touched. The hook hands the check the files a turn changed, which it has already listed, so
                // the check must take the list as given rather than ask git again.
                await CheckInProcessAsync(repo, request => request with
                {
                    ChangedFiles = [repo.PathOf("MyApp.Legacy.Billing", "BillingCalculator.cs")]
                }));
        });

    // One tree carrying both halves of a red run — a touched tracked file inside the scope and a new interior
    // reference from outside it — read on the two channels that have to agree about it. The hook-mode row
    // wants a subset of this arrange, so the tree, its reload and the two runs are the pair's rather than one
    // each; and the tripwire warning the touch adds is text the byte-identity claim below also covers.
    private static readonly Lazy<Task<(CliResult Human, CliResult Hook)>> Red = new(async () =>
    {
        using var repo = new TempGitRepo();
        // Touch a tracked file inside the quarantined scope (tripwire warning) ...
        File.AppendAllText(repo.PathOf("MyApp.Legacy.Billing", "BillingCalculator.cs"), "\n// touched by the tripwire test\n");
        // ... and add a NEW interior reference from outside the scope (containment red).
        FixtureEdits.AppendMemberLine(
            repo.PathOf("MyApp.Web", "HomeController.cs"), "    public BillingCalculator NewCalculator() => new BillingCalculator();");

        string[] args =
            ["check", repo.SolutionPath, "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD"];
        return (
            await CliRunner.InvokeAsync(args),
            await CheckForTheHookAsync(repo));
    });

    [Fact]
    public async Task CheckDiffBase_UntrackedFileInQuarantinedScope_WarnsAndExitsZero()
    {
        CliResult result = (await Untracked.Value).Human;

        result.ShouldSucceed(
            "warn legacy/billing/tripwire",
            "warning: Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing' — " +
            "does the task actually require editing dragon territory? Dragons: sliderule explain legacy/billing/tripwire.",
            // The dragons ride under the warning that fired, so the agent mid-edit reads them here rather than
            // paying a round trip to `explain` for prose the rule already carries.
            QuarantineDragons);
    }

    [Fact]
    public async Task CheckGivenChangedFiles_WarnsForExactlyThoseFiles()
    {
        CliResult result = (await Untracked.Value).GivenChangedFiles;

        result.ShouldSucceed(
            "warn legacy/billing/tripwire",
            "Changed file 'MyApp.Legacy.Billing/BillingCalculator.cs' is inside quarantined scope 'legacy/billing'");
        result.Out.ShouldNotContain(
            "LegacyNote.cs",
            customMessage: "A file the list does not name is not changed to this run, whatever git would say.");
    }

    [Fact]
    public async Task CheckDiffBase_ContainmentRedPlusTouch_ExitsOneWithBoth()
    {
        CliResult result = (await Red.Value).Human;

        // Exit code is containment-driven only; the tripwire warning rides alongside.
        result.ShouldReportViolations(
            "FAIL legacy/billing/containment",
            "MyApp.Web.HomeController references MyApp.Legacy.Billing.BillingCalculator",
            "warn legacy/billing/tripwire",
            "Changed file 'MyApp.Legacy.Billing/BillingCalculator.cs' is inside quarantined scope 'legacy/billing'",
            QuarantineDragons);
    }

    [Fact]
    public async Task CheckDiffBase_TouchedFileInCautionedScope_WarnsWithItsDragonsAndExitsZero()
    {
        // The quarantine row's twin for the posture with no red state at all. The exit code above is
        // containment-driven and this spec's containment holds, so exit 0 here is not "the warning did not
        // gate" but "there was never anything else to gate on" — which is what makes a caution's whole
        // verdict a warning, and this the only spec in the suite that can prove it.
        CliResult result = (await Caution.Value).Human;

        result.ShouldSucceed(
            "warn domain/retry-budget/tripwire",
            // A different question from the quarantine's wording: not whether the task belongs here at all, only
            // that the weirdness gets read before it is edited away.
            $"warning: {CautionWarning}",
            CautionDragons);
        // Domain is outside both quarantined scopes, so this run's one warning is the caution's own.
        result.Out.ShouldNotContain("warn legacy/billing/tripwire");
        result.Out.ShouldNotContain("warn legacy/web-shell/tripwire");
    }

    [Fact]
    public async Task CheckDiffBaseJson_TouchedFileInCautionedScope_CarriesTheKindAndTheSameMessage()
    {
        CliResult result = (await Caution.Value).Json;

        // The machine channel names the posture in the warning's own kind rather than leaving a reader to
        // parse the prose for it — an additive enum value within schemaVersion 3, beside the quarantine's.
        result.ShouldSucceed();
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement warning = CheckJson.Rule(document, "domain/retry-budget/tripwire")
            .GetProperty("warnings")
            .EnumerateArray()
            .ShouldHaveSingleItem();

        warning.GetProperty("kind")
            .GetString()
            .ShouldBe("cautionedScopeTouched");
        warning.GetProperty("message")
            .GetString()
            .ShouldBe(CautionWarning);
    }

    [Fact]
    public async Task CheckForTheHook_CleanRunWithATripwireWarning_WritesTheReportLessItsPassBlocks()
    {
        // The agent reading the context holds the rule board already, and a pass block per rule is what grows
        // with the spec: enough of them push the context past the client's inline limit, and the preview the
        // agent is left with then holds no warning at all. So the text is the plain run's report with exactly
        // those blocks gone, the stamps, the warned block and the summary line all kept.
        (CliResult human, CliResult hook, _) = await Untracked.Value;
        human.Err.ShouldBeEmpty("The plain run wrote diagnostics, which hook mode composes into the same text.");
        string expected = WithoutPassBlocks(human.Out);
        expected.ShouldNotBe(human.Out, "The plain run carried no pass block, so this row would prove nothing.");

        // Exit 0 exactly as outside hook mode: a warning never moves the verdict.
        hook.ShouldSucceed();
        hook.Out.ShouldBe(expected, hook.Transcript());
        hook.Err.ShouldBeEmpty(hook.Transcript());
    }

    [Fact]
    public async Task CheckForTheHook_CleanRunWithACautionWarning_WritesTheSameText()
    {
        // The caution's third channel, and the one it was built for: a rule that only ever warns has only
        // exit 0 to travel on, and an agent hook is exactly the reader the dragons are addressed to. Same
        // two lines as the human run, in the text the hook hands to Claude Code.
        CliResult result = (await Caution.Value).Hook;

        result.ShouldSucceed("warn domain/retry-budget/tripwire", CautionWarning, CautionDragons);
    }

    [Fact]
    public async Task CheckForTheHook_CleanRunWithNoWarnings_WritesNothingAtAll()
    {
        // The same repository untouched: the tripwire finds no changed file, and a hook with nothing to say
        // must say nothing rather than post an empty document. Silence is what keeps the channel bearable at
        // all, so it is pinned as tightly as the document itself.
        CliResult result = (await Caution.Value).Clean;

        result.ShouldSucceed();
        result.Out.ShouldBeEmpty();
        result.Err.ShouldBeEmpty();
    }

    [Fact]
    public async Task CheckForTheHook_RedRule_WritesTheHumanReportItAlwaysWrote()
    {
        // Hook mode shapes the exit-0 channel and nothing else: on a red rule the hook needs the report to
        // put on stderr and block with, so this run must be byte-identical to the same run outside hook mode.
        (CliResult plain, CliResult hook) = await Red.Value;

        hook.ShouldReportViolations("FAIL legacy/billing/containment");
        hook.ShouldMatchTheOutputOf(plain);
    }

    [Fact]
    public async Task CheckDiffBase_SolutionOpenedThroughSymlinkedRoot_StillWarns()
    {
        using var repo = new TempGitRepo();

        // A symlink whose target is the repo root, living beside the repo (outside its working tree, so
        // it is never itself a changed file). Opening the solution through it hands the workspace a
        // symlink-spelled path while `git rev-parse --show-toplevel` returns the canonical one — a divergence
        // that silently defeats the tripwire's prefix match unless the discovery seam canonicalizes the path.
        string linkRoot = Path.Combine(Path.GetDirectoryName(repo.Root)!, "link-" + Guid.NewGuid()
            .ToString("N"));
        SymlinkSupport.CreateDirectorySymlink(linkRoot, repo.Root);
        try
        {
            // A brand-new untracked file in dragon territory (the agent-hook case).
            repo.WriteQuarantineNote();

            // Cache off: a hit on this class's slot would replay fragments an earlier row recorded through the
            // canonical path, and so pass whether or not discovery canonicalized this one.
            CliResult result = await CliRunner.InvokeAsync(
                "check", Path.Combine(linkRoot, "MyApp.sln"), "--spec", CliRunner.QuarantinedSpecDll, "--diff-base", "HEAD",
                "--no-cache");

            result.ShouldSucceed("warn legacy/billing/tripwire", QuarantineWarning);
        }
        finally
        {
            // Delete only the symlink (non-recursive), never through it into the real repo.
            if (Directory.Exists(linkRoot)) Directory.Delete(linkRoot);
        }
    }

    // A human report with the blocks headed `pass` taken out, a block being its header line and the indented
    // lines beneath it, every other byte kept. Written against the rendered text on purpose: it states the
    // context's contract in the terms its reader sees, so a rendering that reshaped anything else would red here.
    private static string WithoutPassBlocks(string report)
    {
        var kept = new List<string>();
        var inPassBlock = false;
        foreach (string line in report.Split('\n'))
        {
            if (!line.StartsWith("  ", StringComparison.Ordinal))
                inPassBlock = line.StartsWith("pass ", StringComparison.Ordinal);
            if (!inPassBlock) kept.Add(line);
        }

        return string.Join('\n', kept);
    }

    // The check run in process from a request rather than a command line, for what only a caller sets: the changed
    // files, and the hook rendering the hook verb asks for.
    private static Task<CliResult> CheckInProcessAsync(
        TempGitRepo repo, Func<CheckRequest, CheckRequest> shape, bool forTheHook = false)
    {
        CheckRequest request = shape(CheckRequests.For(repo.SolutionPath, CliRunner.QuarantinedSpecDll, repo.Root));

        return CliResult.CapturedAsync((output, error) =>
        {
            var runner = new CheckRunner(output, error, WarmWorkspacePool.Source, new FakeEnvironment());
            return forTheHook ? runner.RunForHookAsync(request, Ct) : runner.RunAsync(request, Ct);
        });
    }

    // The check the hook verb runs over the repository: changed files since HEAD, rendered for the hook.
    private static Task<CliResult> CheckForTheHookAsync(TempGitRepo repo)
    {
        return CheckInProcessAsync(repo, static request => request with { DiffBase = "HEAD" }, forTheHook: true);
    }
}
