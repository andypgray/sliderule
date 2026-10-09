using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     This repository commits no baseline section its own spec does not declare, and no baseline file the scan
///     cannot read.
/// </summary>
/// <remarks>
///     The scan runs in this repository's own <c>check</c> and <c>status</c>, but CI asserts only their exit codes,
///     and an orphan fails neither: it prints a line and a warning. So an orphan committed here, a self-spec rule
///     renamed after its capture, would go unseen without this fact. Each finding is asserted as text, so a red
///     names the section and its file.
/// </remarks>
public sealed class SelfOrphanScanTests
{
    [Fact]
    public void SelfSpec_BaselineFiles_HoldNoOrphanedSectionAndNoUnreadableFile()
    {
        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(SelfSpecTests.SelfModel, RepoRoot.Directory);

        scan.Orphans.Select(orphan => $"{orphan.RuleId} in {orphan.Path} ({orphan.Entries})")
            .ShouldBeEmpty();
        scan.Unreadable.Select(file => $"{file.Path}: {file.Reason}")
            .ShouldBeEmpty();
    }
}
