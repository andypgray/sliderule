using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Replay;
using SlideRule.Tests.Replay;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The compiler-lag guard: the pure verdict, the metadata readers, and the list a load and a binlog replay give over
///     real projects whose C# version or source generators the carried compiler cannot read.
/// </summary>
public sealed class CompilerLagFailuresTests
{
    public static TheoryData<string?, LanguageVersion, int, int, string?> JudgeRows => new()
    {
        // A version the carried parser rejected leaves the load at Default, whatever the yardstick reads.
        { "99", LanguageVersion.Default, 0, 14, nameof(CompilerLag.UnknownVersion) },
        { "15.0", LanguageVersion.Default, 15, 14, nameof(CompilerLag.UnknownVersion) },
        { "14.0", LanguageVersion.CSharp14, 14, 14, null },
        // An alias, or nothing asked for, lags only where the build's compiler is newer than the carried one.
        { "latest", LanguageVersion.Latest, 16, 15, nameof(CompilerLag.NewerDefault) },
        { "preview", LanguageVersion.Preview, 16, 15, nameof(CompilerLag.NewerDefault) },
        { "", LanguageVersion.Default, 16, 15, nameof(CompilerLag.NewerDefault) },
        { "default", LanguageVersion.Default, 16, 15, nameof(CompilerLag.NewerDefault) },
        { "latest", LanguageVersion.Latest, 15, 15, null },
        { "preview", LanguageVersion.Preview, 15, 15, null },
        { "", LanguageVersion.Default, 15, 15, null },
        // An unreadable yardstick never makes an alias lag.
        { "latest", LanguageVersion.Latest, 0, 15, null },
        { "", LanguageVersion.Default, 0, 15, null },
        // A failed evaluation says nothing about what the project asked for.
        { null, LanguageVersion.Default, 16, 15, null }
    };

    [Theory]
    [MemberData(nameof(JudgeRows))]
    public void Judge_DecidesFromTheLoadAndTheYardstick(
        string? asked, LanguageVersion specified, int yardstickHighest, int carriedHighest, string? expected)
    {
        var verdict = CompilerLagFailures.Judge(asked, specified, yardstickHighest, carriedHighest)?.ToString();

        verdict.ShouldBe(expected);
    }

    [Fact]
    public void HighestLanguageVersion_OverTheCarriedCompiler_IsTheCarriedHighest()
    {
        string carried = typeof(LanguageVersion).Assembly.Location;

        CompilerLagFailures.HighestLanguageVersion(carried).ShouldBe(CompilerLagFailures.CarriedHighestLanguageVersion);
        CompilerLagFailures.CarriedHighestLanguageVersion.ShouldBe(15);
    }

    [Fact]
    public void HighestLanguageVersion_OverAFileThatIsNoAssembly_IsZero()
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-not-an-assembly");
        string path = temp.WriteFile(["Microsoft.CodeAnalysis.CSharp.dll"], "not metadata");

        CompilerLagFailures.HighestLanguageVersion(path).ShouldBe(0);
    }

    [Theory]
    [InlineData("C:/dotnet/sdk/11.0.100/Sdks/Microsoft.NET.Sdk/analyzers/A.dll", "C:/dotnet/sdk/11.0.100")]
    [InlineData("C:/dotnet/sdk/11.0.100/Roslyn", "C:/dotnet/sdk/11.0.100")]
    [InlineData("C:/repo/sdk/tools/Sdks/A.dll", null)]
    [InlineData("C:/nuget/microsoft.net.compilers.toolset/5.12.0/tasks/netcore", null)]
    public void SdkDirectoryOf_NamesTheSdkVersionDirectoryOnly(string path, string? expected)
    {
        string? normalizedExpected = expected?.Replace('/', Path.DirectorySeparatorChar);

        CompilerLagFailures.SdkDirectoryOf(path).ShouldBe(normalizedExpected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_ALangVersionTheCarriedCompilerRejects_ListsTheProject(bool replay)
    {
        // The build refuses the version too, and its binlog still records the compiler call a replay reads.
        using TempDirectory temp = TestTempRoot.Fresh("lag-langversion");
        string csproj = WriteProject(temp, "App", "<LangVersion>99</LangVersion>");
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) =
            await ShouldReadAsync(solution, replay, buildFails: true);

        verdict.CompilerLagProjects.ShouldBe([csproj]);
        diagnostics.ShouldContain(
            $"'{csproj}' asks for C# version '99', which the C# 15 compiler this tool carries does not know, so its "
            + "code was read as another C# version.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_APlainProject_ListsNothing(bool replay)
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-plain");
        WriteProject(temp, "App", "");
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldReadAsync(solution, replay);

        verdict.CompilerLagProjects.ShouldBeEmpty(string.Join("\n", diagnostics));
    }

    [Theory]
    [InlineData("RazorApp")]
    [InlineData("CSharp15App")]
    public async Task LoadAsync_ABedTheCarriedCompilerReads_ListsNothing(string bed)
    {
        string solution = bed == "RazorApp" ? FixtureBeds.RazorAppSolution : FixtureBeds.CSharp15AppSolution;
        var diagnostics = new List<string>();

        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solution, diagnostics.Add, ct: Ct);

        loaded.LoadDiagnosticsWith(diagnostics).CompilerLagProjects.ShouldBeEmpty(string.Join("\n", diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_AGeneratorBuiltForANewerCompiler_ListsTheProject(bool replay)
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-newer-generator");
        string generator = StubAnalyzers.NewerCompilerGenerator(temp);
        string csproj = WriteProject(temp, "App", "", generator);
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldReadAsync(solution, replay);

        verdict.CompilerLagProjects.ShouldBe([csproj]);
        diagnostics.ShouldContain(
            $"'{csproj}' runs 1 source generator the C# compiler this tool carries cannot load (Gen99.dll: it was "
            + "built against Microsoft.CodeAnalysis 99.0.0.0, newer than this tool's), so the code it generates is "
            + "missing from the model.");
    }

    [Fact]
    public async Task LoadAsync_AGeneratorMissingADependency_ListsTheProject()
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-generator-dependency");
        string generator = StubAnalyzers.GeneratorMissingItsDependency(temp);
        string csproj = WriteProject(temp, "App", "", generator);
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldLoadRestoredAsync(solution);

        verdict.CompilerLagProjects.ShouldBe([csproj]);
        diagnostics.ShouldContain(
            line => line.StartsWith(
                $"'{csproj}' runs 1 source generator the C# compiler this tool carries cannot load (GenDep.dll: "),
            string.Join("\n", diagnostics));
    }

    [Fact]
    public async Task LoadAsync_AnAliasProjectWithNoAnalyzersOfItsOwn_IsJudgedAgainstTheSdkTheSolutionLoadedWith()
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-solution-sdk");
        string sdkAnalyzer = StubAnalyzers.EmptyAssembly(
            temp.PathOf("dotnet", "sdk", "99.0.100", "Sdks", "Fake.Sdk", "analyzers", "Fake.Analyzer.dll"));
        StubAnalyzers.CompilerDeclaring(
            temp.PathOf("dotnet", "sdk", "99.0.100", "Roslyn", "bincore", "Microsoft.CodeAnalysis.CSharp.dll"), 16);
        const string noSdkAnalyzers = "<EnableNETAnalyzers>false</EnableNETAnalyzers>";
        string latest = WriteProject(temp, "Latest", $"{noSdkAnalyzers}<LangVersion>latest</LangVersion>");
        WriteProject(temp, "Pinned", noSdkAnalyzers, sdkAnalyzer);
        string solution = temp.WriteFile(
            ["App.slnx"],
            "<Solution>\n  <Project Path=\"Latest/Latest.csproj\" />\n  <Project Path=\"Pinned/Pinned.csproj\" />\n"
            + "</Solution>\n");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldLoadRestoredAsync(solution);

        verdict.CompilerLagProjects.ShouldBe([latest], string.Join("\n", diagnostics));
        diagnostics.ShouldContain(
            $"'{latest}' asks for C# 'latest', which its build's compiler reads as C# 16, newer than the C# 15 "
            + "compiler this tool carries, so its code was read as an older C# version.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_AnAnalyzerOnlyAssemblyBuiltForANewerCompiler_ListsNothing(bool replay)
    {
        using TempDirectory temp = TestTempRoot.Fresh("lag-newer-analyzer");
        string analyzer = StubAnalyzers.NewerCompilerAnalyzer(temp);
        WriteProject(temp, "App", "", analyzer);
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldReadAsync(solution, replay);

        verdict.CompilerLagProjects.ShouldBeEmpty(string.Join("\n", diagnostics));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadAsync_AGeneratorOnAProjectReferencingAnotherLanguage_IsListedDespiteTheCrossLanguageCompile(
        bool replay)
    {
        // The one row whose load compiles before it returns: the cross-language check compiles a project that
        // references another language, on a replay only when there is a sink to report to, as there is here.
        using TempDirectory temp = TestTempRoot.Fresh("lag-generator-cross-language");
        string generator = StubAnalyzers.NewerCompilerGenerator(temp);
        string csproj = WriteProject(
            temp, "App", "", generator, """<ProjectReference Include="../Legacy/Legacy.vbproj" />""");
        temp.WriteFile(
            ["Legacy", "Legacy.vbproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        temp.WriteFile(["Legacy", "Ledger.vb"], "Public Class Ledger\nEnd Class\n");
        string solution = temp.WriteFile(
            ["App.slnx"],
            "<Solution>\n  <Project Path=\"App/App.csproj\" />\n  <Project Path=\"Legacy/Legacy.vbproj\" />\n</Solution>\n");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ShouldReadAsync(solution, replay);

        verdict.CompilerLagProjects.ShouldBe([csproj], string.Join("\n", diagnostics));
    }

    [Fact]
    public async Task ReplayAsync_AProjectWhoseFrameworksAskForDifferentVersions_JudgesEachFrameworkByItsOwnCall()
    {
        // One project file compiled once per framework. Only the netstandard2.0 call asks for a version the carried
        // compiler rejects, so a framework judged by the other framework's command line names the wrong version or
        // none.
        using TempDirectory temp = TestTempRoot.Fresh("lag-replay-frameworks");
        string csproj = temp.WriteFile(
            ["App", "App.csproj"],
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
                <LangVersion Condition="'$(TargetFramework)' == 'netstandard2.0'">98</LangVersion>
              </PropertyGroup>
            </Project>
            """);
        temp.WriteFile(["App", "Widget.cs"], "namespace App { public sealed class Widget { } }\n");
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) = await ReplayBuiltAsync(solution, buildFails: true);

        verdict.CompilerLagProjects.ShouldBe([csproj], string.Join("\n", diagnostics));
        diagnostics.ShouldBe(
        [
            $"'{csproj}' asks for C# version '98', which the C# 15 compiler this tool carries does not know, so its "
            + "code was read as another C# version."
        ]);
    }

    [Fact]
    public async Task ReplayAsync_AnAliasTheRecordedCompilerReadsAsANewerVersion_ListsTheProject()
    {
        // The yardstick on this path is the compiler the build ran, found beside the executable its call recorded.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The stand-in compiler is a Windows executable.");
        using TempDirectory temp = TestTempRoot.Fresh("lag-replay-alias");
        string compiler = StubAnalyzers.StandInCompiler(temp, 16);
        string csproj = WriteProject(temp, "App", "<LangVersion>latest</LangVersion>");
        string solution = WriteSolution(temp, "App");

        (WorkspaceDiagnostics verdict, List<string> diagnostics) =
            await ReplayBuiltAsync(solution, buildFails: true, $"-p:CscToolPath=\"{compiler}\"");

        verdict.CompilerLagProjects.ShouldBe([csproj], string.Join("\n", diagnostics));
        diagnostics.ShouldContain(
            $"'{csproj}' asks for C# 'latest', which its build's compiler reads as C# 16, newer than the C# 15 "
            + "compiler this tool carries, so its code was read as an older C# version.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayAsync_OfAStoredCapture_JudgesTheCompilerCallsTheCaptureRecorded(bool recordedNone)
    {
        // A capture's replay reads no binlog for its compiler calls, so what its manifest records is what it judges:
        // the call the build made, or, with the record emptied, nothing.
        using TempDirectory temp = TestTempRoot.Fresh("lag-replay-capture");
        using TempDirectory cache = TestTempRoot.Fresh("lag-replay-capture-cache");
        string csproj = WriteProject(temp, "App", "<LangVersion>99</LangVersion>");
        string solution = WriteSolution(temp, "App");
        string binlog = BuildWithBinlog(solution, buildFails: true);
        var store = new BinlogCaptureStore(solution, cache.Path);
        using (ReplayedSolution replayed = await BinlogReplayer.ReplayAsync(binlog, solution, ct: Ct))
        {
            store.Ingest(replayed, binlog, binlog, Ct).ShouldBeTrue();
        }

        if (recordedNone) CaptureFiles.EditManifest(solution, cache.Path, root => root["RecordedCompiles"] = new JsonArray());

        CaptureValidation validation = store.Validate(Ct);
        validation.Notice.ShouldBeNull();
        var diagnostics = new List<string>();
        using ReplayedSolution fromCapture = await BinlogReplayer.ReplayAsync(
            validation.Capture.ShouldNotBeNull(), solution, diagnostics.Add, Ct);

        fromCapture.LoadDiagnosticsWith(diagnostics).CompilerLagProjects.ShouldBe(
            recordedNone ? [] : [csproj], string.Join("\n", diagnostics));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReplayAsync_TheRecordedCompiler_IsWhatTheCaptureAndTheCacheStamp(bool onDisk)
    {
        // The compiler a replayed build ran decides whether the carried one lags, so both stores watch it, and one
        // that is not on disk yet is watched for appearing, as the binlog of another machine's build records one.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The stand-in compiler is a Windows executable.");
        using TempDirectory temp = TestTempRoot.Fresh("lag-replay-stamp");
        using TempDirectory cache = TestTempRoot.Fresh("lag-replay-stamp-cache");
        string compilerAssembly = Path.Combine(StubAnalyzers.StandInCompiler(temp, 15), CompilerAssemblyName);
        WriteProject(temp, "App", "");
        string solution = WriteSolution(temp, "App");
        string binlog = BuildWithBinlog(
            solution, buildFails: true, $"-p:CscToolPath=\"{Path.GetDirectoryName(compilerAssembly)}\"");
        if (!onDisk) File.Delete(compilerAssembly);

        using ReplayedSolution replayed = await BinlogReplayer.ReplayAsync(binlog, solution, ct: Ct);
        new BinlogCaptureStore(solution, cache.Path).Ingest(replayed, binlog, binlog, Ct).ShouldBeTrue();

        SolutionCacheInputs.Collect(replayed.Solution, replayed.CompilerAssemblies)
            .ShouldHaveSingleItem()
            .CompilerAssemblyPaths.ShouldBe([compilerAssembly]);
        CaptureFiles.StructuralStampPaths(solution, cache.Path).ShouldContain(compilerAssembly);
    }

    [Fact]
    public async Task ReplayAsync_AProjectWhoseFrameworksRanDifferentCompilers_IsStampedWithEach()
    {
        // Either framework's compiler can make the carried one lag, so a change to either must reach the stores.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The stand-in compiler is a Windows executable.");
        using TempDirectory temp = TestTempRoot.Fresh("lag-replay-stamp-frameworks");
        using TempDirectory cache = TestTempRoot.Fresh("lag-replay-stamp-frameworks-cache");
        string modern = Path.Combine(StubAnalyzers.StandInCompiler(temp, 15, "modern"), CompilerAssemblyName);
        string legacy = Path.Combine(StubAnalyzers.StandInCompiler(temp, 15, "legacy"), CompilerAssemblyName);
        temp.WriteFile(
            ["App", "App.csproj"],
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFrameworks>net10.0;netstandard2.0</TargetFrameworks>
                 <CscToolPath Condition="'$(TargetFramework)' == 'net10.0'">{Path.GetDirectoryName(modern)}</CscToolPath>
                 <CscToolPath Condition="'$(TargetFramework)' == 'netstandard2.0'">{Path.GetDirectoryName(legacy)}</CscToolPath>
               </PropertyGroup>
             </Project>
             """);
        temp.WriteFile(["App", "Widget.cs"], "namespace App { public sealed class Widget { } }\n");
        string solution = WriteSolution(temp, "App");
        string binlog = BuildWithBinlog(solution, buildFails: true);

        using ReplayedSolution replayed = await BinlogReplayer.ReplayAsync(binlog, solution, ct: Ct);
        new BinlogCaptureStore(solution, cache.Path).Ingest(replayed, binlog, binlog, Ct).ShouldBeTrue();

        IReadOnlyList<string> stamped = CaptureFiles.StructuralStampPaths(solution, cache.Path);
        stamped.ShouldSatisfyAllConditions(
            () => stamped.ShouldContain(modern),
            () => stamped.ShouldContain(legacy));
        SolutionCacheInputs.Collect(replayed.Solution, replayed.CompilerAssemblies)
            .ShouldHaveSingleItem()
            .CompilerAssemblyPaths.ShouldBe([legacy, modern]);
    }

    private const string CompilerAssemblyName = "Microsoft.CodeAnalysis.CSharp.dll";

    private static string WriteProject(
        TempDirectory temp, string name, string properties, string? analyzer = null, string items = "")
    {
        string analyzerItem = analyzer is null ? "" : $"<Analyzer Include=\"{analyzer}\" />";
        string csproj = temp.WriteFile(
            [name, $"{name}.csproj"],
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
                 {properties}
               </PropertyGroup>
               <ItemGroup>
                 {analyzerItem}
                 {items}
               </ItemGroup>
             </Project>
             """);
        temp.WriteFile([name, "Widget.cs"], $"namespace {name} {{ public sealed class Widget {{ }} }}\n");
        return csproj;
    }

    private static string WriteSolution(TempDirectory temp, string name)
    {
        return temp.WriteFile([$"{name}.slnx"], $"<Solution>\n  <Project Path=\"{name}/{name}.csproj\" />\n</Solution>\n");
    }

    // The verdict and the load's lines from a load of the solution, or from a replay of the binlog a build of it wrote.
    private static Task<(WorkspaceDiagnostics Verdict, List<string> Diagnostics)> ShouldReadAsync(
        string solution, bool replay, bool buildFails = false)
    {
        return replay ? ReplayBuiltAsync(solution, buildFails) : ShouldLoadRestoredAsync(solution);
    }

    private static async Task<(WorkspaceDiagnostics Verdict, List<string> Diagnostics)> ReplayBuiltAsync(
        string solution, bool buildFails = false, string buildProperties = "")
    {
        string binlog = BuildWithBinlog(solution, buildFails, buildProperties);
        var diagnostics = new List<string>();
        using ReplayedSolution replayed = await BinlogReplayer.ReplayAsync(binlog, solution, diagnostics.Add, ct: Ct);
        return (replayed.LoadDiagnosticsWith(diagnostics), diagnostics);
    }

    // A build of the solution that writes a binlog beside it; a bed whose compile is meant to be refused must fail.
    private static string BuildWithBinlog(string solution, bool buildFails, string buildProperties = "")
    {
        string binlog = Path.ChangeExtension(solution, ".binlog");
        var arguments = $"build \"{solution}\" -bl:LogFile=\"{binlog}\" {buildProperties}";
        string directory = Path.GetDirectoryName(solution)!;
        if (buildFails) DotnetCli.RunExpectingFailure(arguments, directory);
        else DotnetCli.Run(arguments, directory);
        return binlog;
    }

    // Restored first, because a project with no assets file is a restore failure, and a project already blamed for
    // its restore is never listed again for its compiler.
    private static async Task<(WorkspaceDiagnostics Verdict, List<string> Diagnostics)> ShouldLoadRestoredAsync(
        string solution)
    {
        FixtureRestorer.Restore(solution);
        var diagnostics = new List<string>();
        using LoadedSolution loaded = await WorkspaceLoader.LoadAsync(solution, diagnostics.Add, ct: Ct);
        WorkspaceDiagnostics verdict = loaded.LoadDiagnosticsWith(diagnostics);
        verdict.RestoreFailedProjects.ShouldBeEmpty(string.Join("\n", diagnostics));
        return (verdict, diagnostics);
    }
}

/// <summary>
///     Analyzer assemblies the carried compiler cannot load, compiled per test into its temp directory: one built against
///     a stand-in <c>Microsoft.CodeAnalysis</c> at version 99.0.0.0, and one whose generator's base class sits in an
///     assembly left out of the directory.
/// </summary>
internal static class StubAnalyzers
{
    private const string NewerCompilerContract = """
                                                 [assembly: System.Reflection.AssemblyVersion("99.0.0.0")]
                                                 namespace Microsoft.CodeAnalysis
                                                 {
                                                     [System.AttributeUsage(System.AttributeTargets.Class)]
                                                     public sealed class GeneratorAttribute : System.Attribute { }
                                                     public interface IIncrementalGenerator { }
                                                 }
                                                 namespace Microsoft.CodeAnalysis.Diagnostics
                                                 {
                                                     [System.AttributeUsage(System.AttributeTargets.Class)]
                                                     public sealed class DiagnosticAnalyzerAttribute : System.Attribute
                                                     {
                                                         public DiagnosticAnalyzerAttribute(string firstLanguage, params string[] additionalLanguages) { }
                                                     }
                                                     public abstract class DiagnosticAnalyzer { }
                                                 }
                                                 """;

    internal static string NewerCompilerGenerator(TempDirectory temp)
    {
        return Emit(
            temp, "Gen99",
            "[Microsoft.CodeAnalysis.Generator] public sealed class G99 : Microsoft.CodeAnalysis.IIncrementalGenerator { }",
            NewerContract());
    }

    internal static string NewerCompilerAnalyzer(TempDirectory temp)
    {
        return Emit(
            temp, "Ana99",
            "[Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer(\"C#\")] "
            + "public sealed class A99 : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer { }",
            NewerContract());
    }

    internal static string GeneratorMissingItsDependency(TempDirectory temp)
    {
        byte[] baseImage = SpecAssemblyCompiler.EmitImage(
            "public abstract class BaseGen : Microsoft.CodeAnalysis.IIncrementalGenerator "
            + "{ public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext c) { } }",
            "GenDepBase", WithCompiler(MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location)));
        MetadataReference baseReference = MetadataReference.CreateFromImage(baseImage);
        return Emit(
            temp, "GenDep", "[Microsoft.CodeAnalysis.Generator] public sealed class GD : BaseGen { }",
            MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location), baseReference);
    }

    internal static string EmptyAssembly(string path)
    {
        return Write(path, SpecAssemblyCompiler.EmitImage("public static class Empty { }", "Fake.Analyzer", WithCompiler()));
    }

    // A compiler assembly whose LanguageVersion enum declares C# up to highestMajor, the one fact a yardstick reads.
    internal static string CompilerDeclaring(string path, int highestMajor)
    {
        var source =
            $"namespace Microsoft.CodeAnalysis.CSharp {{ public enum LanguageVersion {{ CSharp{highestMajor} = {highestMajor * 100} }} }}";
        return Write(path, SpecAssemblyCompiler.EmitImage(source, "Microsoft.CodeAnalysis.CSharp", WithCompiler()));
    }

    // A directory a build's CscToolPath can name: an executable that starts and fails, so the build records its call
    // and compiles nothing, beside a compiler assembly declaring C# up to highestMajor.
    internal static string StandInCompiler(TempDirectory temp, int highestMajor, string name = "compiler")
    {
        string directory = temp.PathOf(name);
        Directory.CreateDirectory(directory);
        File.Copy(Environment.ProcessPath!, Path.Combine(directory, "csc.exe"));
        CompilerDeclaring(Path.Combine(directory, "Microsoft.CodeAnalysis.CSharp.dll"), highestMajor);
        return directory;
    }

    private static string Write(string path, byte[] image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        return path;
    }

    private static MetadataReference NewerContract()
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(NewerCompilerContract, "Microsoft.CodeAnalysis", WithCompiler());
        return MetadataReference.CreateFromImage(image);
    }

    private static string Emit(TempDirectory temp, string name, string source, params MetadataReference[] references)
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(source, name, WithCompiler(references));
        string path = temp.PathOf("analyzers", $"{name}.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        return path;
    }

    // The platform closure without the carried compiler, so a stand-in contract never meets the real one.
    private static IReadOnlyList<MetadataReference> WithCompiler(params MetadataReference[] references)
    {
        IEnumerable<MetadataReference> platform = SpecAssemblyCompiler.PlatformReferences
            .Where(reference => !Path.GetFileName(reference.Display ?? "").StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
        return [.. platform, .. references];
    }
}
