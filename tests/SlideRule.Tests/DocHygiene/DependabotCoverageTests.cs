using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that keeps Dependabot's NuGet coverage and the committed lock files describing the
///     same set of projects, in both directions.
/// </summary>
/// <remarks>
///     <para>
///         A committed <c>packages.lock.json</c> that no update entry names is a project whose pinned
///         versions stop being reviewed the day it is added, and nothing about it is visible: the build
///         stays green, the lock file stays valid, and the only symptom is an advisory nobody is told
///         about. The other direction is as quiet, since a listed directory with no lock file raises update
///         PRs against a project that restores unlocked, where the pin it is bumping does not exist.
///     </para>
///     <para>
///         The configuration is read by scan through <see cref="DependabotEntries" />, which cannot follow
///         an anchor or a flow sequence, so the list is pinned non-empty rather than trusted: a rewrite into
///         a form the scan cannot read fails here instead of passing over nothing.
///     </para>
///     <para>
///         Git decides which lock files count, through <see cref="TrackedFiles" />, so this gate and
///         every other hygiene gate agree on what the repository actually publishes.
///     </para>
/// </remarks>
public sealed class DependabotCoverageTests
{
    private const string DependabotConfig = DependabotEntries.ConfigPath;

    private const string LockFileName = TrackedFiles.LockFileName;

    [Fact]
    public void DependabotNuGetList_CoversEveryCommittedLockFile()
    {
        // Arrange
        IReadOnlySet<string> listed = ReadNuGetDirectories();
        IReadOnlySet<string> committed = CommittedLockFileDirectories();

        // Act
        List<string> unlisted = committed.Except(listed)
            .Order(StringComparer.Ordinal)
            .ToList();
        List<string> unlocked = listed.Except(committed)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        unlisted.ShouldReportNothing($"Project(s) carry a committed {LockFileName} that {DependabotConfig} does not update");
        unlocked.ShouldReportNothing($"{DependabotConfig} updates director(ies) with no committed {LockFileName}");
    }

    [Fact]
    public void DependabotNuGetList_IsReadable()
    {
        // Act
        IReadOnlySet<string> listed = ReadNuGetDirectories();

        // Assert: the arm above compares two sets, and an empty parse would make it pass by reading
        // nothing rather than by finding agreement.
        listed.ShouldNotBeEmpty($"No directories were parsed out of the nuget update entry in {DependabotConfig}.");
    }

    // Every tracked lock file's directory, in the leading-slash repository-relative form the update
    // entry lists, so the two sets are comparable as they stand.
    private static IReadOnlySet<string> CommittedLockFileDirectories()
    {
        return TrackedFiles.LockFiles
            .Select(DirectoryOf)
            .ToHashSet(StringComparer.Ordinal);
    }

    // A lock file at the repository root maps to "/", which is the form a root update entry takes.
    private static string DirectoryOf(string lockFilePath)
    {
        int lastSlash = lockFilePath.LastIndexOf('/');

        return lastSlash < 0 ? "/" : $"/{lockFilePath[..lastSlash]}";
    }

    // Every directory any nuget update entry lists. Reading the union rather than the first entry keeps a
    // project in this gate's sight whichever entry lists it.
    private static IReadOnlySet<string> ReadNuGetDirectories()
    {
        return DependabotEntries.NuGetEntries()
            .SelectMany(DependabotEntries.Directories)
            .ToHashSet(StringComparer.Ordinal);
    }
}
