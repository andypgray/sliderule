namespace SlideRule.Codebase;

/// <summary>
///     One type using a member of another: <see cref="Source" />'s own source invokes, reads, writes or
///     subscribes to <see cref="Member" />. Read them from <see cref="CodebaseModel.MemberEdges" />.
/// </summary>
/// <remarks>
///     <para>
///         A method call, a method group handed over as a delegate, a property, field or event access
///         (<c>?.</c>, compound assignment and <c>+=</c> / <c>-=</c> included), and a bare name brought into
///         scope by <c>using static</c> all count.
///     </para>
///     <para>
///         Three uses are deliberately absent, so a rule reading these edges will not see them: a
///         <c>nameof</c> operand, which reads nothing at run time; an indexer; and a member the compiler
///         takes from a pattern rather than from a name the source spells — <c>await</c>'s
///         <c>GetAwaiter</c>, <c>using</c>'s <c>Dispose</c>, <c>foreach</c>'s enumerator, query syntax.
///     </para>
/// </remarks>
internal sealed class MemberEdge
{
    internal MemberEdge(TypeNode source, MemberReference member, EdgeEvidence evidence)
    {
        Source = source;
        Member = member;
        Evidence = evidence;
    }

    /// <summary>Gets the type whose own source contains the use.</summary>
    public TypeNode Source { get; }

    /// <summary>Gets the member that was used.</summary>
    public MemberReference Member { get; }

    public IReadOnlyList<SourceLocation> Sites => Evidence.Sites;

    // A use's instance reaches a copy of the member's declaring type, the target it is attributed to.
    internal EdgeEvidence Evidence { get; }
}
