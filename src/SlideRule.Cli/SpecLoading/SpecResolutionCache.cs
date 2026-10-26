namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     What one spec resolution produced for the run that asked: the <see cref="Resolution" /> itself and the
///     solution's declared <see cref="DeclaredMembers" /> it was computed against (null when membership could not
///     be read), which extraction needs next and resolution had to read anyway.
/// </summary>
internal readonly record struct SessionSpecResolution(
    SpecResolution Resolution,
    IReadOnlySet<string>? DeclaredMembers);

/// <summary>
///     A session-scoped cache of spec resolutions, keyed by the normalized <c>--spec</c> argument and stamped with
///     the workspace generation each was computed under, so the warm MCP server does not repeat the whole
///     resolution on every tool call for something that cannot change while the workspace stands.
/// </summary>
/// <remarks>
///     <para>
///         The generation is the only sound grain. A <see cref="Roslyn.WorkspaceSession" /> mints a fresh
///         snapshot instance per changed document, so keying on snapshot identity would miss on every call after
///         a <c>.cs</c> edit. Everything resolution reads is structural: the project set, their <c>.csproj</c>
///         paths, their evaluated output paths, and the solution's declared membership. None of it can change
///         without a full reload, which bumps the generation; and the solution file itself is one of the
///         structural fingerprints the session's reconcile sweep stats, so a project added to or removed from it
///         arrives here as a miss.
///     </para>
///     <para>
///         Only the candidate half is cached: a hit replays through <see cref="SpecResolver.Replay" />, so a spec
///         DLL that has since been rebuilt, moved or deleted answers in the cold run's words rather than with a
///         path recorded before it went, at the cost of a per-call <c>File.Exists</c>. A prebuilt-DLL
///         <c>--spec</c> never gets here; <c>CodebaseSource.ResolveSpec</c> answers it first and says why.
///     </para>
///     <para>
///         Only successes are stored: a resolution that throws (no spec project, an ambiguous one, nothing
///         built) stores nothing, so every later call re-raises the identical error the cold CLI raises rather
///         than replaying a stale success. Neither the full resolve nor the replay's disk search runs under the
///         gate, which would otherwise serialize every tool call behind one solution's walk; two callers racing
///         the same miss both resolve and the last one recorded wins, which costs a walk and never a wrong answer.
///     </para>
/// </remarks>
internal sealed class SpecResolutionCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    private readonly Lock gate = new();

    /// <summary>
    ///     How many resolutions this cache sent to a full walk rather than replaying, one per <c>--spec</c> argument
    ///     per workspace generation in the steady state; never printed.
    /// </summary>
    internal int FullResolveCount { get; private set; }

    /// <summary>
    ///     The resolution for <paramref name="normalizedSpecArgument" /> under <paramref name="generation" />: the
    ///     recorded candidate half replayed against disk when one was computed under this same generation,
    ///     otherwise <paramref name="resolveFully" />, whose result is recorded for the next call.
    /// </summary>
    /// <remarks>
    ///     <paramref name="normalizedSpecArgument" /> is the <c>--spec</c> value made absolute, or <c>""</c> for the
    ///     convention default: the key the persisted cache records its resolutions under, so the two agree on what
    ///     "the same spec" means.
    /// </remarks>
    internal SessionSpecResolution Resolve(
        string normalizedSpecArgument, long generation, Func<SessionSpecResolution> resolveFully)
    {
        Entry? cached;
        lock (gate)
        {
            entries.TryGetValue(normalizedSpecArgument, out cached);
        }

        if (cached is not null && cached.Generation == generation)
        {
            SpecResolution recorded = cached.Resolved.Resolution;
            SpecResolution replayed = SpecResolver.Replay(
                recorded.SpecProjectName, normalizedSpecArgument, recorded.ExcludeProjectNames,
                recorded.OutputFilePaths, recorded.IntermediateAssemblyPath);
            return cached.Resolved with { Resolution = replayed };
        }

        SessionSpecResolution resolved = resolveFully();

        lock (gate)
        {
            entries[normalizedSpecArgument] = new Entry(generation, resolved);
            FullResolveCount++;
        }

        return resolved;
    }

    private sealed record Entry(long Generation, SessionSpecResolution Resolved);
}
