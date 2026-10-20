using Microsoft.CodeAnalysis;

namespace SlideRule.Roslyn.Solutions;

/// <summary>
///     The empty per-project target-framework map, held once: a single-target solution, the common case, records
///     no discriminators, so the map is empty on most runs and on every carrier at once.
/// </summary>
internal static class TargetFrameworkMaps
{
    /// <summary>The map for a solution with no discriminated framework, and every carrier's fallback.</summary>
    internal static readonly IReadOnlyDictionary<ProjectId, string> None = new Dictionary<ProjectId, string>();
}
