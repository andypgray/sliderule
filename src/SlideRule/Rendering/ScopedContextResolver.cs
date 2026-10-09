using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     Works out which directory each scope's card belongs in: the deepest directory holding every file
///     inside the solution directory that declares one of the scoped types, so the card covers the scope and
///     as little else as it can. One card per scope, rendered from a quarantine's containment rule or a
///     caution's tripwire, so a quarantine never places two cards on one directory.
/// </summary>
/// <remarks>
///     Only types the solution declares are considered, and a file outside the solution directory never moves a
///     card. A scope that matches none of those types, or only types declared outside the solution directory,
///     comes back with a null directory and the reason, for the caller to report or ignore. A scope over a
///     project counts only that project's copy of a type other projects also compile.
/// </remarks>
internal static class ScopedContextResolver
{
    /// <summary>
    ///     Resolves a placement for every card-bearing scope in <paramref name="model" />, in the order the spec
    ///     declares the scopes.
    /// </summary>
    /// <remarks>
    ///     The twin of <see cref="LayerContextResolver.Resolve" />, and it takes the evaluator and the boundary
    ///     for the same reason: a caller resolving both emission keys for one codebase hands one of each to both,
    ///     rather than each materializing the solution-declared type list, and re-splitting the solution
    ///     directory, for itself.
    /// </remarks>
    internal static IReadOnlyList<ScopePlacement> Resolve(
        ArchitectureModel model, SelectionEvaluator evaluator, DirectoryPlacement.Boundary boundary)
    {
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Scope is not { } scope || !IsCardBearing(rule, scope)) continue;

            CardDestination destination = DirectoryPlacement.Place(
                evaluator, scope.Scoped, boundary, "scope", scope.ScopeId);

            placements.Add(new ScopePlacement(scope.ScopeId, rule, destination));
        }

        return placements;
    }

    // The one child of a scope its card is rendered from. A quarantine has two children carrying the same
    // scoped selection, and rendering both would place two cards on one directory, so the containment rule
    // is the card-bearer and its tripwire is passed over; a caution's tripwire is the only child there is.
    private static bool IsCardBearing(ArchRule rule, ScopeData scope)
    {
        return scope.Role == ScopeRole.Containment || rule.Posture == Posture.Caution;
    }
}
