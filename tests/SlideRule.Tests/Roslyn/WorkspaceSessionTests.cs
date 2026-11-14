using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Sessions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Workspace-tier tests for <see cref="WorkspaceSession" /> — the warm, per-call-reconciling solution
///     host. Most cases mutate disk between calls and assert on what the next call does about it.
/// </summary>
/// <remarks>
///     Two shapes of test live here, and the difference decides which session a case gets. A test whose
///     subject is a delta — one reload, a bumped edit version, the same snapshot back — takes the
///     class's <see cref="SharedWorkspaceSession" />: it re-baselines the counters after its own first
///     acquisition, so a tree an earlier case edited cannot skew it, and all of them share one
///     MSBuild load instead of paying one each. A test whose subject is the virgin state — zero reads
///     ever, exactly one load ever, first-load concurrency, recovery from a failed first load — asserts
///     absolute counters and so must mint its own session over its own copy. So must a test that acts on
///     the session's commentary, since the shared session reports to no sink.
/// </remarks>
public sealed class WorkspaceSessionTests(SharedWorkspaceSession shared) : IClassFixture<SharedWorkspaceSession>
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";

    [Fact]
    public async Task GetCurrentAsync_DocumentEditedOnDisk_ReturnsRefreshedSnapshotWithEdit()
    {
        // Arrange
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        string moneyFile = shared.PathOf("MyApp.Domain", "Money.cs");

        // Act — edit the file on disk, then re-acquire.
        FixtureEdits.EditOnDisk(moneyFile, content => content + "\n// external-edit-marker\n");
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — a fresh snapshot whose document reflects the edit.
        after.ShouldNotBeSameAs(before);
        string text = await DocumentTextAsync(after, moneyFile);
        text.ShouldContain("external-edit-marker");

        // …folded in place, not reloaded: the generation holds, and only the edited file's project bumps —
        // the delta the incremental fragment store diffs. Money.cs is in Domain, so Web and Billing are still.
        after.Generation.ShouldBe(before.Generation);
        after.ProjectEditVersions[Domain]
            .ShouldBeGreaterThan(before.ProjectEditVersions[Domain]);
        after.ProjectEditVersions[Web]
            .ShouldBe(before.ProjectEditVersions[Web]);
        after.ProjectEditVersions[Billing]
            .ShouldBe(before.ProjectEditVersions[Billing]);
    }

    [Fact]
    public async Task GetCurrentAsync_NoChange_ReturnsSameSnapshotAndReadsNothing()
    {
        // Arrange — a first load lets the design-time build write the generated sources it owns under obj/;
        // then the whole tree, inputs and outputs alike, is stamped just outside the racy window, so the
        // measured load writes nothing and every stat it takes is trusted from the first sweep: the steady
        // state, reached with no warmup sweep.
        using var fixture = new TempFixtureWorkspace();
        await using (var settling = new WorkspaceSession())
        {
            await settling.GetCurrentAsync(fixture.SolutionPath, Ct);
        }

        FixtureEdits.BackdateTree(Path.GetDirectoryName(fixture.SolutionPath)!);
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot loaded = await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Act — sweep twice with disk untouched.
        WorkspaceSnapshot again = await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        WorkspaceSnapshot againTwice = await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Assert — same snapshot instance throughout, and no sweep re-read a document the load already read.
        again.ShouldBeSameAs(loaded);
        againTwice.ShouldBeSameAs(loaded);
        session.SweepContentReads.ShouldBe(0);

        // …and the delta the store diffs is stable: the generation holds and no project version ever bumped
        // (a pure no-op lifecycle never edits a document, so every seeded counter is still 0).
        again.Generation.ShouldBe(loaded.Generation);
        again.ProjectEditVersions.Values.ShouldAllBe(version => version == 0);
    }

    [Fact]
    public async Task GetCurrentAsync_FileCompiledForTwoFrameworks_IsReadOnceForBoth()
    {
        // Arrange — MultiTfm.Core targets two frameworks, so its Widget.cs is one file behind two documents.
        using var fixture = new TempFixtureWorkspace("TestSolutions/MultiTfm", "MultiTfm.sln");
        await using var session = new WorkspaceSession();
        string widgetFile = Path.GetFullPath(fixture.PathOf("MultiTfm.Core", "Widget.cs"));

        // Act
        WorkspaceSnapshot loaded = await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Assert — one read served both documents: they hold the same text instance, not two equal ones.
        SourceText[] texts = await Task.WhenAll(loaded.Solution.GetDocumentIdsWithFilePath(widgetFile)
            .Select(id => loaded.Solution.GetDocument(id)!.GetTextAsync(Ct)));
        texts.Length.ShouldBe(2, "Widget.cs is compiled for both of MultiTfm.Core's target frameworks");
        texts[1].ShouldBeSameAs(texts[0]);
    }

    [Fact]
    public async Task GetCurrentAsync_MtimeBumpedIdenticalContent_ReturnsSameSnapshot()
    {
        // Arrange
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        string moneyFile = shared.PathOf("MyApp.Domain", "Money.cs");

        // Act — bump the mtime without changing the bytes (an IDE-style touch).
        FixtureEdits.StampForward(moneyFile);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — content-verified equal, so no new snapshot is minted.
        after.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task GetCurrentAsync_KnownDocumentDeleted_TriggersSingleFullReload()
    {
        // Arrange
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string deleted = shared.PathOf("MyApp.Domain", "PricingStrategy.cs");

        // Act
        File.Delete(deleted);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — exactly one reload, and the deleted document is gone from the fresh solution.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(deleted))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCurrentAsync_NewSourceFileInCone_ReloadsAndSurfacesNewType()
    {
        // Arrange — the cone-scan case that exceeds a pure mtime sweep: an SDK-glob add touches no
        // MSBuild file, so only a directory scan can see it.
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string newFile = shared.PathOf("MyApp.Domain", "NewlyAddedType.cs");

        // Act
        await File.WriteAllTextAsync(newFile, "namespace MyApp.Domain;\npublic class NewlyAddedType { }\n", Ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — one reload, and the new type is present in the extracted model.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(after.Solution, ct: Ct);
        model.Types.ShouldContain(t => t.FullName == "MyApp.Domain.NewlyAddedType");
    }

    [Fact]
    public async Task GetCurrentAsync_ExcludedStrayInCone_NeverReloadsAndStaysOutOfModel()
    {
        // Arrange — MyApp.Domain carries a <Compile Remove>'d Snippets/*.cs: it lives in the project cone on
        // disk but is never compiled. A cone scan that compared the disk against the COMPILED document set
        // would read this stray as a perpetual add and force a full reload on every call.
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        File.Exists(shared.PathOf("MyApp.Domain", "Snippets", "ExcludedScratch.cs"))
            .ShouldBeTrue();

        // Act — two more reconcile sweeps with disk untouched.
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — the stray is recorded cone membership, so it never trips the scan (zero reloads) and never
        // enters the extracted model — the two halves of "a removed file is not a compiled document".
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(after.Solution, ct: Ct);
        model.Types.ShouldNotContain(t => t.FullName == "MyApp.Domain.Snippets.ExcludedScratchTypeMustNeverAppearInTheModel");
    }

    [Fact]
    public async Task GetCurrentAsync_CsprojTouched_TriggersReload()
    {
        // Arrange
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch on a project file.
        FixtureEdits.StampForward(shared.PathOf("MyApp.Domain", "MyApp.Domain.csproj"));
        WorkspaceSnapshot after = await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert — a structural reload, which bumps the generation so the store flushes and re-walks all.
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Generation.ShouldBeGreaterThan(before.Generation);
    }

    [Fact]
    public async Task GetCurrentAsync_ProjectAssetsTouched_TriggersReload()
    {
        // Arrange
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a restore signal: obj/project.assets.json changes.
        FixtureEdits.StampForward(shared.PathOf("MyApp.Domain", "obj", "project.assets.json"));
        await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
    }

    [Fact]
    public async Task GetCurrentAsync_DirectoryBuildPropsAppearsInAncestor_TriggersReload()
    {
        // One representative probe rather than all of FileStamping.StructuralProbeFileNames, because every
        // case here loads a real workspace. The array is exercised whole, name by name, against the same
        // existence-flip rule by ExtractionCacheStoreTests.ReadAndValidate_NewStructuralProbeFileAppearsInAncestor_ReturnsMiss.

        // Arrange
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string propsFile = shared.PathOf("Directory.Build.props");

        // The write lands in an ancestor of the whole fixture copy, so it outlives this test's tree
        // unless it is taken back: every later case would build against a props file it never asked for.
        using ScopedFile held = ScopedFile.Hold(propsFile);

        // Act — a props file that did not exist at load appears in the solution directory (a probe-chain
        // ancestor recorded as absent).
        await File.WriteAllTextAsync(propsFile, "<Project />\n", Ct);
        await session.GetCurrentAsync(shared.SolutionPath, Ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
    }

    [Fact]
    public async Task GetCurrentAsync_SolutionAFilterReferencesTouched_TriggersReload()
    {
        // Under a filter, the solution the filter names decides membership while the filter's own bytes stay
        // put, so the sweep has to stamp it too. Its own session: a filter path on the shared one would force
        // a load there and another on the next shared case.

        // Arrange
        using var fixture = new TempFixtureWorkspace();
        using TempDirectory filterDirectory = TestTempRoot.Fresh("session-filter");
        string filter = SolutionPaths.WriteWholeSolutionFilter(filterDirectory, fixture.SolutionPath);
        await using var session = new WorkspaceSession();
        WorkspaceSnapshot before = await session.GetCurrentAsync(filter, Ct);
        long reloadsBefore = session.FullReloadCount;

        // Act — a structural touch on the referenced solution, the filter and every project left alone.
        FixtureEdits.StampForward(fixture.SolutionPath);
        WorkspaceSnapshot after = await session.GetCurrentAsync(filter, Ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        after.Generation.ShouldBeGreaterThan(before.Generation);
    }

    [Fact]
    public async Task GetCurrentAsync_TenConcurrentCallers_AllSucceedWithSingleLoad()
    {
        // Arrange — fire the very first (loading) call ten times at once.
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();

        // Act
        Task<WorkspaceSnapshot>[] calls = Enumerable.Range(0, 10)
            .Select(_ => session.GetCurrentAsync(fixture.SolutionPath, Ct))
            .ToArray();
        WorkspaceSnapshot[] snapshots = await Task.WhenAll(calls);

        // Assert — all callers share one immutable snapshot, and the gate collapsed the burst to one load.
        snapshots.ShouldAllBe(s => ReferenceEquals(s, snapshots[0]));
        session.FullReloadCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_IsIdempotent()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();

        // The scope's own dispose is a third call, and a no-op once the two below have run; it is what
        // releases the session when the arrange step throws.
        await using var session = new WorkspaceSession();
        await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Act — dispose explicitly: this call and the one below are what the row tests.
        // ReSharper disable once DisposeOnUsingVariable
        await session.DisposeAsync();

        // Assert — a second dispose is a no-op, post-dispose access is a clean ObjectDisposedException, and a
        // disposed session no longer reports the solution it held as loaded.
        // ReSharper disable once DisposeOnUsingVariable
        await Should.NotThrowAsync(async () => await session.DisposeAsync());
        await Should.ThrowAsync<ObjectDisposedException>(async () => await session.GetCurrentAsync(fixture.SolutionPath, Ct));
        session.IsLoaded(fixture.SolutionPath).ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentAsync_FailedLoadThenGoodPath_RecoversAndSucceeds()
    {
        // Arrange
        using var fixture = new TempFixtureWorkspace();
        await using var session = new WorkspaceSession();
        string bogusSolution = fixture.PathOf("DoesNotExist.sln");

        // Act — a bad path throws, and drops the solution loaded before it; the session resets to unloaded so the
        // next call retries cleanly.
        bool loadedBeforeAnyCall = session.IsLoaded(fixture.SolutionPath);
        await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        bool loadedAfterFirstLoad = session.IsLoaded(fixture.SolutionPath);
        await Should.ThrowAsync<Exception>(async () => await session.GetCurrentAsync(bogusSolution, Ct));
        bool goodLoadedAfterFailure = session.IsLoaded(fixture.SolutionPath);
        bool bogusLoadedAfterFailure = session.IsLoaded(bogusSolution);
        WorkspaceSnapshot recovered = await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Assert — only a load that succeeded counts as loaded, only for the path it loaded, and only until the
        // next load starts.
        recovered.Solution.Projects.ShouldNotBeEmpty();
        loadedBeforeAnyCall.ShouldBeFalse();
        loadedAfterFirstLoad.ShouldBeTrue();
        goodLoadedAfterFailure.ShouldBeFalse();
        bogusLoadedAfterFailure.ShouldBeFalse();
        session.IsLoaded(fixture.SolutionPath).ShouldBeTrue();
        session.IsLoaded(bogusSolution).ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedFileItsStatVouchesFor_IsReadAgain()
    {
        // Arrange — every document stamped a day back and swept once, so each fingerprint is promoted and a plain
        // reconcile reads nothing; that the plain call reads nothing is checked, or the row would prove no bypass.
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot first = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        FixtureEdits.BackdateAllDocuments(first);
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long readsBeforePlain = session.SweepContentReads;
        WorkspaceSnapshot settled = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long plainReads = session.SweepContentReads - readsBeforePlain;
        string moneyFile = Path.GetFullPath(shared.PathOf("MyApp.Domain", "Money.cs"));

        // Act
        ForcedReconcile forced = await session.GetCurrentAsync(shared.SolutionPath, [moneyFile], Ct);

        // Assert — the listed file alone was read, in place, and the unchanged tree hands back the same snapshot.
        plainReads.ShouldBe(0);
        (session.SweepContentReads - readsBeforePlain).ShouldBe(1);
        forced.Outcomes.ShouldContainKeyAndValue(moneyFile, ForcedReadOutcome.Read);
        forced.Snapshot.ShouldBeSameAs(settled);
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedFileCannotBeRead_ReportsUnreadable()
    {
        // Arrange — an exclusive handle leaves the file's stat readable and its content not.
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string moneyFile = Path.GetFullPath(shared.PathOf("MyApp.Domain", "Money.cs"));

        // Act
        ForcedReconcile forced;
        using (FileHold.Exclusive(moneyFile))
        {
            forced = await session.GetCurrentAsync(shared.SolutionPath, [moneyFile], Ct);
        }

        // Assert
        forced.Outcomes.ShouldContainKeyAndValue(moneyFile, ForcedReadOutcome.Unreadable);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedFileNotInTheSolution_ReportsUntracked()
    {
        // Arrange — the <Compile Remove>'d stray sits in the project cone yet is no document, and no structural file.
        WorkspaceSession session = shared.Session;
        await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string stray = Path.GetFullPath(shared.PathOf("MyApp.Domain", "Snippets", "ExcludedScratch.cs"));
        File.Exists(stray)
            .ShouldBeTrue();

        // Act
        ForcedReconcile forced = await session.GetCurrentAsync(shared.SolutionPath, [stray], Ct);

        // Assert
        forced.Outcomes.ShouldContainKeyAndValue(stray, ForcedReadOutcome.Untracked);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedProjectFile_ReloadsAndReportsEveryPathReloaded()
    {
        // Arrange — the project file's stat is left as the load recorded it, so only the listing can reload.
        WorkspaceSession session = shared.Session;
        WorkspaceSnapshot before = await session.GetCurrentAsync(shared.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;
        string projectFile = Path.GetFullPath(shared.PathOf("MyApp.Domain", "MyApp.Domain.csproj"));
        string moneyFile = Path.GetFullPath(shared.PathOf("MyApp.Domain", "Money.cs"));

        // Act
        ForcedReconcile forced = await session.GetCurrentAsync(shared.SolutionPath, [projectFile, moneyFile], Ct);

        // Assert
        (session.FullReloadCount - reloadsBefore).ShouldBe(1);
        forced.Snapshot.Generation.ShouldBeGreaterThan(before.Generation);
        forced.Outcomes.ShouldContainKeyAndValue(projectFile, ForcedReadOutcome.Reloaded);
        forced.Outcomes.ShouldContainKeyAndValue(moneyFile, ForcedReadOutcome.Reloaded);
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedFilesWrittenBeforeTheReload_ReportUnstable()
    {
        // Arrange — the session's commentary announces the forced reload after the call's opening stats and
        // before the load, so a write made from it lands inside the call. The document's write also lands before
        // the load takes its own stat of it, so only the call's opening stat can tell it moved.
        using var fixture = new TempFixtureWorkspace();
        string projectFile = Path.GetFullPath(fixture.PathOf("MyApp.Domain", "MyApp.Domain.csproj"));
        string moneyFile = Path.GetFullPath(fixture.PathOf("MyApp.Domain", "Money.cs"));
        await using var session = new WorkspaceSession(WriteBothOnForcedReload);
        await session.GetCurrentAsync(fixture.SolutionPath, Ct);

        // Act
        ForcedReconcile forced = await session.GetCurrentAsync(fixture.SolutionPath, [projectFile, moneyFile], Ct);

        // Assert
        forced.Outcomes.ShouldContainKeyAndValue(projectFile, ForcedReadOutcome.Unstable);
        forced.Outcomes.ShouldContainKeyAndValue(moneyFile, ForcedReadOutcome.Unstable);
        return;

        void WriteBothOnForcedReload(string message)
        {
            if (!message.StartsWith("Structural file listed for a forced read", StringComparison.Ordinal)) return;

            FixtureEdits.StampForward(projectFile);
            FixtureEdits.EditOnDisk(moneyFile, content => content + "\n// written-during-the-reload\n");
        }
    }

    [Fact]
    public async Task GetCurrentAsync_ForcedRead_ListedDocumentWrittenDuringTheSweep_ReportsUnstable()
    {
        // Arrange — a second listed file held exclusively makes the sweep report it to the sink, mid-sweep, and the
        // sink writes the first. Whichever of the two the sweep reaches first, the write lands after the call's
        // opening stat and before its closing one.
        using var fixture = new TempFixtureWorkspace();
        string moneyFile = Path.GetFullPath(fixture.PathOf("MyApp.Domain", "Money.cs"));
        string lockedFile = Path.GetFullPath(fixture.PathOf("MyApp.Domain", "PricingStrategy.cs"));
        await using var session = new WorkspaceSession(WriteMoneyOnUnreadable);
        await session.GetCurrentAsync(fixture.SolutionPath, Ct);
        long reloadsBefore = session.FullReloadCount;

        // Act
        ForcedReconcile forced;
        using (FileHold.Exclusive(lockedFile))
        {
            forced = await session.GetCurrentAsync(fixture.SolutionPath, [moneyFile, lockedFile], Ct);
        }

        // Assert — read in place, no reload, and the write still shows.
        forced.Outcomes.ShouldContainKeyAndValue(moneyFile, ForcedReadOutcome.Unstable);
        forced.Outcomes.ShouldContainKeyAndValue(lockedFile, ForcedReadOutcome.Unreadable);
        (session.FullReloadCount - reloadsBefore).ShouldBe(0);
        return;

        void WriteMoneyOnUnreadable(string message)
        {
            if (!message.StartsWith("Skipping unreadable file during reconcile", StringComparison.Ordinal)) return;

            FixtureEdits.EditOnDisk(moneyFile, content => content + "\n// written-during-the-sweep\n");
        }
    }

    private static async Task<string> DocumentTextAsync(WorkspaceSnapshot snapshot, string path)
    {
        DocumentId documentId = snapshot.Solution.GetDocumentIdsWithFilePath(Path.GetFullPath(path))
            .First();
        SourceText text = await snapshot.Solution.GetDocument(documentId)!.GetTextAsync(Ct);
        return text.ToString();
    }
}
