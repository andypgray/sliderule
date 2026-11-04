using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="WebSiteEntries.IsAbout" />, which drops the workspace's failure for a Web Site entry by the path in
///     it. <c>Cli/SolutionEntriesE2ETests</c> drives it through a real load; these pin that a path is matched whole,
///     so a message about a different file whose path starts with a Web Site's is kept.
/// </summary>
public sealed class WebSiteEntriesTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "web-site-entries-tests");

    private static readonly string UrlEntry = Path.Combine(Root, "http:", "localhost:3961");

    private static readonly string FolderEntry = Path.Combine(Root, "LegacySite") + Path.DirectorySeparatorChar;

    public static TheoryData<string, string, bool> Messages => new()
    {
        { "the URL entry, quoted", $"Project file not found: '{UrlEntry}'", true },
        { "the URL entry, at the end", $"Project file not found: {UrlEntry}", true },
        { "the folder entry, quoted", $"Project file not found: '{FolderEntry}'", true },
        { "a longer port", $"Project file not found: '{UrlEntry}0'", false },
        { "a file inside the folder", $"Project file not found: '{FolderEntry}Inner.csproj'", false },
        { "a longer port, then the entry", $"'{UrlEntry}0' and '{UrlEntry}'", true },
        { "no entry at all", "Msbuild failed when processing the file 'App.csproj'", false }
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public void IsAbout_Message_MatchesOnlyAWholeEntryPath(string shape, string message, bool expected)
    {
        WebSiteEntries.IsAbout(message, [UrlEntry, FolderEntry])
            .ShouldBe(expected, shape);
    }
}
