using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The exclusion walk over synthetic project graphs — no workspace, no MSBuild. The pins are the two
///     shapes the product actually has (a spec that references the app it governs; a spec that references
///     the libraries it dogfoods) plus the fallback that keeps an unreadable solution file from emptying the
///     checked universe. Membership tests hit the disk only through
///     <see cref="SpecExclusion.TryReadDeclaredMembers" />, which has its own cases at the bottom.
/// </summary>
public sealed class SpecExclusionTests : IDisposable
{
    // Canonical by construction — TestTempRoot resolves the temp base through PathCanonicalizer once per
    // run — which is what lets the declared-member assertions compare against a path built from it.
    private readonly TempDirectory _temp = TestTempRoot.Fresh("spec-exclusion");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void Compute_SpecReferencesDeclaredAppAndUndeclaredContract_ExcludesSpecAndContractOnly()
    {
        // The example shape: the ArchitectureSpec references the application project (a declared member — and the
        // subject matter) and the contract library from outside the solution (a passenger MSBuild loaded).
        var projects = new SpecExclusionProject[]
        {
            new("Meridian.Interchange.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["Meridian.Interchange", "SlideRule"]),
            new("Meridian.Interchange", "/repo/src/App.csproj", []),
            new("SlideRule", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects,
            Declared("/repo/arch/ArchitectureSpec.csproj", "/repo/src/App.csproj"),
            "Meridian.Interchange.Architecture");

        excluded.ShouldBe(["Meridian.Interchange.Architecture", "SlideRule"]);
    }

    [Fact]
    public void Compute_SpecReferencesOnlyDeclaredMembers_ExcludesJustTheSpecProject()
    {
        // The self-check shape, and the regression pin: this repo's spec references the two projects it
        // dogfoods. Both are declared members, so the subtraction leaves them under law.
        var projects = new SpecExclusionProject[]
        {
            new("SlideRule.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["SlideRule", "SlideRule.Roslyn"]),
            new("SlideRule", "/repo/src/Core.csproj", []),
            new("SlideRule.Roslyn", "/repo/src/Roslyn.csproj", ["SlideRule"])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects,
            Declared("/repo/arch/ArchitectureSpec.csproj", "/repo/src/Core.csproj", "/repo/src/Roslyn.csproj"),
            "SlideRule.Architecture");

        excluded.ShouldBe(["SlideRule.Architecture"]);
    }

    [Fact]
    public void Compute_UndeclaredPlumbingBehindUndeclaredPlumbing_ExcludesTheWholeChain()
    {
        // The rule-pack composition shape: the spec references a rule pack, which references the
        // contract. Neither is declared, and the walk is transitive, so both leave the universe.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["Guidance.Pack", "App"]),
            new("Guidance.Pack", "/elsewhere/Pack.csproj", ["SlideRule"]),
            new("SlideRule", "/elsewhere/Core.csproj", []),
            new("App", "/repo/src/App.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchitectureSpec.csproj", "/repo/src/App.csproj"), "App.Architecture");

        excluded.ShouldBe(["App.Architecture", "Guidance.Pack", "SlideRule"]);
    }

    [Fact]
    public void Compute_ProjectOutsideTheSpecsClosure_StaysInTheUniverse()
    {
        // Only what the spec reaches is plumbing. An undeclared project nobody in the closure references is
        // some other project's passenger and none of this walk's business.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["App"]),
            new("App", "/repo/src/App.csproj", []),
            new("Somebody.Elses.Passenger", "/elsewhere/Other.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchitectureSpec.csproj", "/repo/src/App.csproj"), "App.Architecture");

        excluded.ShouldBe(["App.Architecture"]);
    }

    [Fact]
    public void Compute_UnknownProjectPath_CountsAsDeclared()
    {
        // Nothing disproves membership, and a false negative silently shrinks the codebase under law — so
        // the unknown-path case errs towards keeping the project.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["Mystery"]),
            new("Mystery", null, [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, Declared("/repo/arch/ArchitectureSpec.csproj"), "App.Architecture");

        excluded.ShouldBe(["App.Architecture"]);
    }

    [Fact]
    public void Compute_UnreadableMembership_FallsBackToTheSpecProjectAlone()
    {
        // Never fail open into an empty universe: with no membership to subtract, the answer is the spec
        // project alone. Note what the alternative would be — every one of these projects is undeclared
        // under a null set.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["App", "SlideRule"]),
            new("App", "/repo/src/App.csproj", []),
            new("SlideRule", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, null, "App.Architecture");

        excluded.ShouldBe(["App.Architecture"]);
    }

    [Fact]
    public void Compute_ReferenceCycle_Terminates()
    {
        // MSBuild forbids project-reference cycles, but the walk must not depend on that to halt.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["Left"]),
            new("Left", "/elsewhere/Left.csproj", ["Right"]),
            new("Right", "/elsewhere/Right.csproj", ["Left", "App.Architecture"])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(projects, Declared("/repo/arch/ArchitectureSpec.csproj"), "App.Architecture");

        excluded.ShouldBe(["App.Architecture", "Left", "Right"]);
    }

    [Fact]
    public void Compute_MultiTargetedProjectDeclaredUnderOneEntry_IsDeclaredForAll()
    {
        // A multi-targeted project arrives once per framework under one name; declared by any entry is
        // declared, and the reference edges union across entries.
        var projects = new SpecExclusionProject[]
        {
            new("App.Architecture", "/repo/arch/ArchitectureSpec.csproj", ["App"]),
            new("App", "/repo/src/App.csproj", ["SlideRule"]),
            new("App", null, []),
            new("SlideRule", "/elsewhere/Core.csproj", [])
        };

        IReadOnlyCollection<string> excluded = SpecExclusion.Compute(
            projects, Declared("/repo/arch/ArchitectureSpec.csproj", "/repo/src/App.csproj"), "App.Architecture");

        excluded.ShouldBe(["App.Architecture", "SlideRule"]);
    }

    // ── membership reading: what the parser owns, and what it must refuse ──────────────────────────────────

    [Fact]
    public void TryReadDeclaredMembers_Slnx_ReturnsCanonicalizedDeclaredMembers()
    {
        string solutionPath = _temp.WriteFile(["App.slnx"], """
                                                            <Solution>
                                                              <Project Path="src/App/App.csproj" />
                                                            </Solution>
                                                            """);

        IReadOnlySet<string>? members = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        members.ShouldNotBeNull();
        members.ShouldContain(_temp.PathOf("src", "App", "App.csproj"));
    }

    [Fact]
    public void TryReadDeclaredMembers_SolutionFilter_ReadsThroughToTheFilteredSolutionsWholeMembership()
    {
        // A filter resolves to the solution it points at — and to that solution's WHOLE membership, not the
        // filter's selection: a member left out of the selection is still solution material, so excluding it
        // would silently shrink the codebase under law.
        _temp.WriteFile(["App.slnx"], """
                                      <Solution>
                                        <Project Path="src/App/App.csproj" />
                                        <Project Path="src/Unselected/Unselected.csproj" />
                                      </Solution>
                                      """);
        string solutionPath = _temp.WriteFile(
            ["Filtered.slnf"],
            """{ "solution": { "path": "App.slnx", "projects": [ "src/App/App.csproj" ] } }""");

        IReadOnlySet<string>? members = SpecExclusion.TryReadDeclaredMembers(solutionPath);

        members.ShouldNotBeNull();
        members.ShouldContain(_temp.PathOf("src", "App", "App.csproj"));
        members.ShouldContain(_temp.PathOf("src", "Unselected", "Unselected.csproj"));
    }

    [Fact]
    public void TryReadDeclaredMembers_MalformedSolutionFilter_ReturnsNullRatherThanAnEmptySet()
    {
        // The "never fail open" pin, at the one site that can reach it. Reporting "nothing
        // is declared" for an unreadable filter would subtract the spec's whole closure; null is the fallback.
        string solutionPath = _temp.WriteFile(["Malformed.slnf"], "{ this is not json");

        SpecExclusion.TryReadDeclaredMembers(solutionPath)
            .ShouldBeNull();
    }

    [Fact]
    public void TryReadDeclaredMembers_MissingFile_ReturnsNull()
    {
        SpecExclusion.TryReadDeclaredMembers(_temp.PathOf("does-not-exist.slnx"))
            .ShouldBeNull();
    }

    [Fact]
    public void IsDeclaredMember_UnreadableMembershipOrUnknownPath_IsTrue()
    {
        (CanonicalProjectFiles files, Project app) = LoadedAt("/repo/src/App.csproj");
        (CanonicalProjectFiles pathlessFiles, Project pathless) = LoadedAt(null);

        SpecExclusion.IsDeclaredMember(null, files, app)
            .ShouldBeTrue();
        SpecExclusion.IsDeclaredMember(SpecExclusion.CanonicalMemberSet([]), pathlessFiles, pathless)
            .ShouldBeTrue();
        SpecExclusion.IsDeclaredMember(SpecExclusion.CanonicalMemberSet([]), files, app)
            .ShouldBeFalse();
    }

    [Fact]
    public void SolutionMembershipOf_ReadMembership_SeparatesDeclaredMembersFromPassengers()
    {
        IReadOnlySet<string> declared = Declared("/repo/src/App.csproj");
        (CanonicalProjectFiles appFiles, Project app) = LoadedAt("/repo/src/App.csproj");
        (CanonicalProjectFiles coreFiles, Project core) = LoadedAt("/elsewhere/Core.csproj");

        SpecExclusion.SolutionMembershipOf(declared, appFiles, app)
            .ShouldBe(true);
        SpecExclusion.SolutionMembershipOf(declared, coreFiles, core)
            .ShouldBe(false);
    }

    [Fact]
    public void SolutionMembershipOf_UnreadMembershipOrUnknownPath_IsNullWhereIsDeclaredMemberSaysTrue()
    {
        // The inversion this method exists for, asserted against its sibling on the same inputs: true is the
        // safe answer when the question is what to exclude, and an invented fact when the question is what
        // to report.
        (CanonicalProjectFiles files, Project app) = LoadedAt("/repo/src/App.csproj");
        (CanonicalProjectFiles pathlessFiles, Project pathless) = LoadedAt(null);

        SpecExclusion.SolutionMembershipOf(null, files, app)
            .ShouldBeNull();
        SpecExclusion.SolutionMembershipOf(Declared("/repo/src/App.csproj"), pathlessFiles, pathless)
            .ShouldBeNull();
    }

    [Fact]
    public void SolutionMembershipOf_UnownedFormatOrUnparseableSolution_DegradesToNull()
    {
        // Both disk-side fallbacks reach this method as a null set, so the chain from a bad solution file
        // through to an unlabeled project is pinned end to end rather than as two halves nothing joins.
        // Unparseable XML is unreadable membership, not zero membership: an empty set would answer false.
        string unowned = _temp.WriteFile(["MyApp.csproj"], "<Project />");
        string malformed = _temp.WriteFile(["Unparseable.slnx"], "<Solution><Project Path=\"a.csproj\">");
        (CanonicalProjectFiles files, Project app) = LoadedAt("/repo/src/App.csproj");

        SpecExclusion.SolutionMembershipOf(SpecExclusion.TryReadDeclaredMembers(unowned), files, app)
            .ShouldBeNull();
        SpecExclusion.SolutionMembershipOf(SpecExclusion.TryReadDeclaredMembers(malformed), files, app)
            .ShouldBeNull();
    }

    // One loaded project at projectFilePath, or with no project file when it is null, beside the canonical
    // project files its load resolved.
    private static (CanonicalProjectFiles Files, Project Project) LoadedAt(string? projectFilePath)
    {
        using var workspace = new AdhocWorkspace();
        ProjectInfo project = projectFilePath is null
            ? ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "App", "App", LanguageNames.CSharp)
            : AdhocSolution.Loaded("App", projectFilePath);
        Solution solution = workspace.WithProjects(project);

        return (CanonicalProjectFiles.Of(solution), solution.Projects.Single());
    }

    private static IReadOnlySet<string> Declared(params string[] csprojPaths)
    {
        return SpecExclusion.CanonicalMemberSet(csprojPaths);
    }
}
