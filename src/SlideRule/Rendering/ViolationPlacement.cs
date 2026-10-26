using SlideRule.Checking;
using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>
///     Where a violation lands in the human report and in the build warnings: an edge violation at every site
///     it carries, a shape violation at the first, so the two surfaces point a reader at the same lines.
/// </summary>
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
