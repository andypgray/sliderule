using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="UnsupportedEntryMessages.IsAbout" />, which recognizes the workspace's failure for an unsupported
///     entry by the path in it, and <see cref="UnsupportedEntryMessages.OtherLanguageProjectFiles" />, which reads the
///     other-language project files out of such failures. <c>Cli/SolutionEntriesE2ETests</c> drives both through a
///     real load; these pin that a path is matched whole, so a message about a different file whose path starts with
///     a Web Site's is kept, and which quoted paths count as another language's project file.
/// </summary>
public sealed class UnsupportedEntryMessagesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "web-site-entries-tests");

    private static readonly string UrlEntry = Path.Combine(Root, "http:", "localhost:3961");

    private static readonly string FolderEntry = Path.Combine(Root, "LegacySite") + Path.DirectorySeparatorChar;

    // A folder entry written by hand, without the trailing separator Visual Studio writes.
    private static readonly string BareFolderEntry = Path.Combine(Root, "WebSite1");

    private static readonly char Separator = Path.DirectorySeparatorChar;

    private static readonly string Native = Path.Combine(Root, "Native", "Native.vcxproj");

    private static readonly string Database = Path.Combine(Root, "Db", "Db.sqlproj");

    // The workspace's two failures for one referenced C++ project, as measured, and the shapes around them.
    public static TheoryData<string, string[], string[]> ProjectFileMessages => new()
    {
        {
            "both failures for one project",
            [
                $"Msbuild failed when processing the file '{Native}' with message: The target \"GetTargetPath\" does not exist in the project.",
                $"Cannot open project '{Native}' because the file extension '.vcxproj' is not associated with a language."
            ],
            [Native]
        },
        { "a project file not on disk", [$"Project file not found: '{Native}'"], [Native] },
        { "two projects, in the order named", [$"'{Database}'", $"'{Native}'"], [Database, Native] },
        { "a localized quote", [$"Das Projekt „{Native}“ kann nicht geöffnet werden."], [Native] },
        { "a C# project", [$"Cannot open project '{Path.Combine(Root, "App", "App.csproj")}'"], [] },
        { "a relative path", [$"Cannot open project '{Path.Combine("Native", "Native.vcxproj")}'"], [] },
        { "a file that is no project", [$"Msbuild failed when processing the file '{Path.Combine(Root, "x.targets")}'"], [] }
    };

    public static TheoryData<string, string, bool> Messages => new()
    {
        { "the URL entry, quoted", $"Project file not found: '{UrlEntry}'", true },
        { "the URL entry, at the end", $"Project file not found: {UrlEntry}", true },
        { "the folder entry, quoted", $"Project file not found: '{FolderEntry}'", true },
        { "a longer port", $"Project file not found: '{UrlEntry}0'", false },
        { "a file inside the folder", $"Project file not found: '{FolderEntry}Inner.csproj'", false },
        { "a longer port, then the entry", $"'{UrlEntry}0' and '{UrlEntry}'", true },
        { "no entry at all", "Msbuild failed when processing the file 'App.csproj'", false },
        { "the bare folder entry, quoted", $"Project file not found: '{BareFolderEntry}'", true },
        { "the bare folder entry, at the end", $"Project file not found: {BareFolderEntry}", true },
        { "the bare folder entry, then a space", $"Project file not found: {BareFolderEntry} (a Web Site)", true },
        {
            "the bare folder entry, then a line naming a path",
            $"Project file not found: {BareFolderEntry}\n{Root}{Separator}x.targets", true
        },
        {
            "the URL entry quoted, then a tail naming a path",
            $"Project file not found: '{UrlEntry}' with message: {Root}{Separator}x.targets", true
        },
        {
            "a sibling folder named like the bare entry and a space, quoted",
            $"Msbuild failed when processing the file '{BareFolderEntry} - Copy{Separator}X.csproj'", false
        },
        {
            "a sibling folder named like the bare entry and a space, at the end",
            $"Msbuild failed when processing the file {BareFolderEntry} - Copy{Separator}X.csproj", false
        },
        {
            "a sibling folder named like the bare entry and a parenthesis",
            $"Msbuild failed when processing the file '{BareFolderEntry}(2){Separator}X.csproj'", false
        }
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void IsAbout_Message_MatchesOnlyAWholeEntryPath(string shape, string message, bool expected)
    {
        UnsupportedEntryMessages.IsAbout(message, [UrlEntry, FolderEntry, BareFolderEntry])
            .ShouldBe(expected, shape);
    }

    [Theory]
    [MemberData(nameof(ProjectFileMessages))]
    public void OtherLanguageProjectFiles_Messages_NameEachQuotedAbsoluteNonCsharpProjectFileOnce(
        string shape, string[] messages, string[] expected)
    {
        UnsupportedEntryMessages.OtherLanguageProjectFiles(messages)
            .ShouldBe(expected, shape);
    }
}
