using System.Text.Json.Nodes;
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

    /// <summary>
    ///     Deletes the solution's fragment cache and leaves the capture, so the next run without <c>--binlog</c>
    ///     replays the capture rather than answering from cached fragments.
    /// </summary>
    public static void DropFragmentCache(string solutionPath, string cacheRoot)
    {
        File.Delete(CacheLocations.CacheFilePath(solutionPath, cacheRoot));
    }

    /// <summary>The path of every structural stamp the manifest records, in its order.</summary>
    public static IReadOnlyList<string> StructuralStampPaths(string solutionPath, string cacheRoot)
    {
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(Manifest(solutionPath, cacheRoot)))!;
        return root["StructuralStamps"]!.AsArray()
            .Select(stamp => stamp!["Path"]!.GetValue<string>())
            .ToList();
    }

    /// <summary>Rewrites the manifest through <paramref name="edit" />, which changes its JSON object in place.</summary>
    public static void EditManifest(string solutionPath, string cacheRoot, Action<JsonObject> edit)
    {
        string path = Manifest(solutionPath, cacheRoot);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        edit(root);
        File.WriteAllText(path, root.ToJsonString());
    }
}
