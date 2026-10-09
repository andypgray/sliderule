using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Tests.Checking;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     The rule-to-file-to-section assembly, with the disk taken out: which rules are asked about, how
///     the path they name reaches the caller, how often, and which sections end up captured.
/// </summary>
public sealed class BaselineIndexBuilderTests
{
    [Fact]
    public void ForRules_ADocumentCarryingTheRulesSection_CapturesIt()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/b.json"));
        BaselineDocument document = DocumentWith("data/x");

        BaselineIndex index = BaselineIndexBuilder.ForRules(model.Rules, _ => document);

        index.TryGet("data/x", out RuleBaseline? section)
            .ShouldBeTrue();
        section.ShouldNotBeNull()
            .Entries.ShouldBe([BaselineEntry.ForSubject("T:App.Legacy.Thing")]);
    }

    [Fact]
    public void ForRules_TheRulesPath_ReachesTheCallerExactlyAsTheModelCarriesIt()
    {
        // Forward slashes and relative, as the spec author wrote it: resolving it is the caller's job,
        // and a builder that normalized it first would take that decision away from the one reader that
        // has a directory to resolve against.
        ArchitectureModel model = Ratcheted(("data/x", "arch/baselines/data-access/no-inline-sql.json"));
        var asked = new List<string>();

        BaselineIndexBuilder.ForRules(model.Rules, path => Record(asked, path, null));

        asked.ShouldBe(["arch/baselines/data-access/no-inline-sql.json"]);
    }

    [Fact]
    public void ForRules_NoDocumentForThePath_LeavesTheRuleUncaptured()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/absent.json"));

        BaselineIndex index = BaselineIndexBuilder.ForRules(model.Rules, _ => null);

        index.TryGet("data/x", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void ForRules_ADocumentWithoutTheRulesSection_LeavesTheRuleUncaptured()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/b.json"));
        BaselineDocument document = DocumentWith("other/rule");

        BaselineIndex index = BaselineIndexBuilder.ForRules(model.Rules, _ => document);

        index.TryGet("data/x", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void ForRules_ARuleThatGrandfathersNothing_IsNeverAskedAbout()
    {
        ArchitectureModel model = Checker.Model(arch =>
            arch.Rule("plain/x")
                .Enforce(arch.Types.MustBeSealed())
                .Because("b"));
        model.Rules.ShouldHaveSingleItem()
            .IsRatcheted.ShouldBeFalse();
        var asked = new List<string>();

        BaselineIndex index = BaselineIndexBuilder.ForRules(model.Rules, path => Record(asked, path, null));

        asked.ShouldBeEmpty();
        index.TryGet("plain/x", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void ForRules_TwoRulesSharingOnePath_AskForItOnceAndBothCapture()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/shared.json"), ("data/y", "arch/shared.json"));
        BaselineDocument document = DocumentWith("data/x", "data/y");
        var asked = new List<string>();

        BaselineIndex index = BaselineIndexBuilder.ForRules(model.Rules, path => Record(asked, path, document));

        asked.ShouldBe(["arch/shared.json"]);
        index.TryGet("data/x", out _)
            .ShouldBeTrue();
        index.TryGet("data/y", out _)
            .ShouldBeTrue();
    }

    [Fact]
    public void ForRules_TwoRulesSharingAPathWithNothingToRead_StillAskForItOnce()
    {
        // The absent answer is cached like any other. Without that, a file that does not exist is looked
        // for once per rule naming it, which on a shared baseline is the whole section list over again.
        ArchitectureModel model = Ratcheted(("data/x", "arch/absent.json"), ("data/y", "arch/absent.json"));
        var asked = new List<string>();

        BaselineIndexBuilder.ForRules(model.Rules, path => Record(asked, path, null));

        asked.ShouldBe(["arch/absent.json"]);
    }

    [Fact]
    public void ForRules_TwoRulesNamingDifferentPaths_AskForEachOfThem()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/one.json"), ("data/y", "arch/two.json"));
        var asked = new List<string>();

        BaselineIndexBuilder.ForRules(model.Rules, path => Record(asked, path, null));

        asked.ShouldBe(["arch/one.json", "arch/two.json"]);
    }

    [Fact]
    public void ForRules_ANullArgument_ThrowsBecauseThatIsACallerBug()
    {
        ArchitectureModel model = Ratcheted(("data/x", "arch/b.json"));

        Should.Throw<ArgumentNullException>(() => BaselineIndexBuilder.ForRules(null!, _ => null))
            .ParamName.ShouldBe("rules");
        Should.Throw<ArgumentNullException>(() => BaselineIndexBuilder.ForRules(model.Rules, null!))
            .ParamName.ShouldBe("documentFor");
    }

    private static BaselineDocument? Record(List<string> asked, string path, BaselineDocument? answer)
    {
        asked.Add(path);
        return answer;
    }

    private static BaselineDocument DocumentWith(params string[] ruleIds)
    {
        var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
        foreach (string ruleId in ruleIds) sections[ruleId] = [BaselineEntry.ForSubject("T:App.Legacy.Thing")];

        return new BaselineDocument(sections);
    }

    private static ArchitectureModel Ratcheted(params (string RuleId, string BaselinePath)[] rules)
    {
        return Checker.Model(arch =>
        {
            foreach ((string ruleId, string baselinePath) in rules)
                arch.Rule(ruleId)
                    .Migrate(
                        "old",
                        arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
                    .Baseline(baselinePath)
                    .Because("b");
        });
    }
}
