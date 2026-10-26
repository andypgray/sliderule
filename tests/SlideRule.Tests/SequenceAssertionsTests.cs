using Shouldly;
using Xunit;

namespace SlideRule.Tests;

/// <summary>
///     The cases for <see cref="SequenceAssertions" />: the one match comes back, and anything but one reds naming
///     what was sought and every item there was.
/// </summary>
public sealed class SequenceAssertionsTests
{
    private static readonly string[] Projects = ["App.Domain", "App.Web", "App.Web"];

    [Fact]
    public void ShouldHaveSingle_OneMatch_ReturnsIt()
    {
        Projects.ShouldHaveSingle(project => project == "App.Domain")
            .ShouldBe("App.Domain");
    }

    [Theory]
    [InlineData("App.Data")]
    [InlineData("App.Web")]
    public void ShouldHaveSingle_NoneOrSeveralMatch_RedsWithThePredicateAndEveryItem(string wanted)
    {
        var red = Should.Throw<ShouldAssertException>(() => Projects.ShouldHaveSingle(project => project == wanted));

        red.Message.ShouldContain(
            "Looking for the one item where project => project == wanted; there are: App.Domain, App.Web, App.Web.");
    }

    [Fact]
    public void ShouldHaveSingle_NoMatch_QuotesTheCallersSequence()
    {
        string[] catalog = ["App.Domain"];

        var red = Should.Throw<ShouldAssertException>(() =>
            catalog.ShouldHaveSingle(project => project == "App.Data"));

        red.Message.ShouldContain("catalog");
    }

    [Fact]
    public void ShouldHaveSingle_EmptySequence_SaysThereIsNothing()
    {
        string[] none = [];

        var red = Should.Throw<ShouldAssertException>(() => none.ShouldHaveSingle(project => project == "App.Web"));

        red.Message.ShouldContain("there are: (none).");
    }

    [Fact]
    public void ShouldHaveSingle_ItemsWithADescription_AreNamedByIt()
    {
        Opaque[] items = [new("a"), new("b")];

        var red = Should.Throw<ShouldAssertException>(() => items.ShouldHaveSingle(item => item.Name == "c", item => item.Name));

        red.Message.ShouldContain("there are: a, b.");
    }

    // A type whose ToString names only the type, as most of the product's model nodes are.
    private sealed class Opaque(string name)
    {
        public string Name => name;
    }
}
