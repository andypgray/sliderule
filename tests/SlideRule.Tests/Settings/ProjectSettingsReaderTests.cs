using Shouldly;
using Xunit;
using SlideRule.Cli.Settings;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Settings;

/// <summary>
///     Reading a <c>sliderule.json</c>: every key resolved against the file's own directory, the hand-written
///     leniencies (comments, trailing commas, the editor's <c>$schema</c>), and a refusal naming the file and the
///     key for everything else, since a misspelt key in a committed file would otherwise be ignored on every run.
/// </summary>
public sealed class ProjectSettingsReaderTests : IDisposable
{
    private readonly TempDirectory _temp = TestTempRoot.Fresh("settings-reader");

    public void Dispose()
    {
        _temp.Dispose();
    }

    private string Write(string json)
    {
        return _temp.WriteFile([ProjectSettings.FileName], json);
    }

    [Fact]
    public void Read_EveryKey_ResolvesEachPathAgainstTheFilesDirectory()
    {
        // Arrange
        string path = Write("""
                            {
                              "solution": "src/App.sln",
                              "spec": "arch/App.Spec/App.Spec.csproj",
                              "render": {
                                "model": true,
                                "diagram": { "path": "docs/ARCHITECTURE.md", "only": ["App", "App.*"], "exclude": ["App.Tests"] }
                              }
                            }
                            """);

        // Act
        ProjectSettings settings = ProjectSettingsReader.Read(path);

        // Assert
        settings.FilePath.ShouldBe(path);
        settings.Solution.ShouldBe(_temp.PathOf("src", "App.sln"));
        settings.Spec.ShouldBe(_temp.PathOf("arch", "App.Spec", "App.Spec.csproj"));
        settings.Render.Model.ShouldBeTrue();
        DiagramSettings diagram = settings.Render.Diagram.ShouldNotBeNull();
        diagram.Target.ShouldBe(_temp.PathOf("docs", "ARCHITECTURE.md"));
        diagram.Only.ShouldBe(["App", "App.*"]);
        diagram.Exclude.ShouldBe(["App.Tests"]);
    }

    [Fact]
    public void Read_SolutionAlone_DeclaresNoSpecAndNothingToRenderBeyondTheBlocks()
    {
        ProjectSettings settings = ProjectSettingsReader.Read(Write("""{ "solution": "App.sln" }"""));

        settings.Spec.ShouldBeNull();
        settings.Render.ShouldBe(RenderSettings.None);
    }

    [Fact]
    public void Read_CommentsTrailingCommasAndTheSchemaKey_AreAccepted()
    {
        // The file is written by hand, so the two leniencies a hand editor reaches for are allowed, and the
        // editor's own schema pointer is read and ignored rather than refused as unknown.
        string path = Write("""
                            {
                              "$schema": "https://example.invalid/sliderule.schema.json",
                              // The solution every command reads.
                              "solution": "App.sln",
                            }
                            """);

        ProjectSettingsReader.Read(path)
            .Solution.ShouldBe(_temp.PathOf("App.sln"));
    }

    [Theory]
    [InlineData("""{ "solution": "App.sln", "solutoin": "x" }""", "unknown key 'solutoin'; the file takes solution, spec, render.")]
    [InlineData("""{ "solution": "App.sln", "render": { "modle": true } }""", "unknown key 'render.modle'; 'render' takes model, diagram.")]
    [InlineData(
        """{ "solution": "App.sln", "render": { "diagram": { "path": "A.md", "onyl": [] } } }""",
        "unknown key 'render.diagram.onyl'; 'render.diagram' takes path, only, exclude.")]
    public void Read_UnknownKey_IsRefusedNamingItAndWhatIsTaken(string json, string expected)
    {
        string path = Write(json);

        var ex = Should.Throw<UserErrorException>(() => ProjectSettingsReader.Read(path));

        ex.Message.ShouldBe($"{path}: {expected}");
    }

    [Theory]
    [InlineData("""{ "spec": "A.csproj" }""", "'solution' is required")]
    [InlineData("""{ "solution": "" }""", "'solution' must be a non-blank string")]
    [InlineData("""{ "solution": 3 }""", "'solution' must be a non-blank string")]
    [InlineData("""{ "solution": "App.sln", "spec": " " }""", "'spec' must be a non-blank string")]
    [InlineData("""{ "solution": "App.sln", "render": [] }""", "'render' is an object")]
    [InlineData("""{ "solution": "App.sln", "render": { "model": "yes" } }""", "'render.model' must be true or false.")]
    [InlineData("""{ "solution": "App.sln", "render": { "diagram": "A.md" } }""", "'render.diagram' is an object")]
    [InlineData("""{ "solution": "App.sln", "render": { "diagram": {} } }""", "'render.diagram.path' is required")]
    [InlineData(
        """{ "solution": "App.sln", "render": { "diagram": { "path": "A.md", "only": "App" } } }""",
        "'render.diagram.only' must be an array of non-blank project-name globs.")]
    [InlineData("""[ "App.sln" ]""", "the settings are one JSON object")]
    public void Read_AValueOfTheWrongShape_IsRefusedNamingTheFileAndTheKey(string json, string expected)
    {
        string path = Write(json);

        var ex = Should.Throw<UserErrorException>(() => ProjectSettingsReader.Read(path));

        ex.Message.ShouldStartWith($"{path}: ");
        ex.Message.ShouldContain(expected);
    }

    [Fact]
    public void Read_AFileThatCannotBeRead_IsRefusedNamingIt()
    {
        // A directory where the file should be is the one unreadable file every platform can make.
        string path = _temp.PathOf(ProjectSettings.FileName);
        Directory.CreateDirectory(path);

        var ex = Should.Throw<UserErrorException>(() => ProjectSettingsReader.Read(path));

        ex.Message.ShouldStartWith($"{path}: could not be read: ");
    }

    [Fact]
    public void Read_NotJson_IsRefusedNamingTheFile()
    {
        string path = Write("{ \"solution\": ");

        var ex = Should.Throw<UserErrorException>(() => ProjectSettingsReader.Read(path));

        ex.Message.ShouldStartWith($"{path}: not valid JSON: ");
    }
}
