using SlideRule.Checking;
using SlideRule.Hosting;
using SlideRule.Model;

namespace SlideRule.Rendering;

/// <summary>
///     Works out which directory each scope's card belongs in (<see cref="DirectoryPlacement" />), one card per
///     scope.
/// </summary>
internal static class ScopedContextResolver
{
    /// <summary>
    ///     Resolves a placement for every card-bearing scope in <paramref name="model" />, in the order the spec
    ///     declares the scopes.
    /// </summary>
    internal static IReadOnlyList<ScopePlacement> Resolve(
        ArchitectureModel model, SelectionEvaluator evaluator, DirectoryPlacement.Boundary boundary)
    {
        var placements = new List<ScopePlacement>();

        foreach (ArchRule rule in model.Rules)
        {
            if (rule.Scope is not { } scope || !IsCardBearing(rule, scope)) continue;

            CardDestination destination = DirectoryPlacement.Place(
                evaluator, scope.Scoped, boundary, "scope", scope.ScopeId);

            placements.Add(new ScopePlacement(rule, destination));
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
