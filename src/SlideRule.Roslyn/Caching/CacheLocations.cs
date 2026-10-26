using SlideRule.Internal;

namespace SlideRule.Roslyn.Caching;

/// <summary>
///     Derives the on-disk location of a solution's persisted extraction cache. The default root is
///     <c>%LOCALAPPDATA%/SlideRule/cache</c>, and each solution gets its own subdirectory named
///     <c>&lt;sln-name&gt;-&lt;sha256-prefix-of-canonical-path&gt;</c> holding independent, disposable local
///     derived data keyed to that one solution.
/// </summary>
/// <remarks>
///     The root override is a parameter, so <c>SLIDERULE_CACHE_DIR</c> arrives already resolved and this type
///     reads no environment variable itself. The path identity reuses <see cref="PathCanonicalizer" />
///     (symlink-resolved, fully-qualified), lowercased before hashing on a case-insensitive file system, so the
///     same solution opened through a symlinked root or a case-variant spelling maps to one cache directory.
/// </remarks>
internal static class CacheLocations
{
    private const string ProductFolder = "SlideRule";
    private const string CacheFolder = "cache";
    private const string CacheFileName = "cache.json";
    private const string CaptureManifestFileName = "capture.json";
    private const string CaptureBinlogFileName = "capture.binlog";

    // 16 hex chars (64 bits) of the canonical-path SHA-256 — ample to keep two distinct solutions from
    // colliding while keeping the directory name short. The <sln-name> prefix is for human readability only.
    private const int PathHashHexLength = 16;

    /// <summary>
    ///     The product's data base, <c>%LOCALAPPDATA%/SlideRule</c>: absolute even when the local application
    ///     data folder does not exist yet, and never created here.
    /// </summary>
    // DoNotVerify because the default option returns an empty string for a missing folder, which Path.Combine
    // turns into a path relative to the working directory; on Linux that is any XDG_DATA_HOME naming a folder
    // not yet made. Every writer under this base creates its own directory, so a getter has no reason to create it.
    internal static string ProductDataRoot()
    {
        string localApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        return Path.Combine(localApplicationData, ProductFolder);
    }

    /// <summary>The default cache root — <c>%LOCALAPPDATA%/SlideRule/cache</c>.</summary>
    internal static string DefaultCacheRoot()
    {
        return Path.Combine(ProductDataRoot(), CacheFolder);
    }

    /// <summary>
    ///     The cache directory for <paramref name="solutionPath" /> under
    ///     <paramref name="cacheRootOverride" /> (or the default root when null/blank).
    /// </summary>
    internal static string CacheDirectory(string solutionPath, string? cacheRootOverride)
    {
        string root = string.IsNullOrWhiteSpace(cacheRootOverride) ? DefaultCacheRoot() : cacheRootOverride;
        string canonical = PathCanonicalizer.Resolve(solutionPath);
        string identityKey = PathComparison.Fold(canonical);
        string hashHex = FileStamping.HashText(identityKey);
        string name = Path.GetFileNameWithoutExtension(canonical);
        return Path.Combine(root, $"{name}-{hashHex[..PathHashHexLength]}");
    }

    /// <summary>The absolute path to the single atomic cache file for <paramref name="solutionPath" />.</summary>
    internal static string CacheFilePath(string solutionPath, string? cacheRootOverride)
    {
        return Path.Combine(CacheDirectory(solutionPath, cacheRootOverride), CacheFileName);
    }

    /// <summary>
    ///     The build-capture manifest (<c>capture.json</c>) inside a <paramref name="cacheDirectory" /> derived by
    ///     <see cref="CacheDirectory" />: the structure-only-keyed sidecar beside <see cref="CacheFilePath" />.
    ///     The directory comes in already derived because deriving it costs a symlink resolve, a fold and a
    ///     SHA-256.
    /// </summary>
    internal static string CaptureManifestPathIn(string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, CaptureManifestFileName);
    }

    /// <summary>
    ///     The captured binlog copy (<c>capture.binlog</c>) inside an already-derived
    ///     <paramref name="cacheDirectory" />, beside its <see cref="CaptureManifestPathIn" /> manifest.
    /// </summary>
    internal static string CaptureBinlogPathIn(string cacheDirectory)
    {
        return Path.Combine(cacheDirectory, CaptureBinlogFileName);
    }
}
