namespace SlideRule.Roslyn.Sessions;

/// <summary>
///     How a path listed to a forced-read
///     <see cref="WorkspaceSession.GetCurrentAsync(string, IReadOnlyCollection{string}, CancellationToken)" /> was read
///     in that call. A path is stable when its stat at the end of the call equals its stat at the start, before
///     anything in the call read it.
/// </summary>
internal enum ForcedReadOutcome
{
    /// <summary>A document read by content in this call, and stable.</summary>
    Read,

    /// <summary>The call loaded the solution afresh, and the path is stable.</summary>
    Reloaded,

    /// <summary>A document whose read failed, so the snapshot holds the text an earlier call read.</summary>
    Unreadable,

    /// <summary>The path's stat moved during the call, so the snapshot may hold neither version.</summary>
    Unstable,

    /// <summary>
    ///     The call did not reload, and the path is neither a document nor a structural file of the loaded solution,
    ///     so nothing in the snapshot was read from it.
    /// </summary>
    Untracked
}

/// <summary>
///     What a forced-read reconcile produced: the snapshot, and how each listed path was read, keyed by its full
///     path under the per-OS path rule.
/// </summary>
internal sealed record ForcedReconcile(
    WorkspaceSnapshot Snapshot,
    IReadOnlyDictionary<string, ForcedReadOutcome> Outcomes);
