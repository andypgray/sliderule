using Microsoft.CodeAnalysis;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="UnresolvedReferences" /> over an <see cref="AdhocWorkspace" /> project whose file is a real
///     <c>.csproj</c> on disk and whose metadata references are chosen by name, and the warning text it feeds both
///     load paths. The loads that reach it are pinned in <see cref="WorkspaceLoaderTests" /> and in the binlog
///     fidelity tests.
/// </summary>
public sealed class UnresolvedReferencesTests
{
    [Theory]
    [InlineData("""<Reference Include="Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />""", new string[0], new[] { "Foo" })]
    [InlineData("""<Reference Include="Foo, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />""", new[] { "Foo" }, new string[0])]
    [InlineData("""<Reference Include="Foo.dll" />""", new string[0], new[] { "Foo" })]
    [InlineData("""<Reference Include="..\lib\Foo.dll" />""", new[] { "Foo" }, new string[0])]
    [InlineData("""<Reference Include="foo" />""", new[] { "Foo" }, new string[0])]
    [InlineData("""<Reference Include="Zed;Alpha;Kept" />""", new[] { "Kept" }, new[] { "Alpha", "Zed" })]
    [InlineData("""<Reference Include="Foo" Condition="'$(OS)' == 'Windows_NT'" />""", new string[0], new string[0])]
    [InlineData("""<Reference Include="$(VendorDir)\Foo.dll" />""", new string[0], new string[0])]
    [InlineData("""<Reference Include="lib\*.dll" />""", new string[0], new string[0])]
    public void Detect_OneDeclaredItemGroup_ReportsTheNamesNoMetadataReferenceCarries(
        string reference, string[] resolved, string[] expected)
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references");
        string csproj = WriteProject(temp, "App", $"<ItemGroup>{reference}</ItemGroup>");
        using var workspace = new AdhocWorkspace();

        IReadOnlyList<UnresolvedReferences.Finding> found =
            UnresolvedReferences.Detect(workspace.WithProjects(Project("App", csproj, resolved)), []);

        string[] expectedLines = expected.Length == 0 ? [] : [Line(csproj, expected)];
        found.Select(finding => Line(finding.ProjectPath, finding.Names))
            .ShouldBe(expectedLines);
    }

    [Fact]
    public void Detect_AConditionedItemGroup_IsSkipped()
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references-conditioned-group");
        string csproj = WriteProject(
            temp, "App", """<ItemGroup Condition="'$(Configuration)' == 'Debug'"><Reference Include="Foo" /></ItemGroup>""");
        using var workspace = new AdhocWorkspace();

        UnresolvedReferences.Detect(workspace.WithProjects(Project("App", csproj, [])), [])
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><Reference Include="Foo" />""")]
    [InlineData("""<!DOCTYPE Project><Project><ItemGroup><Reference Include="Foo" /></ItemGroup></Project>""")]
    [InlineData("""<Solution><ItemGroup><Reference Include="Foo" /></ItemGroup></Solution>""")]
    public void Detect_AProjectFileThatCannotBeRead_ReportsNothing(string projectXml)
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references-unreadable");
        string csproj = temp.WriteFile(["App", "App.csproj"], projectXml);
        using var workspace = new AdhocWorkspace();

        UnresolvedReferences.Detect(workspace.WithProjects(Project("App", csproj, [])), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AProjectFileThatIsMissing_ReportsNothing()
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references-missing");
        using var workspace = new AdhocWorkspace();

        UnresolvedReferences.Detect(workspace.WithProjects(Project("App", temp.PathOf("App", "App.csproj"), [])), [])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Detect_AMultiTargetProject_ReportsOnlyTheNamesNoLegResolved()
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references-multi-target");
        string csproj = WriteProject(temp, "App", """<ItemGroup><Reference Include="Foo" /><Reference Include="Bar" /></ItemGroup>""");
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            Project("App(net48)", csproj, ["Foo"]), Project("App(net10.0)", csproj, []));

        UnresolvedReferences.Detect(solution, [])
            .Select(finding => Line(finding.ProjectPath, finding.Names))
            .ShouldBe([Line(csproj, ["Bar"])]);
    }

    [Fact]
    public void Detect_SeveralProjects_AreOrdinalSortedAndTheAlreadyFailedLeftOut()
    {
        using TempDirectory temp = TestTempRoot.Fresh("unresolved-references-several");
        const string missing = """<ItemGroup><Reference Include="Foo" /></ItemGroup>""";
        string zed = WriteProject(temp, "Zed", missing);
        string core = WriteProject(temp, "Core", missing);
        string failed = WriteProject(temp, "Failed", missing);
        using var workspace = new AdhocWorkspace();
        Solution solution = workspace.WithProjects(
            Project("Zed", zed, []), Project("Failed", failed, []), Project("Core", core, []));

        UnresolvedReferences.Detect(solution, [failed])
            .Select(finding => Line(finding.ProjectPath, finding.Names))
            .ShouldBe([Line(core, ["Foo"]), Line(zed, ["Foo"])]);
    }

    [Fact]
    public void Message_OneReference_NamesTheProjectTheReferenceAndTheMissingEdges()
    {
        UnresolvedReferences.Message("/repo/App/App.csproj", ["Vendor.Missing"])
            .ShouldBe(
                "'/repo/App/App.csproj' declares 1 reference the build could not resolve (Vendor.Missing); types from "
                + "that assembly are unbound, so edges to it are absent from the model.");
    }

    [Fact]
    public void Message_SeveralReferences_CountsAndListsThemInThePlural()
    {
        UnresolvedReferences.Message("/repo/App/App.csproj", ["Alpha", "Zed"])
            .ShouldBe(
                "'/repo/App/App.csproj' declares 2 references the build could not resolve (Alpha, Zed); types from "
                + "those assemblies are unbound, so edges to them are absent from the model.");
    }

    private static string WriteProject(TempDirectory temp, string name, string itemGroups)
    {
        return temp.WriteFile(
            [name, name + ".csproj"],
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net10.0</TargetFramework>
               </PropertyGroup>
               {itemGroups}
             </Project>
             """);
    }

    private static ProjectInfo Project(string name, string csproj, IEnumerable<string> resolvedNames)
    {
        string directory = Path.GetDirectoryName(csproj)!;
        IEnumerable<MetadataReference> references = resolvedNames
            .Select(resolvedName => new NamedReference(Path.Combine(directory, "lib", resolvedName + ".dll")));
        return AdhocSolution.Loaded(name, csproj)
            .WithMetadataReferences(references);
    }

    private static string Line(string projectPath, IEnumerable<string> names)
    {
        return $"{projectPath}: {string.Join(", ", names)}";
    }

    // A reference that carries only its path, which is all the detector reads; nothing here builds a compilation,
    // so its metadata is never asked for.
    private sealed class NamedReference(string path) : PortableExecutableReference(MetadataReferenceProperties.Assembly, path)
    {
        protected override DocumentationProvider CreateDocumentationProvider()
        {
            return DocumentationProvider.Default;
        }

        protected override Metadata GetMetadataImpl()
        {
            throw new NotSupportedException("The detector reads a reference's path, never its metadata.");
        }

        protected override PortableExecutableReference WithPropertiesImpl(MetadataReferenceProperties properties)
        {
            return new NamedReference(FilePath!);
        }
    }
}
