using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     The parts of a C# document no other project can bind to: the spans <see cref="SourceShape.SurfaceHash" />
///     leaves out of its digest.
/// </summary>
/// <remarks>
///     <para>
///         Member bodies, non-constant initializers and top-level statements are opaque: a dependent binds to a
///         member's signature, never to its body, and a non-constant initializer is evaluated where it is
///         declared. Everything else stays in the surface, including the parts that feel like implementation
///         but are not: a constant's value and an enum member's value (constant folding and overload
///         resolution in a dependent read them), a parameter's default value, every attribute, a constructor
///         initializer, and a primary constructor's parameter list.
///     </para>
///     <para>
///         Private members stay in the surface. A dependent cannot bind to one, except where its own code fails
///         to compile and the compiler offers the inaccessible member as its best candidate, and a struct's
///         private fields decide what a dependent may do with the struct. Measured over a month of this
///         repository's commits, leaving them out would have spared few edits a re-walk.
///     </para>
///     <para>
///         The spans come back in document order and never overlap: the walk records an opaque node and does
///         not descend into it, which is what lets <see cref="SourceShape" /> sweep them beside its token walk
///         with one cursor.
///     </para>
/// </remarks>
internal static class DeclarationSurface
{
    internal static IReadOnlyList<(TextSpan Span, int RawKind)> OpaqueSpans(SyntaxNode root)
    {
        var spans = new List<(TextSpan, int)>();
        Collect(root);
        return spans;

        void Collect(SyntaxNode node)
        {
            foreach (SyntaxNode child in node.ChildNodes())
                if (IsOpaque(child))
                    spans.Add((child.Span, child.RawKind));
                else
                    Collect(child);
        }
    }

    private static bool IsOpaque(SyntaxNode node)
    {
        return node switch
        {
            BlockSyntax or ArrowExpressionClauseSyntax => node.Parent is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax or BasePropertyDeclarationSyntax,
            EqualsValueClauseSyntax => IsNonConstantInitializer(node),
            GlobalStatementSyntax => true,
            _ => false
        };
    }

    // An initializer evaluated where it is declared: a non-const field's, a field-like event's or an
    // auto-property's.
    private static bool IsNonConstantInitializer(SyntaxNode clause)
    {
        return clause.Parent switch
        {
            PropertyDeclarationSyntax => true,
            VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax field } =>
                !field.Modifiers.Any(SyntaxKind.ConstKeyword),
            VariableDeclaratorSyntax { Parent.Parent: EventFieldDeclarationSyntax } => true,
            _ => false
        };
    }
}
