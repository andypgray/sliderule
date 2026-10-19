using System.Diagnostics;
using System.Text.Json.Nodes;
using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     Drives <c>sliderule hook</c>'s policy in process and holds it to the contract it publishes: the exit-code
///     mapping a Claude Code hook depends on (clean → 0 with whatever the check wrote passed through on stdout, a red
///     rule → 2 with the report on stderr, SlideRule's own error → 1 under a <c>sliderule config error:</c> line),
///     the turn-end behaviour a <c>Stop</c> payload buys, and the guards that decide whether an event spends a check
///     at all. A scripted check supplies the exit code and the output, and records every check the hook asked for,
///     so the policy is measured without a workspace.
/// </summary>
/// <remarks>
///     <para>
///         The hook is started from a directory that is <em>not</em> the project directory unless a row names one,
///         so the <c>CLAUDE_PROJECT_DIR</c> anchoring is exercised too: a check reads the project directory only
///         when the hook chose it over the directory it started in.
///     </para>
///     <para>
///         The working-tree rows build a real git repository with a real linked worktree under
///         <c>.claude/worktrees/</c>, which is where Claude Code puts one. Nothing cheaper reproduces the shape: a
///         worktree is inside the project directory <em>by path</em> and a separate working tree <em>by git</em>,
///         and telling those apart is the whole of what the guard does.
///     </para>
///     <para>
///         Every row keeps its state inside its own sandbox, through <c>SLIDERULE_HOOK_STATE_DIR</c> on a fake
///         environment, so a turn-end verdict is written where the row can read it back and never into the
///         machine's temp root. The skip rows need a real repository underneath them for a second reason: a
///         verdict or a baseline records HEAD and a digest of each changed code file's bytes, and a skip is
///         exactly the claim that no code file's bytes have moved since.
///     </para>
/// </remarks>
public sealed class HookRunnerTests
{
    /// <summary>
    ///     The scripted check's canned report: two lines, so the multi-line path through the hook is covered.
    /// </summary>
    private const string CannedReport =
        "FAIL arch/stub-rule -- canned violation report\n"
        + "  Probe.cs:10 -- second report line, so the multi-line path is covered";

    private const string CodeEditPayload = """{"tool_input":{"file_path":"src/SlideRule/Probe.cs"}}""";
    private const string DocsEditPayload = """{"tool_input":{"file_path":"docs/notes.md"}}""";

    /// <summary>
    ///     The <c>.git</c> of a repository with one empty commit, made once per test process for every row that
    ///     wants a working tree to copy.
    /// </summary>
    private static readonly Lazy<string> TemplateGitDirectory = new(static () =>
    {
        string template = Path.Combine(TestTempRoot.For("hook-verb"), "template");
        Directory.CreateDirectory(template);
        GitCommand.Run(template, "init");
        GitCommand.Commit(template, "--allow-empty", "-m", "baseline");

        return Path.Combine(template, ".git");
    });

    /// <summary>Every row of the exit-code contract, named by the code the check exits with.</summary>
    public static TheoryData<int> ExitContractCases => [.. HookExitContract.Rows.Select(static row => row.CheckExit)];

    [Theory]
    [MemberData(nameof(ExitContractCases))]
    public async Task CheckExit_IsTranslatedAsTheContractSays(int checkExit)
    {
        // Arrange
        HookExitContract row = HookExitContract.Rows.Single(candidate => candidate.CheckExit == checkExit);

        // Act
        HookRun run = await FireAsync(Arrange(), payload: "", checkExit);

        // Assert
        run.ShouldExitWith(row.HookExit);
        switch (row.Outcome)
        {
            case HookOutcome.StopStands:
                // On exit 0 the check writes the hook document and the hook passes it through: Claude Code parses an
                // exit-0 hook's stdout, so what the check wrote has to arrive there verbatim, unprefixed and not
                // diverted to stderr. The scripted check stands in for the real one, so what this row measures is the
                // passthrough rather than the document.
                run.Result.ShouldProceedWith(CannedReport);
                break;
            case HookOutcome.StopRefused:
                // 2 is how a Claude Code hook blocks, and the report on stderr is what the agent reads to correct
                // itself.
                run.Result.ShouldBlockWith(CannedReport);
                break;
            case HookOutcome.ConfigError:
                // Not 2: SlideRule's own error is a config problem the user sees, not a violation the agent is
                // told to "fix".
                run.Result.ShouldReportConfigError(CannedReport);
                break;
            default:
                throw new UnreachableException($"No assertion is written for the {row.Outcome} outcome.");
        }
    }

    [Fact]
    public async Task CleanCheckThatWroteNothing_StaysSilent()
    {
        // Act: the other half of the clean row in the contract theory above. A clean check with no warnings writes
        // nothing at all, and the hook must not turn nothing into a blank line, because people switch off a hook
        // that prints on every edit.
        HookRun run = await FireAsync(Arrange(), payload: "", ExitCodes.Clean, quiet: true);

        // Assert
        run.Result.ShouldProceedSilently();
    }

    [Fact]
    public async Task NonCodePayload_SkipsTheCheckEntirely()
    {
        // Act
        HookRun run = await FireAsync(Arrange(), DocsEditPayload, ExitCodes.Clean);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldNotHaveRunTheCheck("A docs edit must not pay for a check: the payload filter returns first.");
    }

    [Fact]
    public async Task CodePayload_RunsTheCheck()
    {
        // Act: the payload path is relative, which is the fallback the guard reads as project-relative.
        HookRun run = await FireAsync(Arrange(), CodeEditPayload, ExitCodes.Clean);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldHaveRunTheCheckIn(
            run.ProjectDirectory,
            "A .cs edit must reach the check, and the check reads CLAUDE_PROJECT_DIR, not the directory the hook "
            + "was started in.");
    }

    [Fact]
    public async Task AbsolutePayloadInsideTheProjectDirectory_RunsTheCheck()
    {
        // Arrange: a real repository, because this is the everyday path: both sides resolve to the same working
        // tree and the run proceeds. Without one they resolve to nothing, which proceeds by fallback and would pass
        // just as well with the comparison inverted.
        HookSandbox sandbox = ArrangeRepository();
        string edited = Path.Combine(sandbox.ProjectDirectory, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        // Act
        HookRun run = await FireAsync(sandbox, HookPayloads.Edit(edited), ExitCodes.Clean);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldHaveRunTheCheckIn(
            run.ProjectDirectory,
            "This is the payload shape Claude Code actually sends: an absolute path, JSON-escaped, inside the "
            + "project directory and in its working tree. Nothing about it should stop the check.");
    }

    [Fact]
    public async Task PayloadBelowAProjectDirectoryAtAFilesystemRoot_AsksGitAboutTheRoot()
    {
        // Arrange: the project directory is the root of the drive the sandbox repository is on.
        HookSandbox sandbox = ArrangeRepository();
        string root = Path.GetPathRoot(sandbox.Root)!;
        HookSandbox atRoot = sandbox with { ProjectDirectory = root };
        string edited = Path.Combine(sandbox.ProjectDirectory, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        // Act
        HookRun run = await FireAsync(atRoot, HookPayloads.Edit(edited), ExitCodes.Clean);

        // Assert: compared by spelling, because the drive root cannot take the file a directory comparison plants.
        CheckRequest check = run.ShouldHaveRunOneCheck();
        check.WorkingDirectory.ShouldBe(
            root,
            "A root project directory is not a repository, so the guard's doubt runs the check there. The answer must "
            + "not depend on the directory the hook process happens to sit in.");
    }

    [Fact]
    public async Task PayloadOutsideTheProjectDirectory_SkipsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = Arrange();
        string edited = Path.Combine(sandbox.Root, "elsewhere", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(edited)!);

        // Act
        HookRun run = await FireAsync(sandbox, HookPayloads.Edit(edited), ExitCodes.Clean);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldNotHaveRunTheCheck(
            "An edit outside CLAUDE_PROJECT_DIR must not buy a check of the project: the verdict would be about "
            + "code the edit never touched. This is also the leg that covers a file in no repository at all, a "
            + "scratch directory being the everyday case.");
    }

    [Fact]
    public async Task PayloadInALinkedWorktree_SkipsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        // Act
        HookRun run = await FireAsync(sandbox, HookPayloads.Edit(Path.Combine(worktree, "Probe.cs")), ExitCodes.Clean);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldNotHaveRunTheCheck(
            "A session in the main checkout writing to a worktree by absolute path is inside the project "
            + "directory by path and in a different working tree by git. Only the second answer is the right one, "
            + "so the check must not run.");
    }

    [Fact]
    public async Task SessionInsideALinkedWorktree_SkipsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        // Act
        HookRun run = await FireAsync(sandbox, payload: "", ExitCodes.Clean, workingDirectory: worktree);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldNotHaveRunTheCheck(
            "With no payload the directory the hook started in stands in for the edited file, so a hand run inside "
            + "a worktree opts itself out rather than checking the main checkout CLAUDE_PROJECT_DIR names.");
    }

    [Fact]
    public async Task SessionInASubdirectoryOfTheProject_RunsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string subdirectory = Path.Combine(sandbox.ProjectDirectory, "src");
        Directory.CreateDirectory(subdirectory);

        // Act
        HookRun run = await FireAsync(sandbox, payload: "", ExitCodes.Clean, workingDirectory: subdirectory);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldHaveRunTheCheckIn(
            run.ProjectDirectory,
            "A session below the repository root is in the project's own working tree and must be checked. Asking "
            + "git which tree each side is in answers that identically from any depth, which a comparison of git's "
            + "own directory spellings does not.");
    }

    [Fact]
    public async Task NoProjectDirectory_StillChecks()
    {
        // Act
        HookRun run = await FireAsync(Arrange(), payload: "", ExitCodes.Clean, withProjectDirectory: false);

        // Assert
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldHaveRunTheCheckIn(
            run.WorkingDirectory,
            "A hand run outside Claude Code has no CLAUDE_PROJECT_DIR to compare anything against, so the guard "
            + "steps aside and the check runs where the hook was started.");
    }

    [Fact]
    public async Task PayloadThatIsNotJson_IsAHandRun()
    {
        // Act: text that is not a JSON object is no payload, never a crash, so it takes the path a hand run takes.
        HookRun run = await FireAsync(Arrange(), "not a hook payload {", ExitCodes.Clean, quiet: true);

        // Assert
        run.Result.ShouldProceedSilently();
        run.ShouldHaveRunTheCheckIn(run.ProjectDirectory, "A payload that will not parse is read as no payload, which checks.");
    }

    [Fact]
    public async Task EditPayload_ChecksForThePerEditEvent()
    {
        // Act: an event the hook has no branch for, carrying a code edit.
        HookRun run = await FireAsync(
            Arrange(), """{"hook_event_name":"PreToolUse","tool_input":{"file_path":"src/Probe.cs"}}""", ExitCodes.Clean);

        // Assert: the per-edit path always asks for the PostToolUse document, the one per-edit event whose context
        // reaches the agent, and it asks for hook mode over the diff base the command line named.
        CheckRequest check = run.ShouldHaveRunOneCheck();
        check.ShouldSatisfyAllConditions(
            () => check.HookEvent.ShouldBe("PostToolUse"),
            () => check.HookJson.ShouldBeTrue(),
            () => check.Json.ShouldBeFalse(),
            () => check.DiffBase.ShouldBe("HEAD"));
    }

    [Fact]
    public async Task StopPayload_ChecksForTheEventThatFired()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();

        // Act: the worker stops in a session of its own, because in the first stop's session it would skip on that
        // stop's green verdict and ask for no check at all.
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, quiet: true);
        HookRun worker = await FireAsync(
            sandbox, StopPayload(sandbox, "s2", eventName: "SubagentStop"), ExitCodes.Clean, quiet: true);

        // Assert: Claude Code reads the context only from a document naming the event it fired, so each stop asks
        // the check for its own event's document.
        stop.ShouldHaveRunOneCheck()
            .HookEvent.ShouldBe("Stop");
        worker.ShouldHaveRunOneCheck()
            .HookEvent.ShouldBe("SubagentStop");
    }

    [Fact]
    public async Task RelativeSolutionAndSpec_ResolveAgainstTheTree()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        // Act: a session inside a worktree, with the paths a settings entry would carry, relative to the root.
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, cwd: worktree), ExitCodes.Clean, quiet: true,
            solution: "App.slnx", spec: "arch/App.Architecture/App.Architecture.csproj");

        // Assert: relative to the tree the hook checks rather than to where the process started, which is what
        // lets one settings entry serve the main checkout and every worktree under it.
        CheckRequest check = run.ShouldHaveRunOneCheck();
        Path.GetFileName(check.Solution)
            .ShouldBe("App.slnx");
        HookRunAssertions.SameDirectory(Path.GetDirectoryName(check.Solution)!, worktree)
            .ShouldBeTrue($"The solution should resolve against the worktree, not {check.Solution}.");
        check.Spec.ShouldBe(Path.Combine(Path.GetDirectoryName(check.Solution)!, "arch", "App.Architecture", "App.Architecture.csproj"));
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("SubagentStop")]
    public async Task TurnEndPayload_RedRule_BlocksWithExitTwoAndTheReportOnStderr(string eventName)
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox, eventName: eventName), ExitCodes.Violations);

        // Assert: the same 2 the per-edit shape blocks with. At the turn boundary the agent has said it is done, so
        // the 2 refuses that stop, and the report is the agent's reason to keep going. A worker's stop makes the same
        // claim, with the worker's edits already in the tree, so both events block alike.
        run.Result.ShouldBlockWith(CannedReport);
        // Both files are written before the hook exits, because the next stop decides from them: a skip trusts the
        // verdict, and the cap reads the count.
        ReadState(sandbox, "verdict")
            .ShouldStartWith("red");
        ReadState(sandbox, "rounds")
            .Trim()
            .ShouldBe("p1 1");
    }

    [Fact]
    public async Task StopPayload_CleanRunWithWarnings_PassesTheDocumentThrough()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert: a tripwire warning leaves the verdict green, but the agent still has to read it. Exit 0 with the
        // document on stdout is how Claude Code turns it into one continuation of the turn rather than a block.
        run.Result.ShouldProceedWith(CannedReport);
        ReadState(sandbox, "verdict")
            .ShouldStartWith("green");
        // Counted like a block, because the cap bounds every continuation of the turn, whatever caused it.
        ReadState(sandbox, "rounds")
            .Trim()
            .ShouldBe("p1 1");
    }

    [Fact]
    public async Task StopPayload_CleanSilentRun_StaysSilent()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, quiet: true);

        // Assert: the everyday turn. The check is clean, the hook says nothing, and the stop stands.
        run.Result.ShouldProceedSilently();
        ReadState(sandbox, "verdict")
            .ShouldStartWith("green");
        File.Exists(StatePath(sandbox, "rounds"))
            .ShouldBeFalse(
                "A silent green ends the continuations, so the count goes rather than standing to be read by a "
                + "later prompt that has nothing to do with it.");
    }

    [Fact]
    public async Task StopPayload_UnchangedGreenTree_SkipsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        string payload = StopPayload(sandbox);
        HookRun first = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        first.ShouldHaveRunTheCheckIn(sandbox.ProjectDirectory, "The first stop has no verdict to trust, so it checks.");

        // Act
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert: this skip is what makes a question-and-answer turn free. No code file's bytes have moved since the
        // green verdict, so a check could only repeat that verdict, and running one anyway would cost the turn a
        // minute.
        second.Result.ShouldProceedSilently();
        second.ShouldNotHaveRunTheCheck(
            "A second stop over an unchanged tree with a green verdict must not spend a check at all.");
    }

    [Fact]
    public async Task StopPayload_EditedTree_ChecksAgain()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        string payload = StopPayload(sandbox);
        await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        // An edit to a file already in the listing is invisible to the listing: untracked before, untracked after.
        // Its bytes are what moved, which is why the verdict records them.
        Rewrite(probe);

        // Act
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert
        second.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A file the verdict covered holds new bytes, so the verdict no longer speaks for the tree and the check "
            + "has to run again.");
    }

    [Fact]
    public async Task StopPayload_EditedFileWithANonAsciiName_ChecksAgain()
    {
        // Arrange: a name git would quote and escape in its line-oriented listing.
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox, "Caf\u00e9.cs");
        string payload = StopPayload(sandbox);
        await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        Rewrite(probe);

        // Act
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert
        second.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A file whose name git would quote must still resolve to that file, or its re-edit is read as a "
            + "deletion and the stop takes the skip.");
    }

    [Fact]
    public async Task StopPayload_TurnThatRenamedCodeToNonCode_ChecksTheTree()
    {
        // Arrange: a clean tree when the prompt arrives, so the baseline records an empty listing.
        HookSandbox sandbox = ArrangeRepository();
        CommitProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        GitCommand.Run(sandbox.ProjectDirectory, "mv", "src/Probe.cs", "src/Probe.txt");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, quiet: true);

        // Assert
        stop.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A code file renamed to a non-code name is a deletion of code, which the check must see rather than "
            + "the turn reading as one that wrote nothing.");
    }

    [Fact]
    public async Task StopPayload_RedVerdict_NeverSkips()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        string payload = StopPayload(sandbox);
        await FireAsync(sandbox, payload, ExitCodes.Violations);

        // Act
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Violations);

        // Assert: the skip is an optimisation over a verdict that was clean. A tree that has not changed still
        // carries the red one, so the stop checks again and blocks again.
        second.ShouldExitWith(HookExitCodes.Block);
        second.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A red verdict is never skipped, however unchanged the tree is.");
    }

    [Theory]
    [InlineData(3, "sliderule: still red after 3 rounds; not blocking again.")]
    // The --max-rounds leg: the cap moves, and everything the cap does holds at the new one.
    [InlineData(1, "sliderule: still red after 1 rounds; not blocking again.")]
    public async Task StopPayload_AtTheRoundCap_StopsBlocking(int maxRounds, string standDownLine)
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string payload = StopPayload(sandbox);
        for (var round = 0; round < maxRounds; round++)
            (await FireAsync(sandbox, payload, ExitCodes.Violations, maxRounds: maxRounds))
                .ShouldExitWith(HookExitCodes.Block, $"Round {round + 1} is inside the cap and still blocks.");

        // Act
        HookRun capped = await FireAsync(sandbox, payload, ExitCodes.Violations, maxRounds: maxRounds);

        // Assert: a rule the agent cannot satisfy would otherwise be a loop the user watches. At the cap the hook
        // says so on stderr and exits 1, which is not a block: the user reads it and the turn ends.
        capped.ShouldExitWith(HookExitCodes.Report);
        capped.Result.Err.NormalizedLines()
            .ShouldStartWith(standDownLine);
        capped.Result.Err.NormalizedLines()
            .ShouldContain(CannedReport);

        HookRun nextPrompt = await FireAsync(
            sandbox, StopPayload(sandbox, promptId: "p2"), ExitCodes.Violations, maxRounds: maxRounds);

        // The count is per prompt, so the next thing the user asks for starts with its own rounds rather than
        // inheriting a stand-down from the work before it.
        nextPrompt.ShouldExitWith(HookExitCodes.Block);
    }

    [Fact]
    public async Task StopPayload_InAWorktreeSession_ChecksTheWorktree()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string worktree = AddLinkedWorktree(sandbox);

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox, cwd: worktree), ExitCodes.Clean, quiet: true);

        // Assert: the per-edit guard skips a worktree because the edit belongs to a tree the hook does not speak
        // for. At a stop the worktree *is* the session's tree, so the answer inverts: check it, and check it there.
        run.ShouldExitWith(HookExitCodes.Proceed);
        run.ShouldHaveRunTheCheckIn(worktree, "A session inside a linked worktree must have its own tree checked.");
        run.ShouldNotHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "and checked there: a verdict about the main checkout would be about code this session never touched.");
    }

    [Fact]
    public async Task StopPayload_TurnThatWroteALinkedWorktree_ChecksItBesideTheSessionsTree()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, quiet: true);

        // Assert: the session stayed in the main checkout and wrote the worktree by its absolute path. Its directory
        // names only the main checkout, so the transcript is the one place that says where the turn's code went.
        run.Result.ShouldProceedSilently();
        run.ShouldHaveRunTheCheckIn(
            worktree, "A linked worktree the turn wrote code into must be checked, though the session never sat in it.");
        run.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "and the session's own tree still: a shell write never shows in the transcript, so it is checked as ever.");
    }

    [Fact]
    public async Task SubagentStopPayload_RunThatWroteALinkedWorktree_ChecksItBesideTheSessionsTree()
    {
        // Arrange: the worker's own transcript is its run, so a write there counts under any prompt.
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe, promptId: "p0"));
        string payload = StopPayload(
            sandbox, eventName: "SubagentStop", agentId: "a1", agentTranscriptPath: transcript);

        // Act
        HookRun run = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert: a subagent's directory never follows its cd, so this is the shape every worker in a worktree has.
        run.ShouldHaveRunTheCheckIn(
            worktree, "A worktree a subagent wrote into by absolute path must be checked at the subagent's stop.");
        run.ShouldHaveRunTheCheckIn(sandbox.ProjectDirectory, "and the session's own tree beside it.");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("garbage")]
    public async Task StopPayload_TranscriptThatCannotBeRead_ChecksOnlyTheSessionsTree(string shape)
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        AddWrittenWorktree(sandbox);
        string transcript = Path.Combine(sandbox.Root, "transcripts", "t.jsonl");
        if (shape == "garbage")
            new HookTranscript().Raw("not json {")
                .Raw("\0\u0001")
                .SaveTo(transcript);

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, quiet: true);

        // Assert: the transcript is the client's private format. When it will not read, the stop is what it was
        // before the transcript was ever read: the session's own tree, and never a failed stop.
        run.Result.ShouldProceedSilently();
        run.ShouldHaveOnlyRunTheCheckIn(sandbox.ProjectDirectory, "An unreadable transcript names no other tree.");
    }

    [Fact]
    public async Task StopPayload_WriteUnderAnotherPrompt_LeavesTheWorktreeUnchecked()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (_, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe, promptId: "p0"));

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, quiet: true);

        // Assert: an earlier turn's write was that turn's stop to check.
        run.ShouldHaveOnlyRunTheCheckIn(
            sandbox.ProjectDirectory, "A write under another prompt is not this turn's, so it adds no tree.");
    }

    [Fact]
    public async Task StopPayload_WriteIntoAnotherRepository_LeavesItUnchecked()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string beside = Path.Combine(sandbox.Root, "beside");
        Directory.CreateDirectory(beside);
        GitCommand.Run(beside, "init");
        string transcript = SaveTranscript(
            sandbox, new HookTranscript().Wrote(Path.Combine(beside, "src", "Other.cs")));

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, quiet: true);

        // Assert: only a tree that shares the project's git directory is one of its trees.
        run.ShouldHaveOnlyRunTheCheckIn(
            sandbox.ProjectDirectory, "A repository beside the project is not one of its trees, however it was written.");
    }

    [Fact]
    public async Task StopPayload_TurnThatWroteOnlyItsOwnTree_ReadsAsItAlwaysHas()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Violations);

        // Assert: one tree, so the report is the check's own, with no line naming a tree.
        run.Result.ShouldBlockWith(CannedReport);
        run.Result.Err.ShouldNotContain("sliderule: ");
        run.ShouldHaveOnlyRunTheCheckIn(sandbox.ProjectDirectory, "The session's own tree is the one tree here.");
    }

    [Fact]
    public async Task StopPayload_RedRuleInTheWrittenWorktree_BlocksNamingIt()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, quiet: true, redTree: worktree);

        // Assert: a red rule in any tree the turn wrote blocks the stop, and the report says which tree it is in.
        run.Result.ShouldBlockWith("sliderule: ", "/.claude/worktrees/w1");
        ReadState(sandbox, "rounds")
            .Trim()
            .ShouldBe("p1 1", "One stop is one round, however many trees it checked.");
        ReadState(sandbox, "verdict")
            .ShouldStartWith("green", customMessage: "The session's own tree was clean, and keeps its own verdict.");
        ReadTreeState(sandbox, "verdict")
            .ShouldStartWith("red", customMessage: "The worktree keeps the verdict its own check gave.");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopPayload_WarningsInTwoTrees_GoOutInOneDocumentNamingEach(bool checkWritesADocument)
    {
        // Arrange: the check writes the document hook mode writes for a clean run with warnings, or, in the second
        // row, plain text that does not read as one, which is carried over as it stands.
        HookSandbox sandbox = ArrangeRepository();
        (_, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));
        string checkOutput = checkWritesADocument ? HookReportRenderer.Document(CannedReport, "Stop") : CannedReport;

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, transcriptPath: transcript), ExitCodes.Clean, stdout: checkOutput + "\n");

        // Assert: Claude Code parses one document from stdout, so both trees' warnings ride in it, each under its tree.
        run.ShouldExitWith(HookExitCodes.Proceed);
        var context = JsonNode.Parse(run.Result.Out)!["hookSpecificOutput"]!["additionalContext"]!
            .GetValue<string>();
        context.Split("sliderule: ")
            .Length.ShouldBe(3, context);
        context.ShouldContain("/.claude/worktrees/w1\n" + CannedReport);
        ReadState(sandbox, "rounds")
            .Trim()
            .ShouldBe("p1 1");
    }

    [Fact]
    public async Task StopPayload_NowhereToKeepAWorktreesRecords_ChecksItEveryStop()
    {
        // Arrange: a file stands where the session's tree records would go, so none can be made.
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));
        string payload = StopPayload(sandbox, transcriptPath: transcript);
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath(sandbox, "trees"))!);
        File.WriteAllText(StatePath(sandbox, "trees"), "");

        // Act
        HookRun first = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert: with no record to skip on, the worktree is checked whenever the turn wrote it, never skipped on
        // a verdict it could not keep.
        first.ShouldHaveRunTheCheckIn(worktree, "A worktree with nowhere to keep records is still checked.");
        second.ShouldHaveOnlyRunTheCheckIn(
            worktree, "and checked again: only the session's own tree has a green verdict to skip on.");
    }

    [Fact]
    public async Task StopPayload_WrittenWorktreeUnchangedSinceItsGreenVerdict_SkipsIt()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string transcript = SaveTranscript(sandbox, new HookTranscript().Wrote(probe));
        string payload = StopPayload(sandbox, transcriptPath: transcript);
        HookRun first = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        first.ShouldHaveRunTheCheckIn(worktree, "The first stop has no verdict for the worktree, so it checks.");

        // Act
        HookRun second = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        Rewrite(probe);
        HookRun third = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert: the worktree keeps a verdict of its own, so the skip that makes a quiet turn free holds there too,
        // and gives way when its bytes move.
        second.ShouldNotHaveRunTheCheck("Neither tree has changed since its green verdict.");
        third.ShouldHaveOnlyRunTheCheckIn(worktree, "Only the worktree's bytes moved, so only it is checked.");
    }

    [Fact]
    public async Task PromptPayload_AfterAStopCheckedAWorktree_GivesItABaseline()
    {
        // Arrange: a turn that wrote the worktree, checked green there, then a second code file written into the
        // worktree between turns, as another session or a shell would write it.
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, string probe, string payload) = await ArrangeCheckedWorktreeAsync(sandbox);
        WriteOther(worktree, "Other.cs");

        // Act
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        Rewrite(probe);
        HookRun stop = await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        // Assert: the prompt recorded the worktree as it found it, so the file written before the turn began is
        // not the turn's.
        stop.ShouldHaveHandedTheCheckChangedFiles(
            [".claude/worktrees/w1/src/Probe.cs"],
            "A worktree the session has checked gets the prompt's baseline, so its tripwires read only the turn's "
            + "own write there, not a file another writer changed between turns.");
    }

    [Fact]
    public async Task PromptPayload_RecordedWorktreeThatIsGone_IsSkipped()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (string worktree, _, _) = await ArrangeCheckedWorktreeAsync(sandbox);
        string before = ReadTreeState(sandbox, "baseline");
        GitCommand.Run(sandbox.ProjectDirectory, "worktree", "remove", "--force", worktree);

        // Act
        HookRun prompt = await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert
        prompt.Result.ShouldProceedSilently();
        ReadState(sandbox, "baseline")
            .ShouldStartWith("prompt\n", customMessage: "The session's own tree still gets the prompt's baseline.");
        ReadTreeState(sandbox, "baseline")
            .ShouldBe(before, "A recorded worktree that no longer exists is skipped, its record left as it was.");
    }

    [Fact]
    public async Task PromptPayload_TreeRecordWithNoName_IsLeftAsItWas()
    {
        // Arrange: the worktree's records as an older build wrote them, with no file naming the tree.
        HookSandbox sandbox = ArrangeRepository();
        (_, string probe, _) = await ArrangeCheckedWorktreeAsync(sandbox);
        string before = ReadTreeState(sandbox, "baseline");
        File.Delete(TreeStatePath(sandbox, "tree"));
        Rewrite(probe);

        // Act
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert: a record that names no tree cannot be matched to one, so the prompt never writes to it.
        ReadTreeState(sandbox, "baseline")
            .ShouldBe(before, "A tree record with no name must be left as the stop left it.");
    }

    [Fact]
    public async Task PromptPayload_WithABaselineStanding_LeavesTheWorktreesAlone()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        (_, string probe, _) = await ArrangeCheckedWorktreeAsync(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        string first = ReadTreeState(sandbox, "baseline");
        Rewrite(probe);

        // Act: a second prompt with no stop between, which keeps the session's baseline.
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert: the turn the standing baseline belongs to may have written the worktree too, so its baseline there
        // stands as well.
        ReadTreeState(sandbox, "baseline")
            .ShouldBe(first, "A prompt that keeps the session's baseline must keep the worktrees' baselines too.");
    }

    [Fact]
    public async Task StopPayload_StateDirThatCannotBeMade_HonoursStopHookActive()
    {
        // Arrange: a file where the state root should be, so nothing under it can be made.
        HookSandbox sandbox = ArrangeRepository();
        string blocked = Path.Combine(sandbox.Root, "state-root-is-a-file");
        File.WriteAllText(blocked, "not a directory");

        // Act
        HookRun run = await FireAsync(
            sandbox, StopPayload(sandbox, stopHookActive: true), ExitCodes.Violations, stateDirectory: blocked);

        // Assert: with nowhere to count rounds the hook degrades to the one round the payload itself carries.
        // stop_hook_active says this hook has already blocked in this turn, so it stands down rather than blocking
        // a second time on a count it cannot keep.
        run.Result.ShouldProceedSilently();
    }

    [Fact]
    public async Task StopPayload_RedRuleInAStateDirThatKeepsNothing_BlocksOncePerTurn()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        ArrangeStateThatKeepsNothing(sandbox);

        HookRun first = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations, maxRounds: 1);
        first.ShouldExitWith(HookExitCodes.Block, "The turn's first red stop blocks whether or not it can be counted.");

        // Act and assert: the session's directory opens, so the hook is not stateless, but no round it counts
        // reads back. It falls back to the one round stop_hook_active carries and blocks at most once per turn;
        // reading zero rounds at every stop instead would block past any cap.
        for (var stop = 2; stop <= 4; stop++)
            (await FireAsync(
                    sandbox, StopPayload(sandbox, stopHookActive: true), ExitCodes.Violations, maxRounds: 1))
                .ShouldExitWith(HookExitCodes.Proceed, $"Stop {stop} follows a block this turn and must stand down.")
                .Result
                .ShouldProceedSilently();
    }

    [Fact]
    public async Task StopPayload_WarningsInAStateDirThatKeepsNothing_PassOncePerTurn()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        ArrangeStateThatKeepsNothing(sandbox);

        HookRun first = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, maxRounds: 1);
        first.Result.ShouldProceedWith(CannedReport);

        // Act and assert: with no record of what was said, the said-once rule cannot hold the warnings back
        // either, so the round fallback is what stops the same document coming back at every stop.
        for (var stop = 2; stop <= 4; stop++)
            (await FireAsync(sandbox, StopPayload(sandbox, stopHookActive: true), ExitCodes.Clean, maxRounds: 1))
                .ShouldExitWith(HookExitCodes.Proceed, $"Stop {stop} follows a continuation this turn.")
                .Result
                .ShouldProceedSilently();
    }

    [Fact]
    public async Task PromptPayload_NotesTheTreeAndSaysNothing()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);

        // Act: a check that would block, so a check spent here could not pass unnoticed.
        HookRun run = await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Violations);

        // Assert: a prompt hook's stdout joins the prompt and its exit 2 refuses it, so the prompt's end of the turn
        // checks nothing and says nothing. All it does is note the tree as the prompt found it, each changed code
        // file with the digest of its bytes.
        run.Result.ShouldProceedSilently();
        run.ShouldNotHaveRunTheCheck("The prompt's end of the turn must never spend a check.");
        string baseline = ReadState(sandbox, "baseline");
        baseline.ShouldStartWith("prompt\n");
        baseline.ShouldContain($"\n{TreeSnapshot.Digest(probe)} src/Probe.cs\n");
    }

    [Fact]
    public async Task PromptPayload_LeavesTheIndexAsItWas()
    {
        // Arrange: a tracked file unchanged in content but stale in the index's cached stat, which is what a status
        // refreshes and writes back when it is allowed to.
        HookSandbox sandbox = ArrangeRepository();
        string probe = CommitProbe(sandbox);
        FixtureEdits.MakeGitIndexStatStale(probe);
        string index = Path.Combine(sandbox.ProjectDirectory, ".git", "index");
        byte[] before = File.ReadAllBytes(index);

        // Act
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert
        File.ReadAllBytes(index)
            .ShouldBe(
                before,
                "The hook's status must not refresh and rewrite the index: that takes index.lock from under a "
                + "commit made at the same moment.");
    }

    [Fact]
    public async Task PromptPayload_WithABaselineStanding_KeepsIt()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        string first = ReadState(sandbox, "baseline");
        File.WriteAllText(Path.Combine(sandbox.ProjectDirectory, "src", "Second.cs"), "public sealed class Second;\n");

        // Act
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert: no stop has ended the turn the first baseline belongs to: a prompt the client fires mid-turn, or a
        // turn the user interrupted. Replacing the baseline now would take in whatever that turn wrote, and the stop
        // would then skip code nothing ever checked.
        ReadState(sandbox, "baseline")
            .ShouldBe(first);
    }

    [Fact]
    public async Task StopPayload_TurnThatWroteNoCode_SkipsTheCheckOverAChangedTree()
    {
        // Arrange: code that was already changed when the prompt arrived, another session's work or an earlier
        // turn's, still uncommitted.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations);

        // Assert: the session's first stop, with no verdict of its own to lean on, over a tree carrying code it
        // never wrote. The turn has nothing of its own to check, so it spends no check and reports nobody's
        // warnings to an agent that had only read.
        stop.Result.ShouldProceedSilently();
        stop.ShouldNotHaveRunTheCheck("A turn that wrote no code since its prompt must not spend a check.");
        ReadState(sandbox, "baseline")
            .ShouldStartWith(
                "stop\n",
                customMessage: "A stop that stands ends the turn, and the baseline becomes the tree the stop left, "
                               + "for the next prompt to replace.");
    }

    [Fact]
    public async Task StopPayload_TurnThatWroteCode_ChecksWhateverTheBaseline()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        // The listing is the same before and after, untracked both times, so the file's bytes are all that say the
        // turn wrote it.
        Rewrite(probe);

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, quiet: true);

        // Assert
        stop.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory,
            "A file the baseline listed holds new bytes since the prompt, so the turn wrote code and the stop has to "
            + "check it.");
    }

    [Fact]
    public async Task StopPayload_RedVerdict_OutranksTheBaseline()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations);

        // Assert: the turn wrote nothing, but the red verdict is this session's own and still unanswered, so the
        // stop checks again. The baseline decides which changes are the turn's; it never overrides a red verdict.
        stop.ShouldExitWith(HookExitCodes.Block);
        stop.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A red verdict is never skipped, whatever the baseline says.");
    }

    [Fact]
    public async Task SubagentStopPayload_LeavesTheTurnsBaselineStanding()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Act
        HookRun worker = await FireAsync(
            sandbox, StopPayload(sandbox, eventName: "SubagentStop"), ExitCodes.Violations);

        // Assert: a worker that wrote nothing is skipped on the same terms as the main agent, but it stops inside
        // the turn, so the turn's baseline has to outlive it for the main agent's own stop to use.
        worker.ShouldExitWith(HookExitCodes.Proceed);
        worker.ShouldNotHaveRunTheCheck("A worker that wrote no code must not spend a check either.");
        File.Exists(StatePath(sandbox, "baseline"))
            .ShouldBeTrue("A subagent's stop does not end the turn, so the baseline stays for the main agent.");
    }

    [Fact]
    public async Task StopPayload_CodeRewrittenAsItWas_SkipsTheCheck()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        // A formatter run that changed nothing, a checkout of the same content, a build step that touches sources:
        // the file is written and its timestamp moves, but its bytes do not.
        File.WriteAllText(probe, File.ReadAllText(probe));
        File.SetLastWriteTimeUtc(probe, DateTime.UtcNow.AddMinutes(1));

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations);

        // Assert
        stop.Result.ShouldProceedSilently();
        stop.ShouldNotHaveRunTheCheck("Code written back with the same bytes is no change, whatever its timestamp says.");
    }

    [Fact]
    public async Task StopPayload_AnotherWriterDuringTheTurn_ChecksOnlyWhatChanged()
    {
        // Arrange: code already changed when the prompt arrived, and a second file another session writes into the
        // same checkout while this turn only reads.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        WriteOther(sandbox.ProjectDirectory, "Concurrent.cs");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert: bytes cannot say who wrote them, so the turn's stop checks. What it hands the check is what changed
        // during the turn, and nothing that was already changed when the turn began, so a tripwire never warns this
        // agent about code that sat in the tree before its prompt.
        stop.ShouldHaveHandedTheCheckChangedFiles(
            ["src/Concurrent.cs"],
            "Only the file whose bytes moved during the turn is the turn's change; the probe was dirty before it.");
    }

    [Fact]
    public async Task StopPayload_CommitOfCodeAsItStood_SkipsTheCheck()
    {
        // Arrange: another session commits the code that was dirty when the prompt arrived, as it stood.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        GitCommand.Run(sandbox.ProjectDirectory, "add", "src/Probe.cs");
        GitCommand.Commit(sandbox.ProjectDirectory, "-m", "probe");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Violations);

        // Assert: HEAD moved and the listing emptied, but the tree holds the same code it did, so the turn wrote
        // nothing a check could be about.
        stop.Result.ShouldProceedSilently();
        stop.ShouldNotHaveRunTheCheck("A commit that took the code in as it stood changes no code file's bytes.");
    }

    [Fact]
    public async Task StopPayload_HeadMoveThatBroughtCode_ChecksWhatItBrought()
    {
        // Arrange: a clean tree at the prompt, then a commit that adds code, as an integration into this checkout does.
        HookSandbox sandbox = ArrangeRepository();
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        WriteOther(sandbox.ProjectDirectory, "Brought.cs");
        GitCommand.Run(sandbox.ProjectDirectory, "add", "src/Brought.cs");
        GitCommand.Commit(sandbox.ProjectDirectory, "-m", "brought");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert
        stop.ShouldHaveHandedTheCheckChangedFiles(
            ["src/Brought.cs"], "A file a commit changed during the turn holds new bytes, clean or not.");
    }

    [Fact]
    public async Task SubagentStopAfterTheTurnEnded_ComparesWithTheTreeTheStopLeft()
    {
        // Arrange: a turn that wrote nothing ends while a background subagent is still at work.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);
        string worker = StopPayload(sandbox, eventName: "SubagentStop", agentId: "a1");

        // Act
        HookRun idle = await FireAsync(sandbox, worker, ExitCodes.Violations);
        WriteOther(sandbox.ProjectDirectory, "Late.cs");
        HookRun wrote = await FireAsync(sandbox, worker, ExitCodes.Clean);

        // Assert: the turn's stop left its tree as the baseline, so a subagent that stops later has something to be
        // compared with. Had the stop deleted it, the subagent would have only the verdict skip, and with no green
        // verdict over the tree it would spend a full check on code it never touched.
        idle.ShouldNotHaveRunTheCheck("A subagent that changed nothing after the turn's stop must not spend a check.");
        wrote.ShouldHaveHandedTheCheckChangedFiles(
            ["src/Late.cs"], "What the subagent's stop checks is what changed after the turn's stop, and only that.");
    }

    [Fact]
    public async Task PromptPayload_ReplacesTheBaselineAStopLeft()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);
        string second = WriteOther(sandbox.ProjectDirectory, "Second.cs");

        // Act
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);

        // Assert: a stop's baseline stands only until the next turn begins, whose prompt records the tree it found.
        string baseline = ReadState(sandbox, "baseline");
        baseline.ShouldStartWith("prompt\n");
        baseline.ShouldContain($"\n{TreeSnapshot.Digest(second)} src/Second.cs\n");
    }

    [Fact]
    public async Task StopPayload_WithNoBaseline_HandsTheCheckEveryChangedCodeFile()
    {
        // Arrange: a hook wired without the prompt entry, so the first stop has no baseline to compare with.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        WriteOther(sandbox.ProjectDirectory, "Other.cs");
        File.WriteAllText(Path.Combine(sandbox.ProjectDirectory, "notes.md"), "not code\n");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert: the stop already listed the changed code to snapshot it, so the check is handed that listing rather
        // than asking git for the same status a second time.
        stop.ShouldHaveHandedTheCheckChangedFiles(
            ["src/Other.cs", "src/Probe.cs"], "With no baseline, every changed code file the stop listed is changed.");
    }

    [Fact]
    public async Task StopPayload_RecordsAnOlderBuildWrote_ReadAsNone()
    {
        // Arrange: a green verdict and a baseline in the shape a build that compared timestamps wrote, over the tree
        // exactly as it stands, which that build would have skipped.
        HookSandbox sandbox = ArrangeRepository();
        WriteProbe(sandbox);
        string head = GitCommand.Output(sandbox.ProjectDirectory, "rev-parse", "HEAD")
            .Trim();
        string session = Path.GetDirectoryName(StatePath(sandbox, "verdict"))!;
        Directory.CreateDirectory(session);
        File.WriteAllText(StatePath(sandbox, "verdict"), $"green\n{head}\n?? src/Probe.cs\n");
        File.WriteAllText(StatePath(sandbox, "baseline"), $"{head}\n?? src/Probe.cs\n");

        // Act
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean, quiet: true);

        // Assert: neither record holds the bytes a skip now compares, so neither can vouch for the tree.
        stop.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A record that does not read as this build writes one is no record, so the stop checks.");
    }

    [Fact]
    public async Task StopPayload_ConfigError_EndsTheTurn()
    {
        // Arrange: a turn that wrote code, so its stop is due a check.
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        Rewrite(probe);

        // Act: a check that could not run.
        HookRun stop = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Error);

        // Assert: the user reads the error and the turn ends as it would on any stop that stands, so the next
        // prompt records a baseline of its own rather than inheriting this one.
        stop.Result.ShouldReportConfigError();
        ReadState(sandbox, "baseline")
            .ShouldStartWith(
                "stop\n", customMessage: "A stop the hook could not check still ends the turn, and rolls its baseline.");
    }

    [Fact]
    public async Task MalformedSettingsFile_IsAConfigErrorAndSpendsNoCheck()
    {
        // Arrange: the tree's own settings file, which every run resolves the solution through.
        HookSandbox sandbox = ArrangeRepository();
        File.WriteAllText(Path.Combine(sandbox.ProjectDirectory, "sliderule.json"), "{ \"solution\": ");

        // Act
        HookRun run = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert: resolving the solution is the hook's own work, done inside its own error handling, so a settings
        // file that will not parse is reported without blocking the agent, and no check is spent on a guess.
        run.Result.ShouldReportConfigError("sliderule.json: not valid JSON");
        run.ShouldNotHaveRunTheCheck("A settings file that will not parse leaves no solution to check.");
    }

    [Fact]
    public async Task StopPayload_TheSameWarningsAgain_AreNotRepeated()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        string payload = StopPayload(sandbox);
        (await FireAsync(sandbox, payload, ExitCodes.Clean))
            .Result.ShouldProceedWith(CannedReport);
        Rewrite(probe);

        // Act
        HookRun again = await FireAsync(sandbox, payload, ExitCodes.Clean);

        // Assert: a tripwire warns for as long as its file stays changed, which on a tree nobody commits mid-session
        // is every stop. The check still runs, because the tree moved; what it found is what this agent was told
        // last time, so it is not told again.
        again.ShouldHaveRunTheCheckIn(sandbox.ProjectDirectory, "The tree moved, so the check has to run.");
        again.Result.ShouldProceedSilently();
        File.Exists(StatePath(sandbox, "rounds"))
            .ShouldBeFalse("A repeat that was not passed on continues nothing, so the count of continuations resets.");
    }

    [Fact]
    public async Task StopPayload_WarningsThatLapsedAndReturned_AreSaidAgain()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        string payload = StopPayload(sandbox);
        await FireAsync(sandbox, payload, ExitCodes.Clean);
        Rewrite(probe);
        await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);
        Rewrite(probe);

        // Act
        HookRun returned = await FireAsync(sandbox, payload, ExitCodes.Clean);

        // Assert: not "never twice". A clean run with nothing to say in between means the warnings went away, and
        // a warning that comes back is news about a new edit.
        returned.Result.ShouldProceedWith(CannedReport);
    }

    [Fact]
    public async Task StopPayload_WarningsASubagentWasGiven_StillReachTheMainAgent()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string probe = WriteProbe(sandbox);
        (await FireAsync(sandbox, StopPayload(sandbox, eventName: "SubagentStop", agentId: "a1"), ExitCodes.Clean))
            .Result.ShouldProceedWith(CannedReport);
        Rewrite(probe);

        // Act
        HookRun main = await FireAsync(sandbox, StopPayload(sandbox), ExitCodes.Clean);

        // Assert: what a worker was told is in the worker's context, not the main agent's, so the record of what was
        // said is kept per agent and the main agent still gets the warnings once.
        main.Result.ShouldProceedWith(CannedReport);
    }

    [Fact]
    public async Task StopPayload_SessionInAnotherRepository_ChecksTheProject()
    {
        // Arrange
        HookSandbox sandbox = ArrangeRepository();
        string nested = Path.Combine(sandbox.ProjectDirectory, "notes");
        string beside = Path.Combine(sandbox.Root, "beside");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(beside);
        GitCommand.Run(nested, "init");
        GitCommand.Run(beside, "init");

        // Act: two sessions, because in one the second stop would skip on the first's green verdict and check
        // nothing.
        HookRun fromNested = await FireAsync(sandbox, StopPayload(sandbox, cwd: nested), ExitCodes.Clean, quiet: true);
        HookRun fromBeside = await FireAsync(
            sandbox, StopPayload(sandbox, "s2", cwd: beside), ExitCodes.Clean, quiet: true);

        // Assert: a session that changed directory into another repository, one nested inside the project or one
        // beside it, went there to read something. That repository has no solution to check, so adopting it would
        // fail every stop as a config error; the tree the session answers for is still the project's.
        fromNested.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A session in a nested repository must still have the project checked.");
        fromNested.ShouldNotHaveRunTheCheckIn(nested, "and never the nested repository in its place.");
        fromBeside.ShouldHaveRunTheCheckIn(
            sandbox.ProjectDirectory, "A session in a repository beside the project must have the project checked.");
        fromBeside.ShouldNotHaveRunTheCheckIn(beside, "and never the repository beside it.");
    }

    /// <summary>
    ///     Runs the hook against <paramref name="sandbox" /> with <paramref name="payload" /> on stdin, as Claude
    ///     Code feeds a hook its event, and a scripted check that exits <paramref name="checkExit" />.
    /// </summary>
    /// <remarks>
    ///     The optional knobs carry the shapes the guard has to tell apart. <paramref name="workingDirectory" /> is
    ///     the directory the hook is started in, defaulting to the sandbox root, which is <em>not</em> the project
    ///     directory, so every row also proves the <c>CLAUDE_PROJECT_DIR</c> anchoring; the rows that care where the
    ///     session sits name a directory of their own. <paramref name="withProjectDirectory" /> decides whether
    ///     <c>CLAUDE_PROJECT_DIR</c> is set at all: false is the hand run outside Claude Code.
    ///     <paramref name="quiet" /> silences the check, which is how a clean check with no warnings, the check's
    ///     own silence under hook mode, is put in front of the hook. <paramref name="redTree" /> names a tree whose
    ///     check reports violations whatever <paramref name="checkExit" /> says, for the rows that check two, and
    ///     <paramref name="stdout" /> replaces what every check writes to stdout.
    /// </remarks>
    private static async Task<HookRun> FireAsync(
        HookSandbox sandbox,
        string payload,
        int checkExit,
        string? workingDirectory = null,
        bool withProjectDirectory = true,
        bool quiet = false,
        string? stateDirectory = null,
        int maxRounds = 3,
        string? solution = null,
        string? spec = null,
        string? redTree = null,
        string? stdout = null)
    {
        string launchDirectory = workingDirectory ?? sandbox.Root;
        string checkOutput = stdout ?? (quiet ? "" : CannedReport + Environment.NewLine);
        var check = new ScriptedHookCheck(checkExit, checkOutput, redTree);
        FakeEnvironment environment = new FakeEnvironment()
            .SetVariable(SlideRuleEnvVars.HookStateDirectory, stateDirectory ?? sandbox.StateDirectory)
            .SetVariable(SlideRuleEnvVars.ClaudeProjectDirectory, withProjectDirectory ? sandbox.ProjectDirectory : null);
        var request = new HookRequest(solution, spec, "HEAD", maxRounds, launchDirectory);

        CliResult result = await CliResult.CapturedAsync((output, error) =>
            new HookRunner(output, error, new StringReader(payload), environment, check).RunAsync(request, Ct));

        return new HookRun(result, check.Requests, sandbox.ProjectDirectory, launchDirectory);
    }

    /// <summary>
    ///     A throwaway project directory with a state directory beside it: what one run is fired against. Minting it
    ///     apart from the run is what lets a row build a git repository, or a linked worktree, inside it first.
    /// </summary>
    private static HookSandbox Arrange()
    {
        string root = Path.Combine(TestTempRoot.For("hook-verb"), Guid.NewGuid()
            .ToString("N"));
        string projectDirectory = Path.Combine(root, "project");
        string stateDirectory = Path.Combine(root, "state");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(stateDirectory);

        return new HookSandbox(root, projectDirectory, stateDirectory);
    }

    /// <summary>
    ///     The same sandbox with a git repository under its project directory: what every row that fires at a
    ///     working tree wants, and the only shape in which a linked worktree can be added or a turn-end verdict can
    ///     have a HEAD to record.
    /// </summary>
    private static HookSandbox ArrangeRepository()
    {
        HookSandbox sandbox = Arrange();
        InitialiseRepository(sandbox.ProjectDirectory);

        return sandbox;
    }

    /// <summary>
    ///     Makes <paramref name="directory" /> a git repository with one empty commit: enough for a working tree to
    ///     exist and for a worktree to be added, and nothing more.
    /// </summary>
    /// <remarks>
    ///     A copy of <see cref="TemplateGitDirectory" /> rather than an <c>init</c> and a commit of its own: the
    ///     template's one commit, made through <see cref="GitCommand.Commit" /> so it succeeds whatever the host's
    ///     global git config says, costs its child processes once per test process, and every row after it pays for
    ///     a file copy. Every row's HEAD is then the same commit, which is harmless: each row keeps its state in its
    ///     own sandbox, so no row can read a verdict another recorded against that HEAD. The sample scripts under
    ///     <c>hooks/</c> are left behind, since nothing runs them. The rows that need a tracked file commit one in
    ///     their own copy, and every git command the hook itself runs is still a read.
    /// </remarks>
    private static void InitialiseRepository(string directory)
    {
        DirectoryTree.Copy(
            TemplateGitDirectory.Value,
            Path.Combine(directory, ".git"),
            static path => !path.StartsWith("hooks/", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Adds a linked worktree under <c>.claude/worktrees/</c>, which is where Claude Code puts one and therefore
    ///     the only placement that reproduces the shape: inside the project directory by path, and a working tree of
    ///     its own by git.
    /// </summary>
    private static string AddLinkedWorktree(HookSandbox sandbox)
    {
        string worktree = Path.Combine(sandbox.ProjectDirectory, ".claude", "worktrees", "w1");
        GitCommand.Run(sandbox.ProjectDirectory, "worktree", "add", worktree);

        return worktree;
    }

    /// <summary>
    ///     A turn-end payload, with <paramref name="cwd" /> defaulting to the sandbox's project directory, the
    ///     session's own tree, which is what the hook resolves the check's tree from.
    /// </summary>
    private static string StopPayload(
        HookSandbox sandbox,
        string sessionId = HookPayloads.Session,
        string promptId = "p1",
        bool stopHookActive = false,
        string eventName = "Stop",
        string? cwd = null,
        string? agentId = null,
        string? transcriptPath = null,
        string? agentTranscriptPath = null)
    {
        string directory = cwd ?? sandbox.ProjectDirectory;

        return HookPayloads.Stop(
            directory, sessionId, promptId, stopHookActive, eventName, agentId, transcriptPath, agentTranscriptPath);
    }

    /// <summary>
    ///     Writes <paramref name="transcript" /> beside the sandbox's state, where no tree of the project can see it,
    ///     and returns its path.
    /// </summary>
    private static string SaveTranscript(HookSandbox sandbox, HookTranscript transcript)
    {
        return transcript.SaveTo(Path.Combine(sandbox.Root, "transcripts", $"{Guid.NewGuid():N}.jsonl"));
    }

    /// <summary>
    ///     A linked worktree with <c>src/Probe.cs</c> written into it, as an agent whose directory stayed in the main
    ///     checkout writes one by its absolute path: the worktree's root and the file.
    /// </summary>
    private static (string Worktree, string Probe) AddWrittenWorktree(HookSandbox sandbox)
    {
        string worktree = AddLinkedWorktree(sandbox);
        string probe = Path.Combine(worktree, "src", "Probe.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        File.WriteAllText(probe, "public sealed class Probe;\n");

        return (worktree, probe);
    }

    /// <summary>
    ///     A written worktree, as <see cref="AddWrittenWorktree" /> adds one, through a turn whose stop checked it
    ///     green: the worktree's root, the file, and that stop's payload for a row to fire again.
    /// </summary>
    private static async Task<(string Worktree, string Probe, string StopPayload)> ArrangeCheckedWorktreeAsync(
        HookSandbox sandbox)
    {
        (string worktree, string probe) = AddWrittenWorktree(sandbox);
        string payload = StopPayload(sandbox, transcriptPath: SaveTranscript(sandbox, new HookTranscript().Wrote(probe)));
        await FireAsync(sandbox, PromptPayload(sandbox), ExitCodes.Clean);
        await FireAsync(sandbox, payload, ExitCodes.Clean, quiet: true);

        return (worktree, probe, payload);
    }

    /// <summary>A <c>UserPromptSubmit</c> payload, with the session's own tree as its directory.</summary>
    private static string PromptPayload(HookSandbox sandbox)
    {
        return HookPayloads.Prompt(sandbox.ProjectDirectory);
    }

    /// <summary>
    ///     Gives <paramref name="probe" /> new bytes, a line more each call: the tree moves, and the next stop has to
    ///     look again.
    /// </summary>
    private static void Rewrite(string probe)
    {
        File.AppendAllText(probe, $"// rewritten {Guid.NewGuid():N}\n");
    }

    /// <summary>
    ///     A second code file, <c>src/<paramref name="name" /></c>, written into the working tree at
    ///     <paramref name="tree" /> as another session, a subagent or a shell would write it.
    /// </summary>
    private static string WriteOther(string tree, string name)
    {
        string file = Path.Combine(tree, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, $"public sealed class {Path.GetFileNameWithoutExtension(name)};\n");

        return file;
    }

    private static string StatePath(HookSandbox sandbox, string name)
    {
        return Path.Combine(sandbox.StateDirectory, "sliderule-hook", HookPayloads.Session, name);
    }

    /// <summary>
    ///     A session directory that exists but keeps nothing: a directory stands where each state file goes, so
    ///     every write is dropped and every read comes back empty, on any platform and without touching permissions.
    /// </summary>
    private static void ArrangeStateThatKeepsNothing(HookSandbox sandbox)
    {
        foreach (string name in new[] { "verdict", "rounds", "baseline", "said" })
            Directory.CreateDirectory(StatePath(sandbox, name));
    }

    /// <summary>
    ///     The <paramref name="name" /> record the session keeps for the one other tree a row wrote into, found under
    ///     the session's <c>trees</c> directory whatever digest names it.
    /// </summary>
    private static string ReadTreeState(HookSandbox sandbox, string name)
    {
        return File.ReadAllText(TreeStatePath(sandbox, name));
    }

    /// <summary>
    ///     Where the session keeps the <paramref name="name" /> record for the one other tree a row wrote into,
    ///     whatever digest names its directory.
    /// </summary>
    private static string TreeStatePath(HookSandbox sandbox, string name)
    {
        string trees = StatePath(sandbox, "trees");
        string[] records = Directory.Exists(trees)
            ? Directory.GetFiles(trees, name, SearchOption.AllDirectories)
            : [];
        records.Length.ShouldBe(1, $"The hook should have kept one {name} record for the other tree under {trees}.");

        return records[0];
    }

    private static string ReadState(HookSandbox sandbox, string name)
    {
        string path = StatePath(sandbox, name);
        File.Exists(path)
            .ShouldBeTrue($"The hook should have written its {name} file at {path}.");

        return File.ReadAllText(path);
    }

    /// <summary>
    ///     An untracked <c>src/<paramref name="name" /></c> in the sandbox, <c>src/Probe.cs</c> unless a row names
    ///     another: one code file for the changed-code listing to carry, and its bytes for the skip to compare.
    /// </summary>
    private static string WriteProbe(HookSandbox sandbox, string name = "Probe.cs")
    {
        string probe = Path.Combine(sandbox.ProjectDirectory, "src", name);
        Directory.CreateDirectory(Path.GetDirectoryName(probe)!);
        File.WriteAllText(probe, "public sealed class Probe;\n");

        return probe;
    }

    /// <summary>Writes <c>src/Probe.cs</c> and commits it, for the rows that need a tracked code file.</summary>
    private static string CommitProbe(HookSandbox sandbox)
    {
        string probe = WriteProbe(sandbox);
        GitCommand.Run(sandbox.ProjectDirectory, "add", "src/Probe.cs");
        GitCommand.Commit(sandbox.ProjectDirectory, "-m", "probe");

        return probe;
    }

    private readonly record struct HookSandbox(string Root, string ProjectDirectory, string StateDirectory);
}
