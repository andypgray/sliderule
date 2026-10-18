using Shouldly;
using SlideRule.Internal;
using Xunit;

namespace SlideRule.Tests.Internal;

/// <summary>
///     The root path globs are read against (GRAMMAR §4.2): a declaring file's path turned into the segments
///     below the solution directory, by text alone, whichever separators either side was written with.
/// </summary>
public sealed class SolutionRootTests
{
    [Theory]
    [InlineData(@"C:\repo\App", @"C:\repo\App\src\Widgets\Button.cs", "src/Widgets/Button.cs")]
    [InlineData(@"C:\repo\App", "C:/repo/App/src/Widgets/Button.cs", "src/Widgets/Button.cs")]
    [InlineData("C:/repo/App/", @"C:\repo\App\src\Button.cs", "src/Button.cs")]
    [InlineData(@"C:\repo\App", @"c:\REPO\app\src\Button.cs", "src/Button.cs")]
    [InlineData(@"\\server\share\App", @"\\server\share\App\src\Button.cs", "src/Button.cs")]
    [InlineData("/home/dev/App", "/home/dev/App/src/Button.cs", "src/Button.cs")]
    [InlineData(@"C:\repo\App", @"C:\repo\App\src\.\Widgets\..\Button.cs", "src/Button.cs")]
    [InlineData(@"C:\repo\App\build\..", @"C:\repo\App\src\Button.cs", "src/Button.cs")]
    public void Relative_AFileBelowTheRoot_IsItsSegmentsBelowIt(string root, string file, string expected)
    {
        new SolutionRoot(root).Relative(file)
            .ShouldBe(expected.Split('/'));
    }

    [Theory]
    [InlineData(@"C:\repo\App", @"C:\repo\AppExtra\src\Button.cs")]
    [InlineData(@"C:\repo\App", @"C:\repo\Shared\Button.cs")]
    [InlineData(@"C:\repo\App", @"D:\repo\App\src\Button.cs")]
    [InlineData(@"C:\repo\App", @"C:\repo\App")]
    [InlineData(@"C:\repo\App", @"C:\repo\App\..\Shared\Button.cs")]
    [InlineData(@"C:\repo\App", "")]
    public void Relative_AFileNotBelowTheRoot_IsNull(string root, string file)
    {
        // A sibling sharing the root's name as a prefix is outside it, segment by segment.
        new SolutionRoot(root).Relative(file)
            .ShouldBeNull();
    }
}
