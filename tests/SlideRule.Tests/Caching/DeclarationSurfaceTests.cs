using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Roslyn.Caching;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     Pins what a document's declaration surface is blind to and what it is not. Every edit inside a member
///     body or a non-constant initializer leaves the surface alone, while the shape still moves; everything a
///     dependent can bind to moves it — a signature, a constant's value, an enum member's value, a parameter's
///     default, an attribute, a constructor initializer, a body appearing where there was none, and any
///     directive, wherever it sits.
/// </summary>
public sealed class DeclarationSurfaceTests
{
    // One of each kind of member the surface has to cut around, on lines of their own.
    private const string Widget = """
                                  using System;
                                  namespace Shop;
                                  public enum Size { Small = 1, Large = 2 }
                                  public sealed class Widget : Part
                                  {
                                      public const int Max = 10;
                                      private readonly int _size = 3;
                                      public event EventHandler Changed = delegate { };
                                      public Widget() : base(1)
                                      {
                                          _size = 4;
                                      }
                                      public int Count { get; } = 5;
                                      public int Double => _size * 2;
                                      public int Total
                                      {
                                          get { return _size; }
                                      }
                                      public int Grow(int by = 1)
                                      {
                                          return _size + by;
                                      }
                                      public string Label() => "widget";
                                      public static Widget operator +(Widget a, Widget b) { return a; }
                                  }
                                  """;

    [Fact]
    public void Of_StatementEditedInAMethodBody_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("return _size + by;", "return by + _size + 0;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_StatementAddedToAMethodBody_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("return _size + by;", "_ = 1;\n        return _size + by;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_LocalFunctionAddedToAMethodBody_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("return _size + by;", "int Twice(int x) => x * 2;\n        return Twice(_size) + by;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ArrowMethodBodyEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("=> \"widget\"", "=> \"gadget\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_PropertyExpressionBodyEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("=> _size * 2;", "=> _size + _size;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_AccessorBodyEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("get { return _size; }", "get { return _size + 0; }", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ConstructorBodyEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("_size = 4;", "_size = 5;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_FieldInitializerEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("_size = 3;", "_size = 7;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_AutoPropertyInitializerEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("{ get; } = 5;", "{ get; } = 6;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_EventInitializerEdited_KeepsTheSurface()
    {
        ShouldKeepTheSurface(Widget, Widget.Replace("= delegate { };", "= (_, _) => { };", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_TopLevelStatementEdited_KeepsTheSurface()
    {
        const string program = "using System;\nConsole.WriteLine(\"one\");\npublic sealed class Tool { }\n";

        ShouldKeepTheSurface(program, program.Replace("\"one\"", "\"two\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_MethodRenamed_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("Grow", "Expand", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ParameterTypeChanged_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("Grow(int by", "Grow(long by", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_InternalMethodAdded_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("    public string Label()", "    internal void Reset() { }\n    public string Label()", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ConstantValueEdited_ChangesTheSurface()
    {
        // A dependent folds the value into its own code, and a different value can bind a different overload.
        ShouldChangeTheSurface(Widget, Widget.Replace("Max = 10;", "Max = 300;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_EnumMemberValueEdited_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("Large = 2", "Large = 3", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ParameterDefaultEdited_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("int by = 1", "int by = 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_AttributeAdded_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("    public int Grow(", "    [Obsolete]\n    public int Grow(", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_ConstructorInitializerEdited_ChangesTheSurface()
    {
        // Not a body, so it stays in the surface, conservatively.
        ShouldChangeTheSurface(Widget, Widget.Replace(": base(1)", ": base(2)", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_AutoPropertyGivenAnAccessorBody_ChangesTheSurface()
    {
        // An auto-property has a backing field and a bodied one does not, which a struct's layout would show.
        ShouldChangeTheSurface(Widget, Widget.Replace("{ get; } = 5;", "{ get => 5; }", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_DirectiveInsertedInsideABody_ChangesTheSurface()
    {
        // Every directive stays in the surface wherever it sits, the same conservatism the shape keeps.
        ShouldChangeTheSurface(Widget, Widget.Replace("return _size + by;", "#pragma warning disable CS0168\n        return _size + by;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_PrivateMethodAdded_ChangesTheSurface()
    {
        // Private members are surface: a dependent's failed bind can name one as its best candidate.
        ShouldChangeTheSurface(Widget, Widget.Replace("    public string Label()", "    private void Reset() { }\n    public string Label()", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_PrivateConstantAdded_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, Widget.Replace("    public const int Max = 10;", "    public const int Max = 10;\n    private const int Probe = 1;", StringComparison.Ordinal));
    }

    [Fact]
    public void Of_GeneratedBannerLeadingTheFile_ChangesTheSurface()
    {
        ShouldChangeTheSurface(Widget, "// <auto-generated />\n" + Widget);
    }

    [Fact]
    public void Of_CommentAddedInsideABody_KeepsTheSurfaceAndTheShape()
    {
        string commented = Widget.Replace("return _size + by;", "// grows\n        return _size + by;", StringComparison.Ordinal);

        SourceShape before = ShapeOf(Widget);
        SourceShape after = ShapeOf(commented);
        after.Hash.ShouldBe(before.Hash);
        after.SurfaceHash.ShouldBe(before.SurfaceHash);
    }

    // The edit must move the shape, or a held surface would say nothing about the cut: that is the difference
    // guard every "keeps" fact carries.
    private static void ShouldKeepTheSurface(string before, string after)
    {
        SourceShape beforeShape = ShapeOf(before);
        SourceShape afterShape = ShapeOf(after);

        afterShape.Hash.ShouldNotBe(beforeShape.Hash, customMessage: "the edit must change the shape");
        afterShape.SurfaceHash.ShouldBe(beforeShape.SurfaceHash);
    }

    private static void ShouldChangeTheSurface(string before, string after)
    {
        SourceShape beforeShape = ShapeOf(before);
        SourceShape afterShape = ShapeOf(after);

        afterShape.SurfaceHash.ShouldNotBe(beforeShape.SurfaceHash);
    }

    private static SourceShape ShapeOf(string text)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(text, path: "Widget.cs");
        return SourceShape.Of(tree);
    }
}
