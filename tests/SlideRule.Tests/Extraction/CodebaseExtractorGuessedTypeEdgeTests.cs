using Shouldly;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The catch, throw and construct channels over a type the compiler could not bind exactly: each records the
///     candidate the reference channel records for the same written name, and nothing for a name with no candidate.
/// </summary>
public sealed class CodebaseExtractorGuessedTypeEdgeTests
{
    // One worker for every row below. Outer.Hidden is inaccessible, Dup is ambiguous between the two imported
    // namespaces, and Missing and Elsewhere.Missing are declared nowhere, Elsewhere being a namespace that exists.
    // None of these members compiles, which is the point: extraction reads code that does not.
    private static CodebaseModel ExtractWorker(string member)
    {
        return CompilationFactory.Extract(
            "TestProject",
            ("Elsewhere.cs", """
                             namespace A { public class Dup : System.Exception {} }
                             namespace B { public class Dup : System.Exception {} }
                             namespace Elsewhere { public class Present {} }
                             """),
            ("Worker.cs", $$"""
                            using A;
                            using B;
                            namespace N;
                            public class Outer { private class Hidden : System.Exception {} }
                            public class Worker
                            {
                                {{member}}
                            }
                            """));
    }

    private static string[] Expected(string? candidate)
    {
        return candidate is null ? [] : [candidate];
    }

    // The channel and the reference edge answer the same question about one written name, so a guess the channel
    // takes must be one the reference channel takes too, and a name with no candidate must leave the reference
    // channel as silent as the channel: no edge to any type named Missing, the one name in the rows with none.
    private static void ShouldAgreeWithTheReferenceEdge(CodebaseModel model, string? candidate)
    {
        if (candidate is null)
        {
            model.Edges
                .Where(static edge => edge.Source.FullName == "N.Worker")
                .Select(static edge => edge.Target.FullName)
                .ShouldNotContain(static target => target == "Missing" || target.EndsWith(".Missing", StringComparison.Ordinal),
                    "the reference channel records no candidate for a name with none");
            return;
        }

        model.HasEdge("N.Worker", candidate)
            .ShouldBeTrue($"the reference channel records the candidate {candidate}");
    }

    [Theory]
    [InlineData("public void Run() { try { } catch (Outer.Hidden) { } }", "N.Outer.Hidden")]
    [InlineData("public void Run() { try { } catch (Dup) { } }", "A.Dup")]
    [InlineData("public void Run() { try { } catch (Dup e) when (e != null) { } }", "A.Dup")]
    [InlineData("public void Run() { try { } catch (Missing) { } }", null)]
    [InlineData("public void Run() { try { } catch (Elsewhere.Missing) { } }", null)]
    public void Extract_CatchOfAGuessedType_RecordsTheCandidate(string member, string? candidate)
    {
        CodebaseModel model = ExtractWorker(member);

        model.CatchEdges("N.Worker")
            .Select(edge => edge.Caught.FullName)
            .ShouldBe(Expected(candidate));
        ShouldAgreeWithTheReferenceEdge(model, candidate);
    }

    [Theory]
    [InlineData("public void Run() { throw new Outer.Hidden(); }", "N.Outer.Hidden")]
    [InlineData("public void Run() { throw new Dup(); }", "A.Dup")]
    [InlineData("public void Run(Dup e) { throw e; }", "A.Dup")]
    [InlineData("public object Run(object o) => o ?? throw new Dup();", "A.Dup")]
    [InlineData("public void Run() { throw new Missing(); }", null)]
    [InlineData("public void Run() { throw new Elsewhere.Missing(); }", null)]
    public void Extract_ThrowOfAGuessedType_RecordsTheCandidate(string member, string? candidate)
    {
        CodebaseModel model = ExtractWorker(member);

        model.ThrowEdges("N.Worker")
            .Select(edge => edge.Thrown.FullName)
            .ShouldBe(Expected(candidate));
        ShouldAgreeWithTheReferenceEdge(model, candidate);
    }

    [Theory]
    [InlineData("public void Run() { new Outer.Hidden(); }", "N.Outer.Hidden")]
    [InlineData("public void Run() { new Dup(); }", "A.Dup")]
    [InlineData("public void Run() { Outer.Hidden h = new(); }", "N.Outer.Hidden")]
    [InlineData("public void Run() { Dup d = new(); }", "A.Dup")]
    [InlineData("public void Run() { new Missing(); }", null)]
    [InlineData("public void Run() { Missing m = new(); }", null)]
    public void Extract_ConstructionOfAGuessedType_RecordsTheCandidate(string member, string? candidate)
    {
        CodebaseModel model = ExtractWorker(member);

        model.ConstructorEdges("N.Worker")
            .Select(edge => edge.Constructed.FullName)
            .ShouldBe(Expected(candidate));
        ShouldAgreeWithTheReferenceEdge(model, candidate);
    }
}
