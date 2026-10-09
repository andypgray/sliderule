namespace SlideRule.Codebase;

/// <summary>
///     One type catching an exception type: a <c>catch</c> clause in <see cref="Source" />'s own source
///     catches <see cref="Caught" />. Read them from <see cref="CodebaseModel.CatchEdges" />. A bare
///     <c>catch</c> is recorded as catching <c>System.Exception</c>, which is what the language means by it.
/// </summary>
/// <remarks>
///     <para>
///         A <c>catch</c> that immediately rethrows still counts — these are facts about the source, not
///         judgements about it — and a type catching itself gives nothing. Exception types are recorded at
///         their definition, and a <c>catch</c> on a type parameter, or on a type the compilation cannot
///         resolve, records nothing at all. Both ends are the very type instances
///         <see cref="CodebaseModel.Types" /> lists, so compare them by reference rather than by name.
///         Because no source names the type, a bare <c>catch</c> adds no ordinary reference, where a typed
///         <c>catch (IOException)</c> does and the catch edge sits beside it.
///     </para>
///     <para>
///         Three site lists narrow one another: <see cref="Sites" />, then
///         <see cref="UnfilteredSites" />, then <see cref="SwallowingSites" />. Both narrowings read what
///         the source spells and never analyse it — <c>when (true)</c> counts as a filter, and only the
///         block's last statement decides whether a clause throws.
///     </para>
/// </remarks>
// The two subsets are recorded rather than left to a reader to derive, because sites dedupe by
// (file, line): a filtered and an unfiltered catch of one type on one physical line collapse to a single
// site, and a filter a #if applies under one target framework but not another reaches the merge as one
// site reported both ways (GRAMMAR §4.8). Subtracting one set from another loses both cases.
internal sealed class CatchEdge : IEdgeEnds
{
    internal CatchEdge(
        TypeNode source,
        TypeNode caught,
        EdgeEvidence evidence,
        EdgeEvidence unfilteredEvidence,
        EdgeEvidence swallowingEvidence)
    {
        Source = source;
        Caught = caught;
        Evidence = evidence;
        UnfilteredEvidence = unfilteredEvidence;
        SwallowingEvidence = swallowingEvidence;
    }

    /// <summary>
    ///     Gets the catching type. A <c>catch</c> written inside a lambda or a local function counts for the
    ///     type that encloses it, and one in top-level statements for <c>Program</c>.
    /// </summary>
    public TypeNode Source { get; }

    /// <summary>Gets the caught exception type — <c>System.Exception</c> for a bare <c>catch</c>.</summary>
    public TypeNode Caught { get; }

    TypeNode IEdgeEnds.Target => Caught;

    /// <summary>
    ///     Gets each distinct <c>catch</c> position, ordered by file then line. Two <c>catch</c> clauses for the
    ///     same type on one line count as one site.
    /// </summary>
    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    /// <summary>
    ///     Gets the sites of <see cref="Sites" /> whose <c>catch</c> clause carries no <c>when</c> filter, in
    ///     the same order. Empty when every site is filtered. Whether a filter is present is read from what the
    ///     source spells, never from what the filter tests, so <c>when (true)</c> counts as filtered. A bare
    ///     <c>catch</c> is unfiltered; a bare <c>catch when (...)</c> is not.
    /// </summary>
    public IReadOnlyList<SourceLocation> UnfilteredSites => UnfilteredEvidence.Sites;

    /// <summary>
    ///     Gets the sites of <see cref="UnfilteredSites" /> whose <c>catch</c> block does not end in a
    ///     <c>throw</c> statement — the ones that hold a failure and carry on. Empty when every unfiltered site
    ///     rethrows or throws something else. Only the block's last statement is read, and never whether every
    ///     path reaches it: <c>catch { if (x) return; throw; }</c> is absent from this list, and
    ///     <c>catch { if (x) throw; Cleanup(); }</c> is in it.
    /// </summary>
    public IReadOnlyList<SourceLocation> SwallowingSites => SwallowingEvidence.Sites;

    internal EdgeEvidence Evidence { get; }

    // UnfilteredSites with the same instances as Evidence, in the same order, each carrying its share of this list
    // instead — the narrowing has to carry its own, because which of a copy's catch clauses spell a filter is a
    // fact about that copy. Its instances are null exactly when Evidence's are, and index-aligned with them on the
    // same instance keys, so a rule that owns Evidence.Instances[i] cites UnfilteredEvidence.Instances[i]. An
    // instance none of whose sites survive the narrowing carries an empty list, which the filter-aware ban reads
    // as green rather than as a violation with no lines.
    internal EdgeEvidence UnfilteredEvidence { get; }

    // SwallowingSites with its shares, aligned to Evidence's instances exactly as UnfilteredEvidence's are: null
    // exactly when they are, and an empty list for an instance none of whose sites swallow, which reads green.
    internal EdgeEvidence SwallowingEvidence { get; }
}
