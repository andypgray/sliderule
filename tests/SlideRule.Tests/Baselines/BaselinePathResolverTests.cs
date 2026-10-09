using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Tests.ModelFile;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     Pins the pure-string path arithmetic equal to <c>Path.GetFullPath(path, baseDirectory)</c>, the
///     oracle it stands in for, over every baseline path the real specs carry and over a table of the
///     shapes they do not. Where it deliberately answers differently, that input is a fact of its own
///     saying so.
/// </summary>
/// <remarks>
///     The oracle is OS-shaped — <c>GetFullPath</c> splits on <c>\</c> and treats a drive letter as a
///     root only on Windows — so only the portable vectors run everywhere: the Windows theory skips off
///     Windows, and a POSIX theory carries the shapes POSIX reads its own way, a leading <c>/</c> as an
///     absolute path and a <c>\</c> as part of a name. CI runs all three platforms, so every vector is
///     reached.
/// </remarks>
public sealed class BaselinePathResolverTests
{
    private static readonly string SolutionDirectory = OperatingSystem.IsWindows() ? @"C:\sln" : "/sln";

    [Theory]
    [InlineData("x.json")]
    [InlineData("./x.json")]
    [InlineData("a/b/../c.json")]
    [InlineData("../x.json")]
    [InlineData("a//b.json")]
    [InlineData("a/./b/./c.json")]
    [InlineData("../../../../x.json")] // climbs past the base's own depth
    [InlineData("a/b/..")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("")]
    [InlineData("arch/baselines/data-access/no-inline-sql.json")]
    public void Resolve_APortablePath_MatchesGetFullPath(string baselinePath)
    {
        BaselinePathResolver.Resolve(baselinePath, SolutionDirectory)
            .ShouldBe(Path.GetFullPath(baselinePath, SolutionDirectory));
    }

    [Theory]
    [InlineData(".\\x.json")]
    [InlineData("a\\b\\..\\c.json")]
    [InlineData("a\\\\b.json")]
    [InlineData("a/b\\c.json")] // mixed separators
    [InlineData("a/b /c.json")] // a trailing space inside a path is kept by both
    [InlineData("C:\\abs\\x.json")]
    [InlineData("C:/abs/x.json")]
    [InlineData("/x.json")] // root-relative: takes the base directory's root
    [InlineData("\\x.json")]
    [InlineData("\\\\server\\share\\x.json")]
    [InlineData("\\\\server\\share\\..\\..\\x.json")] // a `..` at a UNC root stays there
    public void Resolve_AWindowsPath_MatchesGetFullPath(string baselinePath)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows separator and drive-letter vectors.");

        BaselinePathResolver.Resolve(baselinePath, SolutionDirectory)
            .ShouldBe(Path.GetFullPath(baselinePath, SolutionDirectory));
    }

    [Theory]
    [InlineData("/abs/x.json")]
    [InlineData("a\\b.json")] // a backslash is an ordinary character in a file name here
    public void Resolve_APosixPath_MatchesGetFullPath(string baselinePath)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX rooted-path vectors.");

        BaselinePathResolver.Resolve(baselinePath, SolutionDirectory)
            .ShouldBe(Path.GetFullPath(baselinePath, SolutionDirectory));
    }

    [Theory]
    [InlineData("C:\\sln\\")]
    [InlineData("C:/sln")]
    [InlineData("C:/sln/")]
    [InlineData("C:\\sln\\sub")]
    public void Resolve_ABaseDirectorySpelledAnotherWay_MatchesGetFullPath(string baseDirectory)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows separator and drive-letter vectors.");

        foreach (string baselinePath in new[] { "x.json", "../x.json", "/x.json", "a/b/../c.json" })
            BaselinePathResolver.Resolve(baselinePath, baseDirectory)
                .ShouldBe(Path.GetFullPath(baselinePath, baseDirectory), $"baseline path '{baselinePath}'");
    }

    [Fact]
    public void Resolve_EveryBaselinePathTheCorpusSpecsCarry_MatchesGetFullPath()
    {
        IReadOnlyList<string> paths = CorpusBaselinePaths();

        paths.ShouldNotBeEmpty();
        // This repository's own quarantine containment, which is the default convention's shape: a
        // relative path four directories deep, which no single-segment table row reaches.
        paths.ShouldContain("arch/baselines/roslyn/msbuild-bootstrap/containment.json");

        foreach (string path in paths)
            BaselinePathResolver.Resolve(path, SolutionDirectory)
                .ShouldBe(Path.GetFullPath(path, SolutionDirectory), $"baseline path '{path}'");
    }

    [Fact]
    public void Resolve_APathWithATrailingSeparator_DropsItWhereGetFullPathKeepsIt()
    {
        // A resolved baseline path names a file, and a name is the same name with or without a separator
        // after it — so the two spellings have to fold to one, which is what lets either side of a
        // comparison be normalized rather than special-cased.
        string expected = Combine(SolutionDirectory, "a", "b");

        BaselinePathResolver.Resolve("a/b/", SolutionDirectory)
            .ShouldBe(expected);
        Path.GetFullPath("a/b/", SolutionDirectory)
            .ShouldBe(expected + Path.DirectorySeparatorChar);
    }

    [Fact]
    public void Resolve_ADriveRelativePath_TakesTheDriveAsItsRootWhereGetFullPathReadsPerDriveState()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "A drive-relative path is a Windows shape.");

        // `C:x` means "x under that drive's current directory", which is process state a reader running
        // inside the compiler has none of. The deterministic reading is the drive as a root.
        BaselinePathResolver.Resolve("C:x.json", @"C:\sln")
            .ShouldBe(@"C:\x.json");
        Path.GetFullPath("C:x.json", @"C:\sln")
            .ShouldBe(@"C:\sln\x.json");
    }

    [Theory]
    [InlineData("a/b./c.json", @"C:\sln\a\b.\c.json", @"C:\sln\a\b\c.json")]
    [InlineData("a/b.", @"C:\sln\a\b.", @"C:\sln\a\b")]
    [InlineData("a/b ", @"C:\sln\a\b ", @"C:\sln\a\b")]
    public void Resolve_ASegmentEndingInADotOrASpace_KeepsItWhereWin32TrimsIt(
        string baselinePath, string resolved, string fullPath)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Win32 trims a segment's trailing dots and spaces.");

        // Trimming is a file-system rule, not a path-arithmetic one, and a reader that applied it would
        // be answering for a machine it is not running on. Win32 is not uniform about it either: a
        // trailing space is trimmed off the last segment and kept in the middle of a path, which is the
        // third row and the equality row above it.
        BaselinePathResolver.Resolve(baselinePath, @"C:\sln")
            .ShouldBe(resolved);
        Path.GetFullPath(baselinePath, @"C:\sln")
            .ShouldBe(fullPath);
    }

    [Fact]
    public void Resolve_TextGetFullPathRefuses_StillAnswers()
    {
        // Nothing a file can say is an exception here: the spec author's text is content, and a reader
        // that threw on it would take down the compilation it is running inside.
        const string hostile = "a\0b.json";

        Should.Throw<ArgumentException>(() => Path.GetFullPath(hostile, SolutionDirectory));

        BaselinePathResolver.Resolve(hostile, SolutionDirectory)
            .ShouldBe(Combine(SolutionDirectory, hostile));
    }

    [Theory]
    [InlineData("C:\\sln\\a\\..\\b.json", "C:\\sln\\b.json")]
    [InlineData("C:/sln/a/b.json", "C:\\sln\\a\\b.json")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("C:\\sln\\", "C:\\sln")]
    [InlineData("\\\\server\\share\\a\\..\\b", "\\\\server\\share\\b")]
    [InlineData("\\\\server\\share", "\\\\server\\share")]
    [InlineData("\\\\server\\share\\..\\..\\..\\x", "\\\\server\\share\\x")]
    public void Normalize_AWindowsPath_FoldsItAndLeavesNoTrailingSeparatorButOnABareRoot(
        string absolutePath, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows separator and drive-letter vectors.");

        BaselinePathResolver.Normalize(absolutePath)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("/sln/a/../b.json", "/sln/b.json")]
    [InlineData("/sln//a/./b.json", "/sln/a/b.json")]
    [InlineData("/", "/")]
    [InlineData("/sln/", "/sln")]
    [InlineData("/../../x", "/x")]
    public void Normalize_APosixPath_FoldsItAndLeavesNoTrailingSeparatorButOnABareRoot(
        string absolutePath, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX rooted-path vectors.");

        BaselinePathResolver.Normalize(absolutePath)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("C:\\sln\\arch\\b.json", "C:\\sln\\arch")]
    [InlineData("C:/sln/arch/b.json", "C:\\sln\\arch")]
    [InlineData("C:\\sln\\a\\..\\b.json", "C:\\sln")]
    [InlineData("C:\\b.json", "C:\\")]
    [InlineData("b.json", "")]
    public void DirectoryOf_AWindowsFilePath_DropsItsLastSegment(string filePath, string expected)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows separator and drive-letter vectors.");

        BaselinePathResolver.DirectoryOf(filePath)
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("/sln/arch/b.json", "/sln/arch")]
    [InlineData("/sln/a/../b.json", "/sln")]
    [InlineData("/b.json", "/")]
    [InlineData("b.json", "")]
    public void DirectoryOf_APosixFilePath_DropsItsLastSegment(string filePath, string expected)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "POSIX rooted-path vectors.");

        BaselinePathResolver.DirectoryOf(filePath)
            .ShouldBe(expected);
    }

    [Fact]
    public void Resolve_AgainstTheDirectoryOfTheFileThatNamedIt_IsTheFileBesideIt()
    {
        // The whole shape the compiler-hosted reader needs: it holds a file's path and the relative path
        // written inside it, and neither a current directory nor a BCL call that would consult one.
        string modelFile = Combine(SolutionDirectory, "arch", "sliderule.model.json");

        string resolved = BaselinePathResolver.Resolve(
            "../arch/baselines/data-access/no-inline-sql.json", BaselinePathResolver.DirectoryOf(modelFile));

        resolved.ShouldBe(Combine(SolutionDirectory, "arch", "baselines", "data-access", "no-inline-sql.json"));
    }

    [Fact]
    public void ANullArgument_ThrowsBecauseThatIsACallerBug()
    {
        Should.Throw<ArgumentNullException>(() => BaselinePathResolver.Resolve(null!, SolutionDirectory))
            .ParamName.ShouldBe("baselinePath");
        Should.Throw<ArgumentNullException>(() => BaselinePathResolver.Resolve("x.json", null!))
            .ParamName.ShouldBe("baseDirectory");
        Should.Throw<ArgumentNullException>(() => BaselinePathResolver.Normalize(null!))
            .ParamName.ShouldBe("absolutePath");
        Should.Throw<ArgumentNullException>(() => BaselinePathResolver.DirectoryOf(null!))
            .ParamName.ShouldBe("filePath");
    }

    // Every baseline path the real specs carry — this repository's own and each fixture spec the model
    // corpus already loads — deduplicated and in a stable order.
    private static IReadOnlyList<string> CorpusBaselinePaths()
    {
        IEnumerable<ArchRule> rules = ModelFileCorpus.All.SelectMany(spec => spec.Model.Rules);
        return rules
            .Where(rule => rule.IsRatcheted)
            .Select(rule => rule.BaselinePath!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    private static string Combine(params string[] parts)
    {
        return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
    }
}
