using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Text;
using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Hooks.Handoff;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The hook's side of the hand-off against a scripted cold check: each way a warm attempt ends, the outcome it
///     records, and that every end but a served reply hands back exactly what the cold check gave.
/// </summary>
/// <remarks>
///     A server here is a stand-in that owns the real endpoint for the row's identity and answers with whatever the
///     row scripts, so a reply's shape can be wrong in ways the real server never makes it.
/// </remarks>
public sealed class WarmHookCheckTests
{
    private const string Category = "warm-hook-check";

    private static readonly HookCheckResult ColdAnswer = new(ExitCodes.Violations, "cold out\n", "cold err\n");

    [Fact]
    public void ForThisProcess_ChecksInTheCanonicalEnvironment()
    {
        // Arrange: the cold fallback runs in this environment, so it must be the form a server checks in too.
        FakeEnvironment environment = new FakeEnvironment().SetVariable("SHELL", "/usr/bin/bash");
        ScriptedHookCheck cold = ColdCheck();

        // Act
        _ = WarmHookCheck.ForThisProcess(cold, environment);

        // Assert
        environment.GetVariable("SHELL").ShouldBeNull();
    }

    [Fact]
    public async Task Disabled_ChecksCold()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);
        FakeEnvironment environment = new FakeEnvironment().SetVariable(SlideRuleEnvVars.DisableWarmHook, "TRUE");

        // Act and assert
        await ShouldGoColdAsync(root, environment, Request(root), HandoffOutcomes.Disabled);
    }

    [Fact]
    public async Task NoChangedFileList_ChecksCold()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act and assert
        await ShouldGoColdAsync(
            root, new FakeEnvironment(), Request(root) with { ChangedFiles = null }, HandoffOutcomes.NoChangedFiles);
    }

    [Fact]
    public async Task UnresolvedSolution_ChecksCold_ForTheColdCheckToReport()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);
        CheckRequest request = Request(root) with { Solution = root.PathOf("Missing.sln") };

        // Act and assert
        await ShouldGoColdAsync(root, new FakeEnvironment(), request, HandoffOutcomes.Unresolved);
    }

    [Fact]
    public async Task NoServer_ChecksCold()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act and assert
        await ShouldGoColdAsync(root, new FakeEnvironment(), Request(root), HandoffOutcomes.NoServer);
    }

    [Fact]
    public async Task ASecondTree_SpendsTheSameBudget_AndFindsNoReserveLeft()
    {
        // Arrange: one firing's clock, which the first tree's check moves past the last moment a warm attempt fits.
        using TempDirectory root = TestTempRoot.Fresh(Category);
        ScriptedHookCheck cold = ColdCheck();
        var clock = new SteppedClock(Origin);
        WarmHookCheck check = Check(cold, new FakeEnvironment(), root, clock);

        // Act
        HookCheckResult first = await check.RunAsync(Request(root), Ct);
        clock.Now = Origin + TimeSpan.FromSeconds(460);
        HookCheckResult second = await check.RunAsync(Request(root), Ct);

        // Assert
        (first.Handoff, second.Handoff).ShouldBe((HandoffOutcomes.NoServer, HandoffOutcomes.NoReserve));
    }

    [Fact]
    public async Task ServedReply_IsTheAnswer_AndTheColdCheckNeverRuns()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act
        (HookCheckResult result, ScriptedHookCheck cold) = await AgainstStandInAsync(
            root, static request => new HandoffReply(request.Nonce, null, ExitCodes.Clean, "warm out\n", ""));

        // Assert
        result.ShouldBe(new HookCheckResult(ExitCodes.Clean, "warm out\n", "", HandoffOutcomes.Served));
        cold.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Decline_ChecksCold_NamingTheReason()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act
        (HookCheckResult result, ScriptedHookCheck cold) = await AgainstStandInAsync(
            root, static request => HandoffReply.Decline(request.Nonce, DeclineReasons.Busy));

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Declined(DeclineReasons.Busy) });
        cold.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("another request's nonce")]
    [InlineData("a completed reply without its channels")]
    [InlineData("a completed reply without its exit code")]
    [InlineData("bytes that are not a frame")]
    public async Task MalformedReply_ChecksCold(string shape)
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act
        (HookCheckResult result, _) = await AgainstStandInAsync(root, request => shape switch
        {
            "another request's nonce" => new HandoffReply("other", null, 0, "", ""),
            "a completed reply without its channels" => new HandoffReply(request.Nonce, null, 0, null, null),
            "a completed reply without its exit code" => Framed($$"""{"Nonce":"{{request.Nonce}}","Output":"","Error":""}"""),
            _ => Framed("{{{")
        });

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Malformed }, shape);
    }

    [Fact]
    public async Task ServerThatClosesWithoutAReply_ChecksCold()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act
        (HookCheckResult result, _) = await AgainstStandInAsync(root, static _ => Array.Empty<byte>());

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Disconnected });
    }

    [Fact]
    public async Task ServerThatNeverReplies_ChecksColdAtTheWarmLimit()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);

        // Act
        (HookCheckResult result, _) = await AgainstStandInAsync(root, static _ => null, clock: new ExpiringClock(Origin));

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Timeout });
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task OwnerThatCannotBeProbed_ChecksCold()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The owner probe opens a Windows mutex.");

        // Arrange: the row's ownership mutex exists, with a security descriptor that lets no one open it.
        using TempDirectory root = TestTempRoot.Fresh(Category);
        var environment = new FakeEnvironment();
        CheckRequest request = Request(root);
        HandoffIdentity identity = HandoffIdentity.Resolve(request.Solution, null, root.Path, environment, out _);
        using Mutex locked = LockedMutex(HandoffEndpoint.OwnershipName(identity.EndpointName));
        ScriptedHookCheck cold = ColdCheck();

        // Act
        HookCheckResult result = await Check(cold, environment, root).RunAsync(request, Ct);

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Disconnected });
    }

    [Fact]
    public async Task CancelledHook_ChecksColdUnderTheSameToken()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        (HookCheckResult result, ScriptedHookCheck cold) = await AgainstStandInAsync(
            root, static request => new HandoffReply(request.Nonce, null, 0, "warm", ""), cancelled.Token);

        // Assert
        result.ShouldBe(ColdAnswer with { Handoff = HandoffOutcomes.Cancelled });
        cold.Requests.Count.ShouldBe(1);
    }

    private static readonly DateTimeOffset Origin = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static async Task ShouldGoColdAsync(
        TempDirectory root, FakeEnvironment environment, CheckRequest request, string outcome)
    {
        ScriptedHookCheck cold = ColdCheck();

        HookCheckResult result = await Check(cold, environment, root).RunAsync(request, Ct);

        result.ShouldBe(ColdAnswer with { Handoff = outcome });
        cold.Requests.ShouldBe([request]);
    }

    // One connection to a stand-in owning the row's endpoint, which reads the request and answers it as answer
    // says: a reply framed as the protocol frames it, raw bytes (none for a closed connection), or null to never
    // answer.
    private static async Task<(HookCheckResult Result, ScriptedHookCheck Cold)> AgainstStandInAsync(
        TempDirectory root, Func<HandoffRequest, object?> answer, CancellationToken? token = null,
        TimeProvider? clock = null)
    {
        var environment = new FakeEnvironment();
        CheckRequest request = Request(root);
        HandoffIdentity identity = HandoffIdentity.Resolve(request.Solution, null, root.Path, environment, out _);
        using EndpointOwnership ownership = HandoffEndpoint.TryOwn(identity.EndpointName)
                                            ?? throw new InvalidOperationException("The row's endpoint is owned.");
        using var finished = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        Task standIn = ServeOnceAsync(ownership, answer, finished.Token);
        ScriptedHookCheck cold = ColdCheck();

        HookCheckResult result = await Check(cold, environment, root, clock)
            .RunAsync(request, token ?? Ct);

        await finished.CancelAsync();
        await standIn;
        return (result, cold);
    }

    private static async Task ServeOnceAsync(
        EndpointOwnership ownership, Func<HandoffRequest, object?> answer, CancellationToken finished)
    {
        await using NamedPipeServerStream stream = HandoffEndpoint.CreateServerStream(ownership);
        try
        {
            await stream.WaitForConnectionAsync(finished);
            HandoffRequest request = await HandoffProtocol.ReadFrameAsync(
                stream, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, finished);
            switch (answer(request))
            {
                case HandoffReply reply:
                    await HandoffProtocol.TryWriteFrameAsync(
                        stream, reply, HandoffJson.Default.HandoffReply, HandoffProtocol.MaxReplyBytes, finished);
                    break;
                case byte[] raw:
                    await stream.WriteAsync(raw, finished);
                    break;
                default:
                    await Task.Delay(Timeout.Infinite, finished);
                    break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidDataException)
        {
            // The hook gave up, was never coming, or left before its request frame was whole; the row asserts on
            // what the hook did.
        }
    }

    // A frame as the protocol lays it out: a four-byte little-endian length, then the bytes.
    private static byte[] Framed(string body)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        return [.. length, .. bytes];
    }

    [SupportedOSPlatform("windows")]
    private static Mutex LockedMutex(string name)
    {
        var security = new MutexSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return MutexAcl.Create(false, name, out _, security);
    }

    private static CheckRequest Request(TempDirectory root)
    {
        string solution = File.Exists(root.PathOf("App.sln")) ? root.PathOf("App.sln") : root.WriteFile(["App.sln"], "");
        return CheckRequests.For(solution, null, root.Path) with { ChangedFiles = [root.PathOf("A.cs")] };
    }

    private static ScriptedHookCheck ColdCheck()
    {
        return new ScriptedHookCheck(ColdAnswer.ExitCode, ColdAnswer.Output, error: ColdAnswer.Error);
    }

    private static WarmHookCheck Check(
        IHookCheck cold, FakeEnvironment environment, TempDirectory root, TimeProvider? clock = null)
    {
        var budget = new HookBudget(Origin, HookBudget.DefaultClientTimeout);
        return new WarmHookCheck(
            cold, budget, clock ?? new SteppedClock(Origin), environment,
            HandoffConfiguration.Settle(environment, root.Path));
    }

    private sealed class SteppedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }

    // A clock fixed at now whose timers fire at once, so a limit the check sets on it expires without waiting.
    private sealed class ExpiringClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            return System.CreateTimer(callback, state, TimeSpan.Zero, period);
        }
    }
}
