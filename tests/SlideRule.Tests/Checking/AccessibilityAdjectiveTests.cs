using Shouldly;
using Xunit;
using SlideRule.Codebase;
using SlideRule.Prose;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Checker semantics and prose for <c>.ThatArePublic()</c>, <c>.ThatAreInternal()</c> and
///     <c>.ThatArePrivate()</c> (GRAMMAR §5.2): the adjective half of the accessibility fact family,
///     whose constraint half is <c>MustBePublic</c> and <c>MustBeInternal</c>.
/// </summary>
/// <remarks>
///     The fixture carries a nested type at each accessibility the adjectives select, because nested types
///     are where the adjectives are needed: a nested type reports the namespace of the type nesting it
///     and answers to its own short name, so a namespace-scoped selection reaches it and neither
///     <c>Named</c> nor a glob can pick it out again. The accessibility is the only fact that can.
/// </remarks>
public sealed class AccessibilityAdjectiveTests
{
    private const string Source = """
                                  namespace App.Surface
                                  {
                                      public class Door
                                      {
                                          private sealed class Hinge {}
                                          internal sealed class Frame {}
                                          public sealed class Handle {}
                                      }

                                      internal class Workshop
                                      {
                                          private sealed class Bench {}
                                      }
                                  }
                                  """;

    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(Source);

    private static readonly Arch Arch = new();

    [Fact]
    public void ThatArePublic_SelectsTheTypesDeclaredPublic()
    {
        Checker.Selects(Codebase, arch => arch.Types.InNamespace("App.Surface").ThatArePublic())
            .ShouldBe(["App.Surface.Door", "App.Surface.Door.Handle"], ignoreOrder: true);
    }

    [Fact]
    public void ThatAreInternal_SelectsTheTypesDeclaredInternal()
    {
        // Workshop writes no modifier at namespace scope, which is internal; Frame writes one.
        Checker.Selects(Codebase, arch => arch.Types.InNamespace("App.Surface").ThatAreInternal())
            .ShouldBe(["App.Surface.Workshop", "App.Surface.Door.Frame"], ignoreOrder: true);
    }

    [Fact]
    public void ThatArePrivate_SelectsOnlyNestedTypes()
    {
        // Only a type nested in another can be private, which is the whole reason this spelling exists.
        Checker.Selects(Codebase, arch => arch.Types.InNamespace("App.Surface").ThatArePrivate())
            .ShouldBe(["App.Surface.Door.Hinge", "App.Surface.Workshop.Bench"], ignoreOrder: true);
    }

    [Fact]
    public void ExceptThatArePrivate_IsHowASubjectLeavesNestedStateOut()
    {
        // The shape the extraction front door takes: subtract rather than enumerate, because the thing
        // being left out has no name the selection could reach. Negation is Except — there is no
        // "not private" adjective and deliberately so.
        Checker.Selects(Codebase,
                arch => arch.Types.InNamespace("App.Surface").Except(arch.Types.ThatArePrivate()))
            .ShouldBe(
            [
                "App.Surface.Door", "App.Surface.Door.Handle", "App.Surface.Door.Frame",
                "App.Surface.Workshop"
            ], ignoreOrder: true);
    }

    [Fact]
    public void Accessibility_PremodifiesTheHead_AndComposesWithAKind()
    {
        // A head prefix, not an inline clause: the fact stays on the noun it narrows even where the
        // selection renders in reference position, and it can stand in front of a kind head.
        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").ThatArePublic())
            .ShouldBe("Public types in `MyApp.*`");

        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").ThatArePublic().OfKind(TypeKind.Interface))
            .ShouldBe("Public interfaces in `MyApp.*`");
    }

    [Fact]
    public void Accessibility_StacksWithAuthored_InTheOrderWritten()
    {
        // Both narrow the subject, so the sentence names both: dropping either would describe a wider
        // subject than the checker applies. They read in the order the chain wrote them.
        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").Authored().ThatArePublic())
            .ShouldBe("Authored public types in `MyApp.*`");

        SentenceRenderer.Subject(Arch.Types.InNamespace("MyApp.*").ThatArePublic().Authored())
            .ShouldBe("Public authored types in `MyApp.*`");
    }

    [Fact]
    public void Accessibility_RendersItsOwnWordForEachValue()
    {
        SentenceRenderer.Subject(Arch.Types.ThatAreInternal()).ShouldBe("Internal types");
        SentenceRenderer.Subject(Arch.Types.ThatArePrivate()).ShouldBe("Private types");
    }

    [Theory]
    [InlineData(Accessibility.Public, "public ")]
    [InlineData(Accessibility.Internal, "internal ")]
    [InlineData(Accessibility.Protected, "protected ")]
    [InlineData(Accessibility.ProtectedInternal, "protected internal ")]
    [InlineData(Accessibility.PrivateProtected, "private protected ")]
    [InlineData(Accessibility.Private, "private ")]
    public void Accessibility_HasAWordForEveryValueTheModelFileCanCarry(
        Accessibility accessibility, string expected)
    {
        // Three of these six have no builder — only public, internal and private are spellable on the
        // fluent surface, because they are the three a rule has ever wanted. The other three still need
        // a word: the model-file reader takes whatever `Accessibility` the file names, so a node
        // carrying `protected` round-trips into a sentence, and a fragment table with a hole in it
        // would render that sentence with a word missing rather than refuse it.
        ProseFormat.AccessibilityPrefix(accessibility).ShouldBe(expected);
    }

    [Fact]
    public void Accessibility_OutsideTheEnum_RendersNothingRatherThanThrowing()
    {
        // The same reasoning one step further out. A fragment is assembled into prose a reader sees, so
        // an unmapped value costs a missing word; throwing here would cost the whole report.
        ProseFormat.AccessibilityPrefix((Accessibility)99).ShouldBe("");
    }
}
