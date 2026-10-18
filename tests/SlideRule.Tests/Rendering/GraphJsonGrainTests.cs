using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The survey document's floor rung, driven through <see cref="GraphJsonRenderer.Document" /> directly
///     rather than through the runner — because the slots under test are the load's own diagnostic stream and
///     the advisories grouped beside it, and the MyApp fixture loads cleanly, so no end-to-end row can populate
///     them. The project row's private package references and undeclared project references are here for the
///     same reason: no MyApp project has either, so the golden rungs cannot show where the keys ride and where
///     they are absent.
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
            ProjectStrings(Render(grain, [], packageReferences: packageReferences), "privatePackageReferences")
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

    [Fact]
    public void Document_PackageFrameworks_NameThePackagesOnlySomeFrameworksDeclare_AboveSkeletonOnly()
    {
        // The backport is declared for one of the project's two frameworks and is the one entry; the package both
        // frameworks declare, and the one no evaluation told the frameworks apart for, say nothing a rule could
        // use and stay out. The key qualifies the declared list, so it leaves with it at skeleton.
        IReadOnlyList<PackageReference> packageReferences =
        [
            ProjectFacts.Package("Microsoft.Bcl.HashCode", "Acme.App.csproj", 9, frameworks: ["netstandard2.0"]),
            ProjectFacts.Package("Serilog", "Acme.App.csproj", 10, frameworks: ["net10.0", "netstandard2.0"]),
            ProjectFacts.Package("Unattributed", "Acme.App.csproj", 11)
        ];
        string[] frameworks = ["net10.0", "netstandard2.0"];

        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview })
        {
            using JsonDocument json = JsonDocument.Parse(
                Render(grain, [], packageReferences: packageReferences, targetFrameworks: frameworks));
            JsonElement entry = GraphSurveyJson.Project(json, "Acme.App")
                .GetProperty("packageFrameworks")
                .EnumerateArray()
                .ShouldHaveSingleItem($"at {grain} grain");

            entry.GetProperty("package")
                .GetString()
                .ShouldBe("Microsoft.Bcl.HashCode", $"at {grain} grain");
            GraphSurveyJson.Strings(entry, "frameworks")
                .ShouldBe(["netstandard2.0"], $"at {grain} grain");
        }

        foreach (DocumentGrain grain in new[] { DocumentGrain.Skeleton, DocumentGrain.Index })
            Render(grain, [], packageReferences: packageReferences, targetFrameworks: frameworks)
                .ShouldNotContain("\"packageFrameworks\"", customMessage: $"at {grain} grain");
    }

    [Fact]
    public void Document_PackagesEveryFrameworkDeclares_WriteNoPackageFrameworksKey()
    {
        // Absent rather than an empty array, so the survey of a solution with no framework-conditioned package is
        // the document it was before the key existed; the graph goldens hold that over MyApp.
        string full = Render(
            DocumentGrain.Full,
            [],
            packageReferences: [ProjectFacts.Package("Serilog", "Acme.App.csproj", 10, frameworks: ["net10.0"])],
            targetFrameworks: ["net10.0"]);

        full.ShouldContain("\"packageReferences\"");
        full.ShouldNotContain("\"packageFrameworks\"");
    }

    [Fact]
    public void Document_UndeclaredProjectReferences_RideEveryRungAboveIndex()
    {
        // They qualify the declared list, so they ride every rung it does and leave with it at index.
        foreach (DocumentGrain grain in new[] { DocumentGrain.Full, DocumentGrain.Overview, DocumentGrain.Skeleton })
            ProjectStrings(Render(grain, [], undeclaredProjectReferences: ["Acme.Data"]), "undeclaredProjectReferences")
                .ShouldBe(["Acme.Data"], $"at {grain} grain");

        Render(DocumentGrain.Index, [], undeclaredProjectReferences: ["Acme.Data"])
            .ShouldNotContain("\"undeclaredProjectReferences\"");
    }

    [Fact]
    public void Document_NoUndeclaredProjectReferences_WritesNoKey()
    {
        // Absent rather than an empty array, so the survey of a solution whose declarations explain every
        // reference has no undeclaredProjectReferences key; the graph goldens hold that over MyApp.
        string full = Render(DocumentGrain.Full, []);

        full.ShouldContain("\"projectReferences\"");
        full.ShouldNotContain("\"undeclaredProjectReferences\"");
    }

    private static IReadOnlyList<string> ProjectStrings(string document, string property)
    {
        using JsonDocument json = JsonDocument.Parse(document);
        JsonElement project = GraphSurveyJson.Project(json, "Acme.App");

        return GraphSurveyJson.Strings(project, property);
    }

    private static string Render(
        DocumentGrain grain,
        IReadOnlyList<string> workspaceDiagnostics,
        IReadOnlyList<string>? loadFailures = null,
        IReadOnlyList<PackageReference>? packageReferences = null,
        IReadOnlyList<string>? undeclaredProjectReferences = null,
        IReadOnlyList<string>? targetFrameworks = null)
    {
        ProjectSummary project = new(
            "Acme.App", ["Acme.Core"], 3, 0, [new NamespaceCount("Acme.App", 3, 0)],
            targetFrameworks: targetFrameworks,
            packageReferences: packageReferences,
            undeclaredProjectReferences: undeclaredProjectReferences);
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
