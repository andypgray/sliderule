namespace SlideRule.Extraction.Fragments;

/// <summary>
///     A directed member-use edge inside one fragment (GRAMMAR §4.5): the source type by FQN, the used
///     member's declaring type, simple name, <see cref="MemberKind" /> and stable
///     <see cref="MemberSymbolId">DocumentationCommentId</see>, and the sites of the uses. A use of a member
///     the source type declares itself is not recorded. The pure-data counterpart of
///     <see cref="SlideRule.Codebase.MemberEdge" />.
/// </summary>
/// <remarks>
///     The declaring type, name and kind are read off the member symbol, never parsed back out of the DocID,
///     and travel beside <see cref="MemberSymbolId" /> so the merge builds the model node without re-binding.
/// </remarks>
internal sealed record FragmentMemberEdge(
    string SourceFullName,
    string TargetContainingTypeFullName,
    string MemberName,
    string MemberSymbolId,
    MemberKind MemberKind,
    IReadOnlyList<FragmentSite> Sites);
