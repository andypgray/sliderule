using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Sessions;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     Workspace-tier tests for <see cref="SessionFragmentStore" /> — the warm server's session-scoped
///     incremental fragment store. Each test drives a real <see cref="WorkspaceSession" /> over a restored
///     fixture copy (MyApp, or MultiTfm for the multi-targeting rows) to obtain genuine
///     <see cref="WorkspaceSnapshot" />s (generation + edit versions), then feeds them to the store and asserts on
///     which projects it re-walked and on model equivalence — never on wall time. The MyApp reference graph is
///     Domain → Web → Billing (Domain references Web, Web references Billing, Billing references nothing), so a
///     Web edit's reverse-dependent closure is {Web, Domain} and Billing is reused.
/// </summary>
/// <remarks>
///     <para>
///         Nearly every test reads two or more snapshots from one session and compares them, so the session has
///         to be the same one throughout. The class owns one per solution (<see cref="Sessions" />) rather than renting
///         from <see cref="WarmWorkspacePool" />, whose eviction a class running beside this one could trigger
///         between two snapshots, bringing a new generation the test never caused.
///     </para>
///     <para>
///         Nothing here needs a cold session: a store starts with nothing extracted, so its first call full-walks
///         whatever absolute generation the session is at, and every assertion is either on the store's own
///         counters or on a relative generation comparison between two snapshots of one test. What a test changes
///         on disk it changes through <see cref="EditOnDisk" /> or after <see cref="Sessions.WillChange" />, and
///         <see cref="Dispose" /> puts it back, bytes and mtime, so the next test's first acquisition reconciles
///         the session to the pristine tree.
///     </para>
/// </remarks>
public sealed class SessionFragmentStoreTests(SessionFragmentStoreTests.Sessions sessions)
    : IClassFixture<SessionFragmentStoreTests.Sessions>, IDisposable
{
    private const string Domain = "MyApp.Domain";
    private const string Web = "MyApp.Web";
    private const string Billing = "MyApp.Legacy.Billing";
    private const string MultiTfmCore = "MultiTfm.Core";
    private const string MultiTfmWeb = "MultiTfm.Web";
    private const string MultiTfmWidget = "MultiTfm.Core.Widget";
    private const string MultiTfmLegacyOnly = "MultiTfm.Core.Legacy.LegacyOnly";

    /// <summary>Puts back every fixture file this test changed, whether it passed or not.</summary>
    public void Dispose()
    {
        sessions.RestoreChanged();
    }

    [Fact]
    public async Task GetFragmentsAsync_FirstCallThenSteadyState_ExtractsAllThenReusesAll()
    {
        // Arrange
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();

        // Act 1 — the first call has nothing cached, so it flushes and walks every C# project.
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(snap1, null, Ct);

        // Assert 1 — all three re-extracted, and the merged model is byte-identical to a cold full extraction.
        first.ReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.LastReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.FullWalkCount.ShouldBe(1);

        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(snap1.Solution, ct: Ct);
        FragmentMerger.Merge(first.Fragments)
            .ShouldModelTheSameAs(coldModel);

        // Act 2 — a second call with disk untouched must reuse everything.
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet second = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert 2 — zero re-extraction, no extra full walk, and the reused fragments merge to the same model.
        second.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);
        FragmentMerger.Merge(second.Fragments)
            .ShouldModelTheSameAs(coldModel);
    }

    [Fact]
    public async Task GetFragmentsAsync_SourceEditedInOneProject_ReExtractsDirtyPlusDependents()
    {
        // A new type is declaration surface, which a dependent can bind to.
        await ShouldReWalkWebAndDomain(content => content + "\npublic class WebIncrementalProbe { }\n");
    }

    [Fact]
    public async Task GetFragmentsAsync_CsprojTouched_FlushesAndFullReWalks()
    {
        // Arrange
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);
        long walksBefore = store.FullWalkCount;

        // Act — a structural touch forces a full session reload (new generation), the store's flush signal.
        string csproj = fixture.PathOf(Domain, "MyApp.Domain.csproj");
        sessions.WillChange(csproj);
        FixtureEdits.StampForward(csproj);
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet reloaded = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — the generation moved, so the store flushed and re-walked every project via the reload path.
        snap2.Generation.ShouldBeGreaterThan(snap1.Generation);
        reloaded.ReExtractedProjects.ShouldBe([Billing, Domain, Web], true);
        store.FullWalkCount.ShouldBe(walksBefore + 1);
    }

    [Fact]
    public async Task Merge_StoreFragmentsWithExcludedProject_MatchesColdExcludedExtraction()
    {
        // Arrange — the store always holds fragments for every project (spec included); a tool drops its own
        // at merge time. This pins that the merge-time drop is byte-identical to never extracting the project.
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snapshot = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet all = await store.GetFragmentsAsync(snapshot, null, Ct);

        // Act — merge the store's fragments minus Billing (as CodebaseSource.Retain does) versus a cold
        // extraction that excludes Billing at the input stage.
        List<CodebaseFragment> retained = all.Fragments.Where(fragment => fragment.ProjectName != Billing)
            .ToList();
        CodebaseModel mergedExcluded = FragmentMerger.Merge(retained);
        CodebaseModel coldExcluded = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, [Billing], snapshot.TargetFrameworks, null, snapshot.ProjectFiles, Ct);

        // Assert — dropping a referenced project at merge time (Billing survives as an external of Web) matches
        // never extracting it, so one store serves every tool whatever project each excludes.
        mergedExcluded.ShouldModelTheSameAs(coldExcluded);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectEdited_ReExtractsBothFrameworksUnderOneName()
    {
        // Arrange — the MultiTfm fixture, whose Core csproj yields two Projects under one name and whose Web
        // project is its single reverse-dependent. Three separately-keyed pieces of the store meet on that
        // one name — the edit-version dirty set, the per-name fragment eviction, and the includeProjects
        // filter re-extraction passes — so a project spelled two ways anywhere would strand a stale fragment.
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(clean, null, Ct);

        // Both frameworks' fragments are held under the one project name from the start.
        first.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);

        // Act — an edit to a file both frameworks compile.
        string widget = fixture.PathOf(MultiTfmCore, "Widget.cs");
        EditOnDisk(
            widget,
            content => content + "\nnamespace MultiTfm.Core\n{\n    public class WidgetProbe\n    {\n    }\n}\n");
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet reExtracted = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name went dirty (plus its reverse-dependent), and BOTH of its fragments came back,
        // each still carrying its own framework, with the new type in the merged model.
        reExtracted.ReExtractedProjects.ShouldBe([MultiTfmCore, MultiTfmWeb], true);
        store.FullWalkCount.ShouldBe(1);
        reExtracted.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        FragmentMerger.Merge(reExtracted.Fragments)
            .Types.FullNames()
            .ShouldContain("MultiTfm.Core.WidgetProbe");
    }

    [Fact]
    public async Task GetFragmentsAsync_CommentOnlyEditInOneProject_RemapsSitesWithoutReWalking()
    {
        // Arrange — populate the store from the clean tree, and record where Web's sites sat.
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet clean = await store.GetFragmentsAsync(snap1, null, Ct);
        List<FragmentSite> sitesBefore = WebEdgeSites(clean);

        // Act — two comment lines at the top of a Web file: no fact changes, and every site below them moves
        // down exactly two lines.
        string homeController = fixture.PathOf(Web, "HomeController.cs");
        EditOnDisk(homeController, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — nothing was re-walked. Web's stored fragments were moved instead, and Domain, its only
        // reverse-dependent, was left alone: a dependent's own sites are in its own files.
        edited.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        store.FullWalkCount.ShouldBe(1);

        await edited.ShouldMergeToTheColdModelAsync(snap2);

        // …and the moved sites themselves, which the model comparison establishes but does not show: the same
        // files, and every line inside the edited file two lower than it was.
        List<FragmentSite> sitesAfter = WebEdgeSites(edited);
        List<int> linesBefore = LinesIn(sitesBefore, homeController);
        List<int> linesAfter = LinesIn(sitesAfter, homeController);
        linesBefore.ShouldNotBeEmpty();
        linesAfter.ShouldBe(linesBefore.Select(line => line + 2));
        FilesOf(sitesAfter)
            .ShouldBe(FilesOf(sitesBefore));
    }

    [Fact]
    public async Task GetFragmentsAsync_StringLiteralEditInAMethodBody_ReWalksThatProjectOnly()
    {
        // A token's text changes and nothing moves line. The shape digest reads token text, so this is the
        // cheapest edit that is still not trivia; it sits in a method body, so no dependent can see it.
        await ShouldReWalkWebAlone(content => content.Replace("\"total requested\"", "\"total requested!\""));
    }

    [Fact]
    public async Task GetFragmentsAsync_PublicMethodAdded_ReWalksDirtyPlusDependents()
    {
        // A new member is declaration surface, which a dependent can bind to.
        await ShouldReWalkWebAndDomain(content => content.Replace(
            "    public string RenderOrder(", "    public int Probe() => 1;\n\n    public string RenderOrder("));
    }

    [Fact]
    public async Task GetFragmentsAsync_PreprocessorDirectiveAdded_ReWalks()
    {
        // Wrap a method in an always-true conditional. Every token survives and the code stays active, but a
        // directive is trivia the shape digest deliberately reads, so this re-walks rather than remapping —
        // conservative here, and never wrong.
        await ShouldReWalkWebAndDomain(WrapExportStampInConditional);
    }

    [Fact]
    public async Task GetFragmentsAsync_GeneratedBannerInserted_ReWalks()
    {
        // A comment, and so pure trivia by the token test alone; but it is the one comment a fact reads, so
        // it enters the digest as its verdict and the file's types flip to generated. The cold model is the
        // oracle that the flipped flag really did arrive: a remap would have kept the old one while moving
        // every line correctly.
        await ShouldReWalkWebAndDomain(content => "// <auto-generated />\n" + content);
    }

    [Fact]
    public async Task GetFragmentsAsync_TokenBearingLineSplit_ReWalksThatProjectOnly()
    {
        // Chop one statement across two lines. The shapes stay equal (only whitespace moved), so this is the
        // fallback the line map itself refuses rather than the digest: a split line's sites cannot be moved
        // by line alone. The surface is the same tokens, so the dependent stays.
        await ShouldReWalkWebAlone(content => content.Replace(
            "        log.Append(description);", "        log\n            .Append(description);"));
    }

    [Fact]
    public async Task GetFragmentsAsync_TriviaThenRealThenTrivia_ClassifiesEachCallAgainstTheLastWalk()
    {
        // Arrange — each call classifies against the text the STORED fragments describe, not against the load,
        // so a remap has to move that reference forward or the second trivia edit would be measured twice.
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);
        string homeController = fixture.PathOf(Web, "HomeController.cs");

        // Act/Assert 1 — a comment edit remaps Web.
        EditOnDisk(homeController, content => "// probe one\n" + content);
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(snap2, null, Ct);
        first.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        await first.ShouldMergeToTheColdModelAsync(snap2);

        // Act/Assert 2 — a second comment edit remaps again, against the sites the first one left behind.
        EditOnDisk(homeController, content => "// probe two\n" + content);
        WorkspaceSnapshot snap3 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet second = await store.GetFragmentsAsync(snap3, null, Ct);
        second.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        await second.ShouldMergeToTheColdModelAsync(snap3);

        // Act/Assert 3 — a real edit re-walks Web, and remaps nothing; it is inside a method body, so Web's
        // dependent stays.
        EditOnDisk(
            homeController, content => content.Replace("\"total requested\"", "\"total requested!\""));
        WorkspaceSnapshot snap4 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet third = await store.GetFragmentsAsync(snap4, null, Ct);
        third.ReExtractedProjects.ShouldBe([Web]);
        store.LastRemappedProjects.ShouldBeEmpty();
        await third.ShouldMergeToTheColdModelAsync(snap4);

        // Act/Assert 4 — and a comment edit after the re-walk remaps once more.
        EditOnDisk(homeController, content => "// probe three\n" + content);
        WorkspaceSnapshot snap5 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet fourth = await store.GetFragmentsAsync(snap5, null, Ct);
        fourth.ReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([Web]);
        store.FullWalkCount.ShouldBe(1);
        await fourth.ShouldMergeToTheColdModelAsync(snap5);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectCommentEdit_RemapsBothFrameworksAndLeavesWebAlone()
    {
        // Arrange — one name, two compilations, and one file both of them compile and read alike: each
        // framework's fragment moves by its own map of that file.
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(clean, null, Ct);

        // Act
        string widget = fixture.PathOf(MultiTfmCore, "Widget.cs");
        EditOnDisk(widget, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet remapped = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name remapped, nothing re-walked, and BOTH of its fragments are still there under their
        // own frameworks: a remap that dropped or reordered one would strand a framework's facts.
        ShouldHaveRemappedCoreAlone(store, remapped);

        await remapped.ShouldMergeToTheColdModelAsync(edited);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectCommentEditInAFileWithAFrameworkConditionalRegion_RemapsEachFrameworkByItsOwnMap()
    {
        // Arrange — the sibling of the row above, over a file of the same project that the frameworks do NOT
        // read alike: Widget's other partial, which carries an #if.
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet first = await store.GetFragmentsAsync(clean, null, Ct);
        string widgetLegacy = fixture.PathOf(MultiTfmCore, "Widget.Legacy.cs");
        List<int> linesBefore = LegacyEdgeLines(first, "netstandard2.0", widgetLegacy);

        // Act — the same trivia-only edit as above, on the file with the region.
        EditOnDisk(widgetLegacy, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet remapped = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name remapped and nothing re-walked, the dependent included. Under net10.0 the region's
        // lines are disabled text with no token starts and under netstandard2.0 they are code, so the two
        // frameworks map the path differently and each is right about its own compilation.
        ShouldHaveRemappedCoreAlone(store, remapped);

        // The field's reference is netstandard2.0's alone, on a line net10.0's map has no entry for, so only
        // its own framework's map can have moved it: two lines down, like everything below the edit.
        linesBefore.ShouldNotBeEmpty();
        LegacyEdgeLines(remapped, "netstandard2.0", widgetLegacy)
            .ShouldBe(linesBefore.Select(line => line + 2));
        LegacyEdgeLines(remapped, "net10.0", widgetLegacy)
            .ShouldBeEmpty();

        await remapped.ShouldMergeToTheColdModelAsync(edited);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectCommentEditInsideTheFrameworkConditionalRegion_ReWalksBothFrameworksAndTheDependent()
    {
        // Arrange — the row above's file, edited where only netstandard2.0 reads code. Text a framework disables
        // is part of its shape, so a comment inside the region is a shape change under net10.0.
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(clean, null, Ct);

        // Act — reword the comment above the field, which moves no line.
        string widgetLegacy = fixture.PathOf(MultiTfmCore, "Widget.Legacy.cs");
        EditOnDisk(
            widgetLegacy,
            content => content.Replace("this field is not in the model", "this field is absent from the model"));
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet reWalked = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — net10.0's shape changed, so the name re-walks together with its reverse-dependent: safe, only
        // slower, and the merged model is still the cold one.
        reWalked.ReExtractedProjects.ShouldBe([MultiTfmCore, MultiTfmWeb], true);
        store.LastRemappedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);

        await reWalked.ShouldMergeToTheColdModelAsync(edited);
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectWithNoRecordedFrameworks_ReWalksWhenItsMapsForOnePathDisagree()
    {
        // Widget's other partial, whose framework-conditional region holds a field that carries a site, so the
        // two maps disagree on lines where sites are.
        await ShouldReWalkCoreAloneWithNoRecordedFrameworksAsync("Widget.Legacy.cs");
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectWithNoRecordedFrameworks_ReWalksWhenItsMapsDisagreeOnlyOnLinesNoSiteIsOn()
    {
        // A file whose framework-conditional region holds only a using directive. The two maps disagree on that one
        // line, which carries no site in either compilation, so every stored fragment could be moved through either
        // map: the store's agreement check is the only thing here that can refuse.
        await ShouldReWalkCoreAloneWithNoRecordedFrameworksAsync("LegacyUsings.cs");
    }

    [Fact]
    public async Task GetFragmentsAsync_MultiTargetedProjectCommentEditInAFileOneFrameworkLeavesOut_RemapsAndCarriesTheOtherFrameworkThrough()
    {
        // Arrange — a file net10.0's compile items leave out, so an edit to it changes netstandard2.0's
        // compilation alone and net10.0's has no maps to move its fragment by.
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        string legacyBuildOnly = fixture.PathOf(MultiTfmCore, "LegacyBuildOnly.cs");
        FrameworksCompiling(clean, legacyBuildOnly)
            .ShouldBe(["netstandard2.0"]);
        await store.GetFragmentsAsync(clean, null, Ct);

        // Act
        EditOnDisk(legacyBuildOnly, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet remapped = await store.GetFragmentsAsync(edited, null, Ct);

        // Assert — one name remapped and nothing re-walked: the framework that saw no changed document is not a
        // refusal, and its fragment is still there beside the one that went through the map.
        ShouldHaveRemappedCoreAlone(store, remapped);

        await remapped.ShouldMergeToTheColdModelAsync(edited);
    }

    // The scene the declaration-surface rows above share, differing only in the edit they make: populate the
    // store from the clean tree, edit one Web source file, re-get. [Fact]s rather than a [Theory] because they
    // are distinct facts about what reaches a document's declaration surface, and theory data carrying source
    // text lands in every display name.
    private async Task ShouldReWalkWebAndDomain(Func<string, string> edit)
    {
        // Arrange
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);

        // Act
        EditOnDisk(fixture.PathOf(Web, "HomeController.cs"), edit);
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — the declaration surface changed, so Web and its reverse-dependent Domain were re-walked;
        // Billing, which Web references but which references nothing, stays clean and is reused. Nothing was
        // remapped, and no new full walk was needed.
        edited.ReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastReExtractedProjects.ShouldBe([Web, Domain], true);
        store.LastRemappedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);

        await edited.ShouldMergeToTheColdModelAsync(snap2);
    }

    // The same scene for an edit that changes the file's shape but not its declaration surface: Web re-walks
    // alone, because its reverse-dependent Domain reads Web only through declarations that did not move.
    private async Task ShouldReWalkWebAlone(Func<string, string> edit)
    {
        // Arrange
        SharedWorkspaceSession fixture = sessions.MyApp;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot snap1 = await fixture.GetCurrentAsync(Ct);
        await store.GetFragmentsAsync(snap1, null, Ct);

        // Act
        EditOnDisk(fixture.PathOf(Web, "HomeController.cs"), edit);
        WorkspaceSnapshot snap2 = await fixture.GetCurrentAsync(Ct);
        SessionFragmentSet edited = await store.GetFragmentsAsync(snap2, null, Ct);

        // Assert — the cold model is the oracle that Domain's stored fragment is still right.
        edited.ReExtractedProjects.ShouldBe([Web]);
        store.LastRemappedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);

        await edited.ShouldMergeToTheColdModelAsync(snap2);
    }

    // The scene the two unrecorded-framework rows share, differing only in the Core file they edit. The snapshots
    // record no framework for either compilation, as a replayed build log does for a project that leaves the
    // framework out of its output path. Two comment lines go at the top of a file both frameworks compile but read
    // differently. Both compilations file their maps for it under one key, the maps disagree, and the store refuses
    // rather than resolving: the name re-walks. A comment moves no declaration surface, so its reverse-dependent
    // stays. They are separate [Fact]s rather than a [Theory] because each is a distinct fact about when the store
    // refuses.
    private async Task ShouldReWalkCoreAloneWithNoRecordedFrameworksAsync(string fileName)
    {
        // Arrange
        SharedWorkspaceSession fixture = sessions.MultiTfm;
        using var store = new SessionFragmentStore();
        WorkspaceSnapshot clean = await fixture.GetCurrentAsync(Ct);
        string path = fixture.PathOf(MultiTfmCore, fileName);
        FrameworksCompiling(clean, path)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        await store.GetFragmentsAsync(clean with { TargetFrameworks = TargetFrameworkMaps.None }, null, Ct);

        // Act
        EditOnDisk(path, content => "// probe one\n// probe two\n" + content);
        WorkspaceSnapshot edited = await fixture.GetCurrentAsync(Ct);
        WorkspaceSnapshot unattributed = edited with { TargetFrameworks = TargetFrameworkMaps.None };
        SessionFragmentSet reWalked = await store.GetFragmentsAsync(unattributed, null, Ct);

        // Assert
        reWalked.ReExtractedProjects.ShouldBe([MultiTfmCore]);
        store.LastRemappedProjects.ShouldBeEmpty();
        store.FullWalkCount.ShouldBe(1);
        await reWalked.ShouldMergeToTheColdModelAsync(unattributed);
    }

    // The verdict of a remap confined to Core: nothing re-walked, Core alone remapped, no new full walk, and both of
    // Core's fragments still held under their own frameworks.
    private static void ShouldHaveRemappedCoreAlone(SessionFragmentStore store, SessionFragmentSet remapped)
    {
        remapped.ReExtractedProjects.ShouldBeEmpty();
        store.LastReExtractedProjects.ShouldBeEmpty();
        store.LastRemappedProjects.ShouldBe([MultiTfmCore]);
        store.FullWalkCount.ShouldBe(1);
        remapped.Fragments.Where(fragment => fragment.ProjectName == MultiTfmCore)
            .Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
    }

    // Wraps HomeController's ExportStamp in an always-true conditional, anchored on the two method
    // declarations that bracket it so the edit is line-ending agnostic. Every token and every active line
    // survives; only the two directives are new.
    private static string WrapExportStampInConditional(string source)
    {
        return source.Replace(
                "    public System.DateTime ExportStamp()", "#if true\n    public System.DateTime ExportStamp()")
            .Replace(
                "    public System.DateTime ExportStampUtc()", "#endif\n    public System.DateTime ExportStampUtc()");
    }

    // Every site on every reference edge of the Web project's fragments, in the order the fragments hold
    // them — the store's stored positions, read without a merge in between.
    private static List<FragmentSite> WebEdgeSites(SessionFragmentSet set)
    {
        return set.Fragments.Where(fragment => fragment.ProjectName == Web)
            .SelectMany(fragment => fragment.Edges)
            .SelectMany(edge => edge.Sites)
            .ToList();
    }

    // The lines, inside one file, of every site on Widget's reference to LegacyOnly in one framework's
    // MultiTfm.Core fragment: the one fact of that project which a single framework's map has to move.
    private static List<int> LegacyEdgeLines(SessionFragmentSet set, string framework, string file)
    {
        IEnumerable<FragmentSite> sites = set.Fragments
            .Where(fragment => fragment.ProjectName == MultiTfmCore && fragment.TargetFramework == framework)
            .SelectMany(fragment => fragment.Edges)
            .Where(edge => edge.SourceFullName == MultiTfmWidget && edge.TargetFullName == MultiTfmLegacyOnly)
            .SelectMany(edge => edge.Sites);
        return LinesIn(sites, file);
    }

    // The target frameworks of the compilations that carry a document at one path, in ordinal order: the
    // fixture premise a row over a framework-specific file asserts before it edits that file.
    private static List<string?> FrameworksCompiling(WorkspaceSnapshot snapshot, string file)
    {
        return snapshot.Solution.Projects
            .Where(project => project.Documents.Any(document => string.Equals(document.FilePath, file, PathComparison.Comparison)))
            .Select(project => snapshot.TargetFrameworks.GetValueOrDefault(project.Id))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static List<int> LinesIn(IEnumerable<FragmentSite> sites, string file)
    {
        return sites.Where(site => string.Equals(site.File, file, PathComparison.Comparison))
            .Select(site => site.Line)
            .ToList();
    }

    private static List<string> FilesOf(IEnumerable<FragmentSite> sites)
    {
        return sites.Select(site => site.File)
            .Distinct(PathComparison.Comparer)
            .Order(PathComparison.Comparer)
            .ToList();
    }

    // FixtureEdits.EditOnDisk on a class-shared copy: the file is recorded first, so Dispose puts it back.
    private void EditOnDisk(string path, Func<string, string> transform)
    {
        sessions.WillChange(path);
        FixtureEdits.EditOnDisk(path, transform);
    }

    /// <summary>
    ///     The class's two sessions, one per solution it reads, each over a copy of its own, and the record of
    ///     what the running test changed in either copy.
    /// </summary>
    /// <remarks>
    ///     A session per test would add a cold load to every test; one per class costs about what renting from
    ///     the pool would. The copies outlive each test, so a test that changes a file must put it back, and
    ///     <see cref="RestoreChanged" /> does that for every file recorded through <see cref="WillChange" />.
    /// </remarks>
    public sealed class Sessions : IAsyncDisposable
    {
        private readonly Dictionary<string, (byte[] bytes, DateTime mtime)> _changed = new(PathComparison.Comparer);

        /// <summary>The MyApp fixture's session.</summary>
        internal SharedWorkspaceSession MyApp { get; } = new();

        /// <summary>The MultiTfm fixture's session.</summary>
        internal SharedWorkspaceSession MultiTfm { get; } = new("TestSolutions/MultiTfm", "MultiTfm.sln");

        /// <summary>Disposes both sessions and deletes their copies.</summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await MyApp.DisposeAsync();
            }
            finally
            {
                await MultiTfm.DisposeAsync();
            }
        }

        /// <summary>Records a file's bytes and mtime before the running test first changes it.</summary>
        internal void WillChange(string path)
        {
            if (!_changed.ContainsKey(path)) _changed[path] = FileSnapshot.Capture(path);
        }

        /// <summary>Puts back every recorded file, bytes and mtime, and forgets them.</summary>
        internal void RestoreChanged()
        {
            foreach (KeyValuePair<string, (byte[] bytes, DateTime mtime)> changed in _changed)
                FileSnapshot.Restore(changed.Key, changed.Value);
            _changed.Clear();
        }
    }
}

file static class SessionFragmentStoreAssertions
{
    // The model oracle: the store's fragments merge to exactly the model a cold extraction produces from the
    // snapshot the store was handed, under that snapshot's own framework map.
    internal static async Task ShouldMergeToTheColdModelAsync(this SessionFragmentSet set, WorkspaceSnapshot snapshot)
    {
        CodebaseModel coldModel = await CodebaseExtractor.ExtractFromSolutionAsync(
            snapshot.Solution, null, snapshot.TargetFrameworks, null, snapshot.ProjectFiles, Ct);
        FragmentMerger.Merge(set.Fragments)
            .ShouldModelTheSameAs(coldModel);
    }
}
