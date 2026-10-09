using SlideRule.Hosting;
using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     Assembles the <see cref="BaselineIndex" /> for a model's ratcheted rules from whatever can hand
///     back the document a baseline path names.
/// </summary>
/// <remarks>
///     Where the documents come from is the caller's business — the tool reads them off disk, a reader
///     inside the compiler reads them out of the files it was given — so this holds only the part both
///     have in common: which rules have a baseline, which path each one names, and which section of the
///     document that path yields belongs to which rule.
/// </remarks>
internal static class BaselineIndexBuilder
{
    /// <summary>
    ///     The index for the ratcheted rules among <paramref name="rules" />: asks
    ///     <paramref name="documentFor" /> for each distinct baseline path once, and captures the section
    ///     the returned document holds under the rule's ID.
    /// </summary>
    /// <param name="rules">The model's rules; the ones that grandfather nothing are skipped.</param>
    /// <param name="documentFor">
    ///     The document a baseline path names, as the rule carries the path, or null when there is none
    ///     to read. A null answer is cached like any other, so a missing file is asked about once.
    /// </param>
    /// <remarks>
    ///     A null document or a missing section leaves the rule uncaptured, which is a different answer
    ///     from a section with no entries: uncaptured grandfathers nothing because nothing has been
    ///     recorded yet, where an empty section grandfathers nothing because there was nothing to record.
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
