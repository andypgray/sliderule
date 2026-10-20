using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>arch_context</c> pipeline: the cards <c>render</c> would splice
///     (<see cref="ContextFileComposer.Placements" />) whose directory contains the query path, layer cards before
///     scope cards, or the pinned pointer line when none does.
/// </summary>
/// <remarks>
///     <para>
///         <b>Never a gate.</b> No finding about the codebase changes the exit code: context is a lookup, so
///         an incomplete model is announced rather than enforced. The answer opens with a caveat block naming
///         the load failures, because a card whose project failed to load places nowhere and the pinned
///         pointer line would otherwise read as a clean "not dragon territory".
///     </para>
///     <para>The card body carries no provenance line — that is a <c>render</c> file-splice concern.</para>
/// </remarks>
internal sealed class ContextRunner(
    TextWriter output,
    ISolutionSource source,
    IEnvironment environment)
    : WorkspaceRunner(source)
{
    public async Task<int> RunAsync(ContextRequest request, CancellationToken ct)
    {
        // Ahead of the load, so the incomplete-model caveat below never prints above a refusal it does not
        // qualify. The glob and rule-ID shape refusals fire on a no match; this verb has no no-match
        // state — "no card covers this path" is a legitimate answer — so a bracketed value would come back as a
        // well-formed proven negative on the exact call an agent makes before editing unfamiliar territory.
        RefuseAStringifiedArrayPath(request.Path);

        // Cache-free, and the one verb whose policy is not about what it reads absence as: arch_context has
        // no CLI verb at all (MCP-only), and the warm tool path deliberately leaves cache.json untouched so
        // the file and the warm WorkspaceSession keep independent lifetimes and never race on it. The
        // environment seam still goes down, for discovery alone: under noCache the cache root is never read
        // from it.
        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, environment, request.Solution, request.Spec, request.WorkingDirectory,
            noCache: true, ct);

        // Ahead of every exit below, because each of them can be the false all-clear: the body is context's
        // only channel, so the load failures ride it or reach nobody.
        IncompleteModelNotices.Caveat(output, source.Diagnostics, IncompleteModelGate.ContextCaveat);

        // Beside that caveat rather than instead of it — both can be true of one run, and a broken model
        // outranks a small one, so the gate's block goes first. Context still exits 0: a narrowed universe is
        // a smaller true answer, and this says which paths the pointer line below cannot speak for. There is
        // no --json to suppress it for; the stamp writes itself only when the run was narrowed.
        NarrowingNotices.Stamp(output, source, NarrowedUniverseNotice.ContextStamp);

        // Nothing scoped to place — no scope of either posture and no anchored layer — ⇒ skip the
        // extraction cost and point at the root block.
        if (!ContextFileComposer.HasAnythingToPlace(source.Model))
        {
            await output.WriteLineAsync(PointerLine(request.Path));
            return ExitCodes.Clean;
        }

        CodebaseModel codebase = await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct);

        string queryFullPath = ResolveQueryPath(request.Path, source.SolutionDirectory);

        // Split once for the whole walk: the query path is already resolved, and every card below asks the
        // same question of it against a different directory.
        string[] querySegments = PathFormat.Segments(queryFullPath);

        // The composer's own placements, filtered to the ones covering the query path: layer local-rules
        // card(s) ahead of scope card(s), the same cards in the same order render splices. An
        // unplaceable card carries a null directory and so covers nothing.
        List<string> cards = ContextFileComposer.Placements(source.Model, codebase, source.SolutionDirectory)
            .Where(card => card.Destination.DirectoryPath is not null && PathFormat.Contains(card.Destination.DirectoryPath, querySegments))
            .Select(card => card.Body)
            .ToList();

        if (cards.Count == 0)
        {
            await output.WriteLineAsync(PointerLine(request.Path));
            return ExitCodes.Clean;
        }

        WriteCards(cards);
        return ExitCodes.Clean;
    }

    private void WriteCards(IReadOnlyList<string> cards)
    {
        var first = true;
        foreach (string card in cards)
        {
            if (!first) output.WriteLine();
            first = false;

            LineBlocks.Write(output, card);
        }
    }

    // Canonicalize the query path to the same standard as the model's declaration-site paths (both
    // symlink-resolved), so a user/agent path spelled through a symlinked root still matches a scope. Shared
    // with explain's path form, which matches the same declaration sites.
    internal static string ResolveQueryPath(string path, string solutionDirectory)
    {
        string full = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(solutionDirectory, path));
        return PathCanonicalizer.Resolve(full);
    }

    // A reader, never a repair: a directory named [weird] is refused with its own text quoted back
    // rather than silently rewritten into something the caller did not ask for. Paths this verb has never
    // seen stay legitimate — it answers what rules WILL apply somewhere — and a JSON array is not one of
    // those, so nothing here narrows what a path may be.
    private static void RefuseAStringifiedArrayPath(string path)
    {
        var lead = $"Cannot find architecture scope for '{path}'";
        if (Refusals.SingleValueRefusal(path, lead, "pass one path") is { } refusal)
            throw new UserErrorException(refusal);
    }

    private static string PointerLine(string path)
    {
        return $"No architecture scope covers '{path}'. Architecture context for this solution lives in the root " +
               "AGENTS.md managed block; expand any rule with 'sliderule explain <rule-id>'.";
    }
}
