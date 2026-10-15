using Shouldly;
using SlideRule.Roslyn.Solutions;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The declared references named, over synthetic paths: no workspace, no MSBuild and no disk. The
///     canonicalizer is a parameter, so each row passes the identity or a stand-in for a link, and the
///     filesystem never decides a verdict here.
/// </summary>
public sealed class DeclaredProjectReferencesTests
{
    [Fact]
    public void Names_DeclaredPaths_NameTheLoadedProjectsOrdinallyAndOnce()
    {
        // Arrange — the same include written twice, and the declarations in an order that is not the answer's.
        IReadOnlyDictionary<string, string> byFile = DeclaredProjectReferences.NamesByProjectFile(
            [("/repo/src/Web/Web.csproj", "Web"), ("/repo/src/Core/Core.csproj", "Core")], AsIs);

        // Act
        IReadOnlyList<string> names = DeclaredProjectReferences.Names(
            ["/repo/src/Web/Web.csproj", "/repo/src/Core/Core.csproj", "/repo/src/Core/Core.csproj"], byFile, AsIs);

        // Assert
        names.ShouldBe(["Core", "Web"]);
    }

    [Fact]
    public void Names_APathNoLoadedProjectAnswersTo_IsDropped()
    {
        // Arrange — a declared project the workspace did not load, whether because the file is gone or
        // because its load failed. There is no project to name, and the workspace's own list has none either.
        IReadOnlyDictionary<string, string> byFile =
            DeclaredProjectReferences.NamesByProjectFile([("/repo/src/Core/Core.csproj", "Core")], AsIs);

        // Act
        IReadOnlyList<string> names = DeclaredProjectReferences.Names(
            ["/repo/tools/Gone/Gone.csproj", "/repo/src/Core/Core.csproj"], byFile, AsIs);

        // Assert
        names.ShouldBe(["Core"]);
    }

    [Fact]
    public void NamesByProjectFile_AProjectWithNoFileAndAFrameworkTwin_LeavesOneEntry()
    {
        // Arrange — a project the workspace reported no file for, which no declared path can reach, and a
        // multi-targeted project loaded once per framework under its one name.
        (string? FilePath, string Name)[] loaded =
        [
            (null, "Floating"),
            ("/repo/src/Core/Core.csproj", "Core"),
            ("/repo/src/Core/Core.csproj", "Core")
        ];

        // Act
        IReadOnlyDictionary<string, string> byFile = DeclaredProjectReferences.NamesByProjectFile(loaded, AsIs);

        // Assert
        byFile.ShouldBe([new KeyValuePair<string, string>("/repo/src/Core/Core.csproj", "Core")]);
    }

    [Fact]
    public void Names_ALinkSpellingAndItsResolvedPath_MeetThroughTheCanonicalizer()
    {
        // Arrange — the workspace named the project through a link, and the evaluated include resolved to
        // the directory it points at. Only the canonicalizer makes the two spellings one file.
        IReadOnlyDictionary<string, string> resolved =
            DeclaredProjectReferences.NamesByProjectFile([("/repo/link/Core/Core.csproj", "Core")], ThroughLink);
        IReadOnlyDictionary<string, string> unresolved =
            DeclaredProjectReferences.NamesByProjectFile([("/repo/link/Core/Core.csproj", "Core")], AsIs);

        // Act
        IReadOnlyList<string> named = DeclaredProjectReferences.Names(["/repo/real/Core/Core.csproj"], resolved, ThroughLink);
        IReadOnlyList<string> missed = DeclaredProjectReferences.Names(["/repo/real/Core/Core.csproj"], unresolved, AsIs);

        // Assert: the two legs differ, so the one that meets is the canonicalizer's doing.
        missed.ShouldBeEmpty("without the canonicalizer the two spellings are two files");
        named.ShouldBe(["Core"]);
    }

    [Fact]
    public void Undeclared_WhatADeclaredProjectReaches_IsAccountedFor()
    {
        // Arrange — a plugin that declares only the web framework, and compiles against the framework's own
        // closure as well, which is the SDK adding its transitive references.
        Func<string, IEnumerable<string>> reach = NopReach;

        // Act
        IReadOnlyList<string> undeclared = DeclaredProjectReferences.Undeclared(
            ["Core", "Data", "Framework", "Services"], ["Framework"], reach);

        // Assert
        undeclared.ShouldBeEmpty();
    }

    [Fact]
    public void Undeclared_ADeclarationTheEvaluationLost_NamesEveryReferenceItWouldHaveAccountedFor()
    {
        // Arrange — the same plugin with its one declaration lost, which is what an include spelled with a
        // property the evaluation lacks comes to: the compilation still references all four.
        Func<string, IEnumerable<string>> reach = NopReach;

        // Act
        IReadOnlyList<string> undeclared = DeclaredProjectReferences.Undeclared(
            ["Services", "Framework", "Data", "Core", "Core"], [], reach);

        // Assert — ordinal, and once.
        undeclared.ShouldBe(["Core", "Data", "Framework", "Services"]);
    }

    // nopCommerce's shape, as each project's workspace references (the SDK's closure) by name.
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> NopGraph =
        new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["Core"] = [],
            ["Data"] = ["Core"],
            ["Services"] = ["Core", "Data"],
            ["Framework"] = ["Core", "Data", "Services"]
        };

    // What a project of that shape reaches: its workspace references already carry the SDK's closure, so each list
    // is the project's whole reach.
    private static IEnumerable<string> NopReach(string name)
    {
        return NopGraph.GetValueOrDefault(name) ?? [];
    }

    private static string? AsIs(string? path)
    {
        return path;
    }

    // A stand-in for a link: every path through /repo/link/ resolves to the same place under /repo/real/.
    private static string? ThroughLink(string? path)
    {
        return path?.Replace("/link/", "/real/", StringComparison.Ordinal);
    }
}
