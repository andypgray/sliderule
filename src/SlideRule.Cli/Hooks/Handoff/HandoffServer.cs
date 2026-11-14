using System.Diagnostics;
using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SlideRule.Cli.Mcp;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Verbs;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Sessions;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     What a warm server answers a hook with: its identity and configuration, the solution it is bound to, and the
///     check that warms its workspace before any hook can be served.
/// </summary>
internal sealed record HandoffBinding(
    HandoffIdentity Identity,
    HandoffConfiguration Configuration,
    string SolutionPath,
    CheckRequest WarmUp)
{
    /// <summary>
    ///     The binding of a server bound as <paramref name="binding" /> says, with its configuration as
    ///     <paramref name="environment" /> holds it once canonicalized, which must be before anything registers MSBuild.
    /// </summary>
    /// <exception cref="UserErrorException">The settings file is malformed, or discovery finds no solution.</exception>
    public static HandoffBinding For(McpServerBinding binding, IEnvironment environment)
    {
        HandoffIdentity identity = HandoffIdentity.Resolve(
            binding.Solution, binding.Spec, binding.WorkingDirectory, environment, out string solutionPath);
        HandoffConfiguration configuration = HandoffConfiguration.Settle(environment, binding.WorkingDirectory);
        return new HandoffBinding(identity, configuration, Path.GetFullPath(solutionPath), binding.WarmUpRequest());
    }
}

/// <summary>
///     The server's half of the hand-off: it owns the endpoint for its binding and answers each hook request with a
///     warm check of the hook's own request, or a decline the hook answers cold.
/// </summary>
/// <remarks>
///     <para>
///         A request is served only when its identity and configuration match, it carries its changed files and asks
///         for no output only a cold run writes, the workspace is loaded, a binlog capture is absent, the admission
///         slot frees within the discovery limit, and a forced-read reconcile reads every changed file stably. The
///         check then runs on that very snapshot, never on one a later reconcile produces. A request that finds the
///         workspace unloaded starts the one warm-up.
///     </para>
///     <para>
///         Accepted work counts through <see cref="IdleTimeoutWatchdog" />, so the idle watchdog cannot stop the server
///         mid-answer, and runs under one cancellation linked to the hook's disconnect, the warm limit and the
///         server's stop. A connection that faults is logged and the next is accepted.
///     </para>
/// </remarks>
internal sealed class HandoffServer(
    WorkspaceSession session,
    WarmSolutionSource warm,
    WarmWorkAdmission admission,
    IEnvironment environment,
    HandoffBinding binding,
    ILogger<HandoffServer> logger,
    CancellationToken stopping)
{
    // How long a connection may take to send its request; a hook sends it at once.
    private static readonly TimeSpan RequestLimit = TimeSpan.FromSeconds(5);

    private int warmingUp;

    /// <summary>
    ///     Owns the endpoint and serves it until <paramref name="ct" /> or the server's stop fires. Returns at once
    ///     when another server owns the endpoint, and stops when the endpoint cannot be created.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct, stopping);
        EndpointOwnership? ownership;
        try
        {
            ownership = HandoffEndpoint.TryOwn(binding.Identity.EndpointName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Hand-off endpoint could not be made; hooks will check cold");
            return;
        }

        if (ownership is null)
        {
            logger.LogWarning(
                "Hand-off endpoint {Endpoint} is owned by another server; hooks will reach that one",
                binding.Identity.EndpointName);
            return;
        }

        using (ownership)
        {
            logger.LogInformation("Hand-off listening on {Endpoint} for {Solution}", ownership.PipeName, binding.SolutionPath);
            await AcceptAsync(ownership, stop.Token);
        }
    }

    private async Task AcceptAsync(EndpointOwnership ownership, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream stream;
            try
            {
                stream = HandoffEndpoint.CreateServerStream(ownership);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Hand-off endpoint could not be created; hooks will check cold");
                return;
            }

            try
            {
                await stream.WaitForConnectionAsync(stop);
            }
            catch (OperationCanceledException)
            {
                await stream.DisposeAsync();
                return;
            }
            catch (IOException ex)
            {
                // A client that connected and left before the accept completed costs one connection, not the
                // endpoint.
                await stream.DisposeAsync();
                if (stop.IsCancellationRequested) return;

                logger.LogDebug(ex, "Hand-off accept failed; listening again");
                continue;
            }

            Detach(Task.Run(() => ServeAsync(stream, stop), CancellationToken.None));
        }
    }

    // One request and its reply. A watcher reads past the request so that the hook's leaving cancels the work, and
    // the one cancellation the answer runs under also bounds writing the reply, so a hook that stops reading cannot
    // hold the connection past the warm limit. A connection that sends no request within its limit is dropped.
    private async Task ServeAsync(NamedPipeServerStream stream, CancellationToken stop)
    {
        var gone = new CancellationTokenSource();
        var watch = Task.CompletedTask;
        try
        {
            HandoffRequest request;
            long readStarted = Stopwatch.GetTimestamp();
            using (var reading = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                reading.CancelAfter(RequestLimit);
                try
                {
                    request = await HandoffProtocol.ReadFrameAsync(
                        stream, HandoffJson.Default.HandoffRequest, HandoffProtocol.MaxRequestBytes, reading.Token);
                }
                catch (InvalidDataException ex)
                {
                    (HandoffReply declined, bool told) = await TryReplyAsync(
                        stream, HandoffReply.Decline("", DeclineReasons.Malformed), reading.Token);
                    LogOutcome(declined, readStarted, told, ex);
                    return;
                }
            }

            watch = WatchForLeavingAsync(stream, gone);
            long started = Stopwatch.GetTimestamp();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(gone.Token, stop);
            HandoffReply answer = await AnswerAsync(request, linked, stop);
            (HandoffReply sent, bool delivered) = await TryReplyAsync(stream, answer, linked.Token);
            LogOutcome(sent, started, delivered);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Hand-off connection closed before a request");
        }
        finally
        {
            await stream.DisposeAsync();
            await watch;
            gone.Dispose();
        }
    }

    // What the hook was sent, timed from started, and whether it was there to read it.
    private void LogOutcome(HandoffReply sent, long started, bool delivered, Exception? cause = null)
    {
        logger.LogInformation(
            cause,
            "Hand-off {Outcome} in {ElapsedMs} ms{Undelivered}",
            sent.Declined is null ? HandoffOutcomes.Served : HandoffOutcomes.Declined(sent.Declined),
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            delivered ? "" : "; the hook had gone");
    }

    // linked is cancelled by the hook leaving and the server stopping; admission starts its warm limit.
    private async Task<HandoffReply> AnswerAsync(
        HandoffRequest request, CancellationTokenSource linked, CancellationToken stop)
    {
        string nonce = request.Nonce;
        CheckRequest check = request.Check;
        if (!binding.Identity.Equals(request.Identity)) return HandoffReply.Decline(nonce, DeclineReasons.Identity);

        IReadOnlyList<string> differing = binding.Configuration.DifferingInputs(request.Configuration);
        if (differing.Count > 0)
        {
            logger.LogInformation("Hand-off configuration differs in {Inputs}", string.Join(", ", differing));
            return HandoffReply.Decline(nonce, DeclineReasons.Configuration);
        }

        if (check.ChangedFiles is null) return HandoffReply.Decline(nonce, DeclineReasons.NoChangedFiles);

        if (check.Json || check.Binlog is not null || check.Sarif is not null || check.Annotations is not null
            || check.AnnotationsSummary is not null)
            return HandoffReply.Decline(nonce, DeclineReasons.Unsupported);

        if (check.Solution is null
            || !PathComparison.Comparer.Equals(Path.GetFullPath(check.Solution), binding.SolutionPath))
            return HandoffReply.Decline(nonce, DeclineReasons.Identity);

        if (!session.IsLoaded(binding.SolutionPath))
        {
            StartWarmUp(stop);
            return HandoffReply.Decline(nonce, DeclineReasons.NotLoaded);
        }

        // What the hook's connecting left of the discovery limit, never more than the limit itself.
        TimeSpan admissionWait = TimeSpan.FromMilliseconds(Math.Clamp(
            request.AdmissionWaitMilliseconds, 0, (int)HookBudget.DiscoveryLimit.TotalMilliseconds));
        WarmWorkAdmission.Lease? lease;
        try
        {
            lease = await admission.TryEnterAsync(admissionWait, stop);
        }
        catch (OperationCanceledException)
        {
            return HandoffReply.Decline(nonce, DeclineReasons.ShuttingDown);
        }

        if (lease is null) return HandoffReply.Decline(nonce, DeclineReasons.Busy);

        using (lease)
        {
            IdleTimeoutWatchdog.EnterCall();
            try
            {
                linked.CancelAfter(HookBudget.WarmLimit);
                if (AdmittedDecline() is { } declined) return HandoffReply.Decline(nonce, declined);

                ForcedReconcile forced = await session.GetCurrentAsync(binding.SolutionPath, check.ChangedFiles, linked.Token);
                if (forced.Outcomes.FirstOrDefault(static pair => pair.Value is not (ForcedReadOutcome.Read or ForcedReadOutcome.Reloaded))
                    is { Key: not null } refused)
                {
                    logger.LogInformation("Hand-off could not certify {Path}: {Outcome}", refused.Key, refused.Value);
                    return HandoffReply.Decline(nonce, DeclineReasons.Of(refused.Value));
                }

                ISolutionSource pinned = warm.PinnedTo(forced.Snapshot, binding.SolutionPath);
                HookCheckResult result = await new InProcessHookCheck(pinned, environment)
                    .RunAsync(McpServerBinding.UnderWarmPolicy(check), linked.Token);

                // The command handler answers a cancellation with a completed error, which is no answer to the hook.
                if (linked.IsCancellationRequested) return HandoffReply.Decline(nonce, CancelledReason(stop));

                return new HandoffReply(nonce, null, result.ExitCode, result.Output, result.Error);
            }
            catch (OperationCanceledException)
            {
                return HandoffReply.Decline(nonce, CancelledReason(stop));
            }
            catch (ObjectDisposedException)
            {
                return HandoffReply.Decline(nonce, DeclineReasons.ShuttingDown);
            }
            catch (UserErrorException ex)
            {
                logger.LogInformation(ex, "Hand-off reconcile failed");
                return HandoffReply.Decline(nonce, DeclineReasons.ReconcileFailed);
            }
            finally
            {
                IdleTimeoutWatchdog.ExitCall();
            }
        }
    }

    // The checks that need the slot held: the workspace may have been disposed or reloaded while the request waited,
    // and a capture only a cold check consults may have appeared.
    private string? AdmittedDecline()
    {
        if (!session.IsLoaded(binding.SolutionPath)) return DeclineReasons.NotLoaded;

        string cacheDirectory = CacheLocations.CacheDirectory(binding.SolutionPath, environment.CacheRootOverride());
        return File.Exists(CacheLocations.CaptureManifestPathIn(cacheDirectory)) ? DeclineReasons.Capture : null;
    }

    private static string CancelledReason(CancellationToken stop)
    {
        return stop.IsCancellationRequested ? DeclineReasons.ShuttingDown : DeclineReasons.Timeout;
    }

    // At most one at a time, holding the slot and counting through the watchdog like any other warm work, and
    // independent of the request that started it.
    private void StartWarmUp(CancellationToken stop)
    {
        if (Interlocked.CompareExchange(ref warmingUp, 1, 0) != 0) return;

        Detach(Task.Run(() => WarmUpAsync(stop), CancellationToken.None));
    }

    private async Task WarmUpAsync(CancellationToken stop)
    {
        try
        {
            using (await admission.EnterAsync(stop))
            {
                IdleTimeoutWatchdog.EnterCall();
                try
                {
                    long started = Stopwatch.GetTimestamp();
                    HookCheckResult result = await new InProcessHookCheck(warm, environment).RunAsync(binding.WarmUp, stop);
                    logger.LogInformation(
                        "Hand-off warm-up exited {ExitCode} in {ElapsedMs} ms", result.ExitCode,
                        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                }
                finally
                {
                    IdleTimeoutWatchdog.ExitCall();
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Hand-off warm-up cancelled");
        }
        finally
        {
            Volatile.Write(ref warmingUp, 0);
        }
    }

    // Reads once past the request; whatever ends the read (the hook closing, a stray byte, the stream disposed)
    // means no reply is wanted any more.
    private static async Task WatchForLeavingAsync(Stream stream, CancellationTokenSource gone)
    {
        try
        {
            var one = new byte[1];
            _ = await stream.ReadAsync(one);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The read ended because the connection did, which is what this watches for.
        }

        await gone.CancelAsync();
    }

    // Sent is the reply the hook was given: reply, or the reply-too-large decline in its place when reply's frame is
    // over the limit. Delivered is false when the hook had gone before it was written.
    private static async Task<(HandoffReply Sent, bool Delivered)> TryReplyAsync(
        Stream stream, HandoffReply reply, CancellationToken ct)
    {
        HandoffReply sent = reply;
        try
        {
            if (await HandoffProtocol.TryWriteFrameAsync(
                    stream, reply, HandoffJson.Default.HandoffReply, HandoffProtocol.MaxReplyBytes, ct))
                return (sent, true);

            sent = HandoffReply.Decline(reply.Nonce, DeclineReasons.ReplyTooLarge);
            bool delivered = await HandoffProtocol.TryWriteFrameAsync(
                stream, sent, HandoffJson.Default.HandoffReply, HandoffProtocol.MaxReplyBytes, ct);
            return (sent, delivered);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            return (sent, false);
        }
    }

    // A connection or a warm-up runs detached from the accept loop; a fault in one is logged rather than lost, and
    // never reaches the loop or the host.
    private void Detach(Task work)
    {
        _ = work.ContinueWith(
            faulted => logger.LogWarning(faulted.Exception, "Hand-off work failed"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}

/// <summary>
///     The hosted service that runs a <see cref="HandoffServer" /> for the life of the MCP server. A fault in it is
///     logged and ends the hand-off alone: the host's default stops the whole server when a background service
///     faults, and the tools must outlive the listener.
/// </summary>
internal sealed class HandoffListener(HandoffServer server, ILogger<HandoffListener> logger)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Task run = server.RunAsync(stoppingToken);
        await Task.WhenAny(run);
        if (run.IsFaulted) logger.LogWarning(run.Exception, "Hand-off listener failed; hooks will check cold");
    }
}
