using System.Runtime.InteropServices;
using System.Text;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Windows 8.3 short names, for the tests that reach a directory through one. A volume generates them only
///     when its 8.3 setting allows it, so a test calling <see cref="ShortSpellingOrSkip" /> skips wherever the
///     volume under the run's temp root generates none.
/// </summary>
internal static class ShortPaths
{
    /// <summary>
    ///     <paramref name="path" />, an existing directory or file, spelled with the short name of its last segment,
    ///     or a skip of the calling test off Windows and on a volume that gave that segment no short name.
    /// </summary>
    public static string ShortSpellingOrSkip(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "8.3 short names exist only on Windows.");

        var buffer = new StringBuilder(1024);
        int length = GetShortPathName(path, buffer, buffer.Capacity);
        if (length == 0 || length > buffer.Capacity)
            throw new InvalidOperationException(
                $"GetShortPathName failed for {path} (error {Marshal.GetLastWin32Error()}).");

        string leaf = Path.GetFileName(path);
        string shortSpelling = Path.Join(Path.GetDirectoryName(path), Path.GetFileName(buffer.ToString()));
        if (string.Equals(Path.GetFileName(shortSpelling), leaf, StringComparison.OrdinalIgnoreCase))
            Assert.Skip($"The volume under {path} generates no 8.3 short names, so '{leaf}' has none.");

        return shortSpelling;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}
