using System.Text.Json;
using SlideRule.Cli.Diff;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Settings;
using SlideRule.Internal;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>hook</c> pipeline: read a Claude Code hook payload → decide whether the event calls for a check → run
///     it in this process → answer in Claude Code's exit codes (<see cref="HookExitCodes" />), with the report on
///     the channel Claude Code reads for that code.
/// </summary>
/// <remarks>
///     <para>
///         <b>The events.</b> <c>Stop</c> and <c>SubagentStop</c> are the turn boundary: the agent has said it is
///         done, so the check reads the working tree, which also covers the edits a shell or a language server
///         made and no tool payload names. <c>UserPromptSubmit</c> is the turn's other end, where nothing is
///         checked and the session's tree, and each other tree the session keeps records for, is noted as the
///         prompt found it. Any other event, and no payload at all, take
///         the per-edit path, which checks statelessly and skips an edit that touched no code or another tree.
///     </para>
///     <para>
///         <b>The trees.</b> A stop checks the tree the session is in, and beside it each other working tree of the
///         project that the turn, or the subagent's run, wrote code into through the agent's edit tools, as the
///         transcript the payload names records them. Each tree is weighed by the skips below against records of
///         its own; one round is counted per stop, and a report covering a tree other than the session's names each
///         tree it covers. A transcript that cannot be read adds no tree.
///     </para>
///     <para>
///         <b>The skips.</b> A stop spends no check when no code file's bytes have changed since the last green
///         verdict, or since the turn's baseline: the tree its prompt found, or for a subagent that stops after the
///         turn has ended, the tree the turn's stop left. Another tree's baseline is the tree the turn's prompt
///         found there, or the tree the last stop that weighed it left, whichever came later; a tree no stop of the
///         session has weighed has none. Bytes, not timestamps and not HEAD, so a file rewritten as
///         it was and a commit of code as it stood both leave the skip standing. Nothing skips past a red verdict.
///         Any doubt, such as no state, no HEAD or a record that will not read, runs the check.
///     </para>
///     <para>
///         <b>What the tripwires see.</b> A check a stop runs is handed the code files whose bytes changed since
///         the baseline, so its warnings are about the turn's changes rather than everything uncommitted. Bytes
///         cannot say who wrote them, so a file another session changed during the turn is among them. With no
///         baseline the stop hands over every changed code file it listed, and only the per-edit path, or a stop
///         with no snapshot of the tree (no state, or no answer from git), leaves the check to list changed files
///         since <see cref="HookRequest.DiffBase" />.
///     </para>
///     <para>
///         <b>The exit map.</b> Clean is 0 with nothing on stdout; clean with warnings is 0 with the hook document
///         on stdout, said once per agent until the warnings lapse; a red rule is 2 with the report on stderr, for
///         at most <see cref="HookRequest.MaxRounds" /> consecutive continuations per prompt, after which it is 1
///         with the report under a line saying so. SlideRule's own failure is 1 under
///         <see cref="ConfigErrorLine" />, never 2, because 2 is the one code that tells an agent to keep working:
///         resolving the solution and spec happens in here for that reason, and so does everything else that can
///         fail.
///     </para>
/// </remarks>
/// <param name="output">Where the hook document goes: the stdout Claude Code parses on exit 0.</param>
/// <param name="error">Where reports go: the stderr Claude Code reads on a non-zero exit.</param>
/// <param name="input">The payload's stdin, or null when nothing was piped in, which reads as no payload.</param>
/// <param name="environment">
///     The environment seam <c>CLAUDE_PROJECT_DIR</c>, the state root and <c>SLIDERULE_SOLUTION_PATH</c> are
///     read through.
/// </param>
/// <param name="check">The check to spend when one is due.</param>
internal sealed class HookRunner(
    TextWriter output,
    TextWriter error,
    TextReader? input,
    IEnvironment environment,
    IHookCheck check)
{
    /// <summary>The line SlideRule's own failure is reported under on stderr, with the report below it.</summary>
    internal const string ConfigErrorLine = "sliderule config error:";

    private const string PromptEvent = "UserPromptSubmit";
    private const string NoPrompt = "noprompt";

    /// <summary>Runs the hook for the payload on stdin and returns the code Claude Code reads.</summary>
    public async Task<int> RunAsync(HookRequest request, CancellationToken ct)
    {
        var failure = new StringWriter();

        // The top-level handler every command runs under, so a failure nothing below anticipated is reported rather
        // than thrown. Here a failure is the hook's non-blocking error, not the CLI's exit 2.
        (bool decided, int code) = await CommandEntryPoint.TryRunAsync(() => DecideAsync(request, ct), failure, ct);
        if (decided) return code;

        return await ReportConfigErrorAsync(error, failure.ToString());
    }

    private async Task<int> DecideAsync(HookRequest request, CancellationToken ct)
    {
        // Read whole and once: stdin does not rewind, and every gate below answers from it.
        string? text = input is null ? null : await input.ReadToEndAsync(ct);
        HookPayload payload = HookPayload.Parse(text);
        string? projectDirectory = environment.NonBlankVariable(SlideRuleEnvVars.ClaudeProjectDirectory);
        string? hookEvent = payload.EventName;

        if (hookEvent == PromptEvent) return await OpenTurnAsync(payload, projectDirectory, request, ct);

        if (hookEvent is HookReportRenderer.StopEvent or HookReportRenderer.SubagentStopEvent)
            return await CloseTurnAsync(hookEvent, payload, projectDirectory, request, ct);

        return await EditAsync(payload, projectDirectory, request, ct);
    }

    // The prompt's end of the turn: the tree as the prompt found it, so the stop can tell the code its turn wrote
    // from the code it found there. A prompt's baseline still standing belongs to a turn no stop has ended, and it is
    // kept, along with every other tree's: it predates everything since, and replacing it would hide what that turn
    // wrote. The baseline the last turn's stop left is replaced, and so is each other tree's the session keeps
    // records for. Never a word on stdout and never an exit but 0, because a prompt hook's stdout joins the prompt
    // and its exit 2 refuses it.
    private async Task<int> OpenTurnAsync(
        HookPayload payload, string? projectDirectory, HookRequest request, CancellationToken ct)
    {
        HookState state = OpenState(payload);
        if (!state.Stateful || state.ReadBaseline() is { FromPrompt: true }) return HookExitCodes.Proceed;

        string tree = await SessionTreeAsync(payload.Cwd, projectDirectory, ct) ?? request.WorkingDirectory;
        TreeSnapshot snapshot = await HookGit.SnapshotAsync(tree, ct);
        if (!snapshot.Known) return HookExitCodes.Proceed;

        state.RecordBaseline(snapshot);
        await RecordOtherBaselinesAsync(state, tree, ct);

        return HookExitCodes.Proceed;
    }

    // Each other tree a stop of this session has weighed gets the prompt's baseline too, replacing whatever stands
    // there, so the next stop that weighs it reads the turn's own changes rather than everything since the last stop
    // that weighed it. The prompt waits on them, and they share nothing, so they are snapshotted together. A tree
    // that is gone, or whose directory git no longer answers for as that top level, keeps its records as they are;
    // so does a record that names no tree.
    private static async Task RecordOtherBaselinesAsync(HookState state, string ownTree, CancellationToken ct)
    {
        IEnumerable<Task> recordings = state.RecordedTrees()
            .Where(other => IsOtherTreeOnDisk(other.TopLevel, ownTree))
            .Select(other => RecordBaselineAsync(other.TopLevel, other.State, ct));
        await Task.WhenAll(recordings);
    }

    private static async Task RecordBaselineAsync(string tree, HookState state, CancellationToken ct)
    {
        TreeSnapshot snapshot = await HookGit.SnapshotAsync(tree, ct);
        if (snapshot.Known && snapshot.TopLevel is { } topLevel && SameDirectory(topLevel, tree))
            state.RecordBaseline(snapshot);
    }

    private async Task<int> CloseTurnAsync(
        string hookEvent, HookPayload payload, string? projectDirectory, HookRequest request, CancellationToken ct)
    {
        string tree = await SessionTreeAsync(payload.Cwd, projectDirectory, ct) ?? request.WorkingDirectory;
        HookState state = OpenState(payload);
        var trees = new List<TreeTurn> { await ExamineAsync(tree, state, own: true, ct) };

        // The project's other trees the turn wrote code into by an absolute path, each weighed by the same skips
        // against records of its own.
        foreach (string other in await WrittenTreesAsync(hookEvent, payload, tree, ct))
            trees.Add(await ExamineAsync(other, state.ForTree(other), own: false, ct));

        if (!trees.Any(static turn => turn.Due)) return EndTurn(hookEvent, trees);

        // Consecutive continuations for this prompt: a block and a warning passed on both count, and a silent green
        // resets. At the cap the hook says its piece and lets the turn end. A count that cannot be kept reads as no
        // rounds, and the continuation that fails to count it falls back to the payload's own stop_hook_active. One
        // count per stop, on the session's own state, however many trees the stop checks.
        string promptId = payload.PromptId ?? NoPrompt;
        int rounds = state.RoundsFor(promptId);
        bool atCap = rounds >= request.MaxRounds;

        var firing = new Firing(hookEvent, state, trees, promptId, rounds, atCap, payload.StopHookActive);
        return await CheckAsync(firing, request, ct);
    }

    // One tree at a stop: whether its check is due, and what its tripwires are to read. The session's own tree is
    // weighed on the session's state, another tree on the state the session keeps for it.
    private static async Task<TreeTurn> ExamineAsync(string tree, HookState state, bool own, CancellationToken ct)
    {
        TreeSnapshot snapshot = state.Stateful ? await HookGit.SnapshotAsync(tree, ct) : TreeSnapshot.Unknown;
        bool known = snapshot.Known;

        // The skip that makes a question-and-answer turn free: the last verdict still stands if it was green and no
        // code file's bytes have changed since the tree it covered.
        HookVerdict? verdict = known ? state.ReadVerdict() : null;
        if (verdict is { Green: true } && await ChangesAsync(tree, verdict.Tree, snapshot, ct) is { Count: 0 })
            return new TreeTurn(tree, state, snapshot, own, Due: false, ChangedFiles: null);

        // The same question put to the baseline, which a session's first stop can ask too: a turn that has changed
        // no code since its prompt has nothing of its own to check, whatever the tree carries from other sessions
        // or earlier turns. Never past a red verdict, which is this session's own and still unanswered.
        HookBaseline? baseline = known ? state.ReadBaseline() : null;
        IReadOnlyList<string>? turnChanges =
            baseline is null ? null : await ChangesAsync(tree, baseline.Tree, snapshot, ct);
        if (verdict is not { Green: false } && turnChanges is { Count: 0 })
            return new TreeTurn(tree, state, snapshot, own, Due: false, ChangedFiles: null);

        // What the check's tripwires read: the turn's own changes, or with no baseline every changed code file the
        // stop listed, so the check lists nothing again. With neither, it lists changed files itself.
        IReadOnlyList<string>? changed = turnChanges ?? (known ? snapshot.Paths : null);
        IReadOnlyList<string>? changedFiles = changed is null || snapshot.TopLevel is null
            ? null
            : GitChangedFiles.ComposeAbsolute(snapshot.TopLevel, changed);

        return new TreeTurn(tree, state, snapshot, own, Due: true, changedFiles);
    }

    // The project's trees other than the session's own that the turn, or the subagent's run, wrote code into
    // through the agent's edit tools, read from the transcript the payload names: each written code file goes to
    // the deepest of the project's working trees that holds it. A turn's writes are those under its prompt, and a
    // subagent's are its whole transcript. Any doubt, such as no transcript, one that will not read, or no answer
    // from git, names no tree, and the stop checks the session's own tree as it always has.
    private static async Task<IReadOnlyList<string>> WrittenTreesAsync(
        string hookEvent, HookPayload payload, string ownTree, CancellationToken ct)
    {
        bool subagent = hookEvent == HookReportRenderer.SubagentStopEvent;
        string? transcript = subagent ? payload.AgentTranscriptPath : payload.TranscriptPath;
        string? promptId = subagent ? null : payload.PromptId;
        if (transcript is null || (!subagent && promptId is null)) return [];

        IReadOnlyList<string>? written = await TurnWrites.ReadAsync(transcript, promptId, ct);
        List<string> code = written?.Where(HookCodeFiles.IsCode)
            .ToList() ?? [];
        if (code.Count == 0) return [];

        IReadOnlyList<string>? roots = await HookGit.WorktreesAsync(ownTree, ct);
        if (roots is null) return [];

        return code.Select(path => DeepestRootOf(path, roots))
            .OfType<string>()
            .Distinct(PathComparison.Comparer)
            .Where(root => IsOtherTreeOnDisk(root, ownTree))
            .ToList();
    }

    // A tree of the project other than the session's own that is still on disk: a worktree list, like the records a
    // session keeps, can outlive the removal of a worktree it names.
    private static bool IsOtherTreeOnDisk(string root, string ownTree)
    {
        return !SameDirectory(root, ownTree) && Directory.Exists(root);
    }

    // The deepest root holding `path`, or null for a path in none of them: a worktree under .claude/worktrees is
    // inside the main checkout by path, and the file belongs to the worktree.
    private static string? DeepestRootOf(string path, IReadOnlyList<string> roots)
    {
        return roots.Where(root => path.StartsWith(DirectoryForm(root).TrimEnd('/') + "/", PathComparison.Comparison))
            .MaxBy(static root => root.Length);
    }

    // The per-edit path, and a hand run. The check reads code, so an edit to anything else skips it, and so does an
    // edit that landed outside the project's own tree: the verdict would be about code the edit never touched. With
    // no payload the directory the hook was started in stands in for the edited file. Stateless: nothing is kept,
    // and nothing skips past the check once it is due.
    private async Task<int> EditAsync(
        HookPayload payload, string? projectDirectory, HookRequest request, CancellationToken ct)
    {
        string? edited = payload.EditedFile;
        if (edited is not null && !HookCodeFiles.IsCode(edited)) return HookExitCodes.Proceed;

        if (projectDirectory is not null
            && !await IsInProjectTreeAsync(edited, projectDirectory, request.WorkingDirectory, ct))
            return HookExitCodes.Proceed;

        string tree = projectDirectory ?? request.WorkingDirectory;
        TreeTurn[] trees =
            [new(tree, HookState.None, TreeSnapshot.Unknown, Own: true, Due: true, ChangedFiles: null)];
        var firing = new Firing(
            HookReportRenderer.DefaultEvent, HookState.None, trees, NoPrompt, 0, AtCap: false, StopHookActive: false);
        return await CheckAsync(firing, request, ct);
    }

    // Each due tree's check in turn, the session's own first, answered as one: SlideRule's own failure in any of
    // them ends the turn, a red rule in any blocks, and otherwise the warnings go out together. Each tree records
    // the verdict its own check gave.
    private async Task<int> CheckAsync(Firing firing, HookRequest request, CancellationToken ct)
    {
        var checks = new List<(TreeTurn Tree, HookCheckResult Result)>();
        foreach (TreeTurn tree in firing.Trees.Where(static turn => turn.Due))
            checks.Add((tree, await RunCheckAsync(firing.Event, tree, request, ct)));

        // Named when a tree other than the session's own was checked, so the agent can tell which tree a report
        // is about; a stop that checked only its own tree reads exactly as it always has.
        bool named = checks.Any(static check => !check.Tree.Own);
        foreach ((TreeTurn tree, HookCheckResult result) in checks)
            if (result.ExitCode is ExitCodes.Clean or ExitCodes.Violations)
                tree.State.RecordVerdict(result.ExitCode == ExitCodes.Clean, tree.Snapshot);

        List<(TreeTurn Tree, HookCheckResult Result)> failed = checks.Where(static check => check.Result.ExitCode is not (ExitCodes.Clean or ExitCodes.Violations))
            .ToList();
        if (failed.Count > 0)
        {
            int reported = await ReportConfigErrorAsync(error, Reports(failed, named));
            return EndTurn(firing, reported);
        }

        List<(TreeTurn Tree, HookCheckResult Result)> red = checks.Where(static check => check.Result.ExitCode == ExitCodes.Violations)
            .ToList();
        if (red.Count > 0) return await RedAsync(firing, Reports(red, named), request.MaxRounds);

        return await CleanAsync(firing, checks, named);
    }

    private async Task<int> CleanAsync(
        Firing firing, IReadOnlyList<(TreeTurn Tree, HookCheckResult Result)> checks, bool named)
    {
        HookState state = firing.State;

        // Stdout belongs to the hook document alone. A notice the run printed goes to stderr, where an exit-0 hook's
        // text reaches only the debug log, rather than into the document Claude Code parses.
        foreach ((_, HookCheckResult result) in checks)
            if (result.Error.Length > 0)
                await error.WriteAsync(result.Error);

        string document = Document(firing.Event, checks, named);
        if (document.Length == 0)
        {
            // Nothing to say. Users switch off a hook that prints something on every clean run.
            state.ForgetRounds();
            state.ForgetSaid();
            return EndTurn(firing);
        }

        // A tripwire warns for as long as its file stays changed, and a warning repeated at every stop stops being
        // read, so this agent is told a given set once.
        if (state.AlreadySaid(document))
        {
            state.ForgetRounds();
            return EndTurn(firing);
        }

        if (firing.AtCap || !MayContinue(firing)) return EndTurn(firing);

        state.RememberSaid(document);
        await output.WriteLineAsync(document);
        return HookExitCodes.Proceed;
    }

    private async Task<int> RedAsync(Firing firing, string report, int maxRounds)
    {
        if (firing.AtCap)
        {
            // The user reads it and the turn ends: a rule the agent cannot satisfy stops being a loop.
            await error.WriteLineAsync($"sliderule: still red after {maxRounds} rounds; not blocking again.");
            await error.WriteLineAsync(report);
            return EndTurn(firing, HookExitCodes.Report);
        }

        if (!MayContinue(firing)) return EndTurn(firing);

        await error.WriteLineAsync(report);
        return HookExitCodes.Block;
    }

    // Resolution and the check, each under the command handler, so whatever the CLI would refuse with exit 2 comes
    // back as a result with that code and its message: a malformed settings file, a solution that is not there, a
    // check that fails. The exit map then reports it as SlideRule's own error and ends the turn, as for any other.
    private async Task<HookCheckResult> RunCheckAsync(
        string hookEvent, TreeTurn tree, HookRequest request, CancellationToken ct)
    {
        var refusal = new StringWriter();
        (bool resolved, CheckRequest? checkRequest) = await CommandEntryPoint.TryRunAsync(
            () => Task.FromResult(CheckRequestFor(hookEvent, tree, request)), refusal, ct);

        return resolved
            ? await check.RunAsync(checkRequest!, ct)
            : new HookCheckResult(ExitCodes.Error, "", refusal.ToString());
    }

    // The check this firing calls for, rendered for the hook, with the solution and spec resolved from the tree
    // rather than from where the process started: relative paths are the tree's, and so is the settings file. Given
    // the changed files, the check reads them rather than listing its own since the diff base.
    private CheckRequest CheckRequestFor(string hookEvent, TreeTurn turn, HookRequest request)
    {
        string tree = Path.GetFullPath(turn.Tree);
        string? solution = FullPath(request.Solution, tree);
        string? spec = FullPath(request.Spec, tree);
        InvocationTargets targets = InvocationTargets.Resolve(solution, spec, tree, environment);

        return new CheckRequest(
            targets.Solution,
            targets.Spec,
            Json: false,
            HookJson: true,
            turn.ChangedFiles is null ? request.DiffBase : null,
            tree,
            NoCache: false,
            Binlog: null,
            AllowWorkspaceDiagnostics: false,
            Sarif: null,
            Rules: null,
            DocumentGrain.Full,
            hookEvent,
            turn.ChangedFiles);
    }

    // The tree the session is in. The payload names the directory the session ran from and git names the working
    // tree that directory belongs to, so a session inside a linked worktree checks its own worktree rather than the
    // checkout CLAUDE_PROJECT_DIR points at. A directory in another repository, nested in the project or beside it,
    // is not one of the project's trees: a session that went there to read something is still the project's. Only
    // a tree sharing the project's git directory counts. Null when neither the payload nor the environment names one.
    private static async Task<string?> SessionTreeAsync(
        string? sessionDirectory, string? projectDirectory, CancellationToken ct)
    {
        string? tree = sessionDirectory is null ? null : await HookGit.TopLevelAsync(sessionDirectory, ct);
        if (tree is null || projectDirectory is null || SameDirectory(tree, projectDirectory))
            return tree ?? projectDirectory;

        string? projectTree = await HookGit.TopLevelAsync(projectDirectory, ct);
        if (projectTree is null || string.Equals(tree, projectTree, StringComparison.Ordinal)) return tree;

        Task<string?> treeGitDirectoryRun = HookGit.SharedGitDirectoryAsync(tree, ct);
        Task<string?> projectGitDirectoryRun = HookGit.SharedGitDirectoryAsync(projectTree, ct);
        string? treeGitDirectory = await treeGitDirectoryRun;
        string? projectGitDirectory = await projectGitDirectoryRun;
        return string.Equals(treeGitDirectory, projectGitDirectory, StringComparison.Ordinal) ? tree : projectDirectory;
    }

    // Inside by path is not inside by tree: a linked worktree lives under the project directory, and git names the
    // worktree's own root for it. A relative payload path is project-relative. An absolute one outside the project
    // directory belongs to no tree this hook speaks for, and that compare ignores case, so what it rules out is
    // plainly somewhere else. Every doubt answers true: when in doubt, check.
    private static async Task<bool> IsInProjectTreeAsync(
        string? edited, string projectDirectory, string workingDirectory, CancellationToken ct)
    {
        string project = DirectoryForm(projectDirectory);
        string projectPrefix = project.EndsWith('/') ? project : project + "/";
        string editedDirectory = workingDirectory;
        if (edited is not null)
        {
            string parent = ParentOf(edited);
            if (!HookGit.IsRooted(parent))
                editedDirectory = projectPrefix + parent;
            else if ($"{parent}/".StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
                editedDirectory = parent;
            else
                return false;
        }

        string? editedRoot = await HookGit.TopLevelAsync(editedDirectory, ct);
        if (editedRoot is null || SameDirectory(editedRoot, project)) return true;

        string? projectRoot = await HookGit.TopLevelAsync(project, ct);
        return projectRoot is null || string.Equals(editedRoot, projectRoot, StringComparison.Ordinal);
    }

    // Whether a directory as the environment spells it is the given git top level: git prints a top level in its
    // on-disk spelling however it was asked, so a directory equal to one is that top level.
    private static bool SameDirectory(string gitTopLevel, string directory)
    {
        string normalized = DirectoryForm(directory);
        return string.Equals(gitTopLevel, normalized, PathComparison.Comparison);
    }

    // A directory as the environment spells it: forward-slashed, without a trailing slash, except that a root keeps
    // its own. Trimmed bare, "/" becomes "" and "C:\" becomes "C:", and git resolves either as the directory the
    // hook was started in.
    private static string DirectoryForm(string directory)
    {
        string trimmed = directory.Replace('\\', '/')
            .TrimEnd('/');
        bool root = trimmed.Length == 0 || (trimmed.Length == 2 && trimmed[1] == ':');
        return root ? trimmed + "/" : trimmed;
    }

    // The code files whose bytes changed between `before` and `after`, or null for doubt. Only a moved HEAD costs a
    // question, the paths its commits changed, asked commit against commit so the index is never touched.
    private static async Task<IReadOnlyList<string>?> ChangesAsync(
        string tree, TreeSnapshot before, TreeSnapshot after, CancellationToken ct)
    {
        if (!before.Known || !after.Known || after.TopLevel is not { } topLevel) return null;

        IReadOnlyList<string>? committed = string.Equals(before.Head, after.Head, StringComparison.Ordinal)
            ? null
            : await HookGit.CommittedBetweenAsync(tree, before.Head!, after.Head!, ct);
        return TurnChanges.Between(
            before, after, committed, path => TreeSnapshot.Digest(Path.Combine(topLevel, path)));
    }

    // A stop that stands ends the turn. Its baseline becomes the tree the stop found, for a subagent that stops after
    // the turn has ended, and the next prompt replaces it with its own. Only the main agent's stop ends a turn: a
    // subagent stops inside one. Every stop that weighed another tree rolls that tree's baseline, the subagent's
    // included: a stop that writes there before the next prompt is compared with the tree as this stop left it, and
    // the next prompt records its own.
    private static int EndTurn(
        string hookEvent, IReadOnlyList<TreeTurn> trees, int exitCode = HookExitCodes.Proceed)
    {
        foreach (TreeTurn tree in trees)
            if (!tree.Own || hookEvent == HookReportRenderer.StopEvent)
                tree.State.RollBaseline(tree.Snapshot);

        return exitCode;
    }

    private static int EndTurn(Firing firing, int exitCode = HookExitCodes.Proceed)
    {
        return EndTurn(firing.Event, firing.Trees, exitCode);
    }

    private HookState OpenState(HookPayload payload)
    {
        string root = environment.NonBlankVariable(SlideRuleEnvVars.HookStateDirectory) ?? Path.GetTempPath();
        return HookState.Open(root, payload.SessionId, payload.AgentId);
    }

    // SlideRule's own failure, the way the hook reports every one: the config-error line, the report under it,
    // and the non-blocking code. CliEntry reports a hook command line that does not parse through it too.
    internal static async Task<int> ReportConfigErrorAsync(TextWriter error, string report)
    {
        await error.WriteLineAsync(ConfigErrorLine);
        await error.WriteLineAsync(report.TrimEnd('\r', '\n'));
        return HookExitCodes.Report;
    }

    // Both channels, stderr first: under hook mode the report is flushed at the end of the run, so anything on
    // stderr was written before it.
    private static string Report(HookCheckResult result)
    {
        return (result.Error + result.Output).TrimEnd('\r', '\n');
    }

    // The reports of several checks as one, each under a line naming its tree when the stop is `named`.
    private static string Reports(IReadOnlyList<(TreeTurn Tree, HookCheckResult Result)> checks, bool named)
    {
        IEnumerable<string> reports = checks.Select(check => named
            ? $"{TreeLine(check.Tree)}\n{Report(check.Result)}"
            : Report(check.Result));
        return string.Join("\n\n", reports);
    }

    // The hook document for the clean checks, or "" when none had anything to say. A stop that is not `named`
    // checked one tree, and passes its document through as the check wrote it. Otherwise each check's context goes
    // under a line naming its tree, in one document.
    private static string Document(
        string hookEvent, IReadOnlyList<(TreeTurn Tree, HookCheckResult Result)> checks, bool named)
    {
        if (!named) return checks.Count == 0 ? "" : checks[0].Result.Output.TrimEnd('\r', '\n');

        List<string> contexts = checks.Select(check => (check.Tree, Context: ContextOf(check.Result.Output)))
            .Where(static check => check.Context.Length > 0)
            .Select(static check => $"{TreeLine(check.Tree)}\n{check.Context}")
            .ToList();
        return contexts.Count == 0 ? "" : HookReportRenderer.Document(string.Join("\n\n", contexts), hookEvent);
    }

    // The context a check's hook document carries, or the output itself should it not read as one.
    private static string ContextOf(string output)
    {
        string trimmed = output.TrimEnd('\r', '\n');
        if (trimmed.Length == 0) return "";

        try
        {
            HookJson? document = JsonSerializer.Deserialize(trimmed, SlideRuleJson.Context.HookJson);
            // A document without the object deserializes with it null, whatever the annotation says.
            // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
            return document?.HookSpecificOutput?.AdditionalContext ?? trimmed;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    private static string TreeLine(TreeTurn tree)
    {
        return $"sliderule: {DirectoryForm(tree.Tree)}";
    }

    // The parent of a forward-slashed path, as POSIX dirname spells it.
    private static string ParentOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash switch
        {
            < 0 => ".",
            0 => "/",
            _ => path[..slash]
        };
    }

    private static string? FullPath(string? path, string tree)
    {
        return string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path, tree);
    }

    // Whether a continuation may go ahead: counted toward the cap, or, where the count cannot be kept, the first of
    // the turn by the payload's own stop_hook_active. Either way the blocks stay bounded, where a state directory
    // that exists but keeps nothing would otherwise block at every stop.
    private static bool MayContinue(Firing firing)
    {
        bool counted = firing.State.CountRound(firing.PromptId, firing.Rounds);
        return counted || !firing.StopHookActive;
    }

    // One firing that reached the check: the event the document answers, the session's state, which keeps the round
    // count and what was said, the trees it weighed, the round the prompt is on, whether that round is the cap, where
    // the hook reports once and lets the turn end, and whether the payload says a stop hook has already continued
    // this turn.
    private sealed record Firing(
        string Event,
        HookState State,
        IReadOnlyList<TreeTurn> Trees,
        string PromptId,
        int Rounds,
        bool AtCap,
        bool StopHookActive);

    // One tree a firing weighed: its top level, the state its verdict and baseline are kept in, the tree as it
    // stood, whether it is the session's own, whether its check is due, and the changed files its tripwires are to
    // read (null for the check to list its own).
    private sealed record TreeTurn(
        string Tree,
        HookState State,
        TreeSnapshot Snapshot,
        bool Own,
        bool Due,
        IReadOnlyList<string>? ChangedFiles);
}
