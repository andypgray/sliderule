using Shouldly;
using SlideRule.Cli.Settings;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Settings;

/// <summary>
///     <see cref="InvocationTargets.Resolve" /> over real directory trees: which solution and spec a run is about
///     once the nearest <c>sliderule.json</c> applies, and when no settings file applies at all.
/// </summary>
public sealed class InvocationTargetsTests : IDisposable
{
    private readonly FakeEnvironment _environment = new();

    private readonly TempDirectory _temp = TestTempRoot.Fresh("settings-targets");

    public void Dispose()
    {
        _temp.Dispose();
    }

    private string Dir(params string[] segments)
    {
        string directory = _temp.PathOf(segments);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteSettings(string directory, string json)
    {
        File.WriteAllText(Path.Combine(directory, ProjectSettings.FileName), json);
    }

    [Fact]
    public void Resolve_NoSettingsAnywhere_LeavesTheArgumentsAsTheyWere()
    {
        string root = Dir("repo");
        SolutionPaths.CreateSln(root, "App.sln");

        InvocationTargets targets = InvocationTargets.Resolve(null, "my.dll", root, _environment);

        targets.ShouldBe(new InvocationTargets(null, "my.dll", null));
    }

    [Fact]
    public void Resolve_SettingsAtTheRoot_NameTheSolutionUnderSrcAndItsSpec()
    {
        // The shape the walk alone cannot resolve: the solution sits under src/, and the root holds none.
        string root = Dir("repo");
        string solution = SolutionPaths.CreateSln(Dir("repo", "src"), "App.sln");
        WriteSettings(root, """{ "solution": "src/App.sln", "spec": "arch/App.Spec/App.Spec.csproj" }""");

        InvocationTargets targets = InvocationTargets.Resolve(null, null, root, _environment);

        targets.Solution.ShouldBe(solution);
        targets.Spec.ShouldBe(_temp.PathOf("repo", "arch", "App.Spec", "App.Spec.csproj"));
        targets.Settings.ShouldNotBeNull();
    }

    [Fact]
    public void Resolve_FromTheSolutionsOwnDirectory_TheSettingsAboveThatNameItStillApply()
    {
        // The walk stops at src/, which holds the solution, before it reaches the settings; the settings above
        // name that solution, so they apply all the same.
        string root = Dir("repo");
        string source = Dir("repo", "src");
        string solution = SolutionPaths.CreateSln(source, "App.sln");
        WriteSettings(root, """{ "solution": "src/App.sln", "spec": "App.Spec.dll" }""");

        InvocationTargets targets = InvocationTargets.Resolve(null, null, source, _environment);

        targets.Solution.ShouldBe(solution);
        targets.Spec.ShouldBe(_temp.PathOf("repo", "App.Spec.dll"));
    }

    [Fact]
    public void Resolve_ASolutionTheNearestSettingsDoNotName_TakesNothingFromThem()
    {
        // A fixture solution staged inside another repository's tree: the repository's settings name its own
        // solution, so the fixture's run must not take that repository's spec.
        string root = Dir("repo");
        SolutionPaths.CreateSln(root, "Repo.slnx");
        WriteSettings(root, """{ "solution": "Repo.slnx", "spec": "arch/Repo.Spec.csproj" }""");
        string fixture = Dir("repo", "tests", "bin", "Fixture");
        string fixtureSolution = SolutionPaths.CreateSln(fixture, "Fixture.sln");

        InvocationTargets fromItsDirectory = InvocationTargets.Resolve(null, null, fixture, _environment);
        InvocationTargets named = InvocationTargets.Resolve(fixtureSolution, null, root, _environment);

        fromItsDirectory.ShouldBe(new InvocationTargets(null, null, null));
        named.ShouldBe(new InvocationTargets(fixtureSolution, null, null));
    }

    [Fact]
    public void Resolve_AnExplicitSolutionTheSettingsName_TakesTheirSpec_AndAnExplicitSpecWins()
    {
        string root = Dir("repo");
        string solution = SolutionPaths.CreateSln(root, "App.sln");
        WriteSettings(root, """{ "solution": "App.sln", "spec": "App.Spec.dll" }""");

        InvocationTargets fromSettings = InvocationTargets.Resolve(solution, null, root, _environment);
        InvocationTargets explicitSpec = InvocationTargets.Resolve(solution, "Other.dll", root, _environment);

        fromSettings.Spec.ShouldBe(_temp.PathOf("repo", "App.Spec.dll"));
        explicitSpec.Spec.ShouldBe("Other.dll");
    }

    [Fact]
    public void Resolve_TheEnvironmentVariable_BeatsTheSettingsWalk()
    {
        // The walk from the root would take the settings' App.sln; the variable names another solution, which
        // the settings do not name, so it runs as given and takes nothing from them.
        string root = Dir("repo");
        SolutionPaths.CreateSln(root, "App.sln");
        string other = SolutionPaths.CreateSln(Dir("repo", "other"), "Other.sln");
        WriteSettings(root, """{ "solution": "App.sln", "spec": "App.Spec.dll" }""");
        _environment.SetVariable(SlideRuleEnvVars.SolutionPath, other);

        InvocationTargets targets = InvocationTargets.Resolve(null, null, root, _environment);

        targets.ShouldBe(new InvocationTargets(null, null, null));
    }

    [Fact]
    public void Resolve_TheEnvironmentVariableNamingTheSettingsSolution_TakesTheirSpec()
    {
        string root = Dir("repo");
        string solution = SolutionPaths.CreateSln(root, "App.sln");
        WriteSettings(root, """{ "solution": "App.sln", "spec": "App.Spec.dll" }""");
        _environment.SetVariable(SlideRuleEnvVars.SolutionPath, solution);

        InvocationTargets targets = InvocationTargets.Resolve(null, null, Dir("elsewhere"), _environment);

        targets.Solution.ShouldBe(solution);
        targets.Spec.ShouldBe(_temp.PathOf("repo", "App.Spec.dll"));
    }

    [Fact]
    public void Resolve_ADirectoryArgument_IsWalkedTheWayTheWorkingDirectoryIs()
    {
        string root = Dir("repo");
        string solution = SolutionPaths.CreateSln(Dir("repo", "src"), "App.sln");
        WriteSettings(root, """{ "solution": "src/App.sln" }""");

        InvocationTargets targets = InvocationTargets.Resolve(root, null, Dir("elsewhere"), _environment);

        targets.Solution.ShouldBe(solution);
    }

    [Fact]
    public void Resolve_ADirectoryHoldingSeveralSolutions_IsClimbedPastToTheSettingsAbove()
    {
        string root = Dir("repo");
        string solution = SolutionPaths.CreateSln(root, "App.sln");
        string crowded = Dir("repo", "samples");
        SolutionPaths.CreateSln(crowded, "One.sln");
        SolutionPaths.CreateSln(crowded, "Two.sln");
        WriteSettings(root, """{ "solution": "App.sln" }""");

        InvocationTargets targets = InvocationTargets.Resolve(null, null, crowded, _environment);

        targets.Solution.ShouldBe(solution);
    }

    [Fact]
    public void Resolve_SettingsNamingAMissingSolution_AreRefusedNamingTheFile()
    {
        string root = Dir("repo");
        WriteSettings(root, """{ "solution": "src/Gone.sln" }""");

        var ex = Should.Throw<UserErrorException>(() => InvocationTargets.Resolve(null, null, root, _environment));

        ex.Message.ShouldStartWith(
            $"{Path.Combine(root, ProjectSettings.FileName)} names a solution that does not exist: "
            + _temp.PathOf("repo", "src", "Gone.sln"));
    }
}
