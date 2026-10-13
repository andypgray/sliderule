using Shouldly;
using SlideRule.Tests.Dogfood;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The quote-sync gate over the counts this repository's reader-facing pages state about itself: how
///     many packages ship, how many lock files are committed, how many projects the solution declares,
///     how many layers the self-spec declares. Each is registered as the sentence it is written in and held
///     to a number derived from the solution file, the tracked set or the self-spec, so adding a project
///     fails the suite instead of publishing a page that contradicts the repository.
/// </summary>
/// <remarks>
///     <para>
///         The table has a second half. One registered count needs the extracted codebase to answer it —
///         how many project lines the survey prints — and lives beside the gates that already hold a
///         workspace, in <see cref="SelfSpecTests" />. Everything here is answered without a workspace load
///         or a CLI invocation, so this half stays cheap and parallel-safe. A reader looking for the whole
///         table needs both.
///     </para>
///     <para>
///         Sibling gates sweep the tracked markdown for a shape and fail on an unregistered site, which
///         works where the claim has a shape to find — a grandfathered count, a rule count. A count of
///         packages has none: it is an ordinary number in an ordinary sentence, and a sweep for one would
///         match every number in every doc. So the registry is the contract, and the match-exactly-once
///         rule in <see cref="HandCounts" /> is what keeps a registered sentence from rotting into one that
///         matches nothing.
///     </para>
/// </remarks>
public sealed class HandCountSyncTests
{
    private const string ShippingPackages = "the packages that ship";

    /// <summary>
    ///     Every hand-written count under gate, each with the authority that answers it. The authorities
    ///     are deferred rather than evaluated here, so a registry entry costs nothing until the arm below
    ///     reads it and a failure in one cannot take the class's initializer down with it.
    /// </summary>
    private static readonly CountClaim[] Claims =
    [
        new(DocPaths.Contributing, "There are {count}:", "the committed lock files", CommittedLockFiles,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.Contributing, "one for each of the {count} packages", ShippingPackages, PackableCount,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.Contributing, "The {count} packages (", ShippingPackages, PackableCount,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.RootReadme, "{count} lockstep-versioned packages make up a release", ShippingPackages,
            PackableCount, GrandfatheredCounts.CountStyle.TitleWord),
        new(DocPaths.Security, "covering all {count} packages", ShippingPackages, PackableCount,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.ContractReadme, "The {count} SlideRule packages ship one version in lockstep", ShippingPackages,
            PackableCount, GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.AdapterReadme, "All {count} SlideRule packages ship one version in lockstep",
            ShippingPackages, PackableCount, GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.DeriveSpecPrompt, "The {count} SlideRule packages ship one version in lockstep",
            ShippingPackages, PackableCount, GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.Architecture, "{count} declared projects", "the projects the solution declares", DeclaredCount,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.SelfCheck, "across {count} declared layers", "the layers the self-spec declares", LayerCount,
            GrandfatheredCounts.CountStyle.WordToTen),
        new(DocPaths.SelfCheck, "its {count} projects outside the tests", "the projects the diagram draws",
            DrawnCount, GrandfatheredCounts.CountStyle.WordToTen)
    ];

    [Fact]
    public void EveryRegisteredCount_MatchesWhatThisRepositoryDeclares()
    {
        // Act
        List<string> drift = Claims
            .SelectMany(claim => HandCounts.Drift(claim.Doc, claim.Claim, claim.Subject, claim.Authority(), claim.Style))
            .ToList();

        // Assert
        drift.ShouldReportNothing(
            "These pages state a count this repository contradicts. Each number is derived from the "
            + "solution file or the tracked set, so a project or a lock file added without the sentence "
            + "following it reports here");
    }

    [Fact]
    public void SolutionInventory_YieldsProjects()
    {
        // Assert: every authority above is a count over one of these sets, and an inventory that came
        // back empty would compare each sentence against zero — a gate reporting drift because it read
        // nothing rather than because a number moved.
        SolutionInventory.All.ShouldNotBeEmpty("The solution file yielded no declared projects.");
        SolutionInventory.SrcProjects.ShouldNotBeEmpty("The solution file declares nothing under src/.");
        SolutionInventory.DrawnProjects.ShouldNotBeEmpty("The solution file declares nothing the diagram draws.");
        SolutionInventory.PackableProjects.ShouldNotBeEmpty("The solution file declares no project that packs.");
    }

    private static int PackableCount()
    {
        return SolutionInventory.PackableProjects.Count;
    }

    private static int DrawnCount()
    {
        return SolutionInventory.DrawnProjects.Count;
    }

    private static int DeclaredCount()
    {
        return SolutionInventory.All.Count;
    }

    private static int LayerCount()
    {
        return SelfSpecTests.SelfModel.Layers.Count;
    }

    // Git decides which lock files count, for the reason every hygiene gate here reads the tracked set:
    // a walk of the working tree would sweep in the copies a build leaves behind.
    private static int CommittedLockFiles()
    {
        return TrackedFiles.LockFiles.Count;
    }

    /// <summary>
    ///     One registered count: the <paramref name="Doc" /> it is written in, the
    ///     <paramref name="Claim" /> fragment that finds it, what the number is a count of as a failure
    ///     should name it (<paramref name="Subject" />), the <paramref name="Authority" /> that answers
    ///     it, and the <paramref name="Style" /> the sentence spells it in.
    /// </summary>
    private sealed record CountClaim(
        string Doc,
        string Claim,
        string Subject,
        Func<int> Authority,
        GrandfatheredCounts.CountStyle Style);
}
