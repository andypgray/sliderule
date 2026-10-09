using System.Text;
using Shouldly;
using Xunit;
using SlideRule.Rendering;
using SlideRule.Tests.Rendering;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     <c>render</c> writes inside the solution directory and nowhere else, on a real restore of a package that
///     compiles a source file into the project from outside the checkout.
/// </summary>
/// <remarks>
///     <para>
///         A source-only package adds its file to the compilation from the package folder the restore extracted it
///         into, and that folder sits wherever the machine keeps packages: usually under the user's home
///         directory, beside the checkout rather than inside it. Only a restore produces that path, so only a
///         restore shows where render places a card over it.
///     </para>
///     <para>
///         The copy puts the solution at <c>b/repo/src</c> and restores into <c>a/home/.nuget/packages</c>, so the
///         project and the package share nothing but the copy's own root: a card collapsed over both lands there,
///         three levels above the solution. The package is manufactured into a folder feed at test time, the bed's
///         <c>NuGet.config</c> clears every other source, and <c>NUGET_PACKAGES</c> points inside the copy, so no
///         warm package folder on the machine can take part.
///     </para>
///     <para>
///         The same layout, a solution below its repository root, is where a <c>CLAUDE.md</c> above the solution
///         directory is met, so the warning that names one from there is proved here too.
///     </para>
/// </remarks>
[Collection("Serial")]
public sealed class RenderContainmentE2ETests
{
    private const string FixtureDirectory = "RenderContainmentSolutions/SourceOnlyPackage";

    private const string SolutionFileName = "Acme.slnx";

    private const string PackageId = "Contoso.Shims";

    private const string PackageVersion = "1.0.0";

    private const string ShimEntry = "contentFiles/cs/any/Shims/XShim.cs";

    private const string ProjectLayerSpecSource =
        """
        using SlideRule;

        namespace ContainmentSpecs
        {
            public sealed class ProjectLayerSpec : IArchitectureSpec
            {
                public void Define(Arch arch)
                {
                    Layer json = arch.Layer("Json", arch.Project("Acme.Json"));
                    arch.Rule("layering/json-no-console")
                        .Enforce(json.MustNotReference(arch.Namespace("Acme.Console.*")))
                        .Because("The serializer must not reach the console surface.");
                }
            }
        }
        """;

    private const string ShimLayerSpecSource =
        """
        using SlideRule;

        namespace ContainmentSpecs
        {
            public sealed class ShimLayerSpec : IArchitectureSpec
            {
                public void Define(Arch arch)
                {
                    Layer shims = arch.Layer("Shims", "Contoso.Shims.*");
                    arch.Rule("layering/shims-no-json")
                        .Enforce(shims.MustNotReference(arch.Namespace("Acme.Json.*")))
                        .Because("A shim must not reach back into the code it supports.");
                }
            }
        }
        """;

    private static readonly string SolutionFile = Path.Combine("b", "repo", "src", SolutionFileName);

    // Each spec compiled once for the class: the compilation drags in the whole platform closure, and every fact
    // writes the bytes into its own copy because --spec takes a path.
    private static readonly Lazy<byte[]> ProjectLayerSpec =
        new(() => SpecAssemblyCompiler.EmitImage(ProjectLayerSpecSource, "ProjectLayerSpec"));

    private static readonly Lazy<byte[]> ShimLayerSpec =
        new(() => SpecAssemblyCompiler.EmitImage(ShimLayerSpecSource, "ShimLayerSpec"));

    private static readonly Lazy<byte[]> PackageImage = new(BuildShimPackage);

    [Fact]
    public async Task Render_ProjectCompilingAPackageFile_WritesOnlyInsideTheSolution()
    {
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(FixtureDirectory, SolutionFile, false);
        using ScopedEnvironmentVariable scopedPackages = RestoreWithTheShimPackage(workspace);
        string specDll = WriteSpec(workspace, ProjectLayerSpec, "ProjectLayerSpec");

        CliResult result = await Render(workspace.SolutionPath, specDll);

        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe("wrote AGENTS.md\nwrote Extensions/Acme.Json/AGENTS.md", result.Transcript());
        AgentsFilesUnder(workspace)
            .ShouldBe(["b/repo/src/AGENTS.md", "b/repo/src/Extensions/Acme.Json/AGENTS.md"]);
    }

    [Fact]
    public async Task Render_LayerOfOnlyPackageFiles_WarnsAndWritesNothingUnderThePackages()
    {
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(FixtureDirectory, SolutionFile, false);
        using ScopedEnvironmentVariable scopedPackages = RestoreWithTheShimPackage(workspace);
        string specDll = WriteSpec(workspace, ShimLayerSpec, "ShimLayerSpec");

        CliResult result = await Render(workspace.SolutionPath, specDll);

        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe("wrote AGENTS.md", result.Transcript());
        result.Err.ShouldContain(
            "warning: layer 'Shims' matched only types declared outside the solution directory; no scoped context emitted",
            customMessage: result.Transcript());
        Directory.EnumerateFiles(PackagesRoot(workspace), ContextFileComposer.FileName, SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Render_ThroughASymlinkedSolutionPath_StillPlacesTheCard()
    {
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(FixtureDirectory, SolutionFile, false);

        // Before the restore, so a host that cannot create a link skips without paying for one.
        string linkedTree = workspace.PathOf("linked");
        SymlinkSupport.CreateDirectorySymlink(linkedTree, workspace.PathOf("b"));

        using ScopedEnvironmentVariable scopedPackages = RestoreWithTheShimPackage(workspace);
        string specDll = WriteSpec(workspace, ProjectLayerSpec, "ProjectLayerSpec");
        string linkedSolution = Path.Combine(linkedTree, "repo", "src", SolutionFileName);

        CliResult result = await Render(linkedSolution, specDll);

        // The declaration sites the model records are resolved through links, so a solution directory spelled
        // through one would hold none of them and every card would be skipped.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe("wrote AGENTS.md\nwrote Extensions/Acme.Json/AGENTS.md", result.Transcript());
    }

    [Fact]
    public async Task Render_BelowARepositoryClaudeMd_WarnsNamingItFromTheSolution()
    {
        using TempFixtureWorkspace workspace = TempFixtureWorkspace.Dedicated(FixtureDirectory, SolutionFile, false);
        File.WriteAllText(workspace.PathOf("b", "repo", "CLAUDE.md"), "# Repository rules\n");
        using ScopedEnvironmentVariable scopedPackages = RestoreWithTheShimPackage(workspace);
        string specDll = WriteSpec(workspace, ProjectLayerSpec, "ProjectLayerSpec");

        CliResult result = await Render(workspace.SolutionPath, specDll);

        // Claude Code counts a CLAUDE.md in any directory above the one it starts in, and the path reads from the
        // solution directory like every other path render prints.
        result.ShouldSucceed();
        result.Out.NormalizedTrimmed()
            .ShouldBe("wrote AGENTS.md\nwrote Extensions/Acme.Json/AGENTS.md", result.Transcript());
        result.Err.ShouldContain(
            "warning: by default Claude Code reads ../CLAUDE.md instead of AGENTS.md, so its sessions miss the "
            + "managed block; add a CLAUDE.md beside AGENTS.md holding the line @AGENTS.md.",
            customMessage: result.Transcript());
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    // The package folder this copy restores into, which is the bed's geometry rather than any one fact's: it
    // sits under the copy's own root, beside the checkout and outside it.
    private static string PackagesRoot(TempFixtureWorkspace workspace)
    {
        return workspace.PathOf("a", "home", ".nuget", "packages");
    }

    // The feed, then a cold restore against it, then the two facts every assertion above rests on: the package
    // file was extracted, and the restore handed it to the project to compile. Asserted here so that a failure
    // below them can only be about where render placed a card. The scope over NUGET_PACKAGES comes back for
    // the caller to hold, because the render the fact runs next must read the same package folder.
    private static ScopedEnvironmentVariable RestoreWithTheShimPackage(TempFixtureWorkspace workspace)
    {
        string packagesRoot = PackagesRoot(workspace);
        var scopedPackages = new ScopedEnvironmentVariable("NUGET_PACKAGES", packagesRoot);
        string feedDirectory = workspace.PathOf("feed");
        ManufacturedPackage.Publish(feedDirectory, PackageId, PackageVersion, PackageImage.Value);

        DotnetCli.Run($"restore {SolutionFileName}", Path.GetDirectoryName(workspace.SolutionPath)!);

        string extractedShim = Path.Combine(packagesRoot, "contoso.shims", PackageVersion, ShimEntry);
        File.Exists(extractedShim)
            .ShouldBeTrue();
        string assetsFile = workspace.PathOf("b", "repo", "src", "Extensions", "Acme.Json", "obj", "project.assets.json");
        File.ReadAllText(assetsFile)
            .ShouldContain(ShimEntry);

        return scopedPackages;
    }

    // Cold and uncached: the tree is the whole subject, and a pooled session or a cache hit could answer from
    // another fact's copy.
    private static Task<CliResult> Render(string solutionPath, string specDll)
    {
        return CliRunner.InvokeColdAsync("render", solutionPath, "--spec", specDll, "--no-cache");
    }

    // Outside the solution directory, so the spec itself can never be counted as a file render wrote.
    private static string WriteSpec(TempFixtureWorkspace workspace, Lazy<byte[]> image, string assemblyName)
    {
        string specDirectory = workspace.PathOf("spec");
        Directory.CreateDirectory(specDirectory);
        string specDll = Path.Combine(specDirectory, $"{assemblyName}.dll");
        File.WriteAllBytes(specDll, image.Value);
        return specDll;
    }

    // Every context file anywhere in the copy, packages and feed included, relative to the copy's root.
    private static List<string> AgentsFilesUnder(TempFixtureWorkspace workspace)
    {
        string root = workspace.PathOf();
        return Directory.EnumerateFiles(root, ContextFileComposer.FileName, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)
                .Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // Assembled here rather than packed: a pack would need a project, a restore and a feed of its own to produce
    // a zip holding one nuspec and one source file.
    private static byte[] BuildShimPackage()
    {
        byte[] shim = Encoding.UTF8.GetBytes(ContextFixtures.ShimSource);

        return ManufacturedPackage.Image(PackageId, Nuspec, ShimEntry, shim);
    }

    // The contentFiles declaration is what makes the restore hand the file to the compiler rather than copy it.
    private static string Nuspec =>
        $"""
         <?xml version="1.0" encoding="utf-8"?>
         <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
             <metadata>
                 <id>{PackageId}</id>
                 <version>{PackageVersion}</version>
                 <authors>SlideRule tests</authors>
                 <description>A source-only stub package manufactured by RenderContainmentE2ETests.</description>
                 <contentFiles>
                     <files include="cs/any/**/*.cs" buildAction="Compile" />
                 </contentFiles>
             </metadata>
         </package>
         """;
}
