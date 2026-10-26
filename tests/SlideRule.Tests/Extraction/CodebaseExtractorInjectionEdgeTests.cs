using Shouldly;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Constructor-injection edges (GRAMMAR §4.7), over the MSBuild-free fast path, one fact per
///     edge-producing (or edge-suppressing) constructor shape. Line numbers count from the first content line
///     of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorInjectionEdgeTests
{
    // One row per constructor shape that injects: an explicit constructor on a class, a primary constructor on a
    // class, and an explicit constructor on a record. Each mints the edge at the parameter's line.
    [Theory]
    [InlineData("public class Svc { public Svc(IDep d) {} }", "N.Svc")]
    [InlineData("public class Svc(IDep d) { }", "N.Svc")]
    [InlineData("public record Rec { public Rec(IDep d) {} }", "N.Rec")]
    public void ExtractFromCompilations_ConstructorShape_ProducesInjectionEdgeAtParameterLine(
        string declaration, string injector)
    {
        CodebaseModel model = CompilationFactory.Extract($$"""
                                                           namespace N;
                                                           public interface IDep {}
                                                           {{declaration}}
                                                           """);

        model.InjectionEdge(injector, "N.IDep")
            .Sites.Lines()
            .ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_PrimaryConstructorOnRecord_ProducesOnlyTheParameterEdgeNoCopyCtorSelfEdge()
    {
        // The positional record's primary constructor mints the parameter edge; its compiler-generated copy
        // constructor (Rec(Rec)) is implicitly declared, so it mints nothing — and would only ever be a
        // self-edge, which is dropped regardless. So exactly one injection edge, to IDep.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public record Rec(IDep D);
                                                         """);

        model.InjectionEdges("N.Rec")
            .TargetNames()
            .ShouldBe(["N.IDep"]);
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericParameter_DecomposesToDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IHandler<T> {}
                                                         public class Order {}
                                                         public class C { public C(IHandler<Order> h) {} }
                                                         """);

        model.InjectionEdge("N.C", "N.IHandler<T>")
            .Sites.Lines()
            .ShouldBe([4]);
        model.InjectionEdge("N.C", "N.Order")
            .Sites.Lines()
            .ShouldBe([4]);
    }

    [Fact]
    public void ExtractFromCompilations_ArrayParameter_DecomposesToElementType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class C { public C(IDep[] deps) {} }
                                                         """);

        // The element type is the endpoint; the array type itself is not a node.
        model.InjectionEdges("N.C")
            .TargetNames()
            .ShouldBe(["N.IDep"]);
    }

    [Fact]
    public void ExtractFromCompilations_EnumerableOfInterfaceParameter_DecomposesToDefinitionAndArgument()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IFoo {}
                                                         public class C { public C(System.Collections.Generic.IEnumerable<IFoo> foos) {} }
                                                         """);

        model.ShouldHaveInjectionEdge("N.C", "System.Collections.Generic.IEnumerable<T>");
        model.ShouldHaveInjectionEdge("N.C", "N.IFoo");
    }

    [Fact]
    public void ExtractFromCompilations_ExternalParameterType_GetsAMatchableExternalNode()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public C(System.IDisposable d) {} }
                                                         """);

        InjectionEdge edge = model.InjectionEdge("N.C", "System.IDisposable");
        edge.Injected.IsExternal.ShouldBeTrue();
        edge.Sites.Lines()
            .ShouldBe([2]);
    }

    [Fact]
    public void ExtractFromCompilations_StaticConstructor_ProducesNoInjectionEdge()
    {
        // A static constructor is not an instance constructor, and the implicit parameterless instance
        // constructor is filtered — so neither mints an injection edge.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class WithStaticCtor { static WithStaticCtor() {} }
                                                         """);

        model.InjectionEdges("N.WithStaticCtor")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_SelfInjection_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Selfish { public Selfish(Selfish other) {} }
                                                         """);

        model.HasInjectionEdge("N.Selfish", "N.Selfish")
            .ShouldBeFalse();
        model.InjectionEdges("N.Selfish")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_EnumAndDelegateSources_ProduceNoInjectionEdges()
    {
        // Enum and delegate types declare no walkable instance constructors, so they are the source of no
        // injection edge (the delegate's Invoke parameter and the enum's members are not constructor params).
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public delegate void Notify(int x);
                                                         public enum Color { Red }
                                                         """);

        model.InjectionEdges("N.Notify")
            .TargetNames()
            .ShouldBeEmpty();
        model.InjectionEdges("N.Color")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TwoParametersOfSameType_UnionsToTwoSites()
    {
        // Two constructor parameters of the same injected type record two distinct sites (one per parameter
        // line), unioned under the one (source, injected) edge.
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public interface IDep {}
                                                         public class Svc
                                                         {
                                                             public Svc(
                                                                 IDep a,
                                                                 IDep b) {}
                                                         }
                                                         """);

        model.InjectionEdge("N.Svc", "N.IDep")
            .Sites.Lines()
            .ShouldBe([6, 7]);
    }
}
