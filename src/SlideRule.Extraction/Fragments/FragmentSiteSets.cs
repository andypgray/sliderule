using SlideRule.Codebase;

namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The operations both ends of the extraction pipeline run over site collections —
///     <see cref="FragmentExtractor" />'s per-compilation accumulator tables and
///     <see cref="FragmentMerger" />'s cross-fragment ones: get the set an axis unions into, materialize a
///     whole table in canonical order, and project a materialized site list the two ways the model reads it.
/// </summary>
/// <remarks>
///     Each operation carries a determinism contract that would otherwise be restated once per edge axis, in
///     both files. <see cref="For{TKey}" /> holds the single <c>new SortedSet&lt;FragmentSite&gt;()</c>, so the
///     default comparer — which <em>is</em> the pinned (file, line) site ordering, see
///     <see cref="FragmentSite.CompareTo" /> — cannot be spelled differently on one axis and silently reorder
///     that family's sites. <see cref="OrderedPairs{TFirst,TSecond,TValue,TOut}" /> and
///     <see cref="OrderedRegistrations{TOut}" /> hold the sorts that make a serialized fragment and a merged
///     model byte-stable however a dictionary happened to lay its entries out — one sort for both ends of the
///     pipeline, with each end naming the comparer its own key element is ordered by.
///     <see cref="FilePaths" /> holds the GRAMMAR §5.6 first-occurrence-order contract. Adding an edge axis
///     costs a table and a call, not a copy of any of those rules.
/// </remarks>
internal static class FragmentSiteSets
{
    /// <summary>
    ///     The site set <paramref name="key" /> unions into, adding an empty one on first mention. One hash
    ///     lookup where the key is already there, a second only where it is new.
    /// </summary>
    internal static SortedSet<FragmentSite> For<TKey>(Dictionary<TKey, SortedSet<FragmentSite>> map, TKey key)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out SortedSet<FragmentSite>? sites)) return sites;

        sites = [];
        map[key] = sites;
        return sites;
    }

    /// <summary>
    ///     The ordinal name set <paramref name="key" /> unions into, adding an empty one on first mention — the
    ///     name-axis twin of <see cref="For{TKey}" />, so the comparer is spelled once here rather than at every
    ///     table that keys names by project or by declarer set.
    /// </summary>
    internal static SortedSet<string> OrdinalNamesFor<TKey>(Dictionary<TKey, SortedSet<string>> map, TKey key)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out SortedSet<string>? names)) return names;

        names = new SortedSet<string>(StringComparer.Ordinal);
        map[key] = names;
        return names;
    }

    /// <summary>
    ///     Materializes a table keyed by an endpoint pair as a list ordered by the key's first element then its
    ///     second, each through the comparer its element type is ordered by, projecting each entry through
    ///     <paramref name="make" />. The key's tuple element names are erased at runtime, so every axis —
    ///     <c>(src, target)</c>, <c>(src, caught)</c>, <c>(src, member SymbolId)</c> — is the one shape here,
    ///     whether an endpoint is a name (extraction, one compilation's view) or a node key (the merge, where
    ///     one name can denote two).
    /// </summary>
    internal static List<TOut> OrderedPairs<TFirst, TSecond, TValue, TOut>(
        Dictionary<(TFirst, TSecond), TValue> map,
        IComparer<TFirst> firstComparer,
        IComparer<TSecond> secondComparer,
        Func<TFirst, TSecond, TValue, TOut> make)
    {
        return map
            .OrderBy(kv => kv.Key.Item1, firstComparer)
            .ThenBy(kv => kv.Key.Item2, secondComparer)
            .Select(kv => make(kv.Key.Item1, kv.Key.Item2, kv.Value))
            .ToList();
    }

    /// <summary>
    ///     The same over a name-keyed pair, the shape extraction's own tables take — ordinal on both elements,
    ///     so an axis there names no comparer and the sort is still spelled exactly once.
    /// </summary>
    internal static List<TOut> OrderedPairs<TValue, TOut>(
        Dictionary<(string, string), TValue> map, Func<string, string, TValue, TOut> make)
    {
        return OrderedPairs(map, StringComparer.Ordinal, StringComparer.Ordinal, make);
    }

    /// <summary>
    ///     The registration axis's own ordering, for the one table whose key is a triple rather than an
    ///     endpoint pair: lifetime first, then ordinal by service, then ordinal by implementation with the
    ///     no-implementation form sorting as the empty string.
    /// </summary>
    /// <remarks>
    ///     It sits beside <see cref="OrderedPairs{TFirst,TSecond,TValue,TOut}" /> for the same reason that one
    ///     exists: both ends of the pipeline materialize this table, and a rule spelled twice is a rule that can
    ///     come to be spelled two ways.
    /// </remarks>
    internal static List<TOut> OrderedRegistrations<TOut>(
        Dictionary<(Lifetime, string, string?), SortedSet<FragmentSite>> map,
        Func<Lifetime, string, string?, SortedSet<FragmentSite>, TOut> make)
    {
        return map
            .OrderBy(kv => kv.Key.Item1)
            .ThenBy(kv => kv.Key.Item2, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key.Item3 ?? "", StringComparer.Ordinal)
            .Select(kv => make(kv.Key.Item1, kv.Key.Item2, kv.Key.Item3, kv.Value))
            .ToList();
    }

    /// <summary>
    ///     A project's declared packages materialized the way the model reads them: one entry per name,
    ///     ordinal by name. A name declared more than once is private only where every declaration of it is,
    ///     so a declaration that is not private decides and brings its site with it; between declarations
    ///     that agree, the ordinally-first site stays. Neither rule reads arrival order, so the list is
    ///     stable whatever order the declarations arrived in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It sits here for the same reason <see cref="OrderedRegistrations{TOut}" /> does, and folds rather
    ///         than merely sorts because both ends of the pipeline meet the duplicates too: the evaluation reads
    ///         one project's declarations, the merge reads one project's fragments back together, and a package
    ///         declared once per framework is declared once. The declarations themselves come back, so the end
    ///         that carries them into the model projects the survivors itself.
    ///     </para>
    ///     <para>
    ///         Deliberately stricter than restore, which keeps the first of two duplicate declarations and
    ///         matches names ignoring case. The two part only on duplicates restore itself warns about (NU1504)
    ///         or on names differing in case, and always toward a violation.
    ///     </para>
    /// </remarks>
    internal static List<FragmentPackageReference> OrderedPackages(IEnumerable<FragmentPackageReference> declarations)
    {
        var byName = new Dictionary<string, FragmentPackageReference>(StringComparer.Ordinal);
        foreach (FragmentPackageReference declaration in declarations)
            if (!byName.TryGetValue(declaration.Name, out FragmentPackageReference existing)
                || Outranks(declaration, existing))
                byName[declaration.Name] = declaration;

        return byName
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value)
            .ToList();
    }

    // A declaration that is not private outranks one that is; between two alike, the ordinally-first site does.
    private static bool Outranks(FragmentPackageReference declaration, FragmentPackageReference held)
    {
        if (declaration.IsPrivate != held.IsPrivate) return !declaration.IsPrivate;

        return declaration.Site.CompareTo(held.Site) < 0;
    }

    /// <summary>
    ///     A site collection as the model's <see cref="SourceLocation" /> list — the projection every axis
    ///     applies on the way out of extraction, order preserved exactly as the collection holds it.
    /// </summary>
    internal static IReadOnlyList<SourceLocation> Locations(IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];

        var locations = new SourceLocation[sites.Count];
        var index = 0;
        foreach (FragmentSite site in sites) locations[index++] = Location(site);

        return locations;
    }

    /// <summary>
    ///     The share of a materialized site list that <paramref name="subset" /> holds: the very
    ///     <see cref="SourceLocation" /> objects <paramref name="allLocations" /> holds, in its order, for the
    ///     sites of <paramref name="all" /> that <paramref name="subset" /> also contains.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <paramref name="allLocations" /> must be <see cref="Locations" /> of <paramref name="all" />, and
    ///         <paramref name="subset" /> a subset of it — both hold by construction, since a site recorded
    ///         under one instance (or under one of a catch edge's narrowings) was unioned into the edge in the
    ///         same call. Both sets carry the default (file, line) comparer, so one index walks them together.
    ///     </para>
    ///     <para>
    ///         A subset holding every site gets <paramref name="allLocations" /> itself, which is what lets a
    ///         reader compare the two by reference and lets an edge only one copy makes print the list the
    ///         model already built. That reference identity is the contract, not an optimization.
    ///     </para>
    /// </remarks>
    internal static IReadOnlyList<SourceLocation> Subsequence(
        SortedSet<FragmentSite> all, IReadOnlyList<SourceLocation> allLocations, SortedSet<FragmentSite> subset)
    {
        if (subset.Count == 0) return [];
        if (subset.Count == all.Count) return allLocations;

        var kept = new List<SourceLocation>(subset.Count);
        var index = 0;
        foreach (FragmentSite site in all)
        {
            if (subset.Contains(site)) kept.Add(allLocations[index]);
            index++;
        }

        return kept;
    }

    /// <summary>
    ///     One site as the model's <see cref="SourceLocation" /> — the single crossing between the two
    ///     shapes, which every projection here goes through. A site that carries a span hands it over; one
    ///     that carries none gives a location that is a line and nothing more.
    /// </summary>
    internal static SourceLocation Location(FragmentSite site)
    {
        SourceSpan? span = site.Length > 0 ? new SourceSpan(site.Start, site.Length) : null;

        return new SourceLocation(site.File, site.Line, span);
    }

    /// <summary>
    ///     One site as the model's <see cref="SourceLocation" />, or <see langword="null" /> for a site that
    ///     was never recorded — the singular twin of <see cref="Locations" />, for the facts that have one
    ///     declaration rather than a set of mentions.
    /// </summary>
    internal static SourceLocation? Location(FragmentSite? site)
    {
        return site is { } known ? Location(known) : null;
    }

    /// <summary>
    ///     The distinct files a site collection touches, in first-occurrence order (the GRAMMAR §5.6
    ///     <c>FilePaths</c> contract).
    /// </summary>
    /// <remarks>
    ///     Every collection reaching here is already (file, line) ordinal-ordered — it came out of a
    ///     <see cref="SortedSet{T}" />, or out of a fragment that built one — so first-occurrence order
    ///     <em>is</em> ordinal file order, and <c>Distinct</c> is what preserves it. The one-site case is the
    ///     overwhelming majority (a type or member declared in a single file) and skips the set entirely.
    /// </remarks>
    internal static IReadOnlyList<string> FilePaths(IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];
        if (sites.Count == 1) return [sites.First().File];

        return sites
            .Select(s => s.File)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
