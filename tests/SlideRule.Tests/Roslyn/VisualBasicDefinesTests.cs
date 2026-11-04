using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Shouldly;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="VisualBasicDefines" /> over an <see cref="AdhocWorkspace" /> Visual Basic project whose file is a real
///     <c>.vbproj</c> on disk, loaded with the symbols a Linux workspace load leaves it: none of the build's. A load on
///     Windows keeps them, so these are where the restore runs on every platform. <c>Cli/SolutionEntriesE2ETests</c>
///     drives the reference it makes bind through a real load.
/// </summary>
public sealed class VisualBasicDefinesTests
{
    [Theory]
    [InlineData("", "Empty")]
    [InlineData("<MyType>Console</MyType>", "Console")]
    public void Restore_AProjectLoadedWithoutTheBuildsSymbols_GetsThemInBothParseOptions(string property, string myType)
    {
        // Arrange
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-restored");
        string vbproj = WriteProject(temp, "<TargetFramework>net10.0</TargetFramework>" + property);
        using var workspace = new AdhocWorkspace();
        ProjectInfo legacy = VisualBasicProject(vbproj);
        Solution solution = workspace.WithProjects(legacy);

        // Act
        Project restored = VisualBasicDefines.Restore(solution, new Dictionary<ProjectId, string>()).GetProject(legacy.Id)!;

        // Assert
        IReadOnlyDictionary<string, object> symbols = Symbols(restored.ParseOptions);
        symbols.ShouldContainKeyAndValue("_MyType", myType);
        symbols.ShouldContainKeyAndValue("CONFIG", "Debug");
        symbols.ShouldContainKeyAndValue("NET10_0", -1);
        Symbols(((VisualBasicCompilationOptions)restored.CompilationOptions!).ParseOptions)
            .ShouldBe(symbols, ignoreOrder: true);
    }

    [Fact]
    public void Restore_ASymbolTheProjectAlreadyCarries_KeepsItsValue()
    {
        // Arrange — DEBUG loaded as 0, where the build defines it as -1.
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-kept");
        string vbproj = WriteProject(temp, "<TargetFramework>net10.0</TargetFramework>");
        using var workspace = new AdhocWorkspace();
        ProjectInfo legacy = VisualBasicProject(vbproj, new KeyValuePair<string, object>("DEBUG", 0));
        Solution solution = workspace.WithProjects(legacy);

        // Act
        Project restored = VisualBasicDefines.Restore(solution, new Dictionary<ProjectId, string>()).GetProject(legacy.Id)!;

        // Assert
        IReadOnlyDictionary<string, object> symbols = Symbols(restored.ParseOptions);
        symbols.ShouldContainKeyAndValue("DEBUG", 0);
        symbols.ShouldContainKeyAndValue("_MyType", "Empty");
    }

    [Fact]
    public void Restore_AProjectThatLoadedWithMyType_IsLeftAsTheSameSolution()
    {
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-untouched");
        string vbproj = WriteProject(temp, "<TargetFramework>net10.0</TargetFramework>");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            VisualBasicProject(vbproj, new KeyValuePair<string, object>("_MyType", "Empty")));

        VisualBasicDefines.Restore(solution, new Dictionary<ProjectId, string>())
            .ShouldBeSameAs(solution);
    }

    [Fact]
    public void Restore_AMultiTargetedProject_GivesEachFrameworkItsOwnSymbols()
    {
        // Arrange — one project file, loaded once per framework, defining EIGHT (a bare symbol, so True) for net8.0 alone.
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-frameworks");
        string vbproj = WriteProject(
            temp,
            "<TargetFrameworks>net8.0;net10.0</TargetFrameworks>"
            + "<DefineConstants Condition=\"'$(TargetFramework)' == 'net8.0'\">EIGHT</DefineConstants>");
        using var workspace = new AdhocWorkspace();
        ProjectInfo eight = VisualBasicProject(vbproj);
        ProjectInfo ten = VisualBasicProject(vbproj);
        Solution solution = workspace.WithProjects(eight, ten);
        var frameworks = new Dictionary<ProjectId, string> { [eight.Id] = "net8.0", [ten.Id] = "net10.0" };

        // Act
        Solution restored = VisualBasicDefines.Restore(solution, frameworks);

        // Assert
        IReadOnlyDictionary<string, object> eightSymbols = Symbols(restored.GetProject(eight.Id)!.ParseOptions);
        IReadOnlyDictionary<string, object> tenSymbols = Symbols(restored.GetProject(ten.Id)!.ParseOptions);
        eightSymbols.ShouldContainKeyAndValue("EIGHT", true);
        eightSymbols.ShouldContainKeyAndValue("NET8_0", -1);
        eightSymbols.ShouldNotContainKey("NET10_0");
        tenSymbols.ShouldContainKeyAndValue("NET10_0", -1);
        tenSymbols.ShouldNotContainKey("EIGHT");
    }

    [Fact]
    public void Restore_AProjectWhoseInitialTargetFailsOutsideADesignTimeBuild_IsStillRestored()
    {
        // Arrange — an initial target the workspace's design-time load skips, which fails any other build.
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-design-time");
        string vbproj = WriteProjectFile(
            temp,
            """
            <Project Sdk="Microsoft.NET.Sdk" InitialTargets="RequireBuildTooling">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <Target Name="RequireBuildTooling" Condition="'$(DesignTimeBuild)' != 'true'">
                <Error Text="This project builds only with its tooling installed." />
              </Target>
            </Project>
            """);
        using var workspace = new AdhocWorkspace();
        ProjectInfo legacy = VisualBasicProject(vbproj);
        Solution solution = workspace.WithProjects(legacy);

        // Act
        Project restored = VisualBasicDefines.Restore(solution, new Dictionary<ProjectId, string>()).GetProject(legacy.Id)!;

        // Assert
        Symbols(restored.ParseOptions).ShouldContainKeyAndValue("_MyType", "Empty");
    }

    [Fact]
    public void Restore_AProjectThatPredatesTheSdk_GetsItsEvaluatedSymbols()
    {
        // Arrange — no SDK, so no target adds framework symbols, and the evaluated list is the build's whole list.
        using TempDirectory temp = TestTempRoot.Fresh("vb-defines-classic");
        string vbproj = WriteProjectFile(
            temp,
            """
            <Project>
              <PropertyGroup>
                <MyType>Windows</MyType>
                <DefineConstants>LEDGER=2</DefineConstants>
              </PropertyGroup>
              <Import Project="$(MSBuildToolsPath)\Microsoft.VisualBasic.targets" />
            </Project>
            """);
        using var workspace = new AdhocWorkspace();
        ProjectInfo legacy = VisualBasicProject(vbproj);
        Solution solution = workspace.WithProjects(legacy);

        // Act
        Project restored = VisualBasicDefines.Restore(solution, new Dictionary<ProjectId, string>()).GetProject(legacy.Id)!;

        // Assert
        IReadOnlyDictionary<string, object> symbols = Symbols(restored.ParseOptions);
        symbols.ShouldContainKeyAndValue("_MyType", "Windows");
        symbols.ShouldContainKeyAndValue("LEDGER", 2);
        symbols.ShouldNotContainKey("NET10_0");
        Symbols(((VisualBasicCompilationOptions)restored.CompilationOptions!).ParseOptions)
            .ShouldBe(symbols, ignoreOrder: true);
    }

    private static string WriteProject(TempDirectory temp, string properties)
    {
        return WriteProjectFile(
            temp,
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 {properties}
                 <RootNamespace></RootNamespace>
               </PropertyGroup>
             </Project>
             """);
    }

    private static string WriteProjectFile(TempDirectory temp, string xml)
    {
        return temp.WriteFile(["Legacy", "Legacy.vbproj"], xml);
    }

    private static ProjectInfo VisualBasicProject(string vbproj, params KeyValuePair<string, object>[] symbols)
    {
        var parseOptions = new VisualBasicParseOptions(preprocessorSymbols: symbols);
        return ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Legacy", "Legacy", LanguageNames.VisualBasic, vbproj,
            compilationOptions: new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary, parseOptions: parseOptions),
            parseOptions: parseOptions);
    }

    private static IReadOnlyDictionary<string, object> Symbols(ParseOptions? parseOptions)
    {
        return ((VisualBasicParseOptions)parseOptions!).PreprocessorSymbols
            .ToDictionary(symbol => symbol.Key, symbol => symbol.Value, StringComparer.OrdinalIgnoreCase);
    }
}
