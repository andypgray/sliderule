using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Tests.Caching;

/// <summary>The shape of a source text parsed as the document <c>Widget.cs</c>.</summary>
internal static class WidgetShapes
{
    public static SourceShape ShapeOf(string text)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: "Widget.cs");
        return SourceShape.Of(tree);
    }
}
