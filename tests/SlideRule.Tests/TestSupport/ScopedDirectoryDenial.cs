using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A directory the current user cannot list until a <c>using</c> ends, for a test of a walk that meets an
///     inaccessible directory. Windows only: elsewhere the test skips, because a POSIX mode bit does not stop a
///     process running as root, which some CI containers are.
/// </summary>
/// <remarks>
///     Take it inside the <c>using</c> of the temp directory that holds <see cref="Path" />, so the deny rule is
///     removed before the temp directory is deleted; with the rule in place the delete fails.
/// </remarks>
// The ACL calls are Windows-only, and Deny skips the test everywhere else before reaching one, a guard the
// platform analyzer cannot see through Assert.SkipUnless.
#pragma warning disable CA1416
internal sealed class ScopedDirectoryDenial : IDisposable
{
    private readonly FileSystemAccessRule _rule;

    private ScopedDirectoryDenial(string path, FileSystemAccessRule rule)
    {
        Path = path;
        _rule = rule;
    }

    /// <summary>The directory denied.</summary>
    internal string Path { get; }

    /// <summary>Denies the current user the listing of <paramref name="path" />, or skips off Windows.</summary>
    internal static ScopedDirectoryDenial Deny(string path)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Denying a directory listing needs a Windows ACL.");

        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var directory = new DirectoryInfo(path);
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);

        return new ScopedDirectoryDenial(path, rule);
    }

    /// <summary>Removes the deny rule.</summary>
    public void Dispose()
    {
        var directory = new DirectoryInfo(Path);
        DirectorySecurity security = directory.GetAccessControl();
        security.RemoveAccessRule(_rule);
        directory.SetAccessControl(security);
    }
}
#pragma warning restore CA1416
