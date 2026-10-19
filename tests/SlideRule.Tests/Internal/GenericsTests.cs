using Shouldly;
using SlideRule.Internal;
using Xunit;

namespace SlideRule.Tests.Internal;

/// <summary>
///     The generic-definition helper (<see cref="Generics" />) that normalizes a constructed generic anchor to
///     its definition (GRAMMAR §4.1/§4.5).
/// </summary>
public class GenericsTests
{
    [Fact]
    public void Definition_ConstructedGeneric_ReturnsOpenDefinition()
    {
        Generics.Definition(typeof(Task<int>))
            .ShouldBe(typeof(Task<>));
        Generics.Definition(typeof(Dictionary<string, int>))
            .ShouldBe(typeof(Dictionary<,>));

        Generics.Definition(typeof(Task<>))
            .ShouldBe(typeof(Task<>));
        Generics.Definition(typeof(string))
            .ShouldBe(typeof(string));
    }

    [Fact]
    public void IsConstructed_DistinguishesClosedFromOpenAndNonGeneric()
    {
        Generics.IsConstructed(typeof(Task<int>))
            .ShouldBeTrue();
        Generics.IsConstructed(typeof(Dictionary<string, int>))
            .ShouldBeTrue();

        Generics.IsConstructed(typeof(Task<>))
            .ShouldBeFalse();
        Generics.IsConstructed(typeof(string))
            .ShouldBeFalse();
        Generics.IsConstructed(typeof(int))
            .ShouldBeFalse();
    }
}
