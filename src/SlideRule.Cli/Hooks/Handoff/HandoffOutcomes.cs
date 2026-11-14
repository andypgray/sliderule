using SlideRule.Roslyn.Sessions;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     How a hand-off ended, in the words the hook records in its state and the server writes to its log: served,
///     or the reason the check went cold.
/// </summary>
internal static class HandoffOutcomes
{
    /// <summary>The server answered with a completed check.</summary>
    internal const string Served = "served";

    /// <summary><see cref="Roslyn.Hosting.SlideRuleEnvVars.DisableWarmHook" /> is set.</summary>
    internal const string Disabled = "disabled";

    /// <summary>The check lists changed files since a git ref rather than receiving them, so no read can be certified.</summary>
    internal const string NoChangedFiles = "no-changed-files";

    /// <summary>The firing's remaining time would not cover a warm attempt and a cold check after it.</summary>
    internal const string NoReserve = "no-reserve";

    /// <summary>The solution or spec did not resolve, which the cold check reports in its own words.</summary>
    internal const string Unresolved = "unresolved";

    /// <summary>No server owns the endpoint for this identity.</summary>
    internal const string NoServer = "no-server";

    /// <summary>The server did not accept, or did not answer, inside its limit.</summary>
    internal const string Timeout = "timeout";

    /// <summary>The connection failed or closed before a reply.</summary>
    internal const string Disconnected = "disconnected";

    /// <summary>The reply was not a well-formed, complete reply to this request.</summary>
    internal const string Malformed = "malformed";

    /// <summary>The hook was cancelled during the attempt.</summary>
    internal const string Cancelled = "cancelled";

    /// <summary>The outcome of a decline for <paramref name="reason" />.</summary>
    public static string Declined(string reason)
    {
        return "declined " + reason;
    }
}

/// <summary>The reasons a server declines a request, carried in <see cref="HandoffReply.Declined" />.</summary>
internal static class DeclineReasons
{
    /// <summary>The request is for another solution, spec, protocol or build.</summary>
    internal const string Identity = "identity";

    /// <summary>The hook's configuration differs from the server's.</summary>
    internal const string Configuration = "configuration";

    /// <summary>The request carries no changed-file list.</summary>
    internal const string NoChangedFiles = "no-changed-files";

    /// <summary>The request asks for output only a cold run writes: a binlog, SARIF, annotations or JSON.</summary>
    internal const string Unsupported = "unsupported";

    /// <summary>The server holds no loaded workspace yet; it starts loading one.</summary>
    internal const string NotLoaded = "not-loaded";

    /// <summary>Other warm work held the admission slot past the discovery limit.</summary>
    internal const string Busy = "busy";

    /// <summary>A binlog capture exists for the solution, which only a cold check consults.</summary>
    internal const string Capture = "capture";

    /// <summary>The reply would exceed the frame limit.</summary>
    internal const string ReplyTooLarge = "reply-too-large";

    /// <summary>The check did not finish inside the warm limit, or the hook went away.</summary>
    internal const string Timeout = "timeout";

    /// <summary>The server is stopping.</summary>
    internal const string ShuttingDown = "shutting-down";

    /// <summary>Reconciling the workspace failed.</summary>
    internal const string ReconcileFailed = "reconcile-failed";

    /// <summary>The request frame was not a well-formed request.</summary>
    internal const string Malformed = "malformed";

    /// <summary>A changed file's read failed, so the snapshot would hold older text.</summary>
    internal const string Unreadable = "unreadable";

    /// <summary>A changed file's stat moved during the reconcile.</summary>
    internal const string Unstable = "unstable";

    /// <summary>A changed file is neither a document nor a structural file of the loaded solution.</summary>
    internal const string Untracked = "untracked";

    /// <summary>The decline for a changed file the forced read could not certify.</summary>
    internal static string Of(ForcedReadOutcome outcome)
    {
        return outcome switch
        {
            ForcedReadOutcome.Unreadable => Unreadable,
            ForcedReadOutcome.Unstable => Unstable,
            ForcedReadOutcome.Untracked => Untracked,
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Not an uncertified read.")
        };
    }
}
