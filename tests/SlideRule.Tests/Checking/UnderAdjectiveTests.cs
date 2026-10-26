using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The source-location adjective over the fast path (GRAMMAR §5.2): which types a path glob keeps, read against
///     the solution directory a host hands the run. Every type of <see cref="Sources.FolderModel" /> is in one
///     namespace, so a path is the only thing that can tell them apart, and <c>MustBeSealed</c> reds each type the
///     subject kept — none is sealed — so a failure list is exactly a selection.
/// </summary>
public sealed class UnderAdjectiveTests
{
    [Fact]
    public void Under_AFolder_KeepsEveryTypeDeclaredBelowIt()
    {
        // The prefix reading: a folder glob reaches its subfolders, and means what its `/**` form means.
        Selected("src/Widgets")
            .ShouldBe(["App.Button", "App.Panel"]);
        Selected("src/Widgets/**")
            .ShouldBe(["App.Button", "App.Panel"]);
    }

    [Fact]
    public void Under_SegmentWildcards_MatchWithinOneSegment_AndDoubleStarAcrossAny()
    {
        Selected("src/*/Internal")
            .ShouldBe(["App.Ansi"]);
        Selected("**/Internal")
            .ShouldBe(["App.Ansi"]);
        Selected("src/*")
            .ShouldBe(["App.Ansi", "App.Button", "App.Panel", "App.Split", "App.Writer"]);
    }

    [Fact]
    public void Under_AGlobNamingOneFile_KeepsTheTypesDeclaredInIt()
    {
        Selected("src/Console/Writer.cs")
            .ShouldBe(["App.Writer"]);
    }

    [Fact]
    public void Under_IgnoresCase_OnEveryOperatingSystem()
    {
        Selected("SRC/widgets")
            .ShouldBe(["App.Button", "App.Panel"]);
    }

    [Fact]
    public void Under_APartialType_IsUnderAGlobWhenAnyOfItsFilesIs()
    {
        // Split is declared in src/Split and in vendor: either glob reaches it.
        Selected("vendor")
            .ShouldBe(["App.Split"]);
        Selected("src/Split")
            .ShouldBe(["App.Split"]);
    }

    [Fact]
    public void Under_SeveralGlobs_KeepWhatAnyOneKeeps()
    {
        Selected("src/Widgets/Deep", "src/Console/Internal")
            .ShouldBe(["App.Ansi", "App.Panel"]);
    }

    [Fact]
    public void Under_AFileOutsideTheSolutionDirectory_IsUnderNoGlob()
    {
        // Shared sits in /elsewhere, so not even `**` reaches it.
        Selected("**")
            .ShouldBe(["App.Ansi", "App.Button", "App.Panel", "App.Split", "App.Writer"]);
    }

    [Fact]
    public void Under_OnATarget_ReachesTheTypesDeclaredThere()
    {
        // The target side reads the same way: Writer reaches into Internal, and nothing else does.
        RuleResult result = Checker.Run(Sources.FolderModel, root: Sources.FolderRoot, define: arch =>
                arch.Rule("console/internal")
                    .Enforce(arch.Types.Under("src").MustNotReference(arch.Types.Under("src/Console/Internal")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Writer", "App.Ansi");
    }

    [Fact]
    public void Under_AnExternalTarget_IsUnderNoGlob()
    {
        // Writer reaches Ansi and the framework's StringBuilder; a ban over every path reds the first alone,
        // because a type the solution does not declare has no file in it.
        RuleResult result = Checker.Run(Sources.FolderModel, root: Sources.FolderRoot, define: arch =>
                arch.Rule("console/self-contained")
                    .Enforce(arch.Types.Named("Writer").MustNotReference(arch.Types.Under("**")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Writer", "App.Ansi");
    }

    [Fact]
    public void Under_ARunGivenNoRoot_IsARuleError_NotAnEmptySelection()
    {
        // Fail closed: with no directory to read a glob against, the rule cannot be measured, which is a rule
        // error rather than an empty subject that a narrowing could turn into a skip.
        RuleResult result = Checker.Run(Sources.FolderModel, arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Widgets").MustBeSealed())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.RuleError, "A path glob is relative to the solution directory, and this run was given none.");
    }

    [Fact]
    public void Under_ARunOverOneProject_ReadsTheGlobAgainstTheRootItWasGiven()
    {
        Checker.Run(Sources.FolderModel, narrowing: NarrowedUniverse.OneProject, root: Sources.FolderRoot, define: arch =>
                arch.Rule("widgets/sealed")
                    .Enforce(arch.Types.Under("src/Widgets").MustBeSealed())
                    .Because("b"))
            .Single()
            .ShapeSubjects()
            .ShouldBe(["App.Button", "App.Panel"]);
    }

    [Fact]
    public void Under_ATypeOnlyAGeneratorDeclares_IsUnderTheGlobOfThePathItsDocumentWasGiven()
    {
        // A generator's document carries whatever path its host gives it. This driver gives a relative one, as a
        // host that writes no generator output to disk does, so the generated type is under nothing, not even
        // `**`, while the authored type beside it is wherever its own file is.
        CodebaseModel codebase = CompilationFactory.ExtractConsoleAppWithGenerator(
            new PostInitializationGenerator(("Emitted.g.cs", "namespace Gen { public sealed class Emitted { } }")),
            ("/repo/src/App/Program.cs", """
                                         System.Console.WriteLine();

                                         namespace App
                                         {
                                             public sealed class Gizmo
                                             {
                                             }
                                         }
                                         """));
        IReadOnlyList<string> underEverything = Checker.Selects(
            codebase, Sources.FolderRoot, arch => arch.Types.Under("**"));

        underEverything.ShouldContain("App.Gizmo");
        underEverything.ShouldNotContain("Gen.Emitted");
    }

    private static IReadOnlyList<string> Selected(string first, params string[] more)
    {
        return Checker.Selects(Sources.FolderModel, Sources.FolderRoot, arch => arch.Types.Under(first, more));
    }
}
