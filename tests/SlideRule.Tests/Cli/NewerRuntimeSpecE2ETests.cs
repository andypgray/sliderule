using Shouldly;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Cli;

/// <summary>
///     A <c>net11.0</c> spec, whose product reference a <c>net10.0</c> spec cannot hold, through the CLI as a child
///     process: on the newest installed runtime it loads and checks green, and held on .NET 10 it is refused by name
///     rather than dying in discovery on <c>System.Runtime</c>.
/// </summary>
public sealed class NewerRuntimeSpecE2ETests(NewerRuntimeSpecE2ETests.Net11Bed bed)
    : IClassFixture<NewerRuntimeSpecE2ETests.Net11Bed>
{
    private const string RuleId = "net11/lib-calls-no-second";

    private static readonly Lazy<string> InstalledSdks = new(() => DotnetList("--list-sdks"));

    private static readonly Lazy<string> InstalledRuntimes = new(() => DotnetList("--list-runtimes"));

    [Fact]
    public async Task Check_ANet11Spec_LoadsOnTheNewestRuntimeAndChecksGreen()
    {
        SkipUnlessSdk11();

        // Arrange
        (string solution, string specDll) = bed.Built;

        // Act
        ChildProcess.ProcessResult result = await RunCliAsync([], solution, specDll, bed.Directory);

        // Assert
        string transcript = result.Transcript();
        result.ExitCode.ShouldBe(0, transcript);
    }

    [Fact]
    public async Task Check_ANet11SpecOnRuntime10_IsRefusedNamingBothVersions()
    {
        SkipUnlessSdk11();
        Assert.SkipUnless(
            InstalledRuntimes.Value.Contains("Microsoft.NETCore.App 10.", StringComparison.Ordinal),
            "Holding the CLI on .NET 10 needs the .NET 10 runtime; without it --roll-forward Major takes 11.");

        // Arrange
        (string solution, string specDll) = bed.Built;

        // Act
        ChildProcess.ProcessResult result = await RunCliAsync(["--roll-forward", "Major"], solution, specDll, bed.Directory);

        // Assert
        string transcript = result.Transcript();
        result.ExitCode.ShouldBe(2, transcript);
        result.StandardError.ShouldContain(
            "The spec assembly 'Net11.Architecture' was built for .NET 11, and sliderule is running on .NET 10.",
            customMessage: transcript);
        result.StandardError.ShouldContain(
            "Install the .NET 11 runtime, since sliderule runs on the newest .NET runtime installed, or retarget the "
            + "spec project to a framework .NET 10 runs, such as net10.0, and rebuild it.",
            customMessage: transcript);
    }

    private static void SkipUnlessSdk11()
    {
        bool sdk11 = InstalledSdks.Value.Split('\n')
            .Any(line => line.StartsWith("11.", StringComparison.Ordinal));
        Assert.SkipUnless(sdk11, "A net11.0 spec needs SDK 11 to build.");
    }

    // A net11.0 library and a net11.0 spec whose rule anchors one of its members by typeof(), built with the newest
    // SDK installed: no global.json lies above the temp root.
    private static (string Solution, string SpecDll) BuildNet11Bed(TempDirectory bed)
    {
        string contract = Path.Combine(AppContext.BaseDirectory, "SlideRule.dll");
        // Stops MSBuild's upward walk at the bed, so nothing above the temp root reaches the build.
        bed.WriteFile(["Directory.Build.props"], "<Project />\n");
        bed.WriteFile(["Net11.Lib", "Net11.Lib.csproj"], Project(""));
        bed.WriteFile(
            ["Net11.Lib", "Helpers.cs"],
            "namespace Net11.Lib;\n\npublic static class Helpers\n{\n    public static int Second(int[] values) => values[1];\n}\n");
        bed.WriteFile(
            ["Net11.Architecture", "Net11.Architecture.csproj"],
            Project(
                $"""
                   <ItemGroup>
                     <Reference Include="SlideRule"><HintPath>{contract}</HintPath></Reference>
                     <ProjectReference Include="../Net11.Lib/Net11.Lib.csproj" />
                   </ItemGroup>
                 """));
        bed.WriteFile(
            ["Net11.Architecture", "Net11ArchitectureSpec.cs"],
            $$"""
              using Net11.Lib;
              using SlideRule;

              namespace Net11.Architecture;

              public sealed class Net11ArchitectureSpec : IArchitectureSpec
              {
                  public void Define(Arch arch)
                  {
                      arch.Rule("{{RuleId}}")
                          .Enforce(arch.Namespace("Net11.Lib")
                              .MustNotUse(arch.Member(typeof(Helpers), nameof(Helpers.Second))))
                          .Because("A typeof() anchor on a net11.0 product type.");
                  }
              }
              """);
        string solution = bed.WriteFile(
            ["Net11.slnx"],
            ScratchProjects.Solution("Net11.Lib/Net11.Lib.csproj", "Net11.Architecture/Net11.Architecture.csproj"));

        DotnetCli.Run($"build \"{solution}\"", bed.Path);
        string specDll = bed.PathOf("Net11.Architecture", "bin", "Debug", "net11.0", "Net11.Architecture.dll");
        return (solution, specDll);
    }

    private static string Project(string items)
    {
        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                {items}
                </Project>
                """;
    }

    private static async Task<ChildProcess.ProcessResult> RunCliAsync(
        string[] hostOptions,
        string solution,
        string specDll,
        string workingDirectory)
    {
        string[] arguments = [.. hostOptions, TestsBinCli.Dll(), "check", solution, "--spec", specDll, "--no-cache"];
        return await ChildProcess.RunAsync(
            DotnetCommand.StartInfo(arguments, workingDirectory),
            TimeSpan.FromMinutes(3),
            ct: Ct);
    }

    // Uncancellable: the lists are shared by every test in the class.
    private static string DotnetList(string option)
    {
        ChildProcess.ProcessResult result = ChildProcess.Run(DotnetCommand.StartInfo([option], AppContext.BaseDirectory));
        if (result.ExitCode != 0) throw result.FailureOf($"dotnet {option}");

        return result.StandardOutput.Replace("\r", "", StringComparison.Ordinal);
    }

    /// <summary>
    ///     The class's one net11.0 bed, built on first use: each test only runs <c>check --no-cache</c> against it,
    ///     which writes nothing into the bed. A test reads <see cref="Built" /> only after its own skips, so a
    ///     machine without SDK 11 builds nothing.
    /// </summary>
    public sealed class Net11Bed : IDisposable
    {
        private readonly TempDirectory _directory = TestTempRoot.Fresh("net11-spec");

        // Uncancellable: the build is shared by every test in the class.
        private readonly Lazy<(string Solution, string SpecDll)> _built;

        /// <summary>Creates the bed's directory; nothing is written or built until <see cref="Built" /> is read.</summary>
        public Net11Bed()
        {
            _built = new Lazy<(string Solution, string SpecDll)>(() => BuildNet11Bed(_directory));
        }

        /// <summary>The bed's root, the working directory each test runs the CLI from.</summary>
        internal string Directory => _directory.Path;

        /// <summary>The built bed's solution and spec assembly, built by the first read.</summary>
        internal (string Solution, string SpecDll) Built => _built.Value;

        /// <summary>Deletes the bed.</summary>
        public void Dispose()
        {
            _directory.Dispose();
        }
    }
}
