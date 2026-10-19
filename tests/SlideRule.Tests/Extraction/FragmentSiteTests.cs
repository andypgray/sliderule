using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction.Fragments;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     What a site <em>is</em> against what it merely carries: the file and the line are its identity, and
///     the span rides beside them for the analyzer to put a squiggle on — outside equality, outside
///     ordering, and outside the set that collapses two mentions on one line into one site.
/// </summary>
public sealed class FragmentSiteTests
{
    // Two mentions on one line, so every row below has a real difference to be insensitive to: a site pair
    // that agreed on its spans would pass these whether the span were part of identity or not.
    private const string Source = "public class Widget { public string First; public string Second; }";

    [Fact]
    public void TwoSitesOnOneLineWithDifferentSpans_AreEqualAndOrderTogether()
    {
        SyntaxTree tree = Tree();

        FragmentSite first = FragmentSite.Of(tree, SpanOf("First"));
        FragmentSite second = FragmentSite.Of(tree, SpanOf("Second"));

        first.Start.ShouldNotBe(second.Start, "the two mentions must carry different spans for this to say anything");
        first.Equals(second)
            .ShouldBeTrue("a site is its file and its line");
        first.GetHashCode()
            .ShouldBe(second.GetHashCode(), "equal sites must hash alike, or a set would hold both");
        first.CompareTo(second)
            .ShouldBe(0, "and order together, or the sorted set would never compare them");
    }

    [Fact]
    public void ASortedSet_KeepsTheFirstMentionsSpan()
    {
        SyntaxTree tree = Tree();
        FragmentSite first = FragmentSite.Of(tree, SpanOf("First"));
        FragmentSite second = FragmentSite.Of(tree, SpanOf("Second"));

        var firstThenSecond = new SortedSet<FragmentSite> { first, second };
        var secondThenFirst = new SortedSet<FragmentSite> { second, first };

        FragmentSite keptForward = firstThenSecond.ShouldHaveSingleItem("two mentions on one line are one site");
        keptForward.Start.ShouldBe(first.Start, "Add keeps the element the set already holds");
        keptForward.Length.ShouldBe(first.Length, "span and all");

        FragmentSite keptBackward = secondThenFirst.ShouldHaveSingleItem("whichever order they arrive in");
        keptBackward.Start.ShouldBe(second.Start, "so which span survives is which mention was added first");
        keptBackward.Length.ShouldBe(second.Length, "span and all");
    }

    [Fact]
    public void AHandBuiltSite_HasNoSpan()
    {
        var site = new FragmentSite("a.cs", 3);

        SourceLocation location = site.ToLocation();

        site.Length.ShouldBe(0, "a site nothing minted from syntax records no span");
        location.Span.ShouldBeNull("and the location it projects to carries none either");
    }

    [Fact]
    public void ASiteWithASpan_ProjectsItOntoTheLocation()
    {
        TextSpan span = SpanOf("Second");

        SourceLocation location = FragmentSite.Of(Tree(), span).ToLocation();

        location.Span.ShouldBe(
            new SourceSpan(span.Start, span.Length), "the span the site was minted from reaches the model unchanged");
    }

    private static SyntaxTree Tree()
    {
        return CSharpSyntaxTree.ParseText(Source, path: "Widget.cs");
    }

    private static TextSpan SpanOf(string text)
    {
        int start = Source.IndexOf(text, StringComparison.Ordinal);

        return new TextSpan(start, text.Length);
    }
}
