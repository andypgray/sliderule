using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Loader;
using Shouldly;
using SlideRule.Internal;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     Member anchors over a C# 14 extension indexer, which the test project's own compiler cannot declare, read
///     from an assembly the carried compiler emits. An indexer is outside the member-anchor surface, under its
///     property's name and under its emitted accessor's alike.
/// </summary>
public sealed class ExtensionIndexerAnchorTests : IDisposable
{
    private readonly AssemblyLoadContext _context = new("Indexers", isCollectible: true);
    private readonly Type _ext;

    public ExtensionIndexerAnchorTests()
    {
        byte[] image = SpecAssemblyCompiler.EmitImage("""
                                                      namespace N;
                                                      public class Box {}
                                                      public static class Ext { extension(Box b) { public int this[int i] => i; } }
                                                      """, "Indexers", [CompilationFactory.CoreLibrary]);
        _ext = _context.LoadFromStream(new MemoryStream(image)).GetType("N.Ext", throwOnError: true)!;
    }

    public void Dispose()
    {
        _context.Unload();
    }

    private MethodInfo Accessor => _ext.GetMethod("get_Item", BindingFlags.Public | BindingFlags.Static)
                                   ?? throw new InvalidOperationException("The class carries no static get_Item.");

    [Fact]
    public void DeclaredMemberOf_ExtensionIndexer_DeclaresNoMemberUnderEitherName()
    {
        DeclaredMember.Of(_ext, "Item")
            .ShouldBeEmpty();
        DeclaredMember.Of(_ext, Accessor.Name)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Resolve_CallOfAnExtensionIndexersAccessor_IsPoisoned()
    {
        // The tree a static-form anchor over Ext.get_Item(box, 0) lowers to, built by hand since no spec here can
        // spell the call.
        Type box = _ext.Assembly.GetType("N.Box", throwOnError: true)!;
        MethodCallExpression call = Expression.Call(Accessor, Expression.New(box), Expression.Constant(0));
        Expression<Func<object?>> lambda = Expression.Lambda<Func<object?>>(Expression.Convert(call, typeof(object)));

        Member member = MemberExpressionResolver.Resolve(new Arch(), lambda);

        member.PoisonError.ShouldNotBeNull()
            .ShouldContain("indexer accessor (get_Item)");
    }
}
