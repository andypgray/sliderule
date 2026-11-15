using Shouldly;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     One fact per member-use construct (GRAMMAR §4.5), over the MSBuild-free fast path — the member
///     analog of <see cref="CodebaseExtractorEdgeTests" />. Each row asserts the SymbolId form, the
///     containing type, and (where meaningful) the kind and sites. Line numbers are 1-based and count
///     from the first content line of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorMemberEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_StaticPropertyGet_ProducesPropertyEdgeToDeclaredType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public static class Config { public static string Setting { get; } }
                                                         public class Reader { public string Go() => Config.Setting; }
                                                         """);

        MemberEdge edge = model.MemberEdge("N.Reader", "P:N.Config.Setting");
        edge.Member.Kind.ShouldBe(MemberKind.Property);
        edge.Member.ContainingType.FullName.ShouldBe("N.Config");
        edge.Member.Name.ShouldBe("Setting");
    }

    [Fact]
    public void ExtractFromCompilations_InstancePropertyGetAndSet_UnionIntoOnePropertyEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Model { public int P { get; set; } }
                                                         public class User
                                                         {
                                                             public void Go(Model m)
                                                             {
                                                                 int x = m.P;
                                                                 m.P = 5;
                                                             }
                                                         }
                                                         """);

        // The read and the write fold to the SAME property symbol — one P: edge, never M:get_P/M:set_P.
        model.MemberEdges("N.User")
            .ShouldHaveSingleItem();
        MemberEdge edge = model.MemberEdge("N.User", "P:N.Model.P");
        edge.Member.Kind.ShouldBe(MemberKind.Property);
        edge.Sites.Lines()
            .ShouldBe([7, 8]);
    }

    [Fact]
    public void ExtractFromCompilations_CompoundAssignment_ProducesSinglePropertyEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public int P { get; set; } }
                                                         public class D { public void Go(C c) { c.P += 1; } }
                                                         """);

        model.MemberEdge("N.D", "P:N.C.P")
            .Member.Kind.ShouldBe(MemberKind.Property);
    }

    [Fact]
    public void ExtractFromCompilations_ConditionalAccess_ProducesPropertyEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C { public string P { get; set; } }
                                                         public class D { public string Go(C c) => c?.P; }
                                                         """);

        model.MemberEdge("N.D", "P:N.C.P")
            .Member.Kind.ShouldBe(MemberKind.Property);
    }

    [Fact]
    public void ExtractFromCompilations_MethodInvocationOverloads_ProduceOneEdgePerOverload()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Svc { public void M(int x) {} public void M(string s) {} }
                                                         public class Cli { public void Go(Svc s) { s.M(1); s.M("a"); } }
                                                         """);

        // The §4.3 per-overload identity substrate: two distinct M: ids, one per resolved overload.
        model.MemberEdges("N.Cli")
            .Select(e => e.Member.SymbolId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(["M:N.Svc.M(System.Int32)", "M:N.Svc.M(System.String)"]);
        model.MemberEdge("N.Cli", "M:N.Svc.M(System.Int32)")
            .Member.Kind.ShouldBe(MemberKind.Method);
    }

    [Fact]
    public void ExtractFromCompilations_ExtensionMethodCall_ResolvesThroughReducedFromToStaticClass()
    {
        CodebaseModel model = CompilationFactory.ShouldExtractWithoutErrors("""
                                                                            namespace N;
                                                                            public static class Ext { public static string Echo(this string s) => s; }
                                                                            public class Caller { public string Go(string t) => t.Echo(); }
                                                                            """);

        // ReducedFrom-first normalization: the reduced call resolves to the declaring static class's method,
        // and the DocId carries the receiver parameter (System.String), not the reduced no-arg form.
        MemberEdge edge = model.MemberEdge("N.Caller", "M:N.Ext.Echo(System.String)");
        edge.Member.Kind.ShouldBe(MemberKind.Method);
        edge.Member.ContainingType.FullName.ShouldBe("N.Ext");
    }

    [Fact]
    public void ExtractFromCompilations_ExtensionBlockMethod_BothSpellingsAreOneUseOfTheEmittedMethod()
    {
        CodebaseModel model = CompilationFactory.ShouldExtractWithoutErrors("""
                                                                            namespace N;
                                                                            public static class Ext { extension(string s) { public string Echo() => s; } }
                                                                            public class Caller
                                                                            {
                                                                                public string Go(string t) => t.Echo();
                                                                                public string Again(string t) => Ext.Echo(t);
                                                                            }
                                                                            """);

        // The block method resolves to the static method the compiler emits for it, which is what the static
        // spelling binds to, so the receiver is the ID's first parameter, as for a classic extension method.
        MemberEdge edge = model.MemberEdges("N.Caller")
            .ShouldHaveSingleItem();
        edge.Member.SymbolId.ShouldBe("M:N.Ext.Echo(System.String)");
        edge.Member.ContainingType.FullName.ShouldBe("N.Ext");
        edge.Sites.Lines()
            .ShouldBe([5, 6]);
    }

    [Fact]
    public void ExtractFromCompilations_GenericExtensionBlockGenericMethod_UsesAndInventoryShareTheDefinitionId()
    {
        CodebaseModel model = CompilationFactory.ShouldExtractWithoutErrors("""
                                                                            namespace N;
                                                                            public static class Ext { extension<T>(T r) { public T Pick<U>(U u) => r; } }
                                                                            public class Caller
                                                                            {
                                                                                public int Go(int n) => n.Pick("x");
                                                                                public int Again(int n) => Ext.Pick<int, string>(n, "x");
                                                                            }
                                                                            """);

        // A constructed receiver's emitted method numbers its type parameters differently from the definition,
        // so the use resolves to the definition the inventory records.
        const string pick = "M:N.Ext.Pick``2(``0,``1)";
        MemberEdge edge = model.MemberEdges("N.Caller")
            .ShouldHaveSingleItem();
        edge.Member.SymbolId.ShouldBe(pick);
        edge.Sites.Lines()
            .ShouldBe([5, 6]);
        model.Member("N.Ext", pick)
            .Name.ShouldBe("Pick");
    }

    [Fact]
    public void ExtractFromCompilations_ExtensionBlockProperty_IsAUseOfTheMemberTheInventoryRecords()
    {
        CodebaseModel model = CompilationFactory.ShouldExtractWithoutErrors("""
                                                                            namespace N;
                                                                            public static class Ext { extension(string s) { public int Size => s.Length; } }
                                                                            public class Caller { public int Go(string t) => t.Size; }
                                                                            """);

        // A property keeps its own ID, the block's, which the static class's inventory carries too.
        MemberEdge edge = model.MemberEdges("N.Caller")
            .ShouldHaveSingleItem();
        edge.Member.Kind.ShouldBe(MemberKind.Property);
        edge.Member.ContainingType.FullName.ShouldBe("N.Ext");
        edge.Member.SymbolId.ShouldBe(model.Type("N.Ext").MemberNamed("Size").SymbolId);
    }

    [Fact]
    public void ExtractFromCompilations_FieldRead_ProducesFieldEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Box { public int Value; }
                                                         public class Reader { public int Go(Box b) => b.Value; }
                                                         """);

        model.MemberEdge("N.Reader", "F:N.Box.Value")
            .Member.Kind.ShouldBe(MemberKind.Field);
    }

    [Fact]
    public void ExtractFromCompilations_DelegateMemberInvoked_ProducesEdgeToTheMemberNotToInvoke()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public static class Hooks
                                                         {
                                                             public static Action Ping = () => {};
                                                             public static Func<int> Count => () => 1;
                                                         }
                                                         public sealed class Relay { public static Relay? Instance; public Action Pong = () => {}; }
                                                         public static class Caller
                                                         {
                                                             public static object[] Calls { get; } = [Run(() => Hooks.Ping()), Hooks.Count(), Run(() => Relay.Instance?.Pong())];
                                                             private static object Run(Action a) => a;
                                                         }
                                                         """);

        // The call binds the delegate's Invoke; the name it invokes, directly or after `?.`, is the field or property.
        model.MemberEdges("N.Caller")
            .Select(e => e.Member.SymbolId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ShouldBe(["F:N.Hooks.Ping", "F:N.Relay.Instance", "F:N.Relay.Pong", "P:N.Hooks.Count"]);
    }

    [Fact]
    public void ExtractFromCompilations_EventSubscribe_ProducesEventEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Pub { public event System.Action Evt; }
                                                         public class Sub { public void Wire(Pub p) { p.Evt += OnPing; } private void OnPing() {} }
                                                         """);

        // The subscription binds the event (E:); the handler OnPing is same-type and drops out.
        MemberEdge edge = model.MemberEdge("N.Sub", "E:N.Pub.Evt");
        edge.Member.Kind.ShouldBe(MemberKind.Event);
        edge.Member.ContainingType.FullName.ShouldBe("N.Pub");
    }

    [Fact]
    public void ExtractFromCompilations_MethodGroupReference_ProducesMethodEdgeWithoutInvocation()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System;
                                                         namespace N;
                                                         public static class Helper { public static int Square(int x) => x * x; }
                                                         public class Wire { public Func<int, int> Go() => Helper.Square; }
                                                         """);

        model.MemberEdge("N.Wire", "M:N.Helper.Square(System.Int32)")
            .Member.Kind.ShouldBe(MemberKind.Method);
    }

    [Fact]
    public void ExtractFromCompilations_UsingStaticBareName_ProducesSameMethodEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using static N.Helper;
                                                         namespace N;
                                                         public static class Helper { public static int Square(int x) => x * x; }
                                                         public class Caller { public int Go() => Square(2); }
                                                         """);

        model.MemberEdge("N.Caller", "M:N.Helper.Square(System.Int32)")
            .Member.Kind.ShouldBe(MemberKind.Method);
    }

    [Fact]
    public void ExtractFromCompilations_NameofOperands_ProduceTypeEdgeButNoMemberEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Target { public int Member; }
                                                         public class Namer { public string A() => nameof(Target); public string B() => nameof(Target.Member); }
                                                         """);

        // The pinned asymmetry: nameof mints the type edge but never a member edge (it does not use the member).
        model.ShouldHaveEdge("N.Namer", "N.Target");
        model.MemberEdges("N.Namer")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_BclConstructedGeneric_NormalizesToOriginalDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class C
                                                         {
                                                             public int Go(System.Threading.Tasks.Task<int> t) => t.Result;
                                                         }
                                                         """);

        // Constructed member folds to its OriginalDefinition — one edge for every Task<T> construction.
        MemberEdge edge = model.MemberEdge("N.C", "P:System.Threading.Tasks.Task`1.Result");
        edge.Member.Kind.ShouldBe(MemberKind.Property);
        edge.Member.ContainingType.FullName.ShouldBe("System.Threading.Tasks.Task<TResult>");
        edge.Member.ContainingType.IsExternal.ShouldBeTrue();
    }

    [Fact]
    public void ExtractFromCompilations_OwnMemberUse_ProducesNoMemberEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Solo
                                                         {
                                                             public int X;
                                                             public int P { get; set; }
                                                             public void A() {}
                                                             public int Go() { A(); return X + P; }
                                                         }
                                                         """);

        model.MemberEdges("N.Solo")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ExternalAndInternalTargets_CarryCorrectExternalFlagAndSharedNode()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Dep { public int Value; }
                                                         public class C
                                                         {
                                                             public int Go(Dep d)
                                                             {
                                                                 var s = new System.Text.StringBuilder();
                                                                 s.Append("x");
                                                                 return d.Value;
                                                             }
                                                         }
                                                         """);

        MemberReference external = model.MemberEdge("N.C", "M:System.Text.StringBuilder.Append(System.String)")
            .Member;
        MemberReference declared = model.MemberEdge("N.C", "F:N.Dep.Value")
            .Member;

        external.ContainingType.IsExternal.ShouldBeTrue();
        declared.ContainingType.IsExternal.ShouldBeFalse();

        // The member's containing type is the SAME node instance held by model.Types (reference equality).
        declared.ContainingType.ShouldBeSameAs(model.Type("N.Dep"));
        external.ContainingType.ShouldBeSameAs(model.Type("System.Text.StringBuilder"));
    }

    [Fact]
    public void ExtractFromCompilations_ConstructorCall_ProducesTypeEdgeButNoMemberEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Foo { public Foo() {} }
                                                         public class Bar { public Foo Go() => new Foo(); }
                                                         """);

        // A constructor is not a §4.5 use: the type edge stands, the member channel is empty.
        model.ShouldHaveEdge("N.Bar", "N.Foo");
        model.MemberEdges("N.Bar")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_CompilerPatternConsumption_ProducesNoMemberEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         using System.Linq;
                                                         namespace N;
                                                         public class Boundary
                                                         {
                                                             public async System.Threading.Tasks.Task Go(System.Threading.Tasks.Task pending, int[] xs, System.IDisposable gate)
                                                             {
                                                                 await pending;
                                                                 using (gate) {}
                                                                 foreach (int x in xs) {}
                                                             }

                                                             public System.Collections.Generic.IEnumerable<int> Q(int[] xs) => from x in xs select x;
                                                         }
                                                         """);

        // The documented syntax-walk boundary (GRAMMAR §4.5): await's GetAwaiter, using's Dispose, the
        // foreach enumerator pattern, and query-syntax translation are compiler-pattern consumption the
        // walk never sees as a member access — the member channel stays empty.
        model.MemberEdges("N.Boundary")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_IndexerElementAccess_ProducesNoMemberEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Box { public int this[int i] => i; }
                                                         public class User { public int Go(Box b) => b[0]; }
                                                         """);

        // The edge-side indexer exclusion (GRAMMAR §4.5, §11 growth): an element access binds no
        // SimpleNameSyntax, so the indexer never surfaces in the member channel; the type edge stands.
        model.ShouldHaveEdge("N.User", "N.Box");
        model.MemberEdges("N.User")
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ExtensionIndexerElementAccess_ProducesNoMemberEdge()
    {
        CodebaseModel model = CompilationFactory.ShouldExtractWithoutErrors("""
                                                                            namespace N;
                                                                            public class Box {}
                                                                            public static class Ext { extension(Box b) { public int this[int i] => i; } }
                                                                            public class User { public int Go(Box b) => b[0]; }
                                                                            """);

        // An extension indexer references its static class and, as every indexer, is no member use.
        model.ShouldHaveEdge("N.User", "N.Ext");
        model.MemberEdges("N.User")
            .ShouldBeEmpty();
    }
}
