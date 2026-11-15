using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The xUnit adapter's two incomplete-model messages, pinned directly against a synthetic
///     <see cref="WorkspaceDiagnostics" /> rather than through a fixture solution.
/// </summary>
/// <remarks>
///     <para>
///         <c>AdapterTests</c> owns the adapter's behaviour — which test fails, which cases skip — over the
///         real <c>BrokenApp</c> bed that loads partially. There is no such bed for a
///         failed restore, and building one would cost a live SDK and a private package feed to prove
///         something these two methods already decide on their own: both are pure functions of the
///         diagnostics, and everything downstream of them is
///         <see cref="SlideRule.Tests.Cli.RestoreFailureSilentEdgeE2ETests" />' subject.
///     </para>
///     <para>
///         Both messages carry their evidence inline, unlike the four CLI verbs that point at warnings printed
///         above them: in a test report there is nothing above to point at.
///     </para>
/// </remarks>
public sealed class IncompleteModelGateAdapterMessagesTests
{
    private const string BrokenProject = "C:/repo/App.Broken/App.Broken.csproj";

    private const string UnrestoredProject = "C:/repo/App.Unrestored/App.Unrestored.csproj";

    private const string MissingProject = "C:/repo/App.Vendor/App.Vendor.csproj";

    private const string LaggingProject = "C:/repo/App.Newer/App.Newer.csproj";

    private const string LagEvidence =
        $"{LaggingProject}: C# version '99' is one the C# 15 compiler this tool carries does not know.";

    private const string OtherLoadLine =
        "Msbuild failed when processing the file 'C:/repo/App.Broken/App.Broken.csproj' with message: boom";

    private const string AuditAdvisory =
        "Msbuild failed when processing the file 'C:/repo/App.Broken/App.Broken.csproj' with message: Warning "
        + "NU1903: Package 'Acme.Json' 1.0.0 has a known high severity vulnerability, "
        + "https://github.com/advisories/GHSA-0000-0000-0000";

    private const string LagLede =
        "the model is incomplete — the C# 15 compiler this tool carries cannot read all the code of 1 project";

    [Fact]
    public void AdapterRefusal_MissingProjectFile_NamesTheFileAndARemedyThatCanEndIt()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(missing: [MissingProject]));

        refusal.ShouldBe(
            "the model is incomplete — 1 project file the solution declares is not on disk, so every rule test "
            + "was skipped:\n"
            + $"  {MissingProject}\n"
            + "Every rule would be measured over a codebase missing whole projects — a green run against a "
            + "partial model would sign verdicts that were never reached. No restore or build creates a project "
            + "file: initialise the submodule or check out the tree that holds it, generate it, or remove its "
            + "entry from the solution, then retry. To check the partial model as it loaded, override "
            + "AllowWorkspaceDiagnostics to true on the test class.");
    }

    [Fact]
    public void AdapterRefusal_EveryCause_WritesTheMissingLoadRestoreAndLagBlocksInThatOrder()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics([BrokenProject], [UnrestoredProject], [MissingProject], [LaggingProject]));

        refusal.ShouldSatisfyAllConditions(
            () => refusal.ShouldContainInOrder("1 project file the solution declares is not on disk", MissingProject),
            () => refusal.ShouldContainInOrder(MissingProject, "1 project failed to load"),
            () => refusal.ShouldContainInOrder("1 project failed to load", BrokenProject),
            () => refusal.ShouldContainInOrder(BrokenProject, "NuGet packages did not resolve for 1 project"),
            () => refusal.ShouldContainInOrder("NuGet packages did not resolve for 1 project", UnrestoredProject),
            () => refusal.ShouldContainInOrder(UnrestoredProject, LagLede),
            () => refusal.ShouldContainInOrder(LagLede, LaggingProject));
    }

    [Fact]
    public void AdapterRefusal_CompilerLag_NamesTheProjectTheStakesAndANewerToolAsTheRemedy()
    {
        // Nothing done to the solution repairs code the carried compiler cannot read, so the block names the
        // packages a newer compiler ships in, and the MSBuild selection note follows the project as it does for
        // every cause a load reported.
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(lagging: [LaggingProject]));

        refusal.ShouldBe(
            LagLede + ", so every rule test was skipped:\n"
                    + $"  {LaggingProject}\n"
                    + $"  {MsBuildBootstrap.SelectionNote()}\n"
                    + "Every rule would be measured over a codebase missing the code the compiler cannot read — a green "
                    + "run against a partial model would sign verdicts that were never reached. Update SlideRule to a "
                    + "release whose compiler reads it (dotnet tool update -g SlideRule.Cli, or a newer SlideRule.Xunit "
                    + "package), then retry. To check the partial model as it loaded, override AllowWorkspaceDiagnostics "
                    + "to true on the test class.");
    }

    [Fact]
    public void AdapterRefusal_RestoreFailure_NamesTheProjectTheStakesAndTheRestoreRemedy()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(restoreFailed: [UnrestoredProject]));

        refusal.ShouldStartWith(
            "the model is incomplete — NuGet packages did not resolve for 1 project, so every rule test was "
            + "skipped:");
        refusal.ShouldContain(UnrestoredProject);
        refusal.ShouldContain(
            "Every rule about a package would be measured over a codebase where that package resolved to "
            + "nothing");
        refusal.ShouldContain("Restore the solution first (dotnet restore)");
        refusal.ShouldContain("override AllowWorkspaceDiagnostics to true on the test class.");
    }

    [Fact]
    public void AdapterRefusal_LoadFailureOnly_IsUnchanged()
    {
        // The guard the two-block design rests on: a run with one cause carries nothing of the other
        // cause's block.
        string refusal = IncompleteModelGate.AdapterRefusal(Diagnostics(failed: [BrokenProject]));

        refusal.ShouldStartWith(
            "the model is incomplete — 1 project failed to load, so every rule test was skipped:");
        refusal.ShouldNotContain("NuGet packages did not resolve");
        refusal.ShouldContain("Restore and build the solution first (dotnet build)");
    }

    [Fact]
    public void AdapterRefusal_BothCauses_WritesTheLoadBlockThenTheRestoreBlock()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics([BrokenProject], [UnrestoredProject]));

        refusal.ShouldStartWith("the model is incomplete — 1 project failed to load");
        refusal.ShouldContain(BrokenProject);
        refusal.ShouldContain(UnrestoredProject);
        refusal.ShouldContainInOrder("1 project failed to load", "NuGet packages did not resolve for 1 project");
    }

    [Fact]
    public void AdapterOptedIn_RestoreFailure_SaysWhatTheVerdictsWereReachedDespite()
    {
        // Opting in restores the rule verdicts, but a test named Workspace_LoadedCompletely cannot pass while
        // the failure it is named for is real — so it skips, and the skip reason has to name which failure.
        string skip = IncompleteModelGate.AdapterOptedIn(Diagnostics(restoreFailed: [UnrestoredProject]));

        skip.ShouldStartWith(
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, "
            + "despite NuGet packages not resolving for 1 project:");
        skip.ShouldContain(UnrestoredProject);
    }

    [Fact]
    public void AdapterOptedIn_CompilerLag_SaysWhatTheVerdictsWereReachedDespite()
    {
        string skip = IncompleteModelGate.AdapterOptedIn(Diagnostics(lagging: [LaggingProject]));

        skip.ShouldBe(
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + "the C# 15 compiler this tool carries not reading all the code of 1 project:\n"
            + $"  {LaggingProject}");
    }

    [Fact]
    public void AdapterRefusal_WithLoadLines_WritesThemBeneathTheCauseBlocksWithNoSecondSelectionNote()
    {
        // A test report has no stderr above it, so the lines the CLI prints as warnings ride the failure itself,
        // after the remedy. The cause block already carries the selection note, so the load lines add none.
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics(lagging: [LaggingProject], loadFailures: [LagEvidence, OtherLoadLine]));

        refusal.ShouldBe(
            LagLede + ", so every rule test was skipped:\n"
                    + $"  {LaggingProject}\n"
                    + $"  {MsBuildBootstrap.SelectionNote()}\n"
                    + "Every rule would be measured over a codebase missing the code the compiler cannot read — a green "
                    + "run against a partial model would sign verdicts that were never reached. Update SlideRule to a "
                    + "release whose compiler reads it (dotnet tool update -g SlideRule.Cli, or a newer SlideRule.Xunit "
                    + "package), then retry. To check the partial model as it loaded, override AllowWorkspaceDiagnostics "
                    + "to true on the test class.\n"
                    + "The workspace load reported:\n"
                    + $"  {LagEvidence}\n"
                    + $"  {OtherLoadLine}");
    }

    [Fact]
    public void AdapterOptedIn_WithLoadLines_WritesThemBeneathTheCauseBlock()
    {
        string skip = IncompleteModelGate.AdapterOptedIn(
            Diagnostics(lagging: [LaggingProject], loadFailures: [LagEvidence]));

        skip.ShouldBe(
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + "the C# 15 compiler this tool carries not reading all the code of 1 project:\n"
            + $"  {LaggingProject}\n"
            + "The workspace load reported:\n"
            + $"  {LagEvidence}");
    }

    [Fact]
    public void AdapterRefusal_AuditAdvisoriesOnly_WritesNoLoadBlock()
    {
        // An advisory says nothing about how the codebase loaded, and an empty block under a colon lede reads as
        // truncated output, so a load whose only lines are advisories adds nothing beneath the causes.
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics(failed: [BrokenProject], loadFailures: [AuditAdvisory]));

        refusal.ShouldBe(IncompleteModelGate.AdapterRefusal(Diagnostics(failed: [BrokenProject])));
    }

    [Fact]
    public void AdapterRefusal_AuditAdvisoryBesideALoadLine_CarriesOnlyTheLoadLine()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics(failed: [BrokenProject], loadFailures: [AuditAdvisory, OtherLoadLine]));

        refusal.ShouldEndWith(
            "on the test class.\n"
            + "The workspace load reported:\n"
            + $"  {OtherLoadLine}");
    }

    [Fact]
    public void AdapterSkipReason_NamesEveryCauseInTheOnePlaceTheProjectsCanBeRead()
    {
        // Deliberately constant and deliberately short: the projects themselves ride
        // Workspace_LoadedCompletely's failure, so this points there rather than repeating them once per rule.
        IncompleteModelGate.AdapterSkipReason.ShouldBe(
            "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely "
            + "for the projects that are not on disk, failed to load, whose NuGet packages did not resolve or whose "
            + "code the tool's C# compiler cannot read.");
    }

    private static WorkspaceDiagnostics Diagnostics(
        IReadOnlyList<string>? failed = null, IReadOnlyList<string>? restoreFailed = null,
        IReadOnlyList<string>? missing = null, IReadOnlyList<string>? lagging = null,
        IReadOnlyList<string>? loadFailures = null)
    {
        return WorkspaceDiagnostics.None with
        {
            LoadFailures = loadFailures ?? [],
            FailedProjects = failed ?? [],
            RestoreFailedProjects = restoreFailed ?? [],
            MissingProjects = missing ?? [],
            CompilerLagProjects = lagging ?? []
        };
    }
}
