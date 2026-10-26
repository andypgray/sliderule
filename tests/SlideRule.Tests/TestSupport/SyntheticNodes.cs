using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Shallow codebase nodes for the rows whose subject is a shape — an identity, a count, a rendered line —
///     rather than any real code: the caller steers the full name, the symbol ID, the declaring project and
///     the declaration sites, and every other scalar fact is an inert placeholder nothing under test reads. The
///     stand-in violations those same rows count are held here for the same reason.
/// </summary>
/// <remarks>
///     One spelling of the thirteen-argument <see cref="TypeNode" /> constructor, so a slot added to it is
///     one edit here rather than one per test file.
/// </remarks>
internal static class SyntheticNodes
{
    /// <summary>
    ///     A type whose symbol ID is <c>T:</c> + its full name, declared at <paramref name="sites" /> — none
    ///     by default.
    /// </summary>
    internal static TypeNode Type(string fullName, params SourceLocation[] sites)
    {
        return Type(fullName, "T:" + fullName, "TestProject", sites);
    }

    /// <summary>
    ///     A type carrying the symbol ID and declaring project given — for the rows about identity and
    ///     attribution, where the ID is not derivable from the name or the project is what varies.
    /// </summary>
    internal static TypeNode Type(string fullName, string symbolId, string project = "TestProject")
    {
        return Type(fullName, symbolId, project, Array.Empty<SourceLocation>());
    }

    /// <summary>A use of the member <paramref name="name" /> on the type <paramref name="containingFullName" />.</summary>
    internal static MemberReference Member(string containingFullName, string name, string symbolId, MemberKind kind)
    {
        return new MemberReference(Type(containingFullName), name, symbolId, kind);
    }

    /// <summary>
    ///     A public member of <paramref name="declaringFullName" />, declared at no site and carrying no modifier or
    ///     type.
    /// </summary>
    internal static MemberNode MemberSubject(string declaringFullName, string name, string symbolId, MemberKind kind)
    {
        return DeclaredMember(Type(declaringFullName), name, symbolId, kind, Accessibility.Public);
    }

    /// <summary>
    ///     A member of <paramref name="declaringType" /> itself, so an edge built on that node reaches it; its symbol
    ///     ID is the kind's documentation-comment prefix, the type's full name and the member's name.
    /// </summary>
    internal static MemberNode MemberOf(
        TypeNode declaringType, string name, MemberKind kind, Accessibility accessibility = Accessibility.Public)
    {
        string prefix = kind switch
        {
            MemberKind.Method => "M:",
            MemberKind.Property => "P:",
            MemberKind.Field => "F:",
            _ => "E:"
        };

        return DeclaredMember(declaringType, name, $"{prefix}{declaringType.FullName}.{name}", kind, accessibility);
    }

    /// <summary>
    ///     <paramref name="count" /> distinct <c>file:line</c> sites in one file, lines 1 through
    ///     <paramref name="count" />.
    /// </summary>
    internal static IReadOnlyList<SourceLocation> Sites(string file, int count)
    {
        return Enumerable.Range(1, count)
            .Select(line => new SourceLocation(file, line))
            .ToList();
    }

    /// <summary>
    ///     A codebase carrying <paramref name="types" /> and the reference <paramref name="edges" /> among
    ///     them, and nothing else — for the rows whose subject is an edge the extractor cannot mint from
    ///     real source, every one of its edge factories being fed from syntax.
    /// </summary>
    /// <remarks>
    ///     <c>ProjectFacts.Solution</c> is the project-stratum equivalent, and the split is by which
    ///     stratum a row is about rather than by anything the constructor cares for.
    /// </remarks>
    internal static CodebaseModel Referencing(IReadOnlyList<TypeNode> types, params ReferenceEdge[] edges)
    {
        return new CodebaseModel(types, edges, [], [], [], [], [], [], [], [], [], []);
    }

    /// <summary>
    ///     <paramref name="count" /> stand-in violations carrying no site at all, which is what makes
    ///     them the right default: the site total prints only when it exceeds the pair count, so a rule
    ///     built from these prints no site total.
    /// </summary>
    internal static IReadOnlyList<Violation> RuleErrors(int count)
    {
        return Enumerable.Range(0, count)
            .Select(_ => Violation.RuleError("x"))
            .ToList();
    }

    private static MemberNode DeclaredMember(
        TypeNode declaringType, string name, string symbolId, MemberKind kind, Accessibility accessibility)
    {
        return new MemberNode(
            declaringType, symbolId, name, kind,
            accessibility, false, false, false, false, null, null,
            Array.Empty<SourceLocation>(), Array.Empty<string>());
    }

    private static TypeNode Type(string fullName, string symbolId, string project, IReadOnlyList<SourceLocation> sites)
    {
        return new TypeNode(
            fullName, symbolId, fullName, string.Empty, TypeKind.Class, Accessibility.Public,
            false, false, false, false, false, project, false) { DeclarationSites = sites };
    }
}
