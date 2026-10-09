using Shouldly;
using Xunit;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     How a load's NuGet audit lines become advisory entries (<see cref="NuGetAuditDiagnostics.Group" />): one
///     entry per distinct message, naming the projects whose frames carried it, over lines in the shapes real
///     loads deliver. Pure over strings, so no workspace is opened and no collection is needed.
/// </summary>
public sealed class PackageAuditTests
{
    private const string LoadFailure = "Project 'App.Broken' failed to load: simulated workspace-load failure.";

    [Fact]
    public void Group_OneAdvisoryAcrossProjectsAndFrameworks_IsOneEntryNamingEachProjectOnce()
    {
        // MSBuild reports an advisory once per project per framework, and Roslyn's frame names the project but
        // not the framework — so two projects on two frameworks arrive as four lines, two pairs of them alike.
        string app = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);
        string tests = CapturedAuditLines.Framed(
            "/src/App.Tests/App.Tests.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([app, tests, app, tests]);

        entries.ShouldHaveEntries((CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Tests"]));
    }

    [Fact]
    public void Group_WindowsAndPosixFramesOfOneProject_NameItTheSameWay()
    {
        // The name is read after the last separator of either kind, so a Windows capture read on any host
        // names the project a POSIX one does.
        string windows = CapturedAuditLines.Framed(@"C:\src\App\App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);
        string posix = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([windows, posix]);

        entries.ShouldHaveEntries((CapturedAuditLines.BuildTasksGitAdvisory, ["App"]));
    }

    [Fact]
    public void Group_OnePackageWithSeveralAdvisories_IsOneEntryPerAdvisory()
    {
        // Twelve advisories against one version are twelve findings: the whole message is the key, so the
        // package name they share never merges them.
        IReadOnlyList<string> lines = CapturedAuditLines.MessagePackAdvisories
            .Select(body => CapturedAuditLines.Framed("/src/Bus.MessagePack/Bus.MessagePack.csproj", body))
            .ToList();

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group(lines);

        entries.Select(entry => entry.Message)
            .ShouldBe(CapturedAuditLines.MessagePackAdvisories.Order(StringComparer.Ordinal));
        entries.ShouldAllBe(entry => entry.Projects!.Single() == "Bus.MessagePack");
    }

    [Fact]
    public void Group_TheAuditFetchFailure_GroupsLikeAnAdvisory()
    {
        // NU1900 names no package and links no advisory, and it is still the audit speaking rather than the load:
        // an unreachable feed is about the machine, never about the solution.
        string line = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.AuditFetchFailure);
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { LoadFailures = [line] };

        diagnostics.PackageAudit.ShouldHaveEntries((CapturedAuditLines.AuditFetchFailure, ["App"]));
        diagnostics.Rendered.ShouldBeEmpty();
    }

    [Fact]
    public void Group_AnEscalatedAdvisory_IsAnEntryApartFromTheSameAdvisoryUnescalated()
    {
        // The prefix TreatWarningsAsErrors adds stays in the message, so the two read as NuGet wrote them: one
        // project escalates the advisory and the other does not, and that difference is worth seeing.
        string escalated = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.EscalatedAdvisory);
        string plain = CapturedAuditLines.Framed("/src/Tools/Tools.csproj", CapturedAuditLines.DrawingCommonAdvisory);

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([escalated, plain]);

        entries.ShouldHaveEntries(
            (CapturedAuditLines.DrawingCommonAdvisory, ["Tools"]),
            (CapturedAuditLines.EscalatedAdvisory, ["App"]));
    }

    [Fact]
    public void Group_ALocalizedAuditFetchFailure_StaysInTheStreamWithTheNote()
    {
        // NuGet ships its words in many languages and none of them is a contract, so a German NU1900 is a line
        // nothing recognises: it keeps its place in the stream, and the stream keeps its MSBuild note.
        string line = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.GermanAuditFetchFailure);
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with { LoadFailures = [line] };

        diagnostics.PackageAudit.ShouldBeEmpty();
        diagnostics.Rendered.ShouldBe([line, MsBuildBootstrap.SelectionNote()]);
    }

    [Theory]
    [InlineData(CapturedAuditLines.CodedLine)]
    [InlineData(CapturedAuditLines.ConsoleLoggerLine)]
    public void Group_ALineWithNoFrame_IsAnEntryOfItsOwnWithNoProjects(string line)
    {
        // Without Roslyn's frame nothing says which project spoke, so the entry names none rather than guessing
        // one out of a path inside the message.
        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([line]);

        entries.ShouldHaveEntries((line, null));
    }

    [Fact]
    public void Group_TheSameAdvisoryWithAndWithoutAFrame_IsTwoEntriesTheNamedOneFirst()
    {
        // An unnamed line never merges into a named entry: that would claim the named projects are the only
        // ones that reported it.
        string framed = CapturedAuditLines.Framed("/src/App/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

        IReadOnlyList<PackageAuditEntry> entries =
            NuGetAuditDiagnostics.Group([CapturedAuditLines.BuildTasksGitAdvisory, framed]);

        entries.ShouldHaveEntries(
            (CapturedAuditLines.BuildTasksGitAdvisory, ["App"]),
            (CapturedAuditLines.BuildTasksGitAdvisory, null));
    }

    [Fact]
    public void Group_APathCarryingAnApostrophe_StillNamesItsProject()
    {
        // The frame is split at its separator, not at the first quote after the path opens.
        string line = CapturedAuditLines.Framed(
            "/src/Team's Tools/App/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([line]);

        entries.ShouldHaveEntries((CapturedAuditLines.BuildTasksGitAdvisory, ["App"]));
    }

    [Fact]
    public void Group_ProjectNames_AreOrdinalOrdered()
    {
        // Ordinal rather than culture-aware, so the order is the same on every machine: capitals before lower
        // case, whatever the letter.
        IReadOnlyList<string> lines = new[] { "app.Web", "Zeta", "App.Web", "App" }
            .Select(name => CapturedAuditLines.Framed($"/src/{name}/{name}.csproj", CapturedAuditLines.BuildTasksGitAdvisory))
            .ToList();

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group(lines);

        entries.ShouldHaveEntries((CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Web", "Zeta", "app.Web"]));
    }

    [Fact]
    public void Group_TwoProjectFilesSharingAName_AreOneName()
    {
        // A project is known by its name here as it is in the model, so two App.csproj files in different
        // directories are one name, not two.
        string first = CapturedAuditLines.Framed("/src/One/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);
        string second = CapturedAuditLines.Framed("/src/Two/App.csproj", CapturedAuditLines.BuildTasksGitAdvisory);

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([first, second]);

        entries.ShouldHaveEntries((CapturedAuditLines.BuildTasksGitAdvisory, ["App"]));
    }

    [Fact]
    public void Group_NoAdvisories_IsEmpty()
    {
        // A load failure, and a load failure whose project happens to live under a directory named for an audit
        // code: the frame's path is never read for the code, so neither is the audit speaking.
        string underAnAuditCode = CapturedAuditLines.Framed(
            "/src/NU1903-repro/App.csproj", "The imported project was not found.");

        IReadOnlyList<PackageAuditEntry> entries = NuGetAuditDiagnostics.Group([LoadFailure, underAnAuditCode]);

        entries.ShouldBeEmpty();
    }

    [Fact]
    public void Group_InputOrder_DoesNotChangeTheEntries()
    {
        // The load reports in whatever order its projects finished, so the entries cannot depend on it.
        string[] lines =
        [
            CapturedAuditLines.Framed("/src/B/B.csproj", CapturedAuditLines.MessagePackAdvisories[0]),
            CapturedAuditLines.Framed("/src/A/A.csproj", CapturedAuditLines.BuildTasksGitAdvisory),
            CapturedAuditLines.Framed("/src/A/A.csproj", CapturedAuditLines.MessagePackAdvisories[0])
        ];
        (string Message, string[]? Projects)[] expected =
        [
            (CapturedAuditLines.MessagePackAdvisories[0], ["A", "B"]),
            (CapturedAuditLines.BuildTasksGitAdvisory, ["A"])
        ];

        NuGetAuditDiagnostics.Group(lines)
            .ShouldHaveEntries(expected);
        NuGetAuditDiagnostics.Group([.. lines.Reverse()])
            .ShouldHaveEntries(expected);
    }

    [Fact]
    public void Describe_OneProject_CountsItInTheSingular()
    {
        var entry = new PackageAuditEntry(CapturedAuditLines.BuildTasksGitAdvisory, ["App"]);

        entry.Describe()
            .ShouldBe($"{CapturedAuditLines.BuildTasksGitAdvisory} (1 project: 'App')");
    }

    [Fact]
    public void Describe_SeveralProjects_JoinsThemAsTheMergeNotesDo()
    {
        // Quoted, and joined with no Oxford comma, which is how every other list of projects on this stream reads.
        var entry = new PackageAuditEntry(CapturedAuditLines.BuildTasksGitAdvisory, ["App", "App.Tests", "Tools"]);

        entry.Describe()
            .ShouldBe($"{CapturedAuditLines.BuildTasksGitAdvisory} (3 projects: 'App', 'App.Tests' and 'Tools')");
    }

    [Fact]
    public void Describe_NoProjects_IsTheMessageAlone()
    {
        var entry = new PackageAuditEntry(CapturedAuditLines.CodedLine, null);

        entry.Describe()
            .ShouldBe(CapturedAuditLines.CodedLine);
    }
}
