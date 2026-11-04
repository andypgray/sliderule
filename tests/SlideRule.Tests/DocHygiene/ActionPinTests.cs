using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate that keeps every action a workflow or the published action runs pinned to a commit.
/// </summary>
/// <remarks>
///     A tag or a branch is a pointer its owner can move, so a step that names one runs whatever the owner
///     points it at next, with this repository's token in reach. SECURITY.md promises SHA pins, and Scorecard's
///     Pinned-Dependencies check reads the workflows for them, but nothing outside this test reads the action
///     metadata at the root, whose steps run inside every adopter's job.
/// </remarks>
public sealed class ActionPinTests
{
    // owner/repository, an optional path inside it, and a full commit SHA.
    private static readonly Regex PinnedReference = new(@"^[\w.-]+/[\w.-]+(?:/[\w./-]+)?@[0-9a-f]{40}$");

    [Fact]
    public void ActionUses_InTheWorkflowsAndTheAction_ArePinnedToACommit()
    {
        // Arrange
        IReadOnlyList<ActionUse> uses = ReadUses();

        // Act
        List<string> unpinned = uses
            .Where(static use => !use.IsLocal && !PinnedReference.IsMatch(use.Reference))
            .Select(static use => $"  {use.File}:{use.Line}: {use.Reference}")
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        unpinned.ShouldReportNothing(
            "Step(s) name an action by a tag or a branch rather than a full commit SHA, so its owner decides "
            + "what runs");
    }

    [Fact]
    public void ActionUses_AreReadable()
    {
        // Act
        string[] files = ReadUses()
            .Select(static use => use.File)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // Assert: the arm above passes over a file it cannot read, so the action metadata and the CI workflow are
        // each held to contributing steps. A rewrite into a form this scan cannot follow, or the metadata
        // dropping out of the scanned set, fails here.
        files.ShouldContain(TrackedFiles.ActionMetadata);
        files.ShouldContain(TrackedFiles.CiWorkflow);
    }

    private static IReadOnlyList<ActionUse> ReadUses()
    {
        return TrackedFiles.ActionSteps
            .SelectMany(ActionUses.In)
            .ToArray();
    }
}
