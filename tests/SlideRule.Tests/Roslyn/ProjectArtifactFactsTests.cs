using Microsoft.CodeAnalysis;
using Shouldly;
using Xunit;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The evaluated artifact facts, over the real <c>MultiTfm</c> fixture solution and over project files
///     written for one row each: the frameworks a project declares, the packages it declares and which of them
///     are private, whether it packs and whether its restore locks — each read from an MSBuild evaluation rather
///     than from the project file's XML, and each carrying the <c>file:line</c> that decided it. The rows at the
///     bottom read the project files a project declares a reference to, from the same evaluation.
/// </summary>
public sealed class ProjectArtifactFactsTests
{
    private const string Core = "MultiTfm.Core";
    private const string Web = "MultiTfm.Web";

    private static readonly string SolutionPath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions", "MultiTfm", "MultiTfm.sln");

    private static Task<CodebaseModel> Codebase => FixtureCodebase.Of(SolutionPath);

    [Fact]
    public async Task ExtractFromSolutionAsync_MultiTargetedProject_DeclaresBothFrameworksAtItsOwnDeclaration()
    {
        CodebaseModel model = await Codebase;

        // Ordinal order, never the csproj's — which declares netstandard2.0 first — so the first entry stays
        // the one a shared type's facts fall to.
        ProjectNode core = model.Projects.Single(project => project.Name == Core);
        core.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        core.TargetFrameworksSite.ShouldNotBeNull()
            .FilePath.ShouldEndWith("MultiTfm.Core.csproj");
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_SdkProjectDeclaringNothing_IsPackableAtItsOwnFileAndDoesNotLock()
    {
        CodebaseModel model = await Codebase;

        // The SDK default nobody wrote, which is the whole reason these facts are evaluated: the project
        // file says nothing about packability, and the answer is still yes. The site degrades to the project
        // rather than naming a file inside whichever SDK ran.
        ProjectNode web = model.Projects.Single(project => project.Name == Web);
        web.IsPackable.ShouldBe(true);
        web.IsPackableSite.ShouldNotBeNull()
            .FilePath.ShouldEndWith("MultiTfm.Web.csproj");
        web.LocksPackages.ShouldBe(false);
        web.PackageReferences.ShouldBeEmpty();
    }

    [Fact]
    public void EvaluateAll_PropertySetInAPropsFileAbove_IsReportedAtThatFilesLine()
    {
        // The case that makes evaluation the only honest reader: nothing in the project file mentions the
        // lock policy, and the answer — and the line a violation would have to cite — is two directories up.
        string root = TestTempRoot.For("artifact-facts-props");
        string projectDirectory = Path.Combine(root, "src", "Widget");
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(Path.Combine(root, "src", "Directory.Build.props"), """
                                                                              <Project>
                                                                                  <PropertyGroup>
                                                                                      <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                                                                                  </PropertyGroup>
                                                                              </Project>
                                                                              """);
        string projectFile = Path.Combine(projectDirectory, "Widget.csproj");
        File.WriteAllText(projectFile, """
                                       <Project Sdk="Microsoft.NET.Sdk">
                                           <PropertyGroup>
                                               <TargetFramework>net10.0</TargetFramework>
                                               <IsPackable>false</IsPackable>
                                           </PropertyGroup>
                                           <ItemGroup>
                                               <PackageReference Include="Shouldly" Version="4.3.0" />
                                           </ItemGroup>
                                       </Project>
                                       """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert — the far declaration for the policy, the near one for the opt-out, and the package with
        // the line it is written on.
        facts.LocksPackages.ShouldBeTrue();
        facts.LocksPackagesSite.File.ShouldEndWith(Path.Combine("src", "Directory.Build.props"));
        facts.LocksPackagesSite.Line.ShouldBe(3);
        facts.IsPackable.ShouldBe(false);
        facts.IsPackableSite.ShouldBe(new FragmentSite(projectFile, 4));
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("Shouldly", new FragmentSite(projectFile, 7), IsPrivate: false)
        ]);
    }

    [Fact]
    public void EvaluateAll_ProjectPredatingTheSdk_NormalizesItsFrameworkVersionToTheShortMoniker()
    {
        // A 2003-namespace project states an identifier and a version where an SDK project states a
        // moniker. Nothing downstream should have to know that, so the two spellings meet here.
        string projectDirectory = TestTempRoot.For("artifact-facts-classic");
        Directory.CreateDirectory(projectDirectory);
        string projectFile = Path.Combine(projectDirectory, "Classic.csproj");
        File.WriteAllText(projectFile, """
                                       <?xml version="1.0" encoding="utf-8"?>
                                       <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                                           <PropertyGroup>
                                               <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                                               <OutputType>Library</OutputType>
                                           </PropertyGroup>
                                       </Project>
                                       """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert — the moniker, sited at the version it was read from. Packability has no answer at all
        // here: a project outside the SDK's pack machinery is not un-packable, it is unanswered.
        facts.TargetFrameworks.ShouldBe(["net48"]);
        facts.TargetFrameworksSite.ShouldBe(new FragmentSite(projectFile, 4));
        facts.IsPackable.ShouldBeNull();
        facts.LocksPackages.ShouldBeFalse();
    }

    [Fact]
    public void EvaluateAll_PrivateAssetsSpelledEachWay_ClassifiesEveryReference()
    {
        // An attribute and a child element are one fact to MSBuild. Writing nothing is NuGet's default, which
        // hands runtime and compile assets on; ExcludeAssets="all" keeps the package out of this project's own
        // package and still hands it to a project referencing this one; none is the empty set. Only the first
        // two are private.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-private-spellings");
        string projectFile = temp.WriteFile(["Widget", "Widget.csproj"], """
                                                                         <Project Sdk="Microsoft.NET.Sdk">
                                                                             <PropertyGroup>
                                                                                 <TargetFramework>net10.0</TargetFramework>
                                                                             </PropertyGroup>
                                                                             <ItemGroup>
                                                                                 <PackageReference Include="ByAttribute" Version="1.0.0" PrivateAssets="all" />
                                                                                 <PackageReference Include="ByElement" Version="1.0.0">
                                                                                     <PrivateAssets>All</PrivateAssets>
                                                                                 </PackageReference>
                                                                                 <PackageReference Include="Unwritten" Version="1.0.0" />
                                                                                 <PackageReference Include="ExcludesAll" Version="1.0.0" ExcludeAssets="all" />
                                                                                 <PackageReference Include="PrivateNone" Version="1.0.0" PrivateAssets="none" />
                                                                             </ItemGroup>
                                                                         </Project>
                                                                         """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert — ordinal by name, each at its Include.
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("ByAttribute", new FragmentSite(projectFile, 6), IsPrivate: true),
            new FragmentPackageReference("ByElement", new FragmentSite(projectFile, 7), IsPrivate: true),
            new FragmentPackageReference("ExcludesAll", new FragmentSite(projectFile, 11), IsPrivate: false),
            new FragmentPackageReference("PrivateNone", new FragmentSite(projectFile, 12), IsPrivate: false),
            new FragmentPackageReference("Unwritten", new FragmentSite(projectFile, 10), IsPrivate: false)
        ]);
    }

    [Fact]
    public void EvaluateAll_PrivateAssetsSetByAnUpdateInDirectoryBuildTargets_IsPrivateAtTheIncludesLine()
    {
        // The project declares the analyzer plainly and a targets file imported after it makes it private. The
        // evaluated metadata is what counts, so the Update decides; the site stays the Include, the line a
        // reader would delete.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-private-update");
        temp.WriteFile(["Directory.Build.targets"], """
                                                    <Project>
                                                        <ItemGroup>
                                                            <PackageReference Update="StyleCop.Analyzers" PrivateAssets="all" />
                                                        </ItemGroup>
                                                    </Project>
                                                    """);
        string projectFile = temp.WriteFile(["Widget", "Widget.csproj"], OnePackageProject("StyleCop.Analyzers"));

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("StyleCop.Analyzers", new FragmentSite(projectFile, 6), IsPrivate: true)
        ]);
    }

    [Fact]
    public void EvaluateAll_PrivateAssetsFromAnItemDefinitionGroup_IsPrivate()
    {
        // A props file above the project makes every package reference private by default. The project file
        // says nothing about it, and the evaluated metadata still does.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-private-definition");
        temp.WriteFile(["Directory.Build.props"], """
                                                  <Project>
                                                      <ItemDefinitionGroup>
                                                          <PackageReference>
                                                              <PrivateAssets>all</PrivateAssets>
                                                          </PackageReference>
                                                      </ItemDefinitionGroup>
                                                  </Project>
                                                  """);
        string projectFile = temp.WriteFile(["Widget", "Widget.csproj"], OnePackageProject("Roslynator.Analyzers"));

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("Roslynator.Analyzers", new FragmentSite(projectFile, 6), IsPrivate: true)
        ]);
    }

    [Fact]
    public void EvaluateAll_GlobalPackageReferenceUnderCentralManagement_IsPrivateAtTheProjectFile()
    {
        // Central package management turns a GlobalPackageReference into a private PackageReference inside
        // NuGet's own targets, a file no repository owns, so the site falls back to the project file's first
        // line. Evaluation only: nothing here restores.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-global-reference");
        temp.WriteFile(["Directory.Packages.props"], """
                                                     <Project>
                                                         <PropertyGroup>
                                                             <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                                                         </PropertyGroup>
                                                         <ItemGroup>
                                                             <GlobalPackageReference Include="Nerdbank.GitVersioning" Version="3.7.115" />
                                                         </ItemGroup>
                                                     </Project>
                                                     """);
        string projectFile = temp.WriteFile(["Widget", "Widget.csproj"], """
                                                                         <Project Sdk="Microsoft.NET.Sdk">
                                                                             <PropertyGroup>
                                                                                 <TargetFramework>net10.0</TargetFramework>
                                                                             </PropertyGroup>
                                                                         </Project>
                                                                         """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("Nerdbank.GitVersioning", new FragmentSite(projectFile, 1), IsPrivate: true)
        ]);
    }

    [Fact]
    public void EvaluateAll_PackageDeclaredTwiceOnePrivate_IsNotPrivateAtTheOtherDeclaration()
    {
        // One package declared twice, privately first. Restore keeps the first of two duplicates and warns;
        // the model keeps the declaration that ships, deliberately the stricter answer, and cites it.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-private-duplicate");
        string projectFile = temp.WriteFile(["Widget", "Widget.csproj"], """
                                                                         <Project Sdk="Microsoft.NET.Sdk">
                                                                             <PropertyGroup>
                                                                                 <TargetFramework>net10.0</TargetFramework>
                                                                             </PropertyGroup>
                                                                             <ItemGroup>
                                                                                 <PackageReference Include="Polyfill" Version="7.0.0" PrivateAssets="all" />
                                                                                 <PackageReference Include="Polyfill" Version="7.0.0" />
                                                                             </ItemGroup>
                                                                         </Project>
                                                                         """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.PackageReferences.ShouldBe([
            new FragmentPackageReference("Polyfill", new FragmentSite(projectFile, 7), IsPrivate: false)
        ]);
    }

    [Fact]
    public void EvaluateAll_ProjectReferences_AreTheDeclaredOnesThatReferenceTheirOutput()
    {
        // One plain reference and one that only orders the build. Neither target file exists: evaluation
        // resolves an include without opening it. The includes keep their backslashes, which is what a
        // project file written on Windows holds, so the Linux leg proves MSBuild resolves them there too.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-project-references");
        string projectFile = temp.WriteFile(["App", "App.csproj"], """
                                                                   <Project Sdk="Microsoft.NET.Sdk">
                                                                       <PropertyGroup>
                                                                           <TargetFramework>net10.0</TargetFramework>
                                                                       </PropertyGroup>
                                                                       <ItemGroup>
                                                                           <ProjectReference Include="..\Library\Library.csproj" />
                                                                           <ProjectReference Include="..\Tool\Tool.csproj" ReferenceOutputAssembly="False" />
                                                                       </ItemGroup>
                                                                   </Project>
                                                                   """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.ProjectReferencePaths.ShouldBe([temp.PathOf("Library", "Library.csproj")]);
    }

    [Fact]
    public void EvaluateAll_ReferenceOutputAssemblyFromAnItemDefinitionGroup_IsTheEvaluatedValue()
    {
        // A props file above the project makes every project reference a build-order one by default, and one
        // reference turns its output back on. The evaluated metadata decides, as it does for the compiler.
        using TempDirectory temp = TestTempRoot.Fresh("artifact-facts-project-reference-definition");
        temp.WriteFile(["Directory.Build.props"], """
                                                  <Project>
                                                      <ItemDefinitionGroup>
                                                          <ProjectReference>
                                                              <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
                                                          </ProjectReference>
                                                      </ItemDefinitionGroup>
                                                  </Project>
                                                  """);
        string projectFile = temp.WriteFile(["App", "App.csproj"], """
                                                                   <Project Sdk="Microsoft.NET.Sdk">
                                                                       <PropertyGroup>
                                                                           <TargetFramework>net10.0</TargetFramework>
                                                                       </PropertyGroup>
                                                                       <ItemGroup>
                                                                           <ProjectReference Include="..\Library\Library.csproj" ReferenceOutputAssembly="true" />
                                                                           <ProjectReference Include="..\Tool\Tool.csproj" />
                                                                       </ItemGroup>
                                                                   </Project>
                                                                   """);

        // Act
        ProjectArtifactFacts facts = EvaluatedFacts(projectFile).ShouldNotBeNull();

        // Assert
        facts.ProjectReferencePaths.ShouldBe([temp.PathOf("Library", "Library.csproj")]);
    }

    [Fact]
    public void EvaluateAll_InSeveralShards_AnswersEveryRequestAsTheSerialBatchDoes()
    {
        // Arrange — four fixture project files, one of them requested for each of its two frameworks and then
        // once more, and one request with no file at all. Those five shard keys over three shards are a real
        // split, and the repeated request has something for its shard's dedup to answer.
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions");
        string core = Path.Combine(fixtures, "MultiTfm", "MultiTfm.Core", "MultiTfm.Core.csproj");
        ProjectEvaluationRequest[] requests =
        [
            new(core, "net10.0"),
            new(Path.Combine(fixtures, "MyApp", "MyApp.Domain", "MyApp.Domain.csproj"), null),
            new(core, "netstandard2.0"),
            new(Path.Combine(fixtures, "MyApp", "MyApp.Web", "MyApp.Web.csproj"), null),
            new(Path.Combine(fixtures, "MultiTfm", "MultiTfm.Web", "MultiTfm.Web.csproj"), null),
            new(core, "net10.0"),
            new(null, null)
        ];

        // Act
        IReadOnlyList<ProjectArtifactFacts?> serial = ProjectFactsEvaluator.EvaluateAll(requests, shardCount: 1, Ct);
        IReadOnlyList<ProjectArtifactFacts?> sharded = ProjectFactsEvaluator.EvaluateAll(requests, shardCount: 3, Ct);

        // Assert — the batch really split and every file evaluated, so the comparison below is not two empty
        // answers agreeing; then the repeat answered from its shard's dedup, and every answer the serial one.
        ProjectFactsEvaluator.Shards(requests, shardCount: 3).Count.ShouldBe(3);
        serial.Take(6).ShouldAllBe(facts => facts != null);
        sharded[5].ShouldBeSameAs(sharded[0]);
        sharded.ShouldBeEquivalentTo(serial);
    }

    [Fact]
    public void EvaluateAll_InSeveralShardsWithACancelledToken_LeavesAsTheCallersCancellation()
    {
        // Arrange — two files, so the batch really splits, and a token already cancelled.
        string fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TestSolutions");
        ProjectEvaluationRequest[] requests =
        [
            new(Path.Combine(fixtures, "MyApp", "MyApp.Domain", "MyApp.Domain.csproj"), null),
            new(Path.Combine(fixtures, "MyApp", "MyApp.Web", "MyApp.Web.csproj"), null)
        ];
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        // Act + Assert — the cancellation travels rather than reading as every project absent.
        Should.Throw<OperationCanceledException>(() =>
            ProjectFactsEvaluator.EvaluateAll(requests, shardCount: 2, cancelled.Token));
    }

    [Fact]
    public async Task ExtractFromSolutionAsync_AProjectNothingEvaluated_KeepsTheWorkspacesReferences()
    {
        // Arrange — two projects with no project file, so nothing can evaluate either, and one reference
        // between them that only the workspace records. Its list is all there is here.
        using var workspace = new AdhocWorkspace();
        var library = ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Default, "Library", "Library", LanguageNames.CSharp);
        ProjectInfo app = ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Default, "App", "App", LanguageNames.CSharp)
            .WithProjectReferences([new ProjectReference(library.Id)]);
        Solution solution = AdhocSolution.Of(workspace, library, app);

        // Act
        CodebaseModel model = await CodebaseExtractor.ExtractFromSolutionAsync(solution, ct: Ct);

        // Assert
        model.Projects.Single(project => project.Name == "App")
            .ProjectReferences.ShouldBe(["Library"]);
    }

    // One project evaluated on its own, the Act every evaluation row here takes. Non-asserting, so the row
    // keeps its own ShouldNotBeNull and reds on the null with its own name.
    private static ProjectArtifactFacts? EvaluatedFacts(string projectFile)
    {
        return ProjectFactsEvaluator.EvaluateAll([new ProjectEvaluationRequest(projectFile, null)], Ct)
            .Single();
    }

    // An SDK project declaring one package on line 6 and nothing else about it.
    private static string OnePackageProject(string package)
    {
        return $"""
                <Project Sdk="Microsoft.NET.Sdk">
                    <PropertyGroup>
                        <TargetFramework>net10.0</TargetFramework>
                    </PropertyGroup>
                    <ItemGroup>
                        <PackageReference Include="{package}" Version="1.0.0" />
                    </ItemGroup>
                </Project>
                """;
    }
}
