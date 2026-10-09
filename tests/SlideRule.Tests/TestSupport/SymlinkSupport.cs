using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Shared helper for the symlink tests. Creates a directory or file symlink, or skips the calling test (via
///     <see cref="Assert.Skip" />) when the host cannot: Windows without Developer Mode or elevation throws on
///     <see cref="Directory.CreateSymbolicLink" /> and <see cref="File.CreateSymbolicLink" />, so those runners
///     skip while Linux and macOS CI — the platforms whose symlinked roots (macOS <c>/var</c>) motivated the
///     canonicalization fix — exercise the real symlink path.
/// </summary>
internal static class SymlinkSupport
{
    /// <summary>Creates a directory symlink at <paramref name="linkPath" /> targeting <paramref name="target" />, or skips.</summary>
    public static void CreateDirectorySymlink(string linkPath, string target)
    {
        CreateOrSkip(() => Directory.CreateSymbolicLink(linkPath, target));
    }

    /// <summary>Creates a file symlink at <paramref name="linkPath" /> targeting <paramref name="target" />, or skips.</summary>
    public static void CreateFileSymlink(string linkPath, string target)
    {
        CreateOrSkip(() => File.CreateSymbolicLink(linkPath, target));
    }

    private static void CreateOrSkip(Action create)
    {
        try
        {
            create();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip(
                $"Cannot create a symlink on this host ({ex.GetType().Name}: {ex.Message}); " +
                "needs Developer Mode or elevation on Windows.");
        }
    }
}
