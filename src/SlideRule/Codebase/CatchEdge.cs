namespace SlideRule.Codebase;

/// <summary>
///     One type catching an exception type: a <c>catch</c> clause in <see cref="TypeEdge.Source" />'s own source
///     catches <see cref="Caught" />. Read them from <see cref="CodebaseModel.CatchEdges" />. A bare
///     <c>catch</c> is recorded as catching <c>System.Exception</c>, which is what the language means by it.
/// </summary>
/// <remarks>
///     <para>
///         A <c>catch</c> that immediately rethrows still counts — these are facts about the source, not
///         judgements about it — and a <c>catch</c> on a type parameter, or on a type the compilation cannot
///         resolve, records nothing at all.
///     </para>
///     <para>
///         A <c>catch</c> written inside a lambda or a local function counts for the type that encloses it,
///         and one in top-level statements for <c>Program</c>.
///     </para>
///     <para>
///         Three site lists narrow one another: <see cref="TypeEdge.Sites" />, then
///         <see cref="UnfilteredSites" />, then <see cref="SwallowingSites" />. Both narrowings read what
///         the source spells and never analyse it — <c>when (true)</c> counts as a filter, and only the
///         block's last statement decides whether a clause throws.
///     </para>
/// </remarks>
// The two subsets are recorded rather than left to a reader to derive, because sites dedupe by
// (file, line): a filtered and an unfiltered catch of one type on one physical line collapse to a single
// site, and a filter a #if applies under one target framework but not another reaches the merge as one
// site reported both ways (GRAMMAR §4.8). Subtracting one set from another loses both cases.
internal sealed class CatchEdge : TypeEdge
{
    internal CatchEdge(
        TypeNode source,
        TypeNode caught,
        EdgeEvidence evidence,
        EdgeEvidence unfilteredEvidence,
        EdgeEvidence swallowingEvidence)
        : base(source, evidence)
    {
        Caught = caught;
        UnfilteredEvidence = unfilteredEvidence;
        SwallowingEvidence = swallowingEvidence;
    }

    /// <summary>Gets the caught exception type — <c>System.Exception</c> for a bare <c>catch</c>.</summary>
    public TypeNode Caught { get; }

    public override TypeNode Target => Caught;

    /// <summary>
    ///     Gets the sites of <see cref="TypeEdge.Sites" /> whose <c>catch</c> clause spells no <c>when</c> filter,
    ///     in the same order. A bare <c>catch</c> is unfiltered; a bare <c>catch when (...)</c> is not.
    /// </summary>
    public IReadOnlyList<SourceLocation> UnfilteredSites => UnfilteredEvidence.Sites;

    /// <summary>
    ///     Gets the sites of <see cref="UnfilteredSites" /> whose <c>catch</c> block does not end in a
    ///     <c>throw</c> statement, the ones that hold a failure and carry on: <c>catch { if (x) return; throw; }</c>
    ///     is absent from this list, and <c>catch { if (x) throw; Cleanup(); }</c> is in it.
    /// </summary>
    public IReadOnlyList<SourceLocation> SwallowingSites => SwallowingEvidence.Sites;

    // UnfilteredSites with the same instances as Evidence, in the same order, each carrying its share of this list
    // instead — the narrowing has to carry its own, because which of a copy's catch clauses spell a filter is a
    // fact about that copy. Its instances are null exactly when Evidence's are, and index-aligned with them on the
    // same instance keys, so a rule that owns Evidence.Instances[i] cites UnfilteredEvidence.Instances[i]. An
    // instance none of whose sites survive the narrowing carries an empty list.
    internal EdgeEvidence UnfilteredEvidence { get; }

    // SwallowingSites with its shares, aligned to Evidence's instances exactly as UnfilteredEvidence's are.
    internal EdgeEvidence SwallowingEvidence { get; }
}
