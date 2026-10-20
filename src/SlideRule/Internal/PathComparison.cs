using System.Runtime.InteropServices;

namespace SlideRule.Internal;

/// <summary>
///     The one per-OS rule for comparing file-system path segments, so no two path compares drift apart:
///     case-insensitive on Windows and macOS, ordinal on Linux, the file-name reality of each platform.
/// </summary>
/// <remarks>
///     Core is netstandard2.0, so the platform test uses <see cref="RuntimeInformation.IsOSPlatform" />
///     rather than the net5+ <c>OperatingSystem.IsWindows()</c> helpers.
/// </remarks>
internal static class PathComparison
{
    private static readonly bool CaseInsensitive =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static readonly StringComparison Comparison =
        CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static readonly StringComparer Comparer =
        CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     <paramref name="path" /> folded by the same rule, lowercased where this OS ignores case: the form to
    ///     key on when a key must be a string rather than a comparer, such as a hashed cache identity.
    /// </summary>
    public static string Fold(string path)
    {
        return CaseInsensitive ? path.ToLowerInvariant() : path;
    }
}
