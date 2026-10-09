using Shouldly;
using Xunit;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that holds every tracked C# project to declaring <c>RestoreLockedMode</c> for itself.
/// </summary>
/// <remarks>
///     <para>
///         OpenSSF Scorecard's Pinned-Dependencies check parses csproj
///         files directly and credits the property all-or-nothing: one project omitting it marks the whole
///         repository's NuGet dependencies unpinned. Nothing else would say so. The build stays green, the
///         restore behaves identically, the committed lock files stay valid, and the only symptom is a scan
///         reporting every dependency unpinned — by which time the project that caused it is several
///         commits back.
///     </para>
///     <para>
///         The check reads the XML, not MSBuild's
///         evaluation of it, so what it credits is the text: a <c>$(Property)</c> reference fails its bool
///         parse and takes that whole csproj out of the check's reach. It ignores the <c>Condition</c>
///         attribute entirely, which is what lets the declaration stay gated on <c>$(CI)</c> and still count.
///         So this compares the one spelling that is known to be credited, case-sensitively, rather than
///         asking whether a project would restore locked.
///     </para>
///     <para>
///         Scorecard reads the repository as published, so the set that matters is what
///         <see cref="TrackedFiles" /> reports, not what a walk of the working tree would find.
///     </para>
/// </remarks>
public sealed class RestoreLockSiteTests
{
    // The literal Scorecard credits, spelled as every tracked csproj carries it.
    private const string Declaration = "<RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode>";

    [Fact]
    public void EveryTrackedProject_DeclaresRestoreLockedMode()
    {
        // Arrange
        IReadOnlyList<string> projects = TrackedFiles.Projects;

        // Act
        List<string> undeclared = projects
            .Where(static path => !RepoRoot.ReadText(path)
                .Contains(Declaration, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        undeclared.ShouldReportNothing(
            "Project(s) do not declare RestoreLockedMode, which scores every NuGet dependency in the "
            + "repository unpinned — Scorecard's Pinned-Dependencies check credits it all-or-nothing");
    }

    [Fact]
    public void TrackedProjectList_IsNotEmpty()
    {
        // Act
        IReadOnlyList<string> projects = TrackedFiles.Projects;

        // Assert: the arm above reports what a filter left behind, and an empty list would make it pass by
        // reading no project at all rather than by finding every one of them declared.
        projects.ShouldNotBeEmpty("No .csproj files were found among the tracked files.");
    }
}
