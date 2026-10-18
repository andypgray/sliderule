using Shouldly;
using SlideRule.Internal;
using Xunit;

namespace SlideRule.Tests.Internal;

/// <summary>
///     The path glob behind <c>Under</c> (GRAMMAR §4.2): what <see cref="PathGlob.Validate" /> refuses and why,
///     and what a glob matches — a segment's <c>*</c>, a <c>**</c> over any number of segments, the prefix
///     reading that lets a directory stand for everything under it, and case ignored on every operating system.
/// </summary>
public sealed class PathGlobTests
{
    [Theory]
    [InlineData("src")]
    [InlineData("src/Widgets")]
    [InlineData("src/*/Internal")]
    [InlineData("**/Internal")]
    [InlineData("**/*.Designer.cs")]
    [InlineData("**")]
    [InlineData("vendor/FastExpressionCompiler.cs")]
    public void Validate_AWellFormedGlob_HasNoReason(string glob)
    {
        PathGlob.Validate(glob)
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("", "is blank")]
    [InlineData("  ", "is blank")]
    [InlineData("/src", "is rooted; a path glob is relative to the solution directory")]
    [InlineData("C:/src", "is rooted; a path glob is relative to the solution directory")]
    [InlineData(@"src\Widgets", "contains a backslash; separate segments with `/`")]
    [InlineData(@"\\server\share", "contains a backslash; separate segments with `/`")]
    [InlineData("./src", "has a `.` or `..` segment; name the directories from the solution directory down")]
    [InlineData("src/../lib", "has a `.` or `..` segment; name the directories from the solution directory down")]
    [InlineData("src//Widgets", "has an empty segment; write one `/` between segments and none at the end")]
    [InlineData("src/", "has an empty segment; write one `/` between segments and none at the end")]
    [InlineData("src/**Widgets", "puts `**` inside a segment; `**` stands alone between slashes")]
    [InlineData("***", "puts `**` inside a segment; `**` stands alone between slashes")]
    public void Validate_AShapeItRefuses_SaysWhy(string glob, string reason)
    {
        // Each shape is refused rather than normalized, so the model carries what the author wrote.
        PathGlob.Validate(glob)
            .ShouldBe(reason);
    }

    [Fact]
    public void Constructor_ARefusedGlob_Throws()
    {
        Should.Throw<ArgumentException>(() => new PathGlob("/src"))
            .ParamName.ShouldBe("glob");
    }

    [Theory]
    [InlineData("src/Widgets", "src/Widgets/Button.cs", true)]
    [InlineData("src/Widgets", "src/Widgets/Deep/Panel.cs", true)]
    [InlineData("src/Widgets", "src/WidgetsExtra/Button.cs", false)]
    [InlineData("src/Widgets", "src/Button.cs", false)]
    [InlineData("src/Widgets/**", "src/Widgets/Button.cs", true)]
    [InlineData("src/*/Internal", "src/Console/Internal/Ansi.cs", true)]
    [InlineData("src/*/Internal", "src/Console/Deep/Internal/Ansi.cs", false)]
    [InlineData("src/**/Internal", "src/Internal/Ansi.cs", true)]
    [InlineData("src/**/Internal", "src/Console/Deep/Internal/Ansi.cs", true)]
    [InlineData("**/Internal", "Internal/Ansi.cs", true)]
    [InlineData("**/*.Designer.cs", "src/Forms/Main.Designer.cs", true)]
    [InlineData("**/*.Designer.cs", "src/Forms/Main.cs", false)]
    [InlineData("*.cs", "Program.cs", true)]
    [InlineData("*.cs", "src/Program.cs", false)]
    [InlineData("SRC/widgets", "src/Widgets/Button.cs", true)]
    [InlineData("**", "anything/at/all.cs", true)]
    public void Matches_ReadsSegments_AndAPrefixStandsForEverythingUnderIt(string glob, string path, bool expected)
    {
        new PathGlob(glob).Matches(path.Split('/'))
            .ShouldBe(expected);
    }
}
