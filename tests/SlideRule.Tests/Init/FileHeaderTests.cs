using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary>
///     <see cref="FileHeader" />: the header a repository's <c>.editorconfig</c> asks the spec class to carry.
/// </summary>
public sealed class FileHeaderTests : IDisposable
{
    private readonly TempDirectory _temp = TestTempRoot.Fresh("init-file-header");

    public void Dispose()
    {
        _temp.Dispose();
    }

    [Fact]
    public void For_ATemplateAboveTheFile_WritesItAsLineComments()
    {
        // Arrange
        _temp.WriteFile([".editorconfig"], "root = true\n\n[*.cs]\nfile_header_template = Copyright Acme.\\n\\n{fileName} is ours.\n");
        string spec = _temp.PathOf("arch", "Acme.Architecture", "ArchitectureSpec.cs");

        // Act
        string? header = FileHeader.For(spec);

        // Assert
        header.ShouldBe("// Copyright Acme.\n//\n// ArchitectureSpec.cs is ours.\n\n");
    }

    [Fact]
    public void For_ANearerConfigUnsettingIt_WritesNone()
    {
        // Arrange
        _temp.WriteFile([".editorconfig"], "root = true\n\n[*.cs]\nfile_header_template = Copyright Acme.\n");
        _temp.WriteFile(["arch", ".editorconfig"], "[*.cs]\nfile_header_template = unset\n");
        string spec = _temp.PathOf("arch", "Acme.Architecture", "ArchitectureSpec.cs");

        // Act
        string? header = FileHeader.For(spec);

        // Assert
        header.ShouldBeNull();
    }

    [Fact]
    public void For_ASectionThatDoesNotMatchCSharp_WritesNone()
    {
        // Arrange
        _temp.WriteFile([".editorconfig"], "root = true\n\n[*.vb]\nfile_header_template = Copyright Acme.\n");
        string spec = _temp.PathOf("arch", "Acme.Architecture", "ArchitectureSpec.cs");

        // Act
        string? header = FileHeader.For(spec);

        // Assert
        header.ShouldBeNull();
    }
}
