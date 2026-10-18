using Shouldly;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The human survey's line shapes that the MyApp fixture has no example of, over hand-built and compiled
///     summaries. <see cref="GraphCommandTests" /> pins the whole document against the real fixture.
/// </summary>
public sealed class GraphFormatterTests
{
    private const string UnsupportedHeading = "Projects the solution declares that this survey does not cover:";

    [Fact]
    public void Lines_MixedSolutionMembership_AnnotatesOnlyThePassenger()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the annotation sits between the name and the em-dash, so the rest of the line reads
        // exactly as every other project's does and the roster still scans as one column of names. The
        // unread project reads as an ordinary one: the fail-open contract at the surface, since an
        // unreadable solution file must not turn every project into a reported passenger.
        Roster(lines)
            .ShouldBe([
                "  Acme.App — 1 type; references: Acme.Passenger; packages: (none)",
                "  Acme.Passenger (not a solution member) — 1 type; references: (none); packages: (none)",
                "  Acme.Unread — 1 type; references: (none); packages: (none)"
            ]);
    }

    [Fact]
    public void Lines_NoUnsupportedProjects_KeepsTheSectionReadingNone()
    {
        // The formatter's standing rule: a section that appears only when it has content is one a reader
        // never learns to look for, so a solution the survey covers whole says outright that nothing was left
        // out rather than saying nothing at all.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        Section(lines, UnsupportedHeading)
            .ShouldBe(["  (none)"]);
    }

    [Fact]
    public void Lines_UnsupportedProjects_ReadTheReasonTheDocumentCarries()
    {
        // The reason is composed once, off the kind its producer classified, and arrives here already paired
        // with its path — so this line and the document's own entry cannot disagree about what the run
        // reached. Two entries, because the heading above them states no cause: each says its own, and a
        // shared project's is not the .fsproj's.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full,
            [
                new UnsupportedProjectStamp("Acme.Signals/Acme.Signals.fsproj", "not a C# project"),
                new UnsupportedProjectStamp(
                    "Acme.Shared/Acme.Shared.shproj", "a shared project, compiled into the projects that import it")
            ]);

        Section(lines, UnsupportedHeading)
            .ShouldBe([
                "  Acme.Signals/Acme.Signals.fsproj — not a C# project",
                "  Acme.Shared/Acme.Shared.shproj — a shared project, compiled into the projects that import it"
            ]);
    }

    [Fact]
    public void Lines_GeneratedTypes_QualifyTheProjectCountAndEachNamespace()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            GeneratedTypeSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — three namespace shapes in one inventory. A wholly generated namespace reads "all
        // generated" rather than repeating the count it just gave, because that is the shape a reader must
        // catch: it is the one namespace here that must never become a layer glob. A namespace with none
        // says nothing at all, so the qualifier's presence is itself the signal.
        Roster(lines)
            .ShouldBe(["  Acme.Web — 5 types (3 generated); references: (none); packages: (none)"]);
        Section(lines, "Namespaces:")
            .ShouldBe(["  Acme.Web: Acme.Views (2, all generated), Acme.Web (2, 1 generated), Acme.Plain (1)"]);
    }

    [Fact]
    public void Lines_NoGeneratedTypes_LeaveEveryLineExactlyAsItWas()
    {
        // The negative that keeps every existing golden byte-identical: a solution with no generator output
        // must render the survey it rendered before the qualifier existed.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        Roster(lines)
            .ShouldAllBe(line => !line.Contains("generated", StringComparison.Ordinal));
        Section(lines, "Namespaces:")
            .ShouldAllBe(line => !line.Contains("generated", StringComparison.Ordinal));
    }

    [Fact]
    public void Lines_MultiTargetedProjects_AnnotateTheirFrameworksAndOnlyACollapseNamesAWinner()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MultiTargetedSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the clause's two shapes in one roster, plus the single-framework project that says
        // nothing. The parenthesis is what a rule author acts on: it names the one compilation a rule about
        // a shared type is measured against. Acme.Split targets two frameworks that share no type, so
        // nothing was displaced and there is no winner to name — the list alone still says it compiles twice.
        Roster(lines)
            .ShouldBe([
                "  Acme.Plain — 1 type; references: (none); packages: (none)",
                "  Acme.Shared — 3 types; targets net10.0, netstandard2.0 (shared types from net10.0); "
                + "references: (none); packages: (none)",
                "  Acme.Split — 2 types; targets net10.0, netstandard2.0; references: (none); packages: (none)"
            ]);
    }

    [Fact]
    public void Lines_ProjectTargetingMoreThanTwoFrameworks_StatesTheRuleRatherThanOneWinner()
    {
        // Past two frameworks a shared type follows the first of them that declares it. Where the frameworks
        // share different sets of types, that is not one framework for all of them, so the clause states the
        // rule over the list it follows instead of naming FactsFollow.
        ProjectSummary wide = new(
            "Acme.Wide", [], 4, 0, [], solutionMember: true, targetFrameworks: ["net48", "net6.0", "net8.0"],
            factsFollow: "net48");

        IReadOnlyList<string> lines = GraphFormatter.Lines(
            new GraphSummary([wide], [], [], [], []), "Acme.slnx", DocumentGrain.Full, []);

        Roster(lines)
            .ShouldBe([
                "  Acme.Wide — 4 types; targets net48, net6.0, net8.0 (each shared type from the first of these "
                + "that declares it); references: (none); packages: (none)"
            ]);
    }

    [Fact]
    public void Lines_PackagingFacts_AnnotateOnlyTheDeliberateHalfOfEach()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(PackagingSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the passenger's rule applied to two facts whose unremarkable values are the SDK's and
        // NuGet's own: nearly every project is packable and nearly none locks, so the badge goes on the
        // project that opted out and the project that opted in. The declared packages read like the
        // references beside them, "(none)" included, because a project declaring none is what a rule about
        // declared packages is written against.
        Roster(lines)
            .ShouldBe([
                "  Acme.Locked — 0 types; targets net10.0; references: (none); packages: Shouldly; locks restore",
                "  Acme.Ordinary — 0 types; targets net10.0; references: (none); packages: (none)",
                "  Acme.Tooling — 0 types; targets net10.0; references: (none); packages: (none); does not pack"
            ]);
    }

    [Fact]
    public void Lines_SkeletonGrain_DropsTheDeclaredPackagesAndKeepsTheRestOfTheLine()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(PackagingSummary(), "Acme.slnx", DocumentGrain.Skeleton, []);

        // Assert — the packages leave a rung before the project references, beside the external-reference
        // rows they are the build-side twin of; what the project targets and ships stays, because those are
        // one value each rather than a list.
        Roster(lines)
            .ShouldBe([
                "  Acme.Locked — 0 types; targets net10.0; references: (none); locks restore",
                "  Acme.Ordinary — 0 types; targets net10.0; references: (none)",
                "  Acme.Tooling — 0 types; targets net10.0; references: (none); does not pack"
            ]);
    }

    [Fact]
    public void Lines_PrivatePackages_AnnotateEachNameInline()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(PrivatePackageSummary(), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — each private name is marked where it is named rather than listed a second time, and the
        // mark lands only on the names MustReferenceNoPackages passes, so the shipped package beside them reads
        // exactly as it did before the mark existed.
        Roster(lines)
            .ShouldBe([
                "  Acme.Domain — 0 types; targets net10.0; references: (none); "
                + "packages: Newtonsoft.Json, StyleCop.Analyzers (private)",
                "  Acme.Tooling — 0 types; targets net10.0; references: (none); "
                + "packages: Microsoft.SourceLink.GitHub (private), StyleCop.Analyzers (private)"
            ]);
    }

    [Fact]
    public void Lines_PackageDeclaredForSomeFrameworksOnly_IsMarkedWithThoseFrameworks()
    {
        // Arrange — a backport one framework takes, a private analyzer one framework takes, and a package both
        // take, which reads as it always did.
        List<ProjectSummary> projects =
        [
            new(
                "Acme.Console", [], 0, 0, [], true, ["net10.0", "netstandard2.0"], null,
                [
                    Package("Microsoft.Bcl.HashCode", frameworks: ["netstandard2.0"]),
                    Package("PolySharp", isPrivate: true, frameworks: ["netstandard2.0"]),
                    Package("Wcwidth", frameworks: ["net10.0", "netstandard2.0"])
                ])
        ];

        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            new GraphSummary(projects, [], [], [], []), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the framework mark shares the private mark's parenthesis where both apply.
        Roster(lines)
            .ShouldBe([
                "  Acme.Console — 0 types; targets net10.0, netstandard2.0; references: (none); packages: "
                + "Microsoft.Bcl.HashCode (netstandard2.0 only), PolySharp (private; netstandard2.0 only), Wcwidth"
            ]);
    }

    [Fact]
    public void Lines_UndeclaredReferences_FollowTheDeclaredOnesOnlyWhereThereAreAny()
    {
        // Arrange — a plugin whose declaration was lost, beside a project whose declarations explain everything.
        List<ProjectSummary> projects =
        [
            new("Acme.Plugin", [], 0, 0, [], true, ["net10.0"], undeclaredProjectReferences: ["Acme.Core", "Acme.Web"]),
            new("Acme.Web", ["Acme.Core"], 0, 0, [], true, ["net10.0"])
        ];

        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            new GraphSummary(projects, [], [], [], []), "Acme.slnx", DocumentGrain.Full, []);

        // Assert — the healthy line reads exactly as it did before the clause existed.
        Roster(lines)
            .ShouldBe([
                "  Acme.Plugin — 0 types; targets net10.0; references: (none); "
                + "undeclared references: Acme.Core, Acme.Web; packages: (none)",
                "  Acme.Web — 0 types; targets net10.0; references: Acme.Core; packages: (none)"
            ]);
    }

    [Fact]
    public void Lines_SkeletonGrain_DropsThePrivateAnnotationWithThePackages()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            PrivatePackageSummary(), "Acme.slnx", DocumentGrain.Skeleton, []);

        // Assert — the mark qualifies a package name, so it has nothing to stand on once the names are gone.
        Roster(lines)
            .ShouldBe([
                "  Acme.Domain — 0 types; targets net10.0; references: (none)",
                "  Acme.Tooling — 0 types; targets net10.0; references: (none)"
            ]);
    }

    [Fact]
    public void Lines_IndexGrain_KeepEachProjectsNameMembershipAndSizeAndNothingElse()
    {
        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MultiTargetedSummary(), "Acme.slnx", DocumentGrain.Index, []);

        // Assert — the roster line at the floor rung, matching what the document's own row keeps. Both
        // qualifiers go: what a project declares and what it targets each multiply by the project count, on
        // the one rung whose whole job is to stay proportional to the roster.
        Roster(lines)
            .ShouldBe([
                "  Acme.Plain — 1 type",
                "  Acme.Shared — 3 types",
                "  Acme.Split — 2 types"
            ]);
    }

    [Fact]
    public void Lines_IndexGrain_ElidesTheObservedEdgesRatherThanReadingAsNone()
    {
        // Arrange — the survey's last codebase-scaled array. An elided section that read "(none)" would say
        // this solution's projects never reference each other, which is the opposite of the truth and the
        // exact failure the elision lines exist to prevent.
        GraphSummary summary = new(
            MultiTargetedSummary()
                .Projects,
            [new ProjectEdgeSummary("Acme.Plain", "Acme.Shared", 4)],
            [],
            [],
            []);

        // Act
        IReadOnlyList<string> lines = GraphFormatter.Lines(summary, "Acme.slnx", DocumentGrain.Index, []);

        // Assert
        Section(lines, "Observed project references (distinct type pairs):")
            .ShouldBe(["  (elided at index grain — rerun at --grain full for the observed project references)"]);
    }

    [Fact]
    public void Lines_SingleTargetedProjects_LeaveEveryLineExactlyAsItWas()
    {
        // The other half of the byte-identity claim: every project of an ordinary solution targets one
        // framework, so its roster must be the one it was before the clause existed.
        IReadOnlyList<string> lines = GraphFormatter.Lines(
            MixedMembershipSummary(), "Acme.slnx", DocumentGrain.Full, []);

        Roster(lines)
            .ShouldAllBe(line => !line.Contains("targets", StringComparison.Ordinal));
    }

    // Hand-built for the reason GeneratedTypeSummary is: the subject is the LINE, and the three shapes it
    // has to spell — collapsed, uncollapsed, single-framework — read better as literals than as the three
    // extractions that would produce them. MultiTargetFrameworkTests covers the facts themselves, over the
    // real two-framework fixture solution.
    private static GraphSummary MultiTargetedSummary()
    {
        ProjectSummary plain = new("Acme.Plain", [], 1, 0, [], solutionMember: true);
        ProjectSummary shared = new(
            "Acme.Shared", [], 3, 0, [], solutionMember: true, targetFrameworks: ["net10.0", "netstandard2.0"],
            factsFollow: "net10.0");
        ProjectSummary split = new(
            "Acme.Split", [], 2, 0, [], solutionMember: true, targetFrameworks: ["net10.0", "netstandard2.0"]);

        return new GraphSummary([plain, shared, split], [], [], [], []);
    }

    // Hand-built rather than extracted: the subject here is the LINE, and the three namespace shapes it has
    // to spell are easier to read as literals than as the generator run that would produce them.
    // GraphSummarizerTests covers the counting itself, over a real generator.
    private static GraphSummary GeneratedTypeSummary()
    {
        ProjectSummary web = new(
            "Acme.Web",
            [],
            5,
            3,
            [
                new NamespaceCount("Acme.Views", 2, 2),
                new NamespaceCount("Acme.Web", 2, 1),
                new NamespaceCount("Acme.Plain", 1, 0)
            ]);

        return new GraphSummary([web], [], [], [], []);
    }

    // One section: the lines between its heading and the blank line closing it.
    private static IReadOnlyList<string> Section(IReadOnlyList<string> lines, string heading)
    {
        return lines
            .SkipWhile(line => !line.Equals(heading, StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .ToList();
    }

    // The roster block alone: the lines between the "Projects (n):" header and the blank line closing it.
    private static IReadOnlyList<string> Roster(IReadOnlyList<string> lines)
    {
        return lines
            .SkipWhile(line => !line.StartsWith("Projects (", StringComparison.Ordinal))
            .Skip(1)
            .TakeWhile(line => line.Length > 0)
            .ToList();
    }

    // Hand-built rather than extracted, because the facts under test come from an MSBuild evaluation and
    // the fast path has no project file to evaluate. Three projects, one per shape the roster line has to
    // tell apart: the opted-in lock policy, the ordinary SDK defaults, and the opted-out packability.
    private static GraphSummary PackagingSummary()
    {
        List<ProjectSummary> projects =
        [
            new("Acme.Locked", [], 0, 0, [], true, ["net10.0"], null, [Package("Shouldly")], true, true),
            new("Acme.Ordinary", [], 0, 0, [], true, ["net10.0"], null, [], true, false),
            new("Acme.Tooling", [], 0, 0, [], true, ["net10.0"], null, [], false, false)
        ];

        return new GraphSummary(projects, [], [], [], []);
    }

    // A project shipping one package beside an analyzer, and one whose every package is private.
    private static GraphSummary PrivatePackageSummary()
    {
        List<ProjectSummary> projects =
        [
            new(
                "Acme.Domain", [], 0, 0, [], true, ["net10.0"], null,
                [Package("Newtonsoft.Json"), Package("StyleCop.Analyzers", isPrivate: true)]),
            new(
                "Acme.Tooling", [], 0, 0, [], true, ["net10.0"], null,
                [Package("Microsoft.SourceLink.GitHub", isPrivate: true), Package("StyleCop.Analyzers", isPrivate: true)])
        ];

        return new GraphSummary(projects, [], [], [], []);
    }

    // The survey renders names alone, so where a reference was declared is no part of any line under test.
    private static PackageReference Package(string name, bool isPrivate = false, IReadOnlyList<string>? frameworks = null)
    {
        return ProjectFacts.Package(name, "Acme.csproj", 1, isPrivate, frameworks);
    }

    // App (declared) references Passenger (undeclared); Unread was extracted with no membership at all.
    private static GraphSummary MixedMembershipSummary()
    {
        CompilationInput passenger = CompilationFactory.Compile("Acme.Passenger", ("Passenger.cs", """
                                                                                                   namespace Acme.Passenger;
                                                                                                   public class Service {}
                                                                                                   """)) with
        {
            SolutionMember = false
        };
        CompilationInput app = CompilationFactory.CompileReferencing(
                "Acme.App", passenger.Compilation, "Acme.Passenger", ("App.cs", """
                                                                                namespace Acme.App;
                                                                                public class Client {}
                                                                                """)) with
            {
                SolutionMember = true
            };
        CompilationInput unread = CompilationFactory.Compile("Acme.Unread", ("Unread.cs", """
                                                                                          namespace Acme.Unread;
                                                                                          public class Thing {}
                                                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([app, passenger, unread]);
        return GraphSummarizer.Summarize(model);
    }
}
