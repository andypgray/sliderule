using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Validation;

/// <summary>
///     What a spec's declared layer names decide between them (GRAMMAR §8 item 10): a name declared twice.
/// </summary>
/// <remarks>
///     One verdict per repeat, reported at the repeat, unlike <see cref="SpecIdCollisions" />'s one per key at
///     the first occurrence: a rule ID names a thing the model must find, so the first claim on it wins, while a
///     layer name is a label, so each re-use is its own slip to fix.
///     <para>
///         A blank name is held out of the set. Two unnamed layers are two instances of one mistake, which
///         <see cref="SpecSlot.LayerName" /> reports under its own code, and "Duplicate layer name ''."
///         would name neither of them.
///     </para>
/// </remarks>
internal static class SpecLayerNames
{
    /// <summary>
    ///     The collisions a spec's layer names carry, or an empty list when every non-blank name is
    ///     distinct. <c>Tags</c> is the repeat's index then the first occurrence's, both into
    ///     <paramref name="names" />.
    /// </summary>
    /// <param name="names">Every declared layer name, in authoring order.</param>
    internal static IReadOnlyList<CollisionVerdict> Decide(IReadOnlyList<string> names)
    {
        var firstByName = new Dictionary<string, int>(StringComparer.Ordinal);
        var verdicts = new List<CollisionVerdict>();

        for (var index = 0; index < names.Count; index++)
        {
            string name = names[index];
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (firstByName.TryGetValue(name, out int first))
                verdicts.Add(new CollisionVerdict(Code.DuplicateLayerName, name, $"Duplicate layer name '{name}'.",
                    [index, first]));
            else
                firstByName.Add(name, index);
        }

        return verdicts;
    }
}
