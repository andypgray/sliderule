using SlideRule.Cli.Pipeline;
using SlideRule.Hosting;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     A session-scoped cache of loaded spec models, keyed by the spec DLL's file stamp, so the warm MCP server
///     does not repeat the whole spec load on every tool call for something that changes only when the spec
///     project is rebuilt.
/// </summary>
/// <remarks>
///     A hit requires <see cref="FileFreshness.CanTrust" />, so a rebuilt spec misses and reloads, and a spec
///     rebuilt inside the racy window keeps missing and reloading until its stamp promotes, the safe direction to
///     be wrong in. Only successes are stored: a load that throws (a deleted DLL, a spec-validation failure, a
///     dependency that will not resolve) stores nothing, so every later call re-raises the identical error the
///     cold CLI raises rather than replaying a stale success or a cached exception. A loaded model roots its
///     collectible <see cref="SpecLoadContext" />, so caching the model keeps one such context alive instead of a
///     new uncollectable one per tool call.
/// </remarks>
internal sealed class SpecModelCache
{
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    private readonly Lock gate = new();

    /// <summary>
    ///     The model behind <paramref name="specDllPath" />: the cached one when the DLL's stamp still
    ///     proves it current, otherwise a full <see cref="ModelPipeline.LoadModel" /> whose result is
    ///     recorded for the next call.
    /// </summary>
    internal ArchitectureModel Load(string specDllPath)
    {
        string key = Path.GetFullPath(specDllPath);
        FileFreshness stamp = FileFreshness.Capture(key);

        lock (gate)
        {
            if (entries.TryGetValue(key, out Entry? cached) && cached.Stamp.CanTrust(stamp))
                return cached.Model;
        }

        // Loaded outside the lock: this runs reflection and the spec author's own Define(), so holding the
        // lock across it would serialize every tool call behind one spec's load. Two callers racing the same
        // miss both load and the last one recorded wins — a wasted load, never a wrong model.
        ArchitectureModel model = ModelPipeline.LoadModel(specDllPath);

        // The stamp recorded is the one taken BEFORE the load, so a DLL rewritten mid-load records the old
        // file's fingerprint and the next call misses on it. Recording a post-load stamp would pair the new
        // bytes' fingerprint with the old bytes' model.
        lock (gate)
        {
            entries[key] = new Entry(stamp, model);
        }

        return model;
    }

    private sealed record Entry(FileFreshness Stamp, ArchitectureModel Model);
}
