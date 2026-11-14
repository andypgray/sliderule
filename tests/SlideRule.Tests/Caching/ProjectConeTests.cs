using Shouldly;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     The cone walk over hand-built directories: which sources it yields where a file name's case, a directory
///     link, an unreadable directory or a bin or obj beside a project file decides, read through
///     <see cref="ProjectCone.Adds" />, which sorts, and through
///     <see cref="BuildOutputDirectories.IsUnderBuildOutput" />, the document filter's build-output test, which
///     must agree with the walk's.
/// </summary>
public sealed class ProjectConeTests
{
    [Fact]
    public void Adds_UpperCaseCsSuffixWhereFileNamesIgnoreCase_YieldsIt()
    {
        FileNameCase.SkipUnlessIgnored();
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string upper = temp.WriteFile(["Upper.CS"], "class Upper { }");

        ProjectCone.Adds(temp.Path, ".cs", [])
            .ShouldBe([upper]);
    }

    [Fact]
    public void Adds_LinkToADirectoryOutsideTheWalk_YieldsItsSourcesThroughTheLink()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        temp.WriteFile(["Outside", "Linked.cs"], "class Linked { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "Linked"), temp.PathOf("Outside"));

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([temp.PathOf("Project", "Linked", "Linked.cs"), rules]);
    }

    [Fact]
    public void Adds_LinkBackToTheProjectDirectory_EndsWithEachSourceOnce()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "Loop"), project);

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([rules]);
    }

    [Fact]
    public void Adds_LinkReachedThroughAnotherLinkBackToItsOwnTarget_Ends()
    {
        // The second link's path never contains its own target, because the walk reached it through the first.
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        temp.WriteFile(["Outside", "Outer.cs"], "class Outer { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "Out"), temp.PathOf("Outside"));
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Outside", "Self"), temp.PathOf("Outside"));

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([temp.PathOf("Project", "Out", "Outer.cs"), rules]);
    }

    [Fact]
    public void Adds_TwoDirectoriesLinkedToEachOther_EndsAfterOneCrossingEachWay()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string a = temp.WriteFile(["Project", "A", "A.cs"], "class A { }");
        string b = temp.WriteFile(["Project", "B", "B.cs"], "class B { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "A", "ToB"), temp.PathOf("Project", "B"));
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "B", "ToA"), temp.PathOf("Project", "A"));

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([a, temp.PathOf("Project", "A", "ToB", "B.cs"), b, temp.PathOf("Project", "B", "ToA", "A.cs")]);
    }

    [Fact]
    public void Adds_LinkToAnAncestorOfTheProjectDirectory_IsNotEntered()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        temp.WriteFile(["Above.cs"], "class Above { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "Up"), temp.Path);

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([rules]);
    }

    [Fact]
    public void Adds_LinksThatPointAtEachOther_AreSkippedAndTheRestStillWalked()
    {
        // Neither link resolves to a directory, so nothing behind them can be compiled.
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "A"), temp.PathOf("Project", "B"));
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "B"), temp.PathOf("Project", "A"));

        ProjectCone.Adds(project, ".cs", [])
            .ShouldBe([rules]);
    }

    // Each source as the SDK's default compile glob reads it, in a project whose directory also holds a nested
    // project: a bin or obj is build output only beside a project file, and its name matches ordinally.
    public static TheoryData<string[], bool> BuildOutputRows => new()
    {
        { ["Rules.cs"], true },
        { ["Sub", "bin", "DeepBin.cs"], true },
        { ["Sub", "obj", "DeepObj.cs"], true },
        { ["Bin", "InBin.cs"], true },
        { ["bin", "RootBin.cs"], false },
        { ["obj", "RootObj.cs"], false },
        { ["Nested", "obj", "Gen.cs"], false }
    };

    [Theory]
    [MemberData(nameof(BuildOutputRows))]
    public void Adds_SourceUnderABinOrObj_IsYieldedOnlyWhenNoProjectFileSitsBesideIt(string[] segments, bool isSource)
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        temp.WriteFile(["Root.csproj"], "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        temp.WriteFile(["Nested", "Nested.csproj"], "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        string source = temp.WriteFile(segments, "class Probe { }");

        ProjectCone.Adds(temp.Path, ".cs", [])
            .Contains(source)
            .ShouldBe(isSource, string.Join('/', segments));
        new BuildOutputDirectories().IsUnderBuildOutput(temp.Path, source)
            .ShouldBe(!isSource, string.Join('/', segments));
    }

    [Theory]
    [InlineData(new[] { "artifacts", "obj", "App", "debug", "App.AssemblyInfo.cs" }, true)]
    [InlineData(new[] { "Shared", "Shared.cs" }, false)]
    public void IsUnderBuildOutput_DocumentOutsideTheProjectDirectory_IsBuildOutputOnAnyBinOrObjSegment(
        string[] segments, bool isBuildOutput)
    {
        // No project file sits beside the artifacts layout's obj, which the cone never walks into.
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        temp.WriteFile(["src", "App", "App.csproj"], "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        string document = temp.WriteFile(segments, "class Probe { }");

        new BuildOutputDirectories().IsUnderBuildOutput(temp.PathOf("src", "App"), document)
            .ShouldBe(isBuildOutput, string.Join('/', segments));
    }

    [Fact]
    public void IsUnderBuildOutput_BinUnderADirectoryDeletedSinceTheLoad_IsASource()
    {
        // Nothing says a project file sits beside the bin, so the document stays tracked, the side that can cost
        // a cache miss and never a stale answer.
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        temp.WriteFile(["Root.csproj"], "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        string document = temp.PathOf("Gone", "bin", "Deep.cs");

        new BuildOutputDirectories().IsUnderBuildOutput(temp.Path, document)
            .ShouldBeFalse();
    }

    [Fact]
    public void Adds_VisualBasicSuffix_YieldsOnlyThatLanguagesSources()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string ledger = temp.WriteFile(["Ledger.vb"], "Class Ledger\nEnd Class\n");
        temp.WriteFile(["Widget.cs"], "class Widget { }");

        ProjectCone.Adds(temp.Path, ".vb", [])
            .ShouldBe([ledger]);
    }

    [Fact]
    public void Enumerate_InaccessibleDirectory_ThrowsUnlessAskedToSkipIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string rules = temp.WriteFile(["Rules.cs"], "class Rules { }");
        temp.WriteFile(["Denied", "Unseen.cs"], "class Unseen { }");
        using ScopedDirectoryDenial denial = ScopedDirectoryDenial.Deny(temp.PathOf("Denied"));

        Should.Throw<UnauthorizedAccessException>(() => ProjectCone.Enumerate(temp.Path, ".cs").ToList());
        ProjectCone.Enumerate(temp.Path, ".cs", skipInaccessible: true)
            .ShouldBe([rules]);
    }
}
