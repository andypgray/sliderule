using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Whether this host's file system ignores case in file names: on Windows and macOS, and not on Linux.
///     Restated rather than read off the product's own path rule, so a row that branches on it holds the product
///     to the platform rather than to itself.
/// </summary>
internal static class FileNameCase
{
    private static readonly bool IsIgnored = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Skips the running test unless this host ignores case in file names.</summary>
    internal static void SkipUnlessIgnored()
    {
        Assert.SkipUnless(IsIgnored, "This host compares file names by case.");
    }

    /// <summary>Skips the running test unless this host compares file names by case.</summary>
    internal static void SkipUnlessCompared()
    {
        Assert.SkipWhen(IsIgnored, "This host ignores case in file names.");
    }
}
