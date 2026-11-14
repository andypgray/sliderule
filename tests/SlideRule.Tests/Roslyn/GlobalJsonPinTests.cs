using Shouldly;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="GlobalJsonPin" />: the raw values a <c>global.json</c> pins, read from the nearest file, and a
///     file that cannot be read still named rather than thrown over, since the reader runs while a refusal is worded.
/// </summary>
public sealed class GlobalJsonPinTests
{
    [Fact]
    public void Nearest_APinBelowItsFile_ReadsEachSdkValueRaw()
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("global-json-pin");
        string file = temp.WriteFile(
            ["global.json"],
            """
            {
              // A comment and a trailing comma, both of which the SDK accepts.
              "sdk": {
                "version": "12.0.100-alpha.1.25001.1",
                "rollForward": "latestFeature",
                "paths": [ ".dotnet", "$host$" ],
                "errorMessage": "Run ./restore.sh to install it.",
              }
            }
            """);
        string below = temp.PathOf("src", "App");
        Directory.CreateDirectory(below);

        // Act
        GlobalJsonPin? pin = GlobalJsonPin.Nearest(below);

        // Assert
        pin.ShouldBe(new GlobalJsonPin(
            file, "12.0.100-alpha.1.25001.1", "latestFeature", HasPaths: true, "Run ./restore.sh to install it."));
    }

    [Theory]
    [InlineData("""{ "test": { "runner": "Microsoft.Testing.Platform" } }""")]
    [InlineData("""{ "sdk": { "version": 8 } }""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void Parse_NoStringValues_NamesTheFileAlone(string text)
    {
        GlobalJsonPin pin = GlobalJsonPin.Parse("/repo/global.json", text);

        pin.ShouldBe(new GlobalJsonPin("/repo/global.json", null, null, HasPaths: false, null));
    }

    [Fact]
    public void Nearest_NoGlobalJsonAtOrAbove_IsNull()
    {
        using TempDirectory temp = TestTempRoot.Fresh("global-json-pin");

        GlobalJsonPin.Nearest(temp.Path)
            .ShouldBeNull();
    }

    [Fact]
    public void Nearest_AFileThatCannotBeRead_NamesTheFileAlone()
    {
        // Arrange — an exclusive handle makes every other open of the file fail with an IOException.
        using TempDirectory temp = TestTempRoot.Fresh("global-json-pin");
        string file = temp.WriteFile(["global.json"], """{ "sdk": { "version": "99.0.100" } }""");
        using FileHold locked = FileHold.Exclusive(file);

        // Act
        GlobalJsonPin? pin = GlobalJsonPin.Nearest(temp.Path);

        // Assert
        pin.ShouldBe(new GlobalJsonPin(file, null, null, HasPaths: false, null));
    }
}
