using Shouldly;
using SlideRule.Cli.SpecLoading;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The spec-resolution convention core over plain tuples — no workspace needed: the unique
///     <em>declared</em> solution member referencing <c>SlideRule.dll</c> wins;
///     zero and many are loud errors; a missing built output is a loud error pointing at <c>dotnet build</c>.
/// </summary>
public sealed class SpecResolverTests
{
    private const string CoreDll = "C:/pkgs/SlideRule.dll";

    // The load failure field testing found: a --locked-mode restore whose lock file no longer matches
    // the project, which leaves the spec project's reference to the contract library unresolved.
    private static readonly string LockFileFailure = CapturedAuditLines.Framed(
        "C:/repo/src/MyApp.Arch/MyApp.Arch.csproj",
        "NU1004: The packages lock file is inconsistent with the project dependencies.");

    // A NuGetAudit advisory in the shape NuGet really emits: no code in the text, the GHSA link and the
    // severity phrasing NuGetAuditDiagnostics matches on.
    private const string Advisory =
        "Package 'Contoso.Widgets' 1.2.3 has a known high severity vulnerability, "
        + "https://github.com/advisories/GHSA-aaaa-bbbb-cccc";

    // A project that failed to load, in the form the gate carries them: an absolute .csproj path.
    private const string BrokenProject = "C:/repo/src/MyApp.Arch/MyApp.Arch.csproj";

    [Fact]
    public void ResolveConventionProject_UniqueReferencingProject_IsChosen()
    {
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
            Candidate("MyApp.Arch", CoreDll)
        ], WorkspaceDiagnostics.None);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_ProjectReferenceOutputPath_IsChosen()
    {
        // The source-checkout shape: the contract library arrives as a ProjectReference, so the
        // candidate's reference paths carry the referenced project's OUTPUT path rather than a package
        // DLL path (candidate construction concatenates both shapes).
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
            Candidate("MyApp.Arch", "C:/repo/src/SlideRule/bin/Debug/netstandard2.0/SlideRule.dll")
        ], WorkspaceDiagnostics.None);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_NonMemberReferencingCore_IsNotACandidate()
    {
        // A rule-pack library the spec project drags into the workspace references the contract library too.
        // It is not solution material, so it must not turn a perfectly unambiguous solution into an error.
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            Candidate("MyApp.Arch", CoreDll),
            Candidate("Guidance.Pack", CoreDll, false)
        ], WorkspaceDiagnostics.None);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_OnlyNonMembersReferenceCore_ThrowsTheUnchangedZeroCandidateError()
    {
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
                Candidate("Guidance.Pack", CoreDll, false)
            ], WorkspaceDiagnostics.None));

        error.Message.ShouldContain("No spec project found");
    }

    [Fact]
    public void ResolveConventionProject_TwoDeclaredMembersReferenceCore_StillErrorsWithTheUnchangedText()
    {
        // Genuine ambiguity survives the membership filter: two spec projects the solution really declares.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                Candidate("Arch.One", CoreDll),
                Candidate("Arch.Two", CoreDll),
                Candidate("Guidance.Pack", CoreDll, false)
            ], WorkspaceDiagnostics.None));

        error.Message.ShouldContain("Multiple spec projects found");
        error.Message.ShouldContain("Arch.One");
        error.Message.ShouldContain("Arch.Two");
        error.Message.ShouldNotContain("Guidance.Pack");
    }

    [Fact]
    public void ResolveConventionProject_OneCsprojUnderTwoFrameworks_ResolvesToOneCandidate()
    {
        // A multi-target-framework spec project reaches the convention as one Roslyn project per framework,
        // sharing a single .csproj. That is one candidate, not an ambiguity — and blocking here would be a
        // dead end, because neither of Roslyn's discriminated names is typeable as --spec.
        const string csproj = "C:/repo/src/MyApp.Arch/MyApp.Arch.csproj";

        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            CandidateAt("MyApp.Arch", csproj, "C:/repo/src/MyApp.Arch/bin/Debug/net10.0/MyApp.Arch.dll"),
            CandidateAt("MyApp.Arch", csproj, "C:/repo/src/MyApp.Arch/bin/Debug/netstandard2.0/MyApp.Arch.dll")
        ], WorkspaceDiagnostics.None);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_TwoCandidatesWithNoFilePath_StillErrorsWithTheUnchangedText()
    {
        // The fail-open guard for the grouping above: candidates with no project file must never fold
        // together. Folding them would resolve one of two genuinely different spec projects silently, which
        // is the one outcome worse than the block. The message stays byte-identical to the ungrouped text.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                Candidate("Arch.One", CoreDll),
                Candidate("Arch.Two", CoreDll)
            ], WorkspaceDiagnostics.None));

        error.Message.ShouldBe("Multiple spec projects found; pass --spec to disambiguate:\n  Arch.One\n  Arch.Two");
    }

    [Fact]
    public void ResolveConventionProject_TwoCandidatesWithKnownProjectFiles_NamesEachCsprojInTheError()
    {
        // Real ambiguity survives, and the error names what the reader has to pass to --spec: the project
        // names alone are not typeable.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                CandidateAt("Arch.One", @"C:\repo\src\Arch.One\Arch.One.csproj", @"C:\out\Arch.One.dll"),
                CandidateAt("Arch.Two", @"C:\repo\src\Arch.Two\Arch.Two.csproj", @"C:\out\Arch.Two.dll")
            ], WorkspaceDiagnostics.None));

        error.Message.ShouldBe(
            "Multiple spec projects found; pass --spec to disambiguate:\n"
            + @"  Arch.One  (C:\repo\src\Arch.One\Arch.One.csproj)" + "\n"
            + @"  Arch.Two  (C:\repo\src\Arch.Two\Arch.Two.csproj)");
    }

    [Fact]
    public void ResolveConventionProject_SameCsprojSpelledTwoWays_ResolvesToOneCandidate()
    {
        // The group key canonicalizes before it folds, so two spellings of one project file are one
        // candidate. A '.'/'..' round trip proves it without needing a symlink on the test machine.
        SpecProjectCandidate chosen = SpecResolver.ResolveConventionProject([
            CandidateAt("MyApp.Arch", "C:/repo/src/MyApp.Arch/MyApp.Arch.csproj", "C:/out/net10.0/MyApp.Arch.dll"),
            CandidateAt(
                "MyApp.Arch",
                "C:/repo/src/./MyApp.Arch/../MyApp.Arch/MyApp.Arch.csproj",
                "C:/out/netstandard2.0/MyApp.Arch.dll")
        ], WorkspaceDiagnostics.None);

        chosen.Name.ShouldBe("MyApp.Arch");
    }

    [Fact]
    public void ResolveConventionProject_ProjectsFailedToLoad_CarriesTheMsBuildSelectionInsideTheEvidence()
    {
        // The strongest arm: the project that would have matched may be one of the ones that failed, and no
        // --spec argument repairs a load. The projects are the evidence, because that is what the gate
        // itself keys on — a set of paths rather than a set of sentences.
        //
        // Which MSBuild opened them is nearly always the next question about a project that failed to load,
        // and this refusal is thrown before a runner exists to echo the note beside it — so on both its
        // surfaces the reader meets the selection here or not at all. Pinned whole because the placement is
        // the claim: the note belongs inside the indented run, under the projects and above the remedy.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { FailedProjects = [BrokenProject] };

        string message = ShouldRefuseResolution(diagnostics);

        message.ShouldBe(
            "No spec project found: 1 project failed to load, so a project that references "
            + "SlideRule.dll may be among them:\n"
            + $"  {BrokenProject}\n"
            + $"  {MsBuildBootstrap.SelectionNote()}\n"
            + "Restore and build the solution first (dotnet restore, dotnet build), then retry.");
    }

    [Fact]
    public void ResolveConventionProject_PackagesDidNotResolve_BlamesTheRestoreAndNamesTheProject()
    {
        // The measured locked-mode shape. A broken restore leaves the spec project's reference to the contract
        // library unresolved while the project itself still loads completely, so the convention finds zero
        // candidates and the model is incomplete for the second cause alone. The load-failure list is empty
        // here, so a refusal that quoted it whichever cause fired would answer a colon promising the projects
        // with the remedy line. Hence the colon and its first line of evidence asserted together: an empty
        // list reds here rather than on a bed.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { RestoreFailedProjects = [BrokenProject] };

        string message = ShouldRefuseResolution(diagnostics);

        message.ShouldContain("NuGet packages did not resolve for 1 project");
        message.ShouldContain("may have failed to resolve it:\n  " + BrokenProject);
        message.ShouldContain("Restore the solution first (dotnet restore), then retry.");
        message.ShouldNotContain("failed to load");
        message.ShouldNotContain("Pass --spec");
    }

    [Fact]
    public void ResolveConventionProject_BothCauses_TellsThemBothWithTheLoadFirst()
    {
        // One tree can be broken both ways at once, and the two ask for different repairs — so each block
        // names its own project rather than one list standing in for both. The load block comes first: a
        // project that never loaded is more fundamentally broken than one that loaded without its packages.
        // The MSBuild selection closes each block for the same reason the evidence opens it: it is stated
        // per block, beside the projects it is an answer about.
        const string unrestoredProject = "C:/repo/src/MyApp.Web/MyApp.Web.csproj";
        const string loadLede =
            "No spec project found: 1 project failed to load, so a project that references "
            + "SlideRule.dll may be among them:";
        const string restoreLede =
            "No spec project found: NuGet packages did not resolve for 1 project, so a project that "
            + "references SlideRule.dll may have failed to resolve it:";
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            FailedProjects = [BrokenProject],
            RestoreFailedProjects = [unrestoredProject]
        };

        string message = ShouldRefuseResolution(diagnostics);

        string selectionNote = MsBuildBootstrap.SelectionNote();
        message.ShouldContain(loadLede + "\n  " + BrokenProject + "\n  " + selectionNote);
        message.ShouldContain(restoreLede + "\n  " + unrestoredProject + "\n  " + selectionNote);
        message.ShouldContainInOrder(loadLede, restoreLede);
    }

    [Fact]
    public void ResolveConventionProject_LoadDiagnosticsButNothingFailed_CarriesTheMsBuildSelectionInsideTheEvidence()
    {
        // The residual arm: the load reported a problem about this solution while blaming no project, so
        // neither gate cause fired and there is nothing to name but the diagnostic itself. Keying this arm on
        // the diagnostics is legitimate because it gates nothing — it chooses between two spellings of one
        // refusal, and the reader is sent to repair the load rather than to write an argument that cannot help.
        //
        // The two gate arms' reason, on the arm that names diagnostics rather than projects: this refusal is
        // thrown from the same place, before a runner exists to echo the note beside it, and a load that did
        // not go cleanly is nearly always a question about which MSBuild opened the solution. Pinned whole
        // because the placement is the claim: inside the indented run, under the diagnostics, above the remedy.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { LoadFailures = [LockFileFailure] };

        string message = ShouldRefuseResolution(diagnostics);

        message.ShouldBe(
            "No spec project found: the workspace did not load cleanly, so a project that references "
            + "SlideRule.dll may have failed to resolve it:\n"
            + $"  {LockFileFailure}\n"
            + $"  {MsBuildBootstrap.SelectionNote()}\n"
            + "Restore and build the solution first (dotnet restore, dotnet build), then retry.");
    }

    [Fact]
    public void ResolveConventionProject_OnlyNuGetAuditAdvisories_KeepsTheCleanLoadMessage()
    {
        // An advisory's publication date says nothing about whether this solution's references resolved, so
        // a solution that has no spec project must not be told to go and fix its restore. The audit
        // classifier decides no verdict here: it only chooses which spelling of the refusal the reader gets.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { LoadFailures = [Advisory] };

        string message = ShouldRefuseResolution(diagnostics, "MyApp.Web");

        message.ShouldStartWith(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.");
        message.ShouldNotContain("did not load cleanly");
    }

    [Fact]
    public void ResolveConventionProject_CleanLoad_NamesHowManyProjectsWereConsidered()
    {
        // The first sentence stays byte-identical — GETTING-STARTED.md quotes it — and the count is a new
        // line below it, because "no project references the contract library" reads very differently once you
        // can see that the workspace held two projects rather than the thirty you expected.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.ResolveConventionProject([
                Candidate("MyApp.Web", "C:/pkgs/Newtonsoft.Json.dll"),
                Candidate("MyApp.Domain", "C:/pkgs/Newtonsoft.Json.dll")
            ], WorkspaceDiagnostics.None));

        error.Message.ShouldBe(
            "No spec project found: no solution project references SlideRule.dll. Pass --spec to name one.\n"
            + "Considered 2 C# project(s) in the workspace.");
    }

    [Fact]
    public void ResolveConventionProject_ManyLoadFailures_QuotesABoundedNumberAndCountsTheRest()
    {
        // A workspace that fails to load rarely fails once. Quoting all of them buries the remedy under a
        // wall nobody reads to the end of, so the quote is bounded and says how much it left out.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = ["failure one", "failure two", "failure three", "failure four", "failure five"]
        };

        string message = ShouldRefuseResolution(diagnostics, "MyApp.Web");

        message.ShouldContain("  failure one");
        message.ShouldContain("  failure three");
        message.ShouldNotContain("failure four");
        message.ShouldContain("... and 2 more.");
        // The MSBuild selection closes the indented run below the elision line, not above it: what was left
        // out is more evidence, and the selection is the one answer that covers all of it.
        message.ShouldContain("  ... and 2 more.\n  " + MsBuildBootstrap.SelectionNote());
    }

    [Fact]
    public void ResolveConventionProject_ManyFailedProjects_NamesEveryOne()
    {
        // The bound above does not carry over to the stronger evidence, and deliberately: a diagnostic is
        // read to see whether the failures share a cause, so three of them is enough, while a project list
        // is what the reader has to act on entry by entry — an "... and 1 more." there names a repair they
        // cannot make. It is also what every other surface that states this cause does, so a capped list
        // here would be the only place a reader met a partial one.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            FailedProjects = ["C:/repo/one.csproj", "C:/repo/two.csproj", "C:/repo/three.csproj", "C:/repo/four.csproj"]
        };

        string message = ShouldRefuseResolution(diagnostics, "MyApp.Web");

        message.ShouldContain("4 projects failed to load");
        message.ShouldContain("  C:/repo/one.csproj");
        message.ShouldContain("  C:/repo/four.csproj");
        message.ShouldNotContain("more.");
    }

    [Fact]
    public void ResolveConventionProject_AuditAdvisoriesBesideARealDiagnostic_QuotesTheRealOne()
    {
        // What forces the quote to skip the advisories: three freshly published ones arriving first would
        // fill a bounded quote and push the one actionable line out of it — the same defect this refusal
        // exists to remove, wearing a new costume.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [Advisory, Advisory, Advisory, LockFileFailure]
        };

        string message = ShouldRefuseResolution(diagnostics);

        message.ShouldContain("NU1004");
        message.ShouldNotContain("GHSA");
        message.ShouldNotContain("more.");
    }

    [Fact]
    public void ResolveConventionProject_FailedProjectsBesideDiagnostics_PrefersTheProjects()
    {
        // Both arms have evidence; the projects win, because they are what the reader can act on and what
        // the gate itself decided on. The diagnostics still render on whatever channel the surface has.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [LockFileFailure],
            FailedProjects = [BrokenProject]
        };

        string message = ShouldRefuseResolution(diagnostics);

        message.ShouldContain(BrokenProject);
        message.ShouldNotContain("NU1004");
    }

    [Fact]
    public void RequireBuiltOutput_MissingFile_ThrowsUserErrorPointingAtBuild()
    {
        // Pinned whole: the message names every path tried, joined by "or", and with a single path it must stay
        // the plain one-path sentence below, with no list form.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.RequireBuiltOutput("MyApp.Arch", ["C:/nope/does-not-exist.dll"]));

        error.Message.ShouldBe(
            "The spec project 'MyApp.Arch' has no built output at 'C:/nope/does-not-exist.dll'. "
            + "Build the solution first (dotnet build).");
    }

    [Fact]
    public void RequireBuiltOutput_NullOutput_ThrowsUserError()
    {
        // A project the workspace carried with no evaluated output path at all: the null is filtered out
        // rather than probed, leaving nothing to name and the bare "no built output" refusal.
        Should.Throw<UserErrorException>(() => SpecResolver.RequireBuiltOutput("MyApp.Arch", [null]));
    }

    [Fact]
    public void RequireBuiltOutput_EvaluatedConfigMissingButSiblingBuilt_ResolvesSiblingConfiguration()
    {
        // The release-CI shape, one of several ways an evaluated path names a DLL nobody built:
        // MSBuildWorkspace evaluated OutputFilePath in Debug while `dotnet build -c Release` produced only the
        // Release output. Resolution must find the assembly that is actually on disk. It resolves because the
        // anchor walk stops at <temp>/bin — the deepest directory in the evaluated path's own chain that
        // exists — and searches down from there; nothing here depends on the evaluated path having a
        // particular number of segments above the assembly.
        using TempDirectory temp = TestTempRoot.Fresh("spec-resolver");
        string evaluatedDebugOutput = temp.PathOf("bin", "Debug", "net10.0", "MyApp.Arch.dll");
        string builtReleaseOutput = temp.WriteFile(["bin", "Release", "net10.0", "MyApp.Arch.dll"], "");

        string resolved = SpecResolver.RequireBuiltOutput("MyApp.Arch", [evaluatedDebugOutput]);

        resolved.ShouldBe(builtReleaseOutput);
    }

    [Fact]
    public void RequireBuiltOutput_SeveralFrameworkOutputsOnlyOneBuilt_ResolvesTheBuiltOne()
    {
        // A multi-target-framework spec project evaluates one output per framework, and the CLI never
        // builds — so "which one is on disk" is the question, not which one matches the host framework.
        // net10.0 sorts first ordinally and is not built, so a resolution that stopped at the first path
        // would fail here instead of loading the spec that exists.
        using TempDirectory temp = TestTempRoot.Fresh("spec-resolver");
        string modernOutput = temp.PathOf("bin", "Debug", "net10.0", "MyApp.Arch.dll");
        string legacyOutput = temp.WriteFile(["bin", "Debug", "netstandard2.0", "MyApp.Arch.dll"], "");

        string resolved = SpecResolver.RequireBuiltOutput("MyApp.Arch", [modernOutput, legacyOutput]);

        resolved.ShouldBe(legacyOutput);
    }

    [Fact]
    public void RequireBuiltOutput_SeveralFrameworkOutputsNoneBuilt_NamesEveryPathTried()
    {
        // Nothing built names every path the resolution looked at: told only about one framework's missing
        // DLL, a reader builds that framework and hits the same wall from the other one.
        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.RequireBuiltOutput("MyApp.Arch", [
                "C:/nope/bin/Debug/net10.0/MyApp.Arch.dll",
                "C:/nope/bin/Debug/netstandard2.0/MyApp.Arch.dll"
            ]));

        error.Message.ShouldBe(
            "The spec project 'MyApp.Arch' has no built output at 'C:/nope/bin/Debug/net10.0/MyApp.Arch.dll' "
            + "or 'C:/nope/bin/Debug/netstandard2.0/MyApp.Arch.dll'. Build the solution first (dotnet build).");
    }

    [Fact]
    public void RequireBuiltOutput_EvaluatedPathPresent_ReturnsItEvenWhenItIsTheIntermediateAssembly()
    {
        // The binlog-replay contract. A capture records only the compiler's `/out:`,
        // which is the obj-side assembly, so BinlogReplayer hands that path in as the evaluated output on
        // purpose. The intermediate refusal is scoped to search results only: refusing here would refuse every
        // replayed run.
        using TempDirectory temp = TestTempRoot.Fresh("spec-resolver");
        string intermediateAssembly = temp.WriteFile(["obj", "Debug", "net10.0", "MyApp.Arch.dll"], "");

        string resolved = SpecResolver.RequireBuiltOutput("MyApp.Arch", [intermediateAssembly], intermediateAssembly);

        resolved.ShouldBe(intermediateAssembly);
    }

    [Fact]
    public void RequireBuiltOutput_ReplayShapedIntermediatePathAbsent_RefusesRatherThanFindingASiblingIntermediate()
    {
        // The other half of the replay contract, and deliberate: an obj-side evaluated path
        // has no bin or artifacts ancestor, so the search cannot anchor and another configuration's
        // intermediate assembly is never substituted. Under replay the recorded path is the configuration that
        // was really built, so it going missing means the capture is stale — and "build the solution first" is
        // the right answer to that, not silently loading a different build.
        using TempDirectory temp = TestTempRoot.Fresh("spec-resolver");
        temp.WriteFile(["obj", "Release", "net10.0", "MyApp.Arch.dll"], "");
        string recordedIntermediate = temp.PathOf("obj", "Debug", "net10.0", "MyApp.Arch.dll");

        var error = Should.Throw<UserErrorException>(() =>
            SpecResolver.RequireBuiltOutput("MyApp.Arch", [recordedIntermediate]));

        error.Message.ShouldContain("Build the solution first (dotnet build).");
    }

    // The convention's refusal when its one candidate references no contract library, under whatever the load
    // reported: the rows here are about what that refusal cites.
    private static string ShouldRefuseResolution(WorkspaceDiagnostics diagnostics, string candidate = "MyApp.Arch")
    {
        return Should.Throw<UserErrorException>(() => SpecResolver.ResolveConventionProject(
                [Candidate(candidate, "C:/pkgs/Newtonsoft.Json.dll")], diagnostics))
            .Message;
    }

    // No project file on purpose: the convention has to keep treating unknown-path candidates as distinct,
    // and SpecProjectCandidate makes that a spelled-out argument rather than a default nobody reads.
    private static SpecProjectCandidate Candidate(string name, string referencePath, bool isDeclaredMember = true)
    {
        return new SpecProjectCandidate(name, [referencePath], $"C:/out/{name}.dll", FilePath: null, isDeclaredMember);
    }

    // A candidate that knows its own csproj — the shape every real (workspace-derived) candidate has.
    private static SpecProjectCandidate CandidateAt(string name, string filePath, string outputFilePath)
    {
        return new SpecProjectCandidate(name, [CoreDll], outputFilePath, filePath);
    }
}
