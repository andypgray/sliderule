using System.IO.Pipes;
using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Hooks.Handoff;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The hand-off's promise, warm against cold over one tree: a stop the hook sends to a live server prints what
///     the same stop checked in the hook's own process prints, on every channel, whether the server served it or
///     declined it and the hook fell back.
/// </summary>
/// <remarks>
///     <para>
///         Each row fires a real stop through <see cref="HookRunner" /> twice over the same working tree, each arm
///         with a state directory of its own so neither run's records shape the other: once through
///         <see cref="WarmHookCheck" /> against a <see cref="WarmServerUnderTest" />, once through a plain
///         in-process check. The warm arm also asserts how its hand-off ended, because a row that silently went cold
///         would match the cold arm byte for byte and prove nothing.
///     </para>
///     <para>
///         Each row also pins the verdict itself, so two arms failing alike on a setup error cannot pass as a match.
///     </para>
/// </remarks>
public sealed class HandoffParityTests
{
    private const string Category = "handoff-parity";

    private const string QuarantineWarning =
        "Changed file 'MyApp.Legacy.Billing/LegacyNote.cs' is inside quarantined scope 'legacy/billing'";

    [Fact]
    public async Task CleanStop_AfterABodyEdit_IsServedAlike()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.CleanSpecDll, repo.Root);
        EditBody(repo.Root);

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.CleanSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldProceedSilently();
        server.Outcomes.ShouldBe([HandoffOutcomes.Served]);
    }

    [Fact]
    public async Task Violations_AfterABodyEdit_AreServedAlike()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task ReplyOverTheFrameLimit_IsDeclinedAsTooLarge_AndLoggedSo()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        WriteOversizedViolations(repo);

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.ReplyTooLarge));
        server.Outcomes.ShouldBe([HandoffOutcomes.Declined(DeclineReasons.ReplyTooLarge)]);
    }

    [Fact]
    public async Task Warnings_FromAnAddedFile_AreServedAlike()
    {
        // Arrange: a new file inside a project, which the reconcile meets as a reload.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.QuarantinedSpecDll, repo.Root);
        repo.WriteQuarantineNote();

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.QuarantinedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldHaveHookAdditionalContext("Stop")
            .ShouldContain(QuarantineWarning);
    }

    [Fact]
    public async Task DeletedDocument_IsServedAlike()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        File.Delete(repo.PathOf("MyApp.Web", "HomeController.cs"));

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert: the deleted controller was the only thing the domain referenced in the web layer.
        run.ShouldMatchCold(HandoffOutcomes.Served);
        server.Outcomes.ShouldBe([HandoffOutcomes.Served]);
    }

    [Fact]
    public async Task ProjectFileEdit_ReloadsAndIsServedAlike()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        string project = repo.PathOf("MyApp.Domain", "MyApp.Domain.csproj");
        File.AppendAllText(project, "<!-- edited -->\n");
        EditBody(repo.Root);

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task StaleSpec_IsServedAlike_WithItsWarning()
    {
        // Arrange: the convention spec, a project of the solution, edited after its build.
        using TempFixtureWorkspace workspace = LayoutAppFixture.BuiltCopy();
        string root = Path.GetDirectoryName(workspace.SolutionPath)!;
        TempGitRepo.CommitAsRepository(root);
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(workspace.SolutionPath, null, root);
        using ScopedFile edit = LayoutAppFixture.EditSpecAfterBuild(workspace);

        // Act
        ParityRun run = await FireBothAsync(root, workspace.SolutionPath, null);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldHaveHookAdditionalContext("Stop")
            .ShouldContain(LayoutAppFixture.StaleSpecSentence);
    }

    [Fact]
    public async Task LinkedWorktree_IsServedByItsOwnServer()
    {
        // Arrange: a worktree of the repository, restored as its checkout is, with a server bound to the worktree's
        // own solution, which is a different identity from the checkout's.
        using var repo = new TempGitRepo();
        string worktree = Path.Combine(repo.Root, ".claude", "worktrees", "w1");
        GitCommand.Run(repo.Root, "worktree", "add", worktree);
        foreach (string obj in Directory.GetDirectories(repo.Root, "obj", SearchOption.AllDirectories)
                     .Where(path => !path.StartsWith(worktree, StringComparison.OrdinalIgnoreCase)))
            DirectoryTree.Copy(obj, Path.Combine(worktree, Path.GetRelativePath(repo.Root, obj)), static _ => true);
        string solution = Path.Combine(worktree, Path.GetFileName(repo.SolutionPath));
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            solution, CliRunner.ViolatedSpecDll, worktree);
        EditBody(worktree);

        // Act
        ParityRun run = await FireBothAsync(worktree, solution, CliRunner.ViolatedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task ProjectThatNoLongerEvaluates_GivesTheIncompleteModelsAnswerAlike()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        File.WriteAllText(repo.PathOf("MyApp.Legacy.Billing", "MyApp.Legacy.Billing.csproj"), "<Project");

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert: the check refuses a model it could not build whole, warm or cold.
        run.ShouldMatchCold(HandoffOutcomes.Served);
        run.Warm.Err.ShouldContain("MyApp.Legacy.Billing");
    }

    [Fact]
    public async Task ChangedFileThatCannotBeRead_IsDeclinedAsUnreadable()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows lets a process hold a file no other can read.");

        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);

        // Act
        ParityRun run;
        using (FileHold.Exclusive(repo.PathOf("MyApp.Domain", "OrderService.cs")))
        {
            run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);
        }

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.Unreadable));
    }

    [Fact]
    public async Task ChangedFileNoProjectHolds_IsDeclinedAsUntracked()
    {
        // Arrange: a code file at the root, in no project, so nothing in the reconcile reads it.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        File.WriteAllText(repo.PathOf("Loose.cs"), "public sealed class Loose;\n");

        // Act
        ParityRun run = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.Untracked));
        run.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task CapturePresent_IsDeclined_ForTheColdCheckToConsult()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        string cacheDirectory = CacheLocations.CacheDirectory(repo.SolutionPath, server.Environment.CacheRootOverride());
        Directory.CreateDirectory(cacheDirectory);
        string manifest = CacheLocations.CaptureManifestPathIn(cacheDirectory);
        using ScopedFile capture = ScopedFile.Hold(manifest);
        EditBody(repo.Root);

        string persisted = CacheLocations.CacheFilePath(repo.SolutionPath, server.Environment.CacheRootOverride());
        using ScopedFile cache = ScopedFile.Hold(persisted);

        // Act: each arm starts with the capture and no persisted cache. A cold check says the capture's notice only
        // when it loads the solution, which a cache the first arm wrote would spare the second.
        ParityRun run = await FireBothAsync(
            repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll, beforeEachArm: () =>
            {
                File.WriteAllText(manifest, "{}");
                File.Delete(persisted);
            });

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.Capture));
        run.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task ServerNotYetLoaded_IsDeclined_ThenServedOnceItsWarmUpHasRun()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root, warm: false);
        EditBody(repo.Root);

        // Act
        ParityRun first = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);
        await ShouldFinishWarmingUpAsync(server);
        ParityRun second = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        first.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.NotLoaded));
        second.ShouldMatchCold(HandoffOutcomes.Served);
        second.Warm.ShouldBlockWith("FAIL layering/domain-independent");
    }

    [Fact]
    public async Task StopBesideAToolCall_IsDeclinedBusy_ThenServedOnceTheCallEnds()
    {
        // Arrange: the admission slot held, as a tool call in flight holds it.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);

        // Act
        ParityRun busy;
        using (await server.Admission.EnterAsync(Ct))
        {
            busy = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);
        }

        ParityRun after = await FireBothAsync(repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll);

        // Assert
        busy.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.Busy));
        after.ShouldMatchCold(HandoffOutcomes.Served);
    }

    [Fact]
    public async Task HookConfiguredOtherwise_IsDeclined()
    {
        // Arrange: a variable MSBuild would read as a property, set for the hook and not the server.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);

        // Act
        ParityRun run = await FireBothAsync(
            repo.Root, repo.SolutionPath, CliRunner.ViolatedSpecDll,
            environment => environment.SetVariable("DefineConstants", "HOOK_ONLY"));

        // Assert
        run.ShouldMatchCold(HandoffOutcomes.Declined(DeclineReasons.Configuration));
        server.Logs.Transcript()
            .ShouldContain("Hand-off configuration differs in env:");
    }

    [Fact]
    public async Task ServedCheck_NeitherReadsNorWritesThePersistedCache()
    {
        // Arrange: no persisted cache for the solution, so a served check that wrote one would leave it there.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);
        // An earlier row's cold arm may have left one for this leased tree, in the run's own cache root.
        string persisted = CacheLocations.CacheFilePath(repo.SolutionPath, server.Environment.CacheRootOverride());
        if (File.Exists(persisted)) File.Delete(persisted);

        bool before = File.Exists(persisted);

        // Act
        HandoffReply reply = await AskAsync(server, RequestFor(server, repo));

        // Assert
        (before, reply.Declined, reply.ExitCode, File.Exists(persisted))
            .ShouldBe((false, (string?)null, (int?)ExitCodes.Violations, false));
    }

    [Fact]
    public async Task RequestForAnotherIdentity_IsDeclined()
    {
        // Arrange: a request that reached this server's endpoint while naming another build.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        HandoffRequest request = RequestForAnotherBuild(server, repo);

        // Act
        HandoffReply reply = await AskAsync(server, request);

        // Assert
        reply.ShouldBe(HandoffReply.Decline(request.Nonce, DeclineReasons.Identity));
    }

    [Fact]
    public async Task IdleConnections_LeaveTheEndpointServing()
    {
        // Arrange: more connections than a fixed instance count would allow, each sending nothing.
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root, warm: false);
        var idle = new List<NamedPipeClientStream>();
        HandoffReply reply;
        try
        {
            for (var i = 0; i < 5; i++)
                idle.Add(await HandoffEndpoint.ConnectAsync(server.Binding.Identity.EndpointName, TimeSpan.FromSeconds(5), Ct));

            // Act
            reply = await AskAsync(server, RequestForAnotherBuild(server, repo));
        }
        finally
        {
            foreach (NamedPipeClientStream stream in idle) await stream.DisposeAsync();
        }

        // Assert: the request still reached the server and was answered.
        reply.Declined.ShouldBe(DeclineReasons.Identity);
    }

    [Fact]
    public async Task HookThatLeavesMidCheck_CancelsTheWork()
    {
        // Arrange
        using var repo = new TempGitRepo();
        await using WarmServerUnderTest server = await WarmServerUnderTest.StartAsync(
            repo.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root);
        EditBody(repo.Root);
        HandoffRequest request = RequestFor(server, repo);

        // Act: the request sent, and the connection closed with the server still at work.
        await using (NamedPipeClientStream stream = await HandoffEndpoint.ConnectAsync(
                         server.Binding.Identity.EndpointName, TimeSpan.FromSeconds(5), Ct))
        {
            await HandoffProtocol.TryWriteFrameAsync(
                stream, request, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, Ct);
        }

        await ShouldLogAnOutcomeAsync(server);

        // Assert: declined for the limit the hook's leaving cut short, and never written to a hook that had gone.
        server.Outcomes.ShouldBe([HandoffOutcomes.Declined(DeclineReasons.Timeout)]);
        server.Logs.Transcript()
            .ShouldContain("the hook had gone");
    }

    private static void EditBody(string root)
    {
        string service = Path.Combine(root, "MyApp.Domain", "OrderService.cs");
        File.AppendAllText(service, "\n// edited by the turn\n");
    }

    // A red report whose reply frame is over HandoffProtocol.MaxReplyBytes. The report names each type three times, a
    // name may run to 512 characters, and the frame's JSON escapes each non-ASCII character to six bytes.
    private static void WriteOversizedViolations(TempGitRepo repo)
    {
        string name = new('Ω', 500);
        IEnumerable<string> types = Enumerable.Range(0, 1100)
            .Select(i => $"public sealed class {name}{i} {{ public object M() => typeof(HomeController); }}");
        string source = string.Join("\n", ["using MyApp.Web;", "", "namespace MyApp.Domain;", "", .. types, ""]);
        File.WriteAllText(repo.PathOf("MyApp.Domain", "Oversized.cs"), source);
    }

    // The stop, fired warm and then cold over the tree at root, each arm with its own fresh state; the warm arm's
    // recorded outcome read before its state goes.
    private static async Task<ParityRun> FireBothAsync(
        string root, string solution, string? spec, Action<FakeEnvironment>? configureWarm = null,
        Action? beforeEachArm = null)
    {
        using TempDirectory warmState = TestTempRoot.Fresh(Category);
        using TempDirectory coldState = TestTempRoot.Fresh(Category);
        FakeEnvironment warmEnvironment = HookEnvironment.With(warmState.Path, root);
        configureWarm?.Invoke(warmEnvironment);
        FakeEnvironment coldEnvironment = HookEnvironment.With(coldState.Path, root);
        var request = new HookRequest(solution, spec, "HEAD", 3, root);
        var warm = new WarmHookCheck(
            new InProcessHookCheck(new ColdSolutionSource(), warmEnvironment),
            new HookBudget(DateTimeOffset.UtcNow, HookBudget.DefaultClientTimeout),
            TimeProvider.System,
            warmEnvironment,
            HandoffConfiguration.Settle(warmEnvironment, root));

        beforeEachArm?.Invoke();
        CliResult warmResult = await FireAsync(root, warmEnvironment, warm, request);
        string outcomePath = HookPayloads.StatePath(warmState.Path, "handoff");
        string outcome = File.Exists(outcomePath) ? File.ReadAllText(outcomePath) : "(no outcome recorded)";
        beforeEachArm?.Invoke();
        CliResult coldResult = await FireAsync(
            root, coldEnvironment, new InProcessHookCheck(new ColdSolutionSource(), coldEnvironment), request);

        return new ParityRun(warmResult, coldResult, outcome.TrimEnd('\n'));
    }

    private static Task<CliResult> FireAsync(string root, FakeEnvironment environment, IHookCheck check, HookRequest request)
    {
        string payload = HookPayloads.Stop(root);
        return CliResult.CapturedAsync((output, error) =>
            new HookRunner(output, error, new StringReader(payload), environment, check).RunAsync(request, Ct));
    }

    private static HandoffRequest RequestFor(WarmServerUnderTest server, TempGitRepo repo)
    {
        CheckRequest check = CheckRequests.For(server.Binding.SolutionPath, CliRunner.ViolatedSpecDll, repo.Root) with
        {
            ChangedFiles = [repo.PathOf("MyApp.Domain", "OrderService.cs")]
        };
        return new HandoffRequest(
            server.Binding.Identity, server.Binding.Configuration.Digests, "n1", check,
            (int)HookBudget.DiscoveryLimit.TotalMilliseconds);
    }

    // A request that reaches this server's endpoint while naming another build, which the server declines as Identity.
    private static HandoffRequest RequestForAnotherBuild(WarmServerUnderTest server, TempGitRepo repo)
    {
        return RequestFor(server, repo) with { Identity = server.Binding.Identity with { Build = "another" } };
    }

    private static async Task<HandoffReply> AskAsync(WarmServerUnderTest server, HandoffRequest request)
    {
        await using NamedPipeClientStream stream = await HandoffEndpoint.ConnectAsync(
            server.Binding.Identity.EndpointName, TimeSpan.FromSeconds(5), Ct);
        await HandoffProtocol.TryWriteFrameAsync(
            stream, request, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, Ct);
        return await HandoffProtocol.ReadFrameAsync(
            stream, HandoffJson.Default.HandoffReply, HandoffProtocol.MaxReplyBytes, Ct);
    }

    // The warm-up the first request started has loaded the workspace and given the slot back.
    private static async Task ShouldFinishWarmingUpAsync(WarmServerUnderTest server)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromMinutes(3));
        while (!server.Session.IsLoaded(server.Binding.SolutionPath))
            await Task.Delay(100, limit.Token);

        (await server.Admission.EnterAsync(limit.Token)).Dispose();
    }

    private static async Task ShouldLogAnOutcomeAsync(WarmServerUnderTest server)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(HookBudget.WarmLimit + TimeSpan.FromSeconds(30));
        while (server.Outcomes.Count == 0)
            await Task.Delay(100, limit.Token);
    }

    private sealed record ParityRun(CliResult Warm, CliResult Cold, string Outcome)
    {
        // Every channel alike, and the warm arm's outcome the one the row expects.
        public void ShouldMatchCold(string outcome)
        {
            Outcome.ShouldBe(outcome, $"stderr of the warm arm:\n{Warm.Err}");
            (Warm.Exit, Warm.Out, Warm.Err).ShouldBe((Cold.Exit, Cold.Out, Cold.Err));
        }
    }
}
