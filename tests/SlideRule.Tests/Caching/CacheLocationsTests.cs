using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     <see cref="CacheLocations" /> path derivation: the default cache root is
///     <c>%LOCALAPPDATA%/SlideRule/cache</c>, and a null override falls back to it — so a derived
///     cache-file path for any solution roots there and is named <c>cache.json</c>.
/// </summary>
/// <remarks>
///     The type reads no environment variable itself (the override parameter is the only seam), so these
///     facts hold whatever <c>SLIDERULE_CACHE_DIR</c> is set to on the test host. One row sets
///     <c>XDG_DATA_HOME</c> for the process, which is why the class runs in the serial collection.
/// </remarks>
[Collection("Serial")]
public sealed class CacheLocationsTests
{
    [Fact]
    public void DefaultCacheRoot_IsLocalAppDataUnderProductCacheFolder()
    {
        string expected = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "SlideRule",
            "cache");

        CacheLocations.DefaultCacheRoot()
            .ShouldBe(expected);
    }

    [Fact]
    public void ProductDataRoot_DataFolderMissing_StaysAbsolute()
    {
        // On Linux the local application data folder is $XDG_DATA_HOME, and .NET's default lookup returns an
        // empty string when that folder does not exist, which left the cache and the log relative to the
        // working directory. Windows reads no XDG_DATA_HOME, so only a Linux leg can reach the case.
        Assert.SkipUnless(OperatingSystem.IsLinux(), "XDG_DATA_HOME names the data folder on Linux only.");
        var missing = $"/lb-missing-{Guid.NewGuid():N}";
        using var dataHome = new ScopedEnvironmentVariable("XDG_DATA_HOME", missing);

        string cacheRoot = CacheLocations.DefaultCacheRoot();
        string logDirectory = SerilogConfiguration.LogDirectory;

        cacheRoot.ShouldBe($"{missing}/SlideRule/cache");
        logDirectory.ShouldBe($"{missing}/SlideRule/logs");
        Path.IsPathRooted(cacheRoot)
            .ShouldBeTrue();
        Path.IsPathRooted(logDirectory)
            .ShouldBeTrue();
    }

    [Fact]
    public void CacheFilePath_NullOverride_RootsUnderDefaultCacheRoot()
    {
        // A null/blank override falls back to DefaultCacheRoot; the per-solution cache file then lives beneath
        // it (in a <sln-name>-<hash> subdirectory), named cache.json. Using a nonexistent temp path is safe —
        // PathCanonicalizer.Resolve falls back to GetFullPath when the leaf does not exist.
        string solutionPath = Path.Combine(Path.GetTempPath(), "Some.Solution.sln");

        string path = CacheLocations.CacheFilePath(solutionPath, null);

        path.ShouldStartWith(CacheLocations.DefaultCacheRoot());
        Path.GetFileName(path)
            .ShouldBe("cache.json");
    }
}
