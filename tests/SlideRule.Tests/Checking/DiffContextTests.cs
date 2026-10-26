using Shouldly;
using SlideRule.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     <see cref="DiffContext" /> path plumbing (GRAMMAR §7): forward-slash normalization,
///     separator-insensitive membership, per-OS case sensitivity, and solution-relative rendering —
///     pure string logic (no <c>Path.GetRelativePath</c>, netstandard2.0 discipline).
/// </summary>
public sealed class DiffContextTests
{
    private static DiffContext Make(params string[] changed)
    {
        return new DiffContext(@"C:\repo\sln", changed);
    }

    [Fact]
    public void Contains_MatchesRegardlessOfSeparator()
    {
        DiffContext diff = Make(@"C:\repo\sln\App\Foo.cs");

        diff.Contains(@"C:\repo\sln\App\Foo.cs")
            .ShouldBeTrue();
        diff.Contains("C:/repo/sln/App/Foo.cs")
            .ShouldBeTrue();
    }

    [Fact]
    public void Contains_OnCaseInsensitiveFileSystem_MatchesRegardlessOfCase()
    {
        FileNameCase.SkipUnlessIgnored();
        Make(@"C:\repo\sln\App\Foo.cs")
            .Contains("c:/REPO/sln/app/foo.cs")
            .ShouldBeTrue();
    }

    [Fact]
    public void Contains_OnCaseSensitiveFileSystem_DoesNotMatchCaseVariant()
    {
        FileNameCase.SkipUnlessCompared();
        Make(@"C:\repo\sln\App\Foo.cs")
            .Contains("c:/REPO/sln/app/foo.cs")
            .ShouldBeFalse();
    }

    [Fact]
    public void Contains_UnchangedFile_IsFalse()
    {
        Make(@"C:\repo\sln\App\Foo.cs")
            .Contains(@"C:\repo\sln\App\Bar.cs")
            .ShouldBeFalse();
    }

    [Fact]
    public void SolutionRelative_StripsTheSolutionPrefix()
    {
        Make()
            .SolutionRelative(@"C:\repo\sln\App\Foo.cs")
            .ShouldBe("App/Foo.cs");
    }

    [Fact]
    public void SolutionRelative_OutsideTheSolution_ReturnsNormalizedPathUnchanged()
    {
        Make()
            .SolutionRelative(@"D:\other\Baz.cs")
            .ShouldBe("D:/other/Baz.cs");
    }

    [Fact]
    public void SolutionRelative_ASolutionDirectoryWithATrailingSeparator_StillStripsThePrefix()
    {
        new DiffContext(@"C:\repo\sln\", [])
            .SolutionRelative(@"C:\repo\sln\App\Foo.cs")
            .ShouldBe("App/Foo.cs");
    }
}
