using Shouldly;
using Xunit;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The composition seam: <see cref="WorkspaceDiagnostics.Rendered" /> is where the MSBuild-selection
///     note joins the diagnostics, so that the one list can be handed to <em>both</em> the stderr echo and
///     the JSON document, and where the NuGet audit advisories leave that list for
///     <see cref="WorkspaceDiagnostics.PackageAudit" />. Appending the note at write time reached stderr alone,
///     which is the whole reason the MCP surface lost it: the tools pass <see cref="TextWriter.Null" /> there.
///     Pure over an in-memory value, so no workspace is opened; the note's own text and the
///     rendered-once-per-verb behaviour are pinned by <c>WorkspaceDiagnosticsGateE2ETests</c>.
/// </summary>
/// <remarks>
///     The last two rows share the file's economics rather than its subject: they pin what
///     <see cref="WorkspaceTrustStamp.From(WorkspaceDiagnostics, string)" /> makes of an unsupported
///     project, over a hand-built <see cref="WorkspaceDiagnostics" /> and no bed at all — which is what a
///     fixture solution per project kind would have cost.
/// </remarks>
public sealed class WorkspaceDiagnosticsRendererTests
{
    private static readonly string Advisory =
        CapturedAuditLines.FramedFor("App", CapturedAuditLines.BuildTasksGitAdvisory);

    [Fact]
    public void Rendered_EmptyDiagnostics_StaysEmpty()
    {
        // The negative control, and the reason the note is acceptable at all: it is diagnostic context, not
        // a banner. A clean run must say nothing about MSBuild on any surface, including the document.
        IReadOnlyList<string> composed = WorkspaceDiagnostics.None.Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void Rendered_NonEmptyDiagnostics_KeepsThemInOrderAndAppendsTheNoteLast()
    {
        IReadOnlyList<string> composed = (WorkspaceDiagnostics.None with
        {
            LoadFailures = ["first failure", "second failure"]
        }).Rendered;

        composed.Count.ShouldBe(3);
        composed[0]
            .ShouldBe("first failure");
        composed[1]
            .ShouldBe("second failure");
        composed[2]
            .ShouldBe(MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void Rendered_MergeNotesPresent_LeavesThemOutOfTheFiveVerbRendering()
    {
        // The asymmetry, stated as a test: only check renders merge notes, and it asks for them by name.
        // Rendered is what the other five write, so a merge note must not reach it — and a run with nothing
        // but merge notes says nothing at all on those surfaces.
        IReadOnlyList<string> composed = (WorkspaceDiagnostics.None with
        {
            MergeNotes = ["a conflation advisory"]
        }).Rendered;

        composed.ShouldBeEmpty();
    }

    [Fact]
    public void RenderedWithMergeNotes_LoadFailuresFirstThenNotesThenTheNote()
    {
        IReadOnlyList<string> composed = (WorkspaceDiagnostics.None with
        {
            LoadFailures = ["a load failure"],
            MergeNotes = ["a conflation advisory"]
        }).RenderedWithMergeNotes;

        composed.Count.ShouldBe(3);
        composed[0]
            .ShouldBe("a load failure");
        composed[1]
            .ShouldBe("a conflation advisory");
        composed[2]
            .ShouldBe(MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void Rendered_AdvisoriesAlone_ComposeToNothing()
    {
        // The note follows the stream rather than the load. An advisory leaves the stream for PackageAudit and
        // raises no question about which MSBuild opened a project, so a load whose only diagnostics are
        // advisories composes to nothing on either rendering.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { LoadFailures = [Advisory, Advisory] };

        diagnostics.Rendered.ShouldBeEmpty();
        diagnostics.RenderedWithMergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void Rendered_AnAdvisoryBesideALoadFailure_KeepsTheLoadFailureAndTheNote()
    {
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [Advisory, "a load failure"]
        };

        diagnostics.Rendered.ShouldBe(["a load failure", MsBuildBootstrap.SelectionNote()]);
    }

    [Fact]
    public void RenderedWithMergeNotes_AMergeNoteNamingAnAuditCode_StaysInTheStream()
    {
        // The merge notes are this product's own sentences and are never classified, so a type whose name
        // happens to carry an audit code is still a merge note.
        const string note =
            "Projects 'Acme.App' and 'Acme.Tools' each declare 'Acme.NU1903'; its facts and project "
            + "attribution follow 'Acme.App' (the first declarer), but arch.Project('Acme.Tools') selections "
            + "include it too, and each declarer's reference to its own compiled-in copy counts against that "
            + "declarer alone.";
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { MergeNotes = [note] };

        diagnostics.RenderedWithMergeNotes.ShouldBe([note, MsBuildBootstrap.SelectionNote()]);
        diagnostics.PackageAudit.ShouldBeEmpty();
    }

    [Fact]
    public void ActionableDiagnostics_LeaveTheAdvisoriesOut()
    {
        // What a refusal quotes, and still exactly the load failures that are not advisories: no note, and no
        // grouping of its own.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [Advisory, "a load failure", Advisory]
        };

        diagnostics.ActionableDiagnostics.ShouldBe(["a load failure"]);
    }

    [Fact]
    public void LoadFailures_StayRawBesideTheGrouping()
    {
        // The split is recomputed on every read rather than baked into the value, which is what lets the
        // extraction cache keep persisting the load's lines exactly as they arrived.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [Advisory, Advisory, "a load failure"]
        };

        diagnostics.LoadFailures.ShouldBe([Advisory, Advisory, "a load failure"]);
        diagnostics.PackageAudit.ShouldHaveSingleItem();
    }

    [Fact]
    public void Render_WritesWhatItIsGivenAndNothingMore()
    {
        // Render does not append: it echoes the composed list. Handing it a raw list therefore produces no
        // note — which is what keeps the two surfaces reading the same one list rather than each minting one.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, [], ["a load failure"]);

        error.ToString()
            .ShouldBe($"warning: a load failure{Environment.NewLine}");
    }

    [Fact]
    public void Render_Json_DropsTheWarningPrefix()
    {
        // Under --json the diagnostics are structured data in the document and stderr is their unadorned
        // echo, so the prefix would be noise on a stream a hook may be reading alongside stdout.
        var error = new StringWriter();

        WorkspaceDiagnosticsRenderer.Render(error, [], ["a load failure"], true);

        error.ToString()
            .ShouldBe($"a load failure{Environment.NewLine}");
    }

    [Fact]
    public void Render_AdvisoriesFirst_OneLineEachNamingTheProjects()
    {
        // Ahead of the stream, so the stream and its note stay directly above a refusal that points at the
        // warnings above it.
        var error = new StringWriter();
        var entry = new PackageAuditEntry(CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Tests"]);

        WorkspaceDiagnosticsRenderer.Render(error, [entry], ["a load failure"]);

        error.ToString()
            .ShouldBe(
                $"warning: {entry.Describe()}{Environment.NewLine}"
                + $"warning: a load failure{Environment.NewLine}");
    }

    [Fact]
    public void From_ASharedProjectBesideAnFsproj_GivesThemDifferentReasons()
    {
        // The mislabel, as a pin. One reason string was stamped onto every path, so a .shproj — a container
        // whose .projitems compile into the projects that import it, very often C# and already in the model
        // through them — was reported as "not a C# project" on every surface. Both kinds in one stamp,
        // because what went wrong was not the wording of either but that one sentence covered both.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            UnsupportedProjects =
            [
                new UnsupportedProject("/repo/Signals/Signals.fsproj", UnsupportedProjectKind.NotCsharp),
                new UnsupportedProject("/repo/Shared/Shared.shproj", UnsupportedProjectKind.SharedProject)
            ]
        };

        WorkspaceTrustStamp stamp = WorkspaceTrustStamp.From(diagnostics, "/repo");

        (stamp.UnsupportedProjects ?? []).Select(project => project.Describe())
            .ShouldBe([
                "Signals/Signals.fsproj — not a C# project",
                "Shared/Shared.shproj — a shared project, compiled into the projects that import it"
            ]);
    }

    [Fact]
    public void From_NoUnsupportedProjects_OmitsTheKeyRatherThanWritingAnEmptyList()
    {
        // The empty-to-null policy: a run that reached every declared project says nothing, so a clean
        // document carries no unsupported-projects key at all.
        WorkspaceTrustStamp stamp = WorkspaceTrustStamp.From(WorkspaceDiagnostics.None, "/repo");

        stamp.UnsupportedProjects.ShouldBeNull();
    }
}
