using SlideRule.Roslyn.Caching;

namespace SlideRule.Tests.Replay;

/// <summary>
///     The build-capture files of a solution under a cache root, at the paths the capture store derives for
///     them: the manifest (<c>capture.json</c>) and the binlog copy beside it (<c>capture.binlog</c>).
/// </summary>
internal static class CaptureFiles
{
    public static string Manifest(string solutionPath, string cacheRoot)
    {
        string cacheDirectory = CacheLocations.CacheDirectory(solutionPath, cacheRoot);
        return CacheLocations.CaptureManifestPathIn(cacheDirectory);
    }

    public static string Binlog(string solutionPath, string cacheRoot)
    {
        string cacheDirectory = CacheLocations.CacheDirectory(solutionPath, cacheRoot);
        return CacheLocations.CaptureBinlogPathIn(cacheDirectory);
    }
}
