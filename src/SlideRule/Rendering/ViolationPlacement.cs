using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>
///     Where a violation lands in the human report and in the build warnings: an edge violation at every site
///     it carries, a shape violation at the first, so the two surfaces point a reader at the same lines.
/// </summary>
/// <remarks>
///     An edge violation lands at every site because a pair that has grown is red at all of them, and the reader
///     cannot be told which one is new. A shape violation lands at one because it is about the subject rather
///     than about any one of its declarations.
/// </remarks>
internal static class ViolationPlacement
{
    // None for the kinds that are about the rule rather than about the code, which carry no site at all.
    internal static IReadOnlyList<SourceLocation> PlacedSites(this Violation violation)
    {
        if (violation.Kind.IsEdge()) return violation.Sites;
        if (violation.Kind.IsShape())
            return violation.Sites.Count > 0 ? [violation.Sites[0]] : Array.Empty<SourceLocation>();
        return Array.Empty<SourceLocation>();
    }
}
