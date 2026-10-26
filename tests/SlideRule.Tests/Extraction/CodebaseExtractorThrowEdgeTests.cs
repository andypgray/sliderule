using Shouldly;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     Throw edges (GRAMMAR §4.8), over the MSBuild-free fast path — the throw analog of
///     <see cref="CodebaseExtractorConstructorEdgeTests" />, one fact per <c>throw</c> shape. Line numbers count
///     from the first content line of each raw source literal.
/// </summary>
public sealed class CodebaseExtractorThrowEdgeTests
{
    [Fact]
    public void ExtractFromCompilations_ThrowNew_CoexistsWithConstructionAndReferenceEdges()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public void Run() { throw new MyError(); } }
                                                         """);

        // The same site yields all three facts: the throw edge (§4.8), the construction edge (§4.5), and the
        // §4.1 reference edge from the `MyError` name — recorded beside one another, never instead.
        model.ThrowEdge("N.Worker", "N.MyError")
            .Sites.Lines()
            .ShouldBe([3]);
        model.ShouldHaveConstructorEdge("N.Worker", "N.MyError");
        model.ShouldHaveEdge("N.Worker", "N.MyError");
    }

    [Fact]
    public void ExtractFromCompilations_ThrowNewDerivedException_RecordsNaturalTypeNotConvertedException()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public void Run() { throw new MyError(); } }
                                                         """);

        // CRITICAL: the thrown type is the expression's NATURAL static type (MyError), never the throw
        // conversion's ConvertedType (System.Exception) — using ConvertedType would collapse every throw to
        // System.Exception and erase the real type.
        model.ShouldHaveThrowEdge("N.Worker", "N.MyError");
        model.HasThrowEdge("N.Worker", "System.Exception")
            .ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_ExpressionBodiedThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker { public object Run() => throw new MyError(); }
                                                         """);

        // The expression-bodied `=> throw new X()` is a throw EXPRESSION (the most common form in the wild).
        model.ThrowEdge("N.Worker", "N.MyError")
            .Sites.Lines()
            .ShouldBe([3]);
    }

    [Fact]
    public void ExtractFromCompilations_NullCoalescingThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public string Run(string s)
                                                             {
                                                                 return s ?? throw new MyError();
                                                             }
                                                         }
                                                         """);

        // A `?? throw` right-operand is a throw expression.
        model.ThrowEdge("N.Worker", "N.MyError")
            .Sites.Lines()
            .ShouldBe([7]);
    }

    [Fact]
    public void ExtractFromCompilations_SwitchExpressionArmThrow_MintsThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public int Run(int x) => x switch
                                                             {
                                                                 1 => 10,
                                                                 _ => throw new MyError()
                                                             };
                                                         }
                                                         """);

        // A switch-expression arm is a throw expression.
        model.ThrowEdge("N.Worker", "N.MyError")
            .Sites.Lines()
            .ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_ThrowVariable_MintsTheVariableStaticType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError ex) { throw ex; }
                                                             }
                                                         }
                                                         """);

        // `throw ex` mints the VARIABLE's static type — the deliberate asymmetry with the bare rethrow `throw;`
        // (which mints nothing): under strict MustOnlyThrow, `catch (Exception ex) { throw ex; }` is red.
        model.ThrowEdge("N.Worker", "N.MyError")
            .Sites.Lines()
            .ShouldBe([8]);
    }

    [Fact]
    public void ExtractFromCompilations_BareRethrow_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 try { }
                                                                 catch (MyError) { throw; }
                                                             }
                                                         }
                                                         """);

        // A bare rethrow `throw;` (null expression) mints no throw edge.
        model.ThrowEdges("N.Worker")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowNull_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker { public void Run() { throw null; } }
                                                         """);

        // `throw null` has no thrown type (the null literal), so it mints nothing.
        model.ThrowEdges("N.Worker")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_TypeParameterThrow_MintsNothing()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker { public void Run<T>(T ex) where T : System.Exception { throw ex; } }
                                                         """);

        // A type-parameter thrown type is not a named type, so it mints nothing.
        model.ThrowEdges("N.Worker")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowHelper_MintsMemberUseOnlyNoThrowEdge()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Worker
                                                         {
                                                             public void Run(object o)
                                                             {
                                                                 System.ArgumentNullException.ThrowIfNull(o);
                                                             }
                                                         }
                                                         """);

        // A throw helper is an ordinary invocation, not a throw: the walk sees a member use, not a throw. So it
        // mints the member-use (and reference) edge but NO throw edge — the named §4.8 honesty boundary.
        model.ThrowEdges("N.Worker")
            .TargetNames()
            .ShouldBeEmpty();
        model.ShouldHaveEdge("N.Worker", "System.ArgumentNullException");
        model.MemberEdges("N.Worker")
            .ShouldContain(e => e.Member.Name == "ThrowIfNull");
    }

    [Fact]
    public void ExtractFromCompilations_ConstructedGenericThrownType_NormalizesToOpenDefinition()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Boom<T> : System.Exception {}
                                                         public class Worker { public void Run() { throw new Boom<int>(); } }
                                                         """);

        // throw new Boom<int>() records the OPEN definition N.Boom<T> (§4.1).
        model.ShouldHaveThrowEdge("N.Worker", "N.Boom<T>");
    }

    [Fact]
    public void ExtractFromCompilations_SelfThrow_IsDropped()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Recursive : System.Exception
                                                         {
                                                             public void Run() { throw new Recursive(); }
                                                         }
                                                         """);

        // Self-throw is dropped, mirroring the type-edge self-drop (§4.1).
        model.HasThrowEdge("N.Recursive", "N.Recursive")
            .ShouldBeFalse();
        model.ThrowEdges("N.Recursive")
            .TargetNames()
            .ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_ThrowInLocalFunction_AttributesToEnclosingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 void Handle() { throw new MyError(); }
                                                                 Handle();
                                                             }
                                                         }
                                                         """);

        model.ShouldHaveThrowEdge("N.Worker", "N.MyError");
    }

    [Fact]
    public void ExtractFromCompilations_ThrowInLambda_AttributesToEnclosingType()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class MyError : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run()
                                                             {
                                                                 System.Action handle = () => { throw new MyError(); };
                                                                 handle();
                                                             }
                                                         }
                                                         """);

        model.ShouldHaveThrowEdge("N.Worker", "N.MyError");
    }

    [Fact]
    public void ExtractFromCompilations_TopLevelStatementsThrow_AttributesToProgram()
    {
        CodebaseModel model = CompilationFactory.ExtractConsoleApp(("Program.cs", """
                                                                                  throw new System.InvalidOperationException();
                                                                                  """));

        // A top-level-statements throw attributes to the synthesized Program (the TopLevelProgramExtractionTests
        // precedent).
        ThrowEdge edge = model.ThrowEdge("Program", "System.InvalidOperationException");
        edge.Thrown.IsExternal.ShouldBeTrue();
        edge.Sites.Lines()
            .ShouldBe([1]);
    }

    [Fact]
    public void ExtractFromCompilations_ThrowEdges_AreOrdinallyOrdered()
    {
        CodebaseModel model = CompilationFactory.Extract("""
                                                         namespace N;
                                                         public class Beta : System.Exception {}
                                                         public class Alpha : System.Exception {}
                                                         public class Worker
                                                         {
                                                             public void Run(bool b)
                                                             {
                                                                 if (b) throw new Beta();
                                                                 throw new Alpha();
                                                             }
                                                         }
                                                         """);

        model.ThrowEdges("N.Worker")
            .TargetNames()
            .ShouldBe(["N.Alpha", "N.Beta"]);
    }
}
