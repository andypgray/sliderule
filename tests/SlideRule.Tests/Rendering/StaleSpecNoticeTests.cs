using Shouldly;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.SpecLoading;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The stale-spec warning's sentence for every reason a source can differ from its build, and its stamp, with
///     every path made relative to the solution directory.
/// </summary>
public sealed class StaleSpecNoticeTests
{
    private static readonly string SolutionDirectory = Path.Combine(Path.GetTempPath(), "repo");

    private static readonly string ProjectFile = Path.Combine(SolutionDirectory, "arch", "App.Arch", "App.Arch.csproj");

    private static readonly string RulesFile = Path.Combine(SolutionDirectory, "arch", "App.Arch", "Rules.cs");

    // One sentence per reason; the inventory test below holds this table to the enum, so a new reason cannot ship
    // without its wording.
    private static readonly Dictionary<StaleReason, string> Changes = new()
    {
        [StaleReason.Changed] = "'arch/App.Arch/Rules.cs' has changed since it was built",
        [StaleReason.Missing] = "'arch/App.Arch/Rules.cs' no longer exists",
        [StaleReason.Added] = "'arch/App.Arch/Rules.cs' was added after it was built",
        [StaleReason.Newer] = "'arch/App.Arch/Rules.cs' was written after it was built"
    };

    [Fact]
    public void Changes_CoverEveryStaleReason()
    {
        Changes.Keys.ShouldBe(Enum.GetValues<StaleReason>(), ignoreOrder: true);
    }

    [Fact]
    public void Text_EveryReason_NamesTheProjectTheFileAndTheRebuild()
    {
        List<string> sentences = Changes.Keys
            .Select(reason => StaleSpecNotice.Text(Stale(reason), SolutionDirectory))
            .ToList();

        sentences.ShouldBe(Changes.Values
            .Select(change => $"the spec project 'App.Arch' differs from its last build ({change}); this run used "
                              + "the rules that build holds. Rebuild it: dotnet build arch/App.Arch/App.Arch.csproj")
            .ToList());
    }

    [Fact]
    public void Render_HumanRun_PrefixesTheLineAndADocumentRunEchoesItBare()
    {
        var human = new StringWriter();
        var json = new StringWriter();
        string text = StaleSpecNotice.Text(Stale(StaleReason.Changed), SolutionDirectory);

        StaleSpecNotice.Render(human, Stale(StaleReason.Changed), SolutionDirectory);
        StaleSpecNotice.Render(json, Stale(StaleReason.Changed), SolutionDirectory, json: true);

        human.ToString().ShouldBe($"warning: {text}{Environment.NewLine}");
        json.ToString().ShouldBe($"{text}{Environment.NewLine}");
    }

    [Fact]
    public void StampAboveAnAnswer_StaleSpec_WritesTheBareSentenceThenABlankLine()
    {
        var output = new StringWriter();
        string text = StaleSpecNotice.Text(Stale(StaleReason.Changed), SolutionDirectory);

        StaleSpecNotice.Stamp(output, Stale(StaleReason.Changed), SolutionDirectory);

        output.ToString().ShouldBe($"{text}{Environment.NewLine}{Environment.NewLine}");
    }

    [Fact]
    public void StampAboveAnAnswer_FreshSpec_WritesNothing()
    {
        var output = new StringWriter();

        StaleSpecNotice.Stamp(output, null, SolutionDirectory);

        output.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Stamp_NewerByFileTime_IsRelative()
    {
        StaleSpecStamp.From(Stale(StaleReason.Newer, SpecComparison.FileTime), SolutionDirectory)
            .ShouldBe(new StaleSpecStamp("arch/App.Arch/App.Arch.csproj", "arch/App.Arch/Rules.cs", StaleReason.Newer, SpecComparison.FileTime));
    }

    private static StaleSpec Stale(StaleReason reason, SpecComparison comparison = SpecComparison.Checksum)
    {
        return new StaleSpec("App.Arch", ProjectFile, RulesFile, reason, comparison);
    }
}
