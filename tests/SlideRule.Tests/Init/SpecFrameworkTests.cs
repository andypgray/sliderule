using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary>
///     <see cref="SpecFramework" />: the framework <c>init</c> writes from what the product projects target and the
///     SDK a <c>global.json</c> pins.
/// </summary>
public sealed class SpecFrameworkTests
{
    [Theory]
    [InlineData(new[] { "net8.0", "net48" }, null, "net10.0")]
    [InlineData(new[] { "net8.0" }, 9, "net9.0")]
    [InlineData(new[] { "net8.0" }, 10, "net10.0")]
    [InlineData(new[] { "netstandard2.0" }, null, "net10.0")]
    [InlineData(new[] { "netstandard2.0" }, 8, "net8.0")]
    [InlineData(new[] { "net472", "net48" }, null, "net48")]
    [InlineData(new[] { "net472", "net48" }, 8, "net48")]
    [InlineData(new string[0], null, "net10.0")]
    public void Choose_PicksTheFrameworkTheEstateAndThePinAllow(string[] products, int? pin, string expected)
    {
        string framework = SpecFramework.Choose(products, pin, "global.json");

        framework.ShouldBe(expected);
    }

    [Fact]
    public void Choose_UnderAPinBelowSdk6_RefusesNamingThePinAndTheOverride()
    {
        var refusal = Should.Throw<UserErrorException>(() => SpecFramework.Choose(["net5.0"], 5, "C:/repo/global.json"));

        refusal.Message.ShouldBe(
            "C:/repo/global.json pins SDK 5, which cannot compile a spec class (it needs C# 10, SDK 6 or later). "
            + "Pass --framework with a framework your SDK can build, or run init outside the pin.");
    }

    [Theory]
    [InlineData("""{ "sdk": { "version": "8.0.400" } }""", 8)]
    [InlineData("""{ "sdk": { "version": "9.0.100", "rollForward": "latestFeature" } }""", 9)]
    [InlineData("""{ "sdk": { "version": "8.0.400", "rollForward": "latestMajor" } }""", null)]
    [InlineData("""{ "sdk": { "version": "8.0.400", "rollForward": "major" } }""", null)]
    [InlineData("""{ /* a comment */ "sdk": { "version": "9.0.100", }, }""", 9)]
    [InlineData("""{ "test": { "runner": "Microsoft.Testing.Platform" } }""", null)]
    [InlineData("""{ "sdk": { "version": 8 } }""", null)]
    [InlineData("not json", null)]
    public void SdkPin_ReadsTheNearestGlobalJson(string globalJson, int? expected)
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("init-sdk-pin");
        string pinned = temp.WriteFile(["global.json"], globalJson);
        string below = temp.PathOf("src", "App");
        Directory.CreateDirectory(below);

        // Act
        (int? major, string? file) = SpecFramework.SdkPin(below);

        // Assert
        major.ShouldBe(expected);
        file.ShouldBe(pinned);
    }
}
