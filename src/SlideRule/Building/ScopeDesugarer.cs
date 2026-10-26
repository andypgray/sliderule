using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Building;

/// <summary>
///     Desugars a scope into ordinary posture-bearing rule nodes (GRAMMAR §7).
/// </summary>
/// <remarks>
///     A quarantine yields two: a containment rule (<c>{id}/containment</c>) carrying the
///     boundary/baseline and the <c>sel.Except(F).MustOnlyBeReferencedBy(sel ∪ F)</c> predicate, then a
///     tripwire rule (<c>{id}/tripwire</c>) carrying the scoped selection for diff-touch matching but no
///     closed-vocabulary constraint. A caution yields the tripwire alone — it declares no containment
///     law, so there is nothing for a second child to hold. Clause distribution follows §7: Because and
///     dragons go to every child; BoundaryOnlyVia and Baseline go to containment, which only a
///     quarantine has.
/// </remarks>
internal static class ScopeDesugarer
{
    internal static IReadOnlyList<ArchRule> Desugar(ScopeRegistration scope)
    {
        // Validation refuses a dangling scope before desugaring, so the posture is always there.
        return scope.Posture == Posture.Caution
            ? [Tripwire(scope)]
            : [Containment(scope), Tripwire(scope)];
    }

    internal static string ContainmentId(string scopeId)
    {
        return scopeId + "/containment";
    }

    internal static string TripwireId(string scopeId)
    {
        return scopeId + "/tripwire";
    }

    // The boundary a containment rule was desugared from, in the spec's order: its constraint's operands after
    // the scoped selection, as BuildContainment lays them out. A model read back from a file carries the
    // constraint whole, so this holds for a parsed model as for a built one.
    internal static IEnumerable<Selection> BoundaryOf(ArchRule containment)
    {
        return containment.Constraint!.Operands.Skip(1);
    }

    private static ArchRule Containment(ScopeRegistration scope)
    {
        Selection quarantined = scope.Scoped!;
        IReadOnlyList<Selection> boundary = scope.Boundary;
        string id = ContainmentId(scope.Id);
        // .Baseline(path) omitted ⇒ the conventional default derived from the containment rule ID
        // (GRAMMAR §4.4/§7), so the containment child's baseline is never null post-build.
        string baseline = scope.Baselines.FirstOrDefault() ?? BaselineConventions.DefaultPath(id);

        Constraint containment = BuildContainment(quarantined, boundary);
        // The Fix names the first sanctioned operand as the sentence would refer to it (GRAMMAR §5.5):
        // "use `IBillingFacade`" for a type, "use types named `CodeFormatHelper`" for a no-load anchor.
        string? fix = boundary.Count > 0
            ? "use " + boundary[0].ReferenceProse()
            : null;

        return new ArchRule(
            id,
            Posture.Quarantine,
            Because(scope),
            fix,
            SentenceRenderer.Sentence(containment),
            containment,
            null,
            new ScopeData(
                ScopeRole.Containment, boundary, baseline, Dragons(scope), DragonsDoc(scope), scope.Id, quarantined));
    }

    // The one tripwire builder both postures take, so the diff-aware touch check is the same rule node
    // whichever verb declared the scope.
    private static ArchRule Tripwire(ScopeRegistration scope)
    {
        return new ArchRule(
            TripwireId(scope.Id),
            scope.Posture!.Value,
            Because(scope),
            null,
            string.Empty,
            null,
            null,
            new ScopeData(
                ScopeRole.Tripwire, Array.Empty<Selection>(), null, Dragons(scope), DragonsDoc(scope),
                scope.Id, scope.Scoped!));
    }

    private static string Because(ScopeRegistration scope)
    {
        return scope.Becauses.FirstOrDefault() ?? string.Empty;
    }

    private static string? Dragons(ScopeRegistration scope)
    {
        return scope.Dragons.FirstOrDefault();
    }

    private static string? DragonsDoc(ScopeRegistration scope)
    {
        return scope.DragonsDocs.FirstOrDefault();
    }

    private static Constraint BuildContainment(Selection quarantined, IReadOnlyList<Selection> boundary)
    {
        if (boundary.Count == 0)
            // Hermetic quarantine: nothing outside the scope may reference it (GRAMMAR §7).
            return quarantined.MustOnlyBeReferencedBy(quarantined);

        // sel.Except(F) . MustOnlyBeReferencedBy(sel, F...) — the formula holds whether the
        // sanctioned surface lies inside or outside the quarantined selection (GRAMMAR §7), and the
        // union is the node arch.AnyOf mints, so the boundary adds no model node of its own.
        var union = new UnionSelection(quarantined.Owner, boundary);
        Selection subject = quarantined.Except(union);
        return subject.MustOnlyBeReferencedBy(quarantined, boundary.ToArray());
    }
}
