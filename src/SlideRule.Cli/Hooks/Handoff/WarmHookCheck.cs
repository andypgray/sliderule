using System.Diagnostics;
using System.IO.Pipes;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     The hook's check when a live <c>sliderule mcp</c> server for the same solution, spec and build may answer it:
///     the server's warm check where it serves one in time, and the cold check in this process otherwise. Either way
///     the hook prints what the cold check would have.
/// </summary>
/// <remarks>
///     A completed reply is the answer, an error exit included. Every other end (no server, a decline, a timeout, a
///     closed connection, a malformed or incomplete reply) runs the cold check, and the result names which it was
///     in <see cref="HookCheckResult.Handoff" />. The configuration is taken once, at construction, before any check
///     registers MSBuild, because a cold check rewrites variables the next tree's comparison would read.
/// </remarks>
internal sealed class WarmHookCheck(
    IHookCheck cold,
    HookBudget budget,
    TimeProvider clock,
    IEnvironment environment,
    HandoffConfiguration configuration) : IHookCheck
{
    /// <summary>
    ///     The warm check for this process: its own budget and clock, and the configuration it holds in the working
    ///     directory it started in, once <paramref name="environment" /> is canonicalized, so its cold fallback checks
    ///     in the same environment a server would.
    /// </summary>
    public static WarmHookCheck ForThisProcess(IHookCheck cold, IEnvironment environment)
    {
        HookBudget budget = HookBudget.ForThisProcess(environment);
        HandoffConfiguration configuration = HandoffConfiguration.Settle(environment, Directory.GetCurrentDirectory());
        return new WarmHookCheck(cold, budget, TimeProvider.System, environment, configuration);
    }

    /// <inheritdoc />
    public async Task<HookCheckResult> RunAsync(CheckRequest request, CancellationToken ct)
    {
        if (environment.IsFlagSet(SlideRuleEnvVars.DisableWarmHook)) return await ColdAsync(request, HandoffOutcomes.Disabled, ct);

        if (request.ChangedFiles is null) return await ColdAsync(request, HandoffOutcomes.NoChangedFiles, ct);

        if (!budget.LeavesRoomForWarm(clock.GetUtcNow())) return await ColdAsync(request, HandoffOutcomes.NoReserve, ct);

        HandoffIdentity identity;
        string solutionPath;
        try
        {
            identity = HandoffIdentity.Resolve(
                request.Solution, request.Spec, request.WorkingDirectory, environment, out solutionPath);
        }
        catch (UserErrorException)
        {
            return await ColdAsync(request, HandoffOutcomes.Unresolved, ct);
        }

        bool owned;
        try
        {
            owned = HandoffEndpoint.OwnerPresent(identity.EndpointName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return await ColdAsync(request, HandoffOutcomes.Disconnected, ct);
        }

        if (!owned) return await ColdAsync(request, HandoffOutcomes.NoServer, ct);

        string outcome;
        try
        {
            HandoffReply reply = await AskAsync(identity, request with { Solution = solutionPath }, ct);
            if (reply.Declined is null) return new HookCheckResult(reply.ExitCode!.Value, reply.Output!, reply.Error!, HandoffOutcomes.Served);

            outcome = HandoffOutcomes.Declined(reply.Declined);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The hook itself is being cancelled: the cold check meets the same token and ends as a cold run would.
            outcome = HandoffOutcomes.Cancelled;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            outcome = HandoffOutcomes.Timeout;
        }
        catch (InvalidDataException)
        {
            outcome = HandoffOutcomes.Malformed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            outcome = HandoffOutcomes.Disconnected;
        }

        return await ColdAsync(request, outcome, ct);
    }

    // One connection, one request, one reply. The discovery limit covers connecting and admission together, so the
    // server is told what connecting left of it; the warm limit covers the rest. A reply to another nonce, or a
    // completed reply missing its exit code or a channel, is malformed.
    private async Task<HandoffReply> AskAsync(HandoffIdentity identity, CheckRequest request, CancellationToken ct)
    {
        var nonce = Guid.NewGuid().ToString("N");
        long started = Stopwatch.GetTimestamp();
        await using NamedPipeClientStream stream = await HandoffEndpoint.ConnectAsync(
            identity.EndpointName, HookBudget.DiscoveryLimit, ct);
        TimeSpan admissionWait = HookBudget.DiscoveryLimit - Stopwatch.GetElapsedTime(started);

        using var deadline = new CancellationTokenSource(HookBudget.DiscoveryLimit + HookBudget.WarmLimit, clock);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);

        var message = new HandoffRequest(
            identity, configuration.Digests, nonce, request, Math.Max(0, (int)admissionWait.TotalMilliseconds));
        if (!await HandoffProtocol.TryWriteFrameAsync(
                stream, message, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, limit.Token))
            throw new InvalidDataException("The hand-off request exceeds the frame limit.");

        HandoffReply reply = await HandoffProtocol.ReadFrameAsync(
            stream, HandoffJson.Default.HandoffReply, HandoffProtocol.MaxReplyBytes, limit.Token);
        bool complete = reply.Declined is not null
                        || (reply.ExitCode is not null && reply.Output is not null && reply.Error is not null);
        if (reply.Nonce != nonce || !complete) throw new InvalidDataException("The hand-off reply does not answer this request.");

        return reply;
    }

    private async Task<HookCheckResult> ColdAsync(CheckRequest request, string outcome, CancellationToken ct)
    {
        HookCheckResult result = await cold.RunAsync(request, ct);
        return result with { Handoff = outcome };
    }
}
