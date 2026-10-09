using Shouldly;
using Xunit;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     Where a scoped card may land: inside the solution directory, placed by the files a selection declares
///     there. A project that compiles a source file out of a package folder declares a type whose file sits
///     outside the checkout, and collapsing that file with the project's own put the card in the directory above
///     both. No MSBuild and nothing written: the geometry is synthesized through <see cref="CompilationFactory" />.
/// </summary>
public sealed class CardContainmentTests
{
    private const string SpecName = "Acme.Architecture";

    private const string JsonWriterSource = "namespace Acme.Json { public class JsonWriter {} }";

    private const string GuardSource = "namespace Acme.Core { internal static class Guard {} }";

    private const string AnsiWriterSource = "namespace Acme.Ansi { public class AnsiWriter {} }";

    private const string ConsoleWriterSource = "namespace Acme.Console { public class ConsoleWriter {} }";

    private const string ConstantsSource = "namespace Acme.Internal { internal static class Constants {} }";

    private const string OutsideLayerWarning =
        "layer 'Shims' matched only types declared outside the solution directory; no scoped context emitted";

    // A checkout and a package folder that share only an ancestor, which is the shape a per-user package cache
    // takes beside a source tree. The paths are built and never created.
    private static readonly string Root = TestTempRoot.For("card-containment");

    private static readonly string SolutionDirectory = Path.Combine(Root, "b", "repo", "src");

    private static readonly string ProjectDirectory = Path.Combine(SolutionDirectory, "Extensions", "Acme.Json");

    private static readonly string PackageRoot = Path.Combine(Root, "a", "home", ".nuget", "packages");

    private static readonly string ShimFile = Path.Combine(
        PackageRoot, "contoso.shims", "1.0.0", "contentFiles", "cs", "any", "Shims", "XShim.cs");

    // One project compiling its own file and the package's, as a restore hands the compiler both.
    private static readonly CodebaseModel JsonWithShim = CompilationFactory.Extract("Acme.Json",
        (Path.Combine(ProjectDirectory, "JsonWriter.cs"), JsonWriterSource),
        (ShimFile, ContextFixtures.ShimSource));

    // Two sibling projects that each compile their own copy of one name, so the model carries one node for it.
    private static readonly CodebaseModel TwoCopies = CodebaseExtractor.ExtractFromCompilations([
        CompilationFactory.Compile("Acme.Ansi",
            (Path.Combine(SolutionDirectory, "Acme.Ansi", "AnsiWriter.cs"), AnsiWriterSource),
            (Path.Combine(SolutionDirectory, "Acme.Ansi", "Internal", "Constants.cs"), ConstantsSource)),
        CompilationFactory.Compile("Acme.Console",
            (Path.Combine(SolutionDirectory, "Acme.Console", "ConsoleWriter.cs"), ConsoleWriterSource),
            (Path.Combine(SolutionDirectory, "Acme.Console", "Internal", "Constants.cs"), ConstantsSource))
    ]);

    private static readonly ArchitectureModel JsonLayerModel = Checker.Model(DeclareJsonLayer);

    private static readonly ArchitectureModel ShimLayerModel = Checker.Model(DeclareShimLayer);

    private static readonly ArchitectureModel JsonCautionModel = Checker.Model(arch =>
        arch.Scope("json/serializer")
            .Caution(arch.Project("Acme.Json"))
            .Dragons("The writer's escaping is exactly what clients parse.")
            .Because("Every client depends on the exact output."));

    private static readonly ArchitectureModel ShimCautionModel = Checker.Model(arch =>
        arch.Scope("shims")
            .Caution(arch.Namespace("Contoso.Shims.*"))
            .Dragons("A restore rewrites these files.")
            .Because("A hand edit is lost on the next restore."));

    private static readonly ArchitectureModel TwoProjectLayersModel = Checker.Model(arch =>
    {
        Layer ansi = arch.Layer("Ansi", arch.Project("Acme.Ansi"));
        Layer console = arch.Layer("Console", arch.Project("Acme.Console"));
        arch.Rule("layering/ansi-no-console")
            .Enforce(ansi.MustNotReference(console))
            .Because("The escape-code writer sits below the console surface.");
        arch.Rule("layering/console-no-json")
            .Enforce(console.MustNotReference(arch.Namespace("Acme.Json.*")))
            .Because("The console surface formats its own output.");
    });

    private static void DeclareJsonLayer(Arch arch)
    {
        Layer json = arch.Layer("Json", arch.Project("Acme.Json"));
        arch.Rule("layering/json-no-console")
            .Enforce(json.MustNotReference(arch.Namespace("Acme.Console.*")))
            .Because("The serializer must not reach the console surface.");
    }

    private static void DeclareShimLayer(Arch arch)
    {
        Layer shims = arch.Layer("Shims", "Contoso.Shims.*");
        arch.Rule("layering/shims-no-json")
            .Enforce(shims.MustNotReference(arch.Namespace("Acme.Json.*")))
            .Because("A shim must not reach back into the code it supports.");
    }

    [Fact]
    public void Compose_ProjectLayerCompilingAPackageFile_PlacesItsCardAtTheProject()
    {
        // The shape the card is placed against: the package's file compiles into the project as generated code.
        JsonWithShim.Type("Contoso.Shims.XShim")
            .IsGenerated.ShouldBeTrue();

        ContextComposition composition = Compose(JsonLayerModel, JsonWithShim);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md", "Extensions/Acme.Json/AGENTS.md"]);
        composition.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Compose_TwoProjectLayersSharingAName_PlaceEachCardOnItsOwnProject()
    {
        // One node, two declarers: counting both copies for each layer collapsed both cards into the directory
        // the two projects share.
        TwoCopies.Type("Acme.Internal.Constants")
            .AlsoDeclaredBy.ShouldBe(["Acme.Console"]);

        ContextComposition composition = Compose(TwoProjectLayersModel, TwoCopies);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md", "Acme.Ansi/AGENTS.md", "Acme.Console/AGENTS.md"]);
    }

    [Fact]
    public void Compose_LayerOfOnlyPackageFiles_PlacesNoCardAndWarns()
    {
        ContextComposition composition = Compose(ShimLayerModel, JsonWithShim);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md"]);
        composition.Warnings.ShouldBe([OutsideLayerWarning]);
    }

    [Fact]
    public void Compose_CautionOverAProjectCompilingAPackageFile_PlacesItsCardAtTheProject()
    {
        ContextComposition composition = Compose(JsonCautionModel, JsonWithShim);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md", "Extensions/Acme.Json/AGENTS.md"]);
        composition.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Compose_CautionOverOnlyPackageFiles_PlacesNoCardAndWarns()
    {
        ContextComposition composition = Compose(ShimCautionModel, JsonWithShim);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md"]);
        composition.Warnings.ShouldBe(
            ["scope 'shims' matched only types declared outside the solution directory; no scoped context emitted"]);
    }

    [Fact]
    public void Compose_ProjectCompilingASiblingProjectsFile_PlacesAtTheirCommonParent()
    {
        // The other side of the boundary: a file another project owns is still the solution's own, so it moves
        // the card up to the directory both projects share, and the card stays inside.
        string linkedFile = Path.Combine(SolutionDirectory, "Extensions", "Acme.Core", "Guard.cs");
        string jsonFile = Path.Combine(ProjectDirectory, "JsonWriter.cs");
        CodebaseModel codebase = CodebaseExtractor.ExtractFromCompilations([
            CompilationFactory.Compile("Acme.Core", (linkedFile, GuardSource)),
            CompilationFactory.Compile("Acme.Json", (jsonFile, JsonWriterSource), (linkedFile, GuardSource))
        ]);

        ContextComposition composition = Compose(JsonLayerModel, codebase);

        RelativePaths(composition)
            .ShouldBe(["AGENTS.md", "Extensions/AGENTS.md"]);
    }

    [Fact]
    public void Placements_CardWithNoFileInside_KeepsItsSlotWithANullDirectory()
    {
        // The skip is an entry in the list rather than a gap in it, so a render and a path lookup read the same
        // cards in the same order.
        ArchitectureModel model = Checker.Model(arch =>
        {
            DeclareJsonLayer(arch);
            DeclareShimLayer(arch);
        });

        IReadOnlyList<ContextCard> cards = ContextFileComposer.Placements(model, JsonWithShim, SolutionDirectory);

        cards.Select(card => (ContextFixtures.Relative(SolutionDirectory, card.Destination.DirectoryPath), card.Destination.SkipReason))
            .ShouldBe([("Extensions/Acme.Json", null), (null, OutsideLayerWarning)]);
    }

    [Fact]
    public void Placements_QuarantineOverAProjectSharingAName_PlacesByThatProjectsCopy()
    {
        // The scope side of the same rule: a quarantine over a project contains every type the project
        // co-declares, and its card lands by the quarantined project's own files.
        ArchitectureModel model = Checker.Model(arch =>
            arch.Scope("ansi/escapes")
                .Quarantine(arch.Project("Acme.Ansi"))
                .Dragons("The escape sequences are exactly what terminals parse.")
                .Because("Every terminal depends on the exact bytes."));

        IReadOnlyList<ContextCard> cards = ContextFileComposer.Placements(model, TwoCopies, SolutionDirectory);

        cards.Select(card => ContextFixtures.Relative(SolutionDirectory, card.Destination.DirectoryPath))
            .ShouldBe(["Acme.Ansi"]);
    }

    [Fact]
    public void Placements_GlobLayerOverASharedName_CountsEveryCopy()
    {
        // A glob names the type at every project that compiles it, so both copies count and the card lands where
        // the two projects meet: here, the solution directory itself.
        ArchitectureModel model = Checker.Model(arch =>
        {
            Layer internals = arch.Layer("Internals", "Acme.Internal.*");
            arch.Rule("layering/internals-no-console")
                .Enforce(internals.MustNotReference(arch.Namespace("Acme.Console.*")))
                .Because("The shared internals sit below every surface.");
        });

        IReadOnlyList<ContextCard> cards = ContextFileComposer.Placements(model, TwoCopies, SolutionDirectory);

        cards.Select(card => ContextFixtures.Relative(SolutionDirectory, card.Destination.DirectoryPath))
            .ShouldBe(["."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Placements_BlankSolutionDirectory_Throws(string? solutionDirectory)
    {
        // The one entry point every card placement goes through, so a blank directory is refused here or nowhere.
        Action placing = () => ContextFileComposer.Placements(JsonLayerModel, JsonWithShim, solutionDirectory!);

        Should.Throw<ArgumentException>(placing)
            .ParamName.ShouldBe("solutionDirectory");
    }

    [Fact]
    public void DeepestCommonDirectory_ProjectAndPackageFolder_IsTheirSharedAncestor()
    {
        // The arithmetic that put a card above the checkout, pinned on its own: the collapse is right, and what
        // keeps the card inside is which files are allowed to reach it.
        string projectFile = Path.Combine(ProjectDirectory, "JsonWriter.cs");

        DirectoryPlacement.DeepestCommonDirectory([projectFile, ShimFile])
            .ShouldBe(Root);
    }

    // Every scene composes inside the one solution directory its codebases declare their files under.
    private static ContextComposition Compose(ArchitectureModel model, CodebaseModel codebase)
    {
        return ContextFileComposer.Compose(model, codebase, SolutionDirectory, SpecName);
    }

    // Each composed file as the render report prints it: relative to the solution directory, forward-slashed.
    private static List<string?> RelativePaths(ContextComposition composition)
    {
        return composition.Files
            .Select(file => ContextFixtures.Relative(SolutionDirectory, file.Path))
            .ToList();
    }
}
