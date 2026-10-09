using Shouldly;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     That the case-sensitive string overloads are the ones a test file binds to. Deliberately declared
///     in a <em>nested</em> namespace: the whole cure rests on extension lookup reaching the suite's root
///     namespace before the compilation unit's <c>using Shouldly</c>, and only a file below the root can
///     demonstrate it. Moving <see cref="ShouldlyExtensions" /> or this file reds this.
/// </summary>
public sealed class ShouldlyExtensionsTests
{
    private const string Said = "the caller's message";

    [Fact]
    public void ShouldContain_WrongCase_Reds()
    {
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldContain("homecontroller"));
        "App.HomeController".ShouldContain("HomeController");
    }

    [Fact]
    public void ShouldContain_ExplicitInsensitive_FallsThroughToShouldly()
    {
        // The opt-out: naming a Case has no overload here, so the call binds to Shouldly's own.
        "App.HomeController".ShouldContain("homecontroller", Case.Insensitive);
    }

    [Fact]
    public void ShouldContain_WithMessage_WrongCase_Reds()
    {
        const string actual = "App.HomeController";

        Should.Throw<ShouldAssertException>(() => actual.ShouldContain("homecontroller", customMessage: Said))
            .Message
            .ShouldContain(Said);
    }

    [Fact]
    public void ShouldStartWithAndShouldEndWith_WrongCase_Red()
    {
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldStartWith("app."));
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldEndWith("controller"));
    }

    [Fact]
    public void ShouldStartWithAndShouldEndWith_WithMessage_WrongCase_Red()
    {
        // Passed by name, as the suite's call sites pass it. A name with no message twin here sends the
        // call to Shouldly's own overload, which compares case-insensitively and passes.
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldStartWith("app.", customMessage: Said))
            .Message
            .ShouldContain(Said);
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldEndWith("controller", customMessage: Said))
            .Message
            .ShouldContain(Said);
    }

    [Fact]
    public void ShouldNotContain_DifferingOnlyInCase_Passes()
    {
        // The mirror image, and the reason this half of the cure can never turn a green red.
        "App.HomeController".ShouldNotContain("homecontroller");
    }

    [Fact]
    public void ShouldNotContain_WithMessage_DifferingOnlyInCase_Passes()
    {
        "App.HomeController".ShouldNotContain("homecontroller", customMessage: Said);
    }

    [Fact]
    public void ShouldNotStartWithAndShouldNotEndWith_DifferingOnlyInCase_Pass()
    {
        "App.HomeController".ShouldNotStartWith("app.");
        "App.HomeController".ShouldNotEndWith("controller");
        "App.HomeController".ShouldNotStartWith("app.", customMessage: Said);
        "App.HomeController".ShouldNotEndWith("controller", customMessage: Said);
    }

    [Fact]
    public void ShouldNotStartWithAndShouldNotEndWith_WithMessage_ExactCase_Red()
    {
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldNotStartWith("App.", customMessage: Said))
            .Message
            .ShouldContain(Said);
        Should.Throw<ShouldAssertException>(() => "App.HomeController".ShouldNotEndWith("Controller", customMessage: Said))
            .Message
            .ShouldContain(Said);
    }
}
