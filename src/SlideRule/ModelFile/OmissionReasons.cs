using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.ModelFile;

/// <summary>The whole closed set of sentences a rule or a layer can be left out of a model file for.</summary>
/// <remarks>
///     Each is addressed to whoever reads the file and wonders where a rule went, so it says what was met
///     and what follows from it rather than naming a node class. The set is closed because the writer is the
///     only thing that decides: a node it can write produces none of these, and a node it cannot write
///     produces exactly one.
/// </remarks>
internal static class OmissionReasons
{
    internal const string Where = "A Where predicate is code, and this file carries data only.";

    internal const string Must = "A Must predicate is code, and this file carries data only.";

    private static string ClosedGeneric(TypeAnchor anchor)
    {
        return anchor.BacktickedName()
               + " is a closed generic construction where the rule matches on a "
               + "definition, so `check` reports this rule as an error.";
    }

    /// <summary>An anchor on a type with no source-level name: a pointer, a by-ref, a partial construction.</summary>
    private static string Unrepresentable(TypeAnchor anchor)
    {
        return anchor.BacktickedName()
               + " has no source-level name to match on, so `check` reports this rule as an error.";
    }

    /// <summary>
    ///     The layer's own reason is quoted, because the rule is blameless and the fault is one level down.
    /// </summary>
    internal static string UnwritableLayer(string name, string layerReason)
    {
        return "Layer " + ProseFormat.Backtick(name) + " is not in this file: " + layerReason;
    }

    /// <summary>
    ///     The reason an anchor's refusal carries, whichever accessor the position asked. Either refusal already
    ///     makes the rule an error at check time, so leaving it out of the file loses nothing being enforced.
    /// </summary>
    internal static string ForAnchor(TypeAnchor anchor, AnchorRefusal refusal)
    {
        return refusal == AnchorRefusal.ClosedGeneric ? ClosedGeneric(anchor) : Unrepresentable(anchor);
    }
}
