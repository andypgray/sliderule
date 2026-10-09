using System.Collections.Concurrent;

namespace SlideRule.Baselines;

/// <summary>
///     A parsed baseline file: every rule section keyed by rule ID — including sections for rules not
///     in the current model (e.g. a rule removed from the spec but still recorded on a shared file).
///     Unknown sections are preserved so a mutation command (<c>baseline</c>) recomposes them untouched.
/// </summary>
/// <remarks>
///     There are two ways one comes to exist and nothing validates it after the fact:
///     <see cref="BaselineDocumentReader" /> builds one from a file's text, and only once every entry in
///     it has verified against its seal; the <c>baseline</c> command composes one from the sections it
///     means to write. A document assembled any other way is a file this tool never wrote.
/// </remarks>
internal sealed class BaselineDocument
{
    // Concurrent because one document is shared: the build tier's path-keyed read hands every compilation
    // the same instance for as long as the file at that path is unchanged. A RuleBaseline is read-only once built, so
    // sharing one is safe, and a race only builds a duplicate that GetOrAdd discards.
    private readonly ConcurrentDictionary<string, RuleBaseline> _baselines = new(StringComparer.Ordinal);

    /// <summary>Creates a document over <paramref name="sections" />, keyed by rule ID.</summary>
    internal BaselineDocument(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> sections)
    {
        Sections = sections;
    }

    /// <summary>Gets the file's rule sections, keyed by rule ID.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> Sections { get; }

    /// <summary>
    ///     The section under <paramref name="ruleId" /> as a <see cref="RuleBaseline" />, built on the first
    ///     request and shared by every later one, or null when the file has no such section.
    /// </summary>
    internal RuleBaseline? SectionFor(string ruleId)
    {
        if (_baselines.TryGetValue(ruleId, out RuleBaseline? built)) return built;
        if (!Sections.TryGetValue(ruleId, out IReadOnlyList<BaselineEntry>? entries)) return null;

        return _baselines.GetOrAdd(ruleId, new RuleBaseline(entries));
    }
}
