using Shouldly;
using SlideRule.Roslyn.Caching;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     The cone walk over hand-built directories: which <c>*.cs</c> it yields where a file name's case, a directory
///     link or an unreadable directory decides, read through <see cref="ProjectCone.Adds" />, which sorts.
/// </summary>
public sealed class ProjectConeTests
{
    [Fact]
    public void Adds_UpperCaseCsSuffixWhereFileNamesIgnoreCase_YieldsIt()
    {
        FileNameCase.SkipUnlessIgnored();
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string upper = temp.WriteFile(["Upper.CS"], "class Upper { }");

        ProjectCone.Adds(temp.Path, [])
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

        ProjectCone.Adds(project, [])
            .ShouldBe([temp.PathOf("Project", "Linked", "Linked.cs"), rules]);
    }

    [Fact]
    public void Adds_LinkBackToTheProjectDirectory_EndsWithEachSourceOnce()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string project = temp.PathOf("Project");
        string rules = temp.WriteFile(["Project", "Rules.cs"], "class Rules { }");
        SymlinkSupport.CreateDirectorySymlink(temp.PathOf("Project", "Loop"), project);

        ProjectCone.Adds(project, [])
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

        ProjectCone.Adds(project, [])
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

        ProjectCone.Adds(project, [])
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

        ProjectCone.Adds(project, [])
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

        ProjectCone.Adds(project, [])
            .ShouldBe([rules]);
    }

    [Fact]
    public void Enumerate_InaccessibleDirectory_ThrowsUnlessAskedToSkipIt()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-cone");
        string rules = temp.WriteFile(["Rules.cs"], "class Rules { }");
        temp.WriteFile(["Denied", "Unseen.cs"], "class Unseen { }");
        using ScopedDirectoryDenial denial = ScopedDirectoryDenial.Deny(temp.PathOf("Denied"));

        Should.Throw<UnauthorizedAccessException>(() => ProjectCone.Enumerate(temp.Path).ToList());
        ProjectCone.Enumerate(temp.Path, skipInaccessible: true)
            .ShouldBe([rules]);
    }
}
