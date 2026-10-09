using System.Text.Json;
using Shouldly;
using Xunit;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The survey document's floor rung, driven through <see cref="GraphJsonRenderer.Document" /> directly
///     rather than through the runner — because the slots under test are the load's own diagnostic stream and
///     the advisories grouped beside it, and the MyApp fixture loads cleanly, so no end-to-end row can populate
///     them. The project row's private package references are here for the same reason: no MyApp project
///     declares one, so the golden rungs cannot show where the key rides and where it is absent.
/// </summary>
/// <remarks>
///     <see cref="Cli.GraphCommandTests" /> pins the rungs over the real fixture, including the goldens. What is
///     left over is the case that fixture cannot show: a survey of a solution whose load complained. That
///     stream is the one thing on this document that scales with neither the codebase nor the solution but
///     with the load's troubles — one entry per project per framework per complaint — so it is what decides
///     whether the ladder has a floor at all.
/// </remarks>
public sealed class GraphJsonGrainTests
{
    // An ordinary restore warning, the stream's kind of line: NuGet's advice about a package reference, with
    // nothing in it about vulnerability data.
    private static readonly string Diagnostic = CapturedAuditLines.FramedFor(
        "Acme.App",
        "PackageReference System.Text.Encodings.Web will not be pruned. Consider removing this package from "
        + "your dependencies, as it is likely unnecessary.");

    // The same project's audit fetch failing, which is packageAudit's to carry rather than the stream's.
    private static readonly string AuditFetchFailure =
        CapturedAuditLines.FramedFor("Acme.App", CapturedAuditLines.AuditFetchFailure);

    [Fact]
    public void Document_IndexGrain_ElidesTheDiagnosticStreamToItsCount()
    {
        // The stream rides every rung above the floor untouched, so the roster rung alone cannot bring the
        // survey of a troubled load inside a channel. It is the last thing the floor drops.
        string index = Render(DocumentGrain.Index, [Diagnostic, Diagnostic]);

        index.ShouldNotContain("\"workspaceDiagnostics\"");
        index.ShouldContain("\"workspaceDiagnosticCount\": 2");
    }

    [Fact]
    public void Document_IndexGrain_KeepsEveryTrustStampBesideTheCount()
    {
        // What makes the elision safe rather than merely smaller: the actionable half of the stream is
        // already keyed, and stays keyed at the one grain a reader reaches when nothing else fits. A survey
        // that dropped both would be a wrong map wearing a clean one's clothes.
        string index = Render(DocumentGrain.Index, [Diagnostic]);

        index.ShouldContain("\"modelIncomplete\": true");
        index.ShouldContain("\"failedProjects\"");
        index.ShouldContain("\"restoreFailedProjects\"");
    }

    [Fact]
    public void Document_EveryGrainAboveTheFloor_KeepsTheDiagnosticStreamWhole()
    {
        // The elision belongs to the floor rung alone: a reader who can hold a skeleton gets MSBuild's own
        // words in full, which is the only place the load's account of itself survives.
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
        {
            string document = Render(grain, [Diagnostic]);

            document.ShouldContain("\"workspaceDiagnostics\"");
            document.ShouldNotContain("\"workspaceDiagnosticCount\"");
        }
    }

    [Fact]
    public void Document_IndexGrainWithACleanLoad_CarriesNeitherTheStreamNorABareZero()
    {
        // Absent when there is nothing to say, which is the rule every <key>Count on these documents takes:
        // an elision reports what it dropped, so dropping nothing reports nothing.
        string index = Render(DocumentGrain.Index, []);

        index.ShouldNotContain("\"workspaceDiagnostic");
        index.ShouldCarryNoPackageAudit();
    }

    [Fact]
    public void Document_IndexGrain_ElidesThePackageAuditToItsCount()
    {
        // On a field bed whose package-audit feed was unreachable these lines were 98% of the survey and rode
        // every rung untouched (measured). Grouped they are one entry per advisory, and the floor still drops
        // that entry to its count.
        string index = Render(DocumentGrain.Index, [], [AuditFetchFailure, AuditFetchFailure]);

        index.ShouldElidePackageAuditTo(1);
    }

    [Fact]
    public void Document_EveryGrainAboveTheFloor_KeepsThePackageAuditWhole()
    {
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
            Render(grain, [], [AuditFetchFailure])
                .ShouldCarryPackageAudit(CapturedAuditLines.AuditFetchFailure, ["Acme.App"]);
    }

    [Fact]
    public void Document_AdvisoriesAlone_CarryNoDiagnosticStreamKey()
    {
        // A load whose only complaints are the audit's composes to an empty stream, and the survey omits an
        // empty stream rather than writing a bare array beside the entry.
        string full = Render(DocumentGrain.Full, [], [AuditFetchFailure]);

        full.ShouldNotContain("\"workspaceDiagnostic");
        full.ShouldCarryPackageAudit(CapturedAuditLines.AuditFetchFailure, ["Acme.App"]);
    }

    [Fact]
    public void Document_PackageAudit_SerializesAheadOfTheDiagnosticStream()
    {
        // One entry per advisory is small, and the stream beside it has no ceiling, so the stream's bulk comes
        // last, where a cut lands.
        string full = Render(DocumentGrain.Full, [Diagnostic], [AuditFetchFailure]);

        full.ShouldSerializeBefore("packageAudit", "workspaceDiagnostics");
    }

    [Fact]
    public void Document_PrivatePackageReferences_RideAboveSkeletonOnly()
    {
        // The private subset qualifies the declared list, so it rides every rung that list does and leaves with
        // it at skeleton rather than outliving the names it qualifies.
        IReadOnlyList<PackageReference> packageReferences =
        [
            ProjectFacts.Package("Newtonsoft.Json", "Acme.App.csproj", 9),
            ProjectFacts.Package("StyleCop.Analyzers", "Acme.App.csproj", 10, isPrivate: true)
        ];

        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview })
            PrivatePackageReferences(Render(grain, [], packageReferences: packageReferences))
                .ShouldBe(["StyleCop.Analyzers"], $"at {grain} grain");

        foreach (DocumentGrain grain in new[] { DocumentGrain.Skeleton, DocumentGrain.Index })
            Render(grain, [], packageReferences: packageReferences)
                .ShouldNotContain("\"privatePackageReferences\"", customMessage: $"at {grain} grain");
    }

    [Fact]
    public void Document_NoPrivatePackageReferences_WritesNoKey()
    {
        // Absent rather than an empty array, so the survey of a solution with no private reference has no
        // privatePackageReferences key; the graph goldens hold that over MyApp.
        string full = Render(
            DocumentGrain.Full, [], packageReferences: [ProjectFacts.Package("Newtonsoft.Json", "Acme.App.csproj", 9)]);

        full.ShouldContain("\"packageReferences\"");
        full.ShouldNotContain("\"privatePackageReferences\"");
    }

    private static IReadOnlyList<string> PrivatePackageReferences(string document)
    {
        using JsonDocument json = JsonDocument.Parse(document);
        JsonElement project = GraphSurveyJson.Project(json, "Acme.App");

        return GraphSurveyJson.Strings(project, "privatePackageReferences");
    }

    private static string Render(
        DocumentGrain grain,
        IReadOnlyList<string> workspaceDiagnostics,
        IReadOnlyList<string>? loadFailures = null,
        IReadOnlyList<PackageReference>? packageReferences = null)
    {
        ProjectSummary project = new(
            "Acme.App", ["Acme.Core"], 3, 0, [new NamespaceCount("Acme.App", 3, 0)],
            packageReferences: packageReferences);
        GraphSummary summary = new([project], [], [], [], []);

        // Named, because five of the seven slots are IReadOnlyList<string> and this test's whole subject
        // is which of them survive the floor rung.
        var diagnostics = new WorkspaceDiagnostics(
            LoadFailures: loadFailures ?? [],
            MergeNotes: [],
            FailedProjects: ["Acme.App/Acme.App.csproj"],
            UncheckedProjects: [],
            RestoreFailedProjects: ["Acme.Core/Acme.Core.csproj"],
            UnsupportedProjects: [],
            MultiTargetedProjects: []);

        return GraphJsonRenderer.Document(
            summary,
            "Acme.sln",
            workspaceDiagnostics,
            WorkspaceTrustStamp.From(diagnostics, Directory.GetCurrentDirectory()),
            grain,
            []);
    }
}
