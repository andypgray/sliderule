using Shouldly;
using SlideRule.Codebase;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     What a project predicate reads of a declared package through <see cref="IProjectInfo.PackageReferences" />:
///     the identifier, the file and line that declare it, and whether it is private — the same facts the engine keeps
///     on its own node, through the public interface an author names.
/// </summary>
public sealed class PackageReferenceInfoTests
{
    [Fact]
    public void AProjectsPackage_ReadsAsItsDeclarationThroughThePublicInterface()
    {
        // Arrange
        string declaringFile = Path.Combine("repo", "Directory.Packages.props");
        PackageReference declared = ProjectFacts.Package("Serilog", declaringFile, 12, isPrivate: true);
        IProjectInfo project = ProjectFacts.Project("App", packageReferences: [declared]);

        // Act
        IPackageReferenceInfo package = project.PackageReferences.ShouldHaveSingleItem();

        // Assert
        package.Name.ShouldBe("Serilog");
        package.FilePath.ShouldBe(declaringFile);
        package.Line.ShouldBe(12);
        package.IsPrivate.ShouldBeTrue();
    }
}
