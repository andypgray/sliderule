using Shouldly;
using SlideRule.Roslyn.Diagnostics;
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
    public void AdapterRefusal_EveryCause_WritesTheMissingBlockThenTheLoadBlockThenTheRestoreBlock()
    {
        string refusal = IncompleteModelGate.AdapterRefusal(
            Diagnostics([BrokenProject], [UnrestoredProject], [MissingProject]));

        refusal.ShouldSatisfyAllConditions(
            () => refusal.ShouldContainInOrder("1 project file the solution declares is not on disk", MissingProject),
            () => refusal.ShouldContainInOrder(MissingProject, "1 project failed to load"),
            () => refusal.ShouldContainInOrder("1 project failed to load", BrokenProject),
            () => refusal.ShouldContainInOrder(BrokenProject, "NuGet packages did not resolve for 1 project"),
            () => refusal.ShouldContainInOrder("NuGet packages did not resolve for 1 project", UnrestoredProject));
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
    public void AdapterSkipReason_NamesBothCausesInTheOnePlaceTheProjectsCanBeRead()
    {
        // Deliberately constant and deliberately short: the projects themselves ride
        // Workspace_LoadedCompletely's failure, so this points there rather than repeating them once per rule.
        IncompleteModelGate.AdapterSkipReason.ShouldBe(
            "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely "
            + "for the projects that are not on disk, failed to load or whose NuGet packages did not resolve.");
    }

    private static WorkspaceDiagnostics Diagnostics(
        IReadOnlyList<string>? failed = null, IReadOnlyList<string>? restoreFailed = null,
        IReadOnlyList<string>? missing = null)
    {
        return WorkspaceDiagnostics.None with
        {
            FailedProjects = failed ?? [],
            RestoreFailedProjects = restoreFailed ?? [],
            MissingProjects = missing ?? []
        };
    }
}
