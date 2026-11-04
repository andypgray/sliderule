using Shouldly;
using SlideRule.Codebase;
using SlideRule.Prose;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Checker semantics and prose for <c>.ThatAreSealed()</c>, <c>.ThatAreStatic()</c>,
///     <c>.ThatAreAbstract()</c> and <c>.ThatAreRecords()</c> (GRAMMAR §5.2): the adjective halves of the
///     type-shape verbs, reading the same flags with the same C# declaration semantics.
/// </summary>
public sealed class ShapeAdjectiveTests
{
    private const string Source = """
                                  namespace App.Shape
                                  {
                                      public sealed class SealedThing {}
                                      public class OpenThing {}
                                      public static class StaticThing {}
                                      public abstract class AbstractThing {}
                                      public interface IContract {}
                                      public struct Point {}
                                      public record Message(int Id);
                                      public record struct Reading(int Value);
                                  }
                                  """;

    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(Source);

    private static readonly Arch Arch = new();

    [Theory]
    // A struct and a record struct are sealed; a static class is not.
    [InlineData("ThatAreSealed", new[] { "App.Shape.SealedThing", "App.Shape.Point", "App.Shape.Reading" })]
    [InlineData("ThatAreStatic", new[] { "App.Shape.StaticThing" })]
    // An interface is abstract; a static class is not.
    [InlineData("ThatAreAbstract", new[] { "App.Shape.AbstractThing", "App.Shape.IContract" })]
    // A record class and a record struct both, and the plain struct beside them not.
    [InlineData("ThatAreRecords", new[] { "App.Shape.Message", "App.Shape.Reading" })]
    public void ShapeAdjective_SelectsTheTypesOfItsShape(string adjective, string[] expected)
    {
        Checker.Selects(Codebase, arch => Adjective(arch.Types.InNamespace("App.Shape"), adjective))
            .ShouldBe(expected, ignoreOrder: true);
    }

    [Theory]
    [InlineData("ThatAreSealed", "MustBeSealed")]
    [InlineData("ThatAreStatic", "MustBeStatic")]
    [InlineData("ThatAreAbstract", "MustBeAbstract")]
    [InlineData("ThatAreRecords", "MustBeRecords")]
    public void ShapeAdjective_SelectsWhatItsVerbPasses(string adjective, string verb)
    {
        // An adjective and its verb read one flag, so the selected types are the verb's whole green set
        // and the rest are its red set; neither can disagree with the other about a type.
        Checker.Verdict(Codebase,
                arch => ShapeVerbTests.ShapeVerb(Adjective(arch.Types.InNamespace("App.Shape"), adjective), verb))
            .ShouldHavePassed();

        IReadOnlyList<string> selected =
            Checker.Selects(Codebase, arch => Adjective(arch.Types.InNamespace("App.Shape"), adjective));
        IReadOnlyList<string> rest = Checker.Selects(Codebase, arch => arch.Types.InNamespace("App.Shape"))
            .Except(selected)
            .ToList();
        Checker.Verdict(Codebase,
                arch => ShapeVerbTests.ShapeVerb(
                    arch.Types.InNamespace("App.Shape").Except(Adjective(arch.Types, adjective)), verb))
            .ShouldHaveFailedWithSubjects(rest.ToArray());
    }

    [Fact]
    public void ExceptAbstractAndStatic_IsHowASubjectSaysConcreteClasses()
    {
        // The spelling the shape laws want most: the classes of no listed shape, by subtraction, which is
        // why no concrete adjective ships beside these.
        Checker.Selects(Codebase,
                arch => arch.Types.InNamespace("App.Shape")
                    .OfKind(TypeKind.Class)
                    .Except(arch.Types.ThatAreAbstract(), arch.Types.ThatAreStatic()))
            .ShouldBe(["App.Shape.SealedThing", "App.Shape.OpenThing", "App.Shape.Message"], ignoreOrder: true);

        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*")
                .OfKind(TypeKind.Class)
                .Except(Arch.Types.ThatAreAbstract(), Arch.Types.ThatAreStatic()))
            .ShouldBe("Classes in `MyApp.*`, except abstract types or static types");
    }

    [Theory]
    [InlineData("ThatAreSealed", "Sealed")]
    [InlineData("ThatAreStatic", "Static")]
    [InlineData("ThatAreAbstract", "Abstract")]
    [InlineData("ThatAreRecords", "Record")]
    public void ShapeAdjective_PremodifiesTheHead_InEitherOrderBesideAKind(string adjective, string word)
    {
        // A head prefix, not a head: written before or after OfKind, the sentence names both filters the
        // checker applies rather than letting the later head overwrite the earlier one.
        SentenceRenderer.Subject(Adjective(Arch.Types.InNamespace("MyApp.*"), adjective))
            .ShouldBe($"{word} types in `MyApp.*`");

        SentenceRenderer.Subject(Adjective(Arch.Types.InNamespace("MyApp.*").OfKind(TypeKind.Struct), adjective))
            .ShouldBe($"{word} structs in `MyApp.*`");

        SentenceRenderer.Subject(Adjective(Arch.Types.InNamespace("MyApp.*"), adjective).OfKind(TypeKind.Struct))
            .ShouldBe($"{word} structs in `MyApp.*`");
    }

    [Fact]
    public void ShapeAdjectives_StackWithEachOther_InTheOrderWritten()
    {
        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").ThatAreSealed().ThatAreRecords())
            .ShouldBe("Sealed record types in `MyApp.*`");

        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").ThatArePublic().ThatAreRecords())
            .ShouldBe("Public record types in `MyApp.*`");
    }

    private static Selection Adjective(Selection selection, string adjective)
    {
        return adjective switch
        {
            "ThatAreSealed" => selection.ThatAreSealed(),
            "ThatAreStatic" => selection.ThatAreStatic(),
            "ThatAreAbstract" => selection.ThatAreAbstract(),
            "ThatAreRecords" => selection.ThatAreRecords(),
            _ => throw new ArgumentOutOfRangeException(nameof(adjective), adjective, "No shape adjective has this name.")
        };
    }
}
