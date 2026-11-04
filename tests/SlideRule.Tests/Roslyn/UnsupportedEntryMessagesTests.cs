using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="UnsupportedEntryMessages.IsAbout" />, which recognizes the workspace's failure for an unsupported
///     entry by the path in it. <c>Cli/SolutionEntriesE2ETests</c> drives it through a real load; these pin that a
///     path is matched whole, so a message about a different file whose path starts with a Web Site's is kept.
/// </summary>
public sealed class UnsupportedEntryMessagesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "web-site-entries-tests");

    private static readonly string UrlEntry = Path.Combine(Root, "http:", "localhost:3961");

    private static readonly string FolderEntry = Path.Combine(Root, "LegacySite") + Path.DirectorySeparatorChar;

    // A folder entry written by hand, without the trailing separator Visual Studio writes.
    private static readonly string BareFolderEntry = Path.Combine(Root, "WebSite1");

    private static readonly char Separator = Path.DirectorySeparatorChar;

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
}
