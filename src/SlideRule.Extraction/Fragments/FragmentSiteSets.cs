using SlideRule.Codebase;

namespace SlideRule.Extraction.Fragments;

/// <summary>
///     The operations both ends of the extraction pipeline, <see cref="FragmentExtractor" /> and
///     <see cref="FragmentMerger" />, run over site collections, so the two order, materialize and project
///     sites by one rule.
/// </summary>
/// <remarks>
///     The edge and registration tables of both ends are materialized through the sorts here, each end naming
///     the comparer its own key element is ordered by, which keeps a serialized fragment and a merged model
///     byte-stable however a dictionary happened to lay its entries out. A new edge axis calls these rather
///     than spelling its own set or sort, which could order that family's sites differently.
/// </remarks>
internal static class FragmentSiteSets
{
    /// <summary>
    ///     The site set <paramref name="key" /> unions into, adding an empty one on first mention. The set takes
    ///     the default comparer, the pinned (file, line) order of <see cref="FragmentSite.CompareTo" />, so a
    ///     keyed table that makes its sets here cannot spell another.
    /// </summary>
    internal static SortedSet<FragmentSite> SitesFor<TKey>(this Dictionary<TKey, SortedSet<FragmentSite>> map, TKey key)
        where TKey : notnull
    {
        if (map.TryGetValue(key, out SortedSet<FragmentSite>? sites)) return sites;

        sites = [];
        map[key] = sites;
        return sites;
    }

    /// <summary>
    ///     The ordinal name set <paramref name="key" /> unions into, adding an empty one on first mention. The
    ///     set is ordinal, so a keyed name table that makes its sets here cannot spell another comparer.
    /// </summary>
    internal static SortedSet<string> OrdinalNamesFor<TKey>(this Dictionary<TKey, SortedSet<string>> map, TKey key)
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
    ///     <paramref name="make" />.
    /// </summary>
    internal static List<TOut> OrderedPairs<TFirst, TSecond, TValue, TOut>(
        this Dictionary<(TFirst, TSecond), TValue> map,
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

    internal static List<TOut> OrderedPairs<TValue, TOut>(
        this Dictionary<(string, string), TValue> map, Func<string, string, TValue, TOut> make)
    {
        return map.OrderedPairs(StringComparer.Ordinal, StringComparer.Ordinal, make);
    }

    /// <summary>
    ///     The registration table, keyed by a triple rather than an endpoint pair: lifetime first, then ordinal
    ///     by service, then ordinal by implementation with the no-implementation form sorting as the empty string.
    /// </summary>
    internal static List<TOut> OrderedRegistrations<TOut>(
        this Dictionary<(Lifetime, string, string?), SortedSet<FragmentSite>> map,
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
    ///         It folds rather than sorts because both ends of the pipeline meet duplicates: the evaluation
    ///         reads one project's declarations, and the merge reads one project's fragments back together, where
    ///         a package declared once arrives once per framework.
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

    private static bool Outranks(FragmentPackageReference declaration, FragmentPackageReference held)
    {
        if (declaration.IsPrivate != held.IsPrivate) return !declaration.IsPrivate;

        return declaration.Site.CompareTo(held.Site) < 0;
    }

    /// <summary>A site collection as the model's <see cref="SourceLocation" /> list, in its own order.</summary>
    internal static IReadOnlyList<SourceLocation> ToLocations(this IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];

        var locations = new SourceLocation[sites.Count];
        var index = 0;
        foreach (FragmentSite site in sites) locations[index++] = site.ToLocation();

        return locations;
    }

    /// <summary>
    ///     The share of a materialized site list that <paramref name="subset" /> holds: the very
    ///     <see cref="SourceLocation" /> objects <paramref name="allLocations" /> holds, in its order, for the
    ///     sites of <paramref name="all" /> that <paramref name="subset" /> also contains.
    /// </summary>
    /// <remarks>
    ///     <paramref name="allLocations" /> must be <see cref="ToLocations" /> of <paramref name="all" />, and
    ///     <paramref name="subset" /> a subset of it; both sets carry the default (file, line) comparer, so one
    ///     index walks them together. A subset holding every site gets <paramref name="allLocations" /> itself,
    ///     which is what lets a reader compare the two by reference: that identity is the contract, not an
    ///     optimization.
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
    ///     One site as the model's <see cref="SourceLocation" />, the single crossing between the two shapes; the
    ///     span crosses only where the site carries one.
    /// </summary>
    internal static SourceLocation ToLocation(this FragmentSite site)
    {
        SourceSpan? span = site.Length > 0 ? new SourceSpan(site.Start, site.Length) : null;

        return new SourceLocation(site.File, site.Line, span);
    }

    internal static SourceLocation? ToLocation(this FragmentSite? site)
    {
        return site is { } known ? known.ToLocation() : null;
    }

    /// <summary>
    ///     The distinct files a site collection touches, in first-occurrence order (the GRAMMAR §5.6
    ///     <c>FilePaths</c> contract).
    /// </summary>
    /// <remarks>
    ///     Every collection reaching here is already in (file, line) order, so first-occurrence order is ordinal
    ///     file order and <c>Distinct</c> keeps it. The one-site case, nearly every type and member, skips the set.
    /// </remarks>
    internal static IReadOnlyList<string> FilePaths(this IReadOnlyCollection<FragmentSite> sites)
    {
        if (sites.Count == 0) return [];
        if (sites.Count == 1) return [sites.First().File];

        return sites
            .Select(s => s.File)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
