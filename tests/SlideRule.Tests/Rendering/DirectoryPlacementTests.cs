using Shouldly;
using Xunit;
using SlideRule.Rendering;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The collapse a card's directory comes from: the deepest directory holding every file handed in, spelled
///     the way the first file spells it. It is string arithmetic over both separators and never touches the file
///     system, so the drive, share and POSIX rows hold on every platform. Only the case rows are per-OS, because
///     whether two spellings name one directory is the platform's answer.
/// </summary>
public sealed class DirectoryPlacementTests
{
    [Fact]
    public void DeepestCommonDirectory_FilesInOneDirectory_KeepTheRootAndSeparators()
    {
        DirectoryPlacement.DeepestCommonDirectory([@"C:\repo\MyApp.Legacy.Billing\Calc.cs", @"C:\repo\MyApp.Legacy.Billing\Facade.cs"])
            .ShouldBe(@"C:\repo\MyApp.Legacy.Billing");
    }

    [Theory]
    [InlineData(@"C:\a\One.cs", @"C:\b\Two.cs", @"C:\")]
    [InlineData("C:/a/One.cs", "C:/b/Two.cs", "C:/")]
    public void DeepestCommonDirectory_SiblingsUnderADriveRoot_RebuildTheDriveRoot(string first, string second, string expected)
    {
        // A bare "C:" would be drive-relative: it names the working directory on that drive, not its root.
        DirectoryPlacement.DeepestCommonDirectory([first, second])
            .ShouldBe(expected);
    }

    [Fact]
    public void DeepestCommonDirectory_SiblingsUnderThePosixRoot_RebuildTheRoot()
    {
        DirectoryPlacement.DeepestCommonDirectory(["/a/One.cs", "/b/Two.cs"])
            .ShouldBe("/");
    }

    [Fact]
    public void DeepestCommonDirectory_FilesOnOneShare_KeepTheShare()
    {
        DirectoryPlacement.DeepestCommonDirectory([@"\\server\share\a\One.cs", @"\\server\share\b\Two.cs"])
            .ShouldBe(@"\\server\share");
    }

    [Theory]
    [InlineData(@"C:\a\One.cs", @"D:\b\Two.cs")] // two drives
    [InlineData(@"\\one\share\a\One.cs", @"\\two\share\b\Two.cs")] // two servers
    [InlineData("a/One.cs", "b/Two.cs")] // two relative trees
    public void DeepestCommonDirectory_NoSharedRoot_ReturnsNull(string first, string second)
    {
        DirectoryPlacement.DeepestCommonDirectory([first, second])
            .ShouldBeNull();
    }

    [Fact]
    public void DeepestCommonDirectory_TwoCasings_MergeWhereThePlatformIgnoresCase()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "Compares file names by case.");

        DirectoryPlacement.DeepestCommonDirectory(["/Repo/Src/One.cs", "/repo/src/Two.cs"])
            .ShouldBe("/Repo/Src");
    }

    [Fact]
    public void DeepestCommonDirectory_TwoCasings_StayApartWhereThePlatformComparesCase()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), "Ignores case in file names.");

        DirectoryPlacement.DeepestCommonDirectory(["/Repo/Src/One.cs", "/repo/src/Two.cs"])
            .ShouldBe("/");
    }
}
