using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Hooks.Handoff;
using SlideRule.Cli.Mcp;
using SlideRule.Extraction;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     What the hook and a warm server compare before a hand-off: the identity both resolve, the build fingerprint
///     a copy of the build keeps, and the configuration fingerprint with its deny-list.
/// </summary>
public sealed class HandoffIdentityTests
{
    private const string Category = "handoff-identity";

    [Fact]
    public void Resolve_SettingsFileSpecAndTheSameSpecPassed_AgreeOnTheIdentity()
    {
        // Arrange: a server started with no arguments takes both paths from the settings file; the hook names them.
        using TempDirectory root = TestTempRoot.Fresh(Category);
        string solution = root.WriteFile(["App.sln"], "");
        root.WriteFile(["sliderule.json"], """{ "solution": "App.sln", "spec": "arch/Spec.csproj" }""");
        string elsewhere = Directory.CreateDirectory(root.PathOf("src", "deep")).FullName;
        var environment = new FakeEnvironment();

        // Act
        HandoffIdentity fromSettings = HandoffIdentity.Resolve(null, null, root.Path, environment, out string discovered);
        HandoffIdentity fromArguments = HandoffIdentity.Resolve(
            OperatingSystem.IsWindows() ? solution.ToUpperInvariant() : solution,
            Path.Combine("..", "..", "arch", "Spec.csproj"), elsewhere, environment, out _);

        // Assert: one identity, so one endpoint, the solution's case differing only where the OS ignores it.
        discovered.ShouldBe(solution);
        fromArguments.ShouldBe(fromSettings);
        fromSettings.Spec.ShouldEndWith(PathComparison.Fold(Path.Combine("arch", "Spec.csproj")));
    }

    [Fact]
    public void EndpointName_DiffersWithTheSpec()
    {
        // Arrange
        var convention = new HandoffIdentity("c:/repo/app.sln", HandoffIdentity.ConventionSpec, 1, "b");

        // Act
        string other = (convention with { Spec = "c:/repo/spec.dll" }).EndpointName;

        // Assert
        other.ShouldNotBe(convention.EndpointName);
        convention.EndpointName.ShouldStartWith("sliderule-");
    }

    [Fact]
    public void BuildFingerprint_IsTheModuleIdsOfTheCheckAssemblies_WhereverTheyAreCopied()
    {
        // Arrange: the build's assemblies copied elsewhere, as the hook and the server each run from a copy.
        using TempDirectory copy = TestTempRoot.Fresh(Category);
        Type[] anchors = [typeof(Arch), typeof(ProjectArtifactFacts), typeof(PathCanonicalizer), typeof(HandoffIdentity)];
        List<string> copied = anchors.Select(anchor => anchor.Assembly.Location)
            .Select(location => CopyInto(copy.Path, location))
            .ToList();

        // Act
        string fromCopies = FileStamping.HashText(string.Join("\n", copied.Select(ModuleVersionIdOf)));

        // Assert
        BuildFingerprint.Current.ShouldBe(fromCopies);
    }

    [Fact]
    public void Configuration_ADeniedVariableMatches_AndABuildPropertyDiffersByName()
    {
        // Arrange
        using TempDirectory directory = TestTempRoot.Fresh(Category);
        FakeEnvironment server = new FakeEnvironment().SetVariable("CI", "true");
        FakeEnvironment hook = new FakeEnvironment().SetVariable("CI", "true")
            .SetVariable(SlideRuleEnvVars.HookStateDirectory, directory.Path)
            .SetVariable(SlideRuleEnvVars.ClaudeProjectDirectory, directory.Path);
        FakeEnvironment configured = new FakeEnvironment().SetVariable("CI", "true").SetVariable("DefineConstants", "X");

        // Act
        HandoffConfiguration fingerprint = HandoffConfiguration.Capture(server, directory.Path);
        IReadOnlyList<string> fromTheHook = fingerprint.DifferingInputs(HandoffConfiguration.Capture(hook, directory.Path).Digests);
        IReadOnlyList<string> fromABuild = fingerprint.DifferingInputs(
            HandoffConfiguration.Capture(configured, directory.Path).Digests);

        // Assert
        fromTheHook.ShouldBeEmpty();
        fromABuild.ShouldBe([OperatingSystem.IsWindows() ? "env:DEFINECONSTANTS" : "env:DefineConstants"]);
    }

    [Fact]
    public void Canonicalize_RemovesTheShellBookkeeping_AndPutsThePathInItsSearchOrder()
    {
        // Arrange: the hook's environment as Git's launchers leave it, with its own shell bookkeeping, a repeated
        // directory, no trailing separators where the server's has them, and on Windows a repeat in another case.
        using TempDirectory directory = TestTempRoot.Fresh(Category);
        char separator = Path.PathSeparator;
        char slash = Path.DirectorySeparatorChar;
        string first = Path.Combine(directory.Path, "Tools");
        string second = Path.Combine(directory.Path, "sdk");
        string repeat = OperatingSystem.IsWindows() ? first.ToLowerInvariant() : first;
        FakeEnvironment server = new FakeEnvironment()
            .SetVariable("PATH", $"{first}{slash}{separator}{second}{slash}")
            .SetVariable("SHELL", "/bin/bash");
        FakeEnvironment hook = new FakeEnvironment()
            .SetVariable("PATH", $"{first}{separator}{repeat}{separator}{second}")
            .SetVariable("SHELL", "/usr/bin/bash")
            .SetVariable("PWD", directory.Path)
            .SetVariable("OLDPWD", directory.Path)
            .SetVariable("SHLVL", "3")
            .SetVariable("_", "/usr/bin/dotnet");
        FakeEnvironment reordered = new FakeEnvironment().SetVariable("PATH", $"{second}{separator}{first}");

        // Act
        HandoffConfiguration.Canonicalize(server);
        HandoffConfiguration.Canonicalize(hook);
        HandoffConfiguration.Canonicalize(reordered);

        // Assert: both sides now hold the one value MSBuild will read, and a different search order stays different.
        hook.GetVariable("PATH").ShouldBe($"{first}{separator}{second}");
        server.GetVariable("PATH").ShouldBe($"{first}{separator}{second}");
        reordered.GetVariable("PATH").ShouldBe($"{second}{separator}{first}");
        hook.Variables().Keys.ShouldBe([SlideRuleEnvVars.CacheDirectory, "PATH"], ignoreOrder: true);
    }

    [Fact]
    public void ServerBinding_CapturesTheCanonicalEnvironment()
    {
        // Arrange
        using TempDirectory root = TestTempRoot.Fresh(Category);
        string solution = root.WriteFile(["App.sln"], "");
        FakeEnvironment environment = new FakeEnvironment().SetVariable("SHELL", "/bin/bash");

        // Act
        HandoffBinding binding = HandoffBinding.For(new McpServerBinding(solution, null, root.Path), environment);

        // Assert
        environment.GetVariable("SHELL").ShouldBeNull();
        binding.Configuration.Digests.Keys.ShouldNotContain("env:SHELL");
    }

    [Fact]
    public void Configuration_TheNearestGlobalJsonIsPartOfIt()
    {
        // Arrange
        using TempDirectory pinned = TestTempRoot.Fresh(Category);
        string below = Directory.CreateDirectory(pinned.PathOf("a", "b")).FullName;
        var environment = new FakeEnvironment();
        HandoffConfiguration before = HandoffConfiguration.Capture(environment, below);
        pinned.WriteFile(["global.json"], """{ "sdk": { "version": "10.0.100" } }""");

        // Act
        IReadOnlyList<string> differing = before.DifferingInputs(HandoffConfiguration.Capture(environment, below).Digests);

        // Assert
        differing.ShouldBe(["file:global.json"]);
    }

    [Fact]
    public void EveryProductVariable_IsEitherFingerprintedOrDenied_AsDecided()
    {
        // Arrange: a new variable must be placed on one side on purpose. Only these can reach a check's answer.
        string[] fingerprinted =
        [
            SlideRuleEnvVars.SolutionPath, SlideRuleEnvVars.VsInstallPath, SlideRuleEnvVars.CacheDirectory,
            SlideRuleEnvVars.UserProfile, SlideRuleEnvVars.Home
        ];

        // Act
        List<string> kept = DeclaredConstants.Of<string>(typeof(SlideRuleEnvVars))
            .Where(static name => !HandoffConfiguration.IsDenied(name))
            .ToList();

        // Assert
        kept.ShouldBe(fingerprinted, ignoreOrder: true);
    }

    [Theory]
    [InlineData(null, 600)]
    [InlineData("", 600)]
    [InlineData("0", 600)]
    [InlineData("-5", 600)]
    [InlineData("ten", 600)]
    [InlineData("90", 90)]
    public void ClientTimeout_IsAPositiveWholeNumberOfSeconds_OrTheDefault(string? value, int seconds)
    {
        // Arrange
        FakeEnvironment environment = new FakeEnvironment().SetVariable(SlideRuleEnvVars.HookTimeout, value);

        // Act
        TimeSpan timeout = HookBudget.ClientTimeoutFrom(environment);

        // Assert
        timeout.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void DefaultClientTimeout_IsTheSnippetsStopTimeout()
    {
        // Arrange
        string snippet = RepoRoot.ReadText("hooks/settings.snippet.json");
        using JsonDocument document = JsonDocument.Parse(snippet);

        // Act
        int stop = document.RootElement.GetProperty("hooks").GetProperty("Stop")[0].GetProperty("hooks")[0]
            .GetProperty("timeout").GetInt32();

        // Assert
        TimeSpan.FromSeconds(stop).ShouldBe(HookBudget.DefaultClientTimeout);
    }

    [Theory]
    [InlineData(600, 0, true)]
    [InlineData(600, 459, true)]
    [InlineData(600, 460, false)]
    [InlineData(140, 0, false)]
    public void LeavesRoomForWarm_OnlyWhileTheColdReserveSurvivesAWarmAttempt(int timeout, int elapsed, bool room)
    {
        // Arrange: 600 s less the 200 ms discovery limit and the 20 s warm limit leaves 120 s until 459.8 s in.
        var origin = new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);
        var budget = new HookBudget(origin, TimeSpan.FromSeconds(timeout));

        // Act
        bool leaves = budget.LeavesRoomForWarm(origin + TimeSpan.FromSeconds(elapsed));

        // Assert
        leaves.ShouldBe(room);
    }

    private static string CopyInto(string directory, string assembly)
    {
        string target = Path.Combine(directory, Path.GetFileName(assembly));
        File.Copy(assembly, target);
        return target;
    }

    private static string ModuleVersionIdOf(string assembly)
    {
        using FileStream stream = File.OpenRead(assembly);
        using var reader = new PEReader(stream);
        MetadataReader metadata = reader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("N");
    }
}
