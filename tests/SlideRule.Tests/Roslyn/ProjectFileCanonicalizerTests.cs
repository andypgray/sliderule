using Shouldly;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Solutions;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="ProjectFileCanonicalizer" /> answers every path exactly as
///     <see cref="PathCanonicalizer.Resolve(string)" /> does, whatever its memo already holds. The tree needs a
///     directory link and a file link, so the test skips on hosts that cannot create one (Windows without Developer
///     Mode or elevation).
/// </summary>
public sealed class ProjectFileCanonicalizerTests
{
    [Fact]
    public void Resolve_OverSiblingProjectsBehindALink_MatchesTheUnmemoizedResolve()
    {
        using TempDirectory temp = TestTempRoot.Fresh("project-canon");
        string real = temp.PathOf("real");
        string first = Directory.CreateDirectory(Path.Combine(real, "A")).FullName;
        string second = Directory.CreateDirectory(Path.Combine(real, "B")).FullName;
        File.WriteAllText(Path.Combine(first, "A.csproj"), "");
        File.WriteAllText(Path.Combine(first, "Second.csproj"), "");
        File.WriteAllText(Path.Combine(second, "B.csproj"), "");

        string link = temp.PathOf("link");
        SymlinkSupport.CreateDirectorySymlink(link, real);
        SymlinkSupport.CreateFileSymlink(Path.Combine(first, "Leaf.csproj"), Path.Combine(second, "B.csproj"));

        // In walk order: a sibling directory, a second project in a directory the memo already holds, the same
        // directory under another casing, and a project file that is itself a link.
        string[] paths =
        [
            Path.Combine(link, "A", "A.csproj"),
            Path.Combine(link, "B", "B.csproj"),
            Path.Combine(link, "A", "Second.csproj"),
            Path.Combine(link, "a", "A.csproj"),
            Path.Combine(link, "A", "Leaf.csproj")
        ];
        var canonicalizer = new ProjectFileCanonicalizer();

        paths
            .Select(path => (path, canonicalizer.Resolve(path)))
            .ShouldBe(paths.Select(path => (path, (string?)PathCanonicalizer.Resolve(path))));
    }
}
