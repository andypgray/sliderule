using SlideRule.Hosting;
using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     Assembles the <see cref="BaselineIndex" /> for a model's ratcheted rules from whatever can hand
///     back the document a baseline path names.
/// </summary>
internal static class BaselineIndexBuilder
{
    /// <summary>
    ///     The index for the ratcheted rules among <paramref name="rules" />: asks
    ///     <paramref name="documentFor" /> once for each distinct baseline path, as the rules carry it, and
    ///     captures the section the returned document holds under the rule's ID. A null answer, which leaves
    ///     the rule uncaptured, is asked once like any other.
    /// </summary>
    /// <remarks>
    ///     Paths are compared ordinally — they are what the model carries, not what a file system
    ///     resolved — so two rules share a read only when they name the file the same way.
    /// </remarks>
    internal static BaselineIndex ForRules(IEnumerable<ArchRule> rules, Func<string, BaselineDocument?> documentFor)
    {
        IEnumerable<ArchRule> all = Guard.NotNull(rules, nameof(rules));
        Func<string, BaselineDocument?> reader = Guard.NotNull(documentFor, nameof(documentFor));

        var sections = new Dictionary<string, RuleBaseline>(StringComparer.Ordinal);
        var cache = new Dictionary<string, BaselineDocument?>(StringComparer.Ordinal);

        foreach (ArchRule rule in all.Where(candidate => candidate.IsRatcheted))
        {
            string baselinePath = rule.BaselinePath!;
            if (!cache.TryGetValue(baselinePath, out BaselineDocument? document))
            {
                document = reader(baselinePath);
                cache[baselinePath] = document;
            }

            if (document is null) continue;
            if (document.SectionFor(rule.Id) is not { } section) continue;

            sections[rule.Id] = section;
        }

        return new BaselineIndex(sections);
    }
}
