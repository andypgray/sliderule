using SlideRule.Codebase;
using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     One grandfathered violation's identity in a baseline file. A violation of a verb that names a
///     pair (a reference, a construction, an injection, a banned member use, a catch, a throw or an
///     exposure) is keyed by <see cref="Source" /> and <see cref="Target" />, and so is a project's
///     violation over one of its declared packages or target frameworks, from the project to the item; a
///     violation about one type, member or project as a whole (shape, naming, inheritance, attribute, or a
///     custom <c>Must</c> predicate) is keyed by <see cref="Subject" /> alone. The unused slots are null, and
///     <c>Violation.BaselineIdentity()</c> builds the entry that identifies a given violation.
/// </summary>
/// <remarks>
///     A type or member slot holds a Roslyn <c>DocumentationCommentId</c> string, and a project slot
///     holds <c>project:</c> followed by the project's name, and a project item's target slot holds
///     <c>package:</c> or <c>framework:</c> followed by the package identifier or the short moniker, so an
///     entry survives a file move and any
///     amount of reformatting; renaming the type or member it names, or moving it to another namespace
///     or containing type, is what makes it a different entry. Two entries are equal when all three
///     slots match ordinally, which leaves <see cref="Because" /> and <see cref="SiteCount" /> free to
///     be re-recorded without forking the entry. An entry no violation in a run matches is stale: the
///     debt it recorded has been paid off.
/// </remarks>
internal sealed class BaselineEntry : IEquatable<BaselineEntry>
{
    private BaselineEntry(string? source, string? target, string? subject, string? because, int? siteCount)
    {
        Source = source;
        Target = target;
        Subject = subject;
        Because = because;
        SiteCount = siteCount;
    }

    /// <summary>
    ///     Gets the symbol ID of the type the violation comes from: the type that holds the reference, the
    ///     <c>new</c>, the injected constructor parameter, the <c>catch</c>, the <c>throw</c>, the exposed
    ///     signature position or the banned member access, or the identity of the project that declares a
    ///     package or targets a framework.
    /// </summary>
    public string? Source { get; }

    /// <summary>
    ///     Gets the symbol ID of what the source reached: the referenced or constructed type, the injected
    ///     parameter's type, the caught or thrown exception type, the exposed type, the banned member
    ///     itself, or the project's package or framework.
    /// </summary>
    public string? Target { get; }

    /// <summary>
    ///     Gets the symbol ID of the type, member or project the rule judged, for a violation that names
    ///     one thing rather than a pair.
    /// </summary>
    public string? Subject { get; }

    /// <summary>
    ///     Gets the reason recorded with the entry, a single non-blank line, or null when it was captured
    ///     without one.
    /// </summary>
    public string? Because { get; }

    /// <summary>
    ///     Gets how many distinct <c>file:line</c> sites the entry covered when it was recorded, at least
    ///     1, or null when no count was recorded — an entry written before counts existed, or by a write
    ///     with none to record.
    /// </summary>
    /// <remarks>
    ///     <see cref="BaselineRatchet" /> decides how a run's sites stand against it. It is sealed with the
    ///     entry, so an edited count is refused rather than silently widening the allowance.
    /// </remarks>
    public int? SiteCount { get; }

    /// <summary>
    ///     Gets whether this entry is keyed by a pair, <see cref="Source" /> and <see cref="Target" />,
    ///     rather than by a <see cref="Subject" />. Only a pair-keyed entry carries a
    ///     <see cref="SiteCount" />.
    /// </summary>
    public bool IsEdge => Source is not null;

    /// <summary>The entry's identity as one line of text: its subject, or <c>source -&gt; target</c>.</summary>
    internal string SymbolForm => Subject ?? $"{Source} -> {Target}";

    /// <summary>
    ///     Gets the project-wide entry that grandfathered this identity before a project's packages and
    ///     frameworks were keyed apart: the subject entry naming the project, for an edge from a project to one
    ///     of its items, and null for every other identity.
    /// </summary>
    // A file captured before the per-item key holds one subject entry per project. It keeps grandfathering
    // every item of that project, at any number, until accept-reductions splits it into one entry per item;
    // reading it as stale instead would turn a tool upgrade into a wall of red nobody wrote.
    internal BaselineEntry? ProjectWideForm =>
        Source is { } source && source.StartsWith(ProjectNode.SymbolIdPrefix, StringComparison.Ordinal)
            ? ForSubject(source)
            : null;

    /// <inheritdoc />
    public bool Equals(BaselineEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(Source, other.Source, StringComparison.Ordinal)
               && string.Equals(Target, other.Target, StringComparison.Ordinal)
               && string.Equals(Subject, other.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Creates an entry keyed by a pair of symbol IDs, with no reason and no site count; add them with
    ///     <see cref="WithBecause" /> and <see cref="WithSiteCount" />.
    /// </summary>
    public static BaselineEntry ForEdge(string source, string target)
    {
        return new BaselineEntry(
            Guard.NotNull(source, nameof(source)), Guard.NotNull(target, nameof(target)), null, null, null);
    }

    /// <summary>
    ///     Creates an entry keyed by one symbol ID, with no reason; add one with <see cref="WithBecause" />. A
    ///     subject entry never carries a site count.
    /// </summary>
    public static BaselineEntry ForSubject(string subject)
    {
        return new BaselineEntry(null, null, Guard.NotNull(subject, nameof(subject)), null, null);
    }

    /// <summary>
    ///     Returns <paramref name="entries" /> in the canonical baseline order. Single-sourced here because the
    ///     on-disk file, a parsed section's <see cref="RuleBaseline.Entries" /> and a legacy file's whole-file
    ///     digest are all computed over it: a drift between any two of them would change a stored digest without
    ///     changing an entry.
    /// </summary>
    internal static IReadOnlyList<BaselineEntry> InCanonicalOrder(IEnumerable<BaselineEntry> entries)
    {
        return entries
            .OrderBy(e => e.Source ?? e.Subject, StringComparer.Ordinal)
            .ThenBy(e => e.Target ?? string.Empty, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Returns one entry per identity in <paramref name="entries" />, in the order each identity first
    ///     appears. Where several share an identity, the one returned carries the smallest
    ///     <see cref="SiteCount" /> any of them records (none only when none records one) and the first
    ///     <see cref="Because" /> any of them carries. The smallest count is the strictest allowance, so a
    ///     merge that kept both sides' line for one entry never widens what the baseline grandfathers.
    /// </summary>
    internal static IReadOnlyList<BaselineEntry> Collapsed(IEnumerable<BaselineEntry> entries)
    {
        var order = new List<BaselineEntry>();
        var merged = new Dictionary<BaselineEntry, BaselineEntry>();
        foreach (BaselineEntry entry in entries)
        {
            if (!merged.TryGetValue(entry, out BaselineEntry? kept))
            {
                order.Add(entry);
                merged[entry] = entry;
                continue;
            }

            int? siteCount = kept.SiteCount is { } keptCount && entry.SiteCount is { } count
                ? Math.Min(keptCount, count)
                : kept.SiteCount ?? entry.SiteCount;
            string? because = kept.Because ?? entry.Because;
            merged[entry] = new BaselineEntry(kept.Source, kept.Target, kept.Subject, because, siteCount);
        }

        return order.Select(identity => merged[identity]).ToList();
    }

    /// <summary>
    ///     Returns a copy of this entry carrying <paramref name="because" /> as the reason it is
    ///     grandfathered — the same identity, and the same <see cref="SiteCount" />, so attributing an
    ///     entry never disturbs what it grandfathers.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="because" /> is blank or spans more than one line.</exception>
    public BaselineEntry WithBecause(string because)
    {
        bool blankOrMultiline = string.IsNullOrWhiteSpace(because) || SingleLineProse.IsMultiLine(because);
        if (blankOrMultiline)
            throw new ArgumentException("A baseline attribution must be a non-blank single line.", nameof(because));

        return new BaselineEntry(Source, Target, Subject, because, SiteCount);
    }

    /// <summary>
    ///     Returns a copy of this entry grandfathering <paramref name="siteCount" /> sites — the same
    ///     identity, and the same <see cref="Because" />, so the two copy calls compose in either order
    ///     and re-recording a count never drops the reason that justified the entry.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="siteCount" /> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">This entry is keyed by a subject and has no site count.</exception>
    // Zero is refused rather than stored: an entry that grandfathers nothing is a stale entry, which
    // status reports for acceptance instead of recording.
    public BaselineEntry WithSiteCount(int siteCount)
    {
        if (siteCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(siteCount), siteCount, "A baseline site count must be at least 1.");

        if (!IsEdge)
            throw new InvalidOperationException(
                "Only an edge entry carries a site count — a subject entry's sites are declarations.");

        return new BaselineEntry(Source, Target, Subject, Because, siteCount);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as BaselineEntry);
    }

    /// <inheritdoc />
    // Hand-rolled because System.HashCode is unavailable on netstandard2.0.
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (Source is null ? 0 : StringComparer.Ordinal.GetHashCode(Source));
            hash = hash * 31 + (Target is null ? 0 : StringComparer.Ordinal.GetHashCode(Target));
            hash = hash * 31 + (Subject is null ? 0 : StringComparer.Ordinal.GetHashCode(Subject));
            return hash;
        }
    }
}
