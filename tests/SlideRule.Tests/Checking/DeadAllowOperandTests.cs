using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The stricter-than-written report on the <c>MustOnly</c> verbs (GRAMMAR §4.1, §7): each pattern in an
///     allow-list that matched nothing, or each dead name of one's <c>Named</c> list, warns on the rule, since
///     what it was meant to allow reds under the callers' names and nothing else would name the stale pattern.
///     A quarantine's <c>BoundaryOnlyVia</c> is such an allow-list. The warning shares the partly-inert kind, so
///     the rule is not measured in full and <c>baseline init</c> will not capture the reds it caused; a dead
///     allowed pattern only adds violations, so the rule's unmatched entries still count as tested.
/// </summary>
public sealed class DeadAllowOperandTests
{
    private const string ContainmentId = "legacy/quarantined/containment";

    private const string NamespaceCure =
        "A trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses a dot; a "
        + "pattern that matches nothing allows nothing, so what it was meant to allow is reported as a violation.";

    private const string NameCure =
        "A name in a `Named` list is the whole simple name, matched exactly, without its namespace, generic arity or "
        + "containing type; a name that matches nothing allows nothing, so what it was meant to allow is reported "
        + "as a violation.";

    private static RuleResult Containment(string first, params string[] more)
    {
        return Checker.Run(Sources.ContainmentModel, BaselineIndex.Empty, arch =>
                arch.Scope("legacy/quarantined")
                    .Quarantine(arch.Namespace("App.Legacy.*"))
                    .BoundaryOnlyVia(arch.Types.Named(first, more))
                    .Dragons("Internal is structural.")
                    .Because("Replacement scheduled."))
            .ForRule(ContainmentId);
    }

    [Fact]
    public void MustOnlyReference_DeadPatternBesideALiveOne_WarnsAndKeepsTheVerdict()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/dead")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustOnlyReference(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
        result.Warnings.Single()
            .Hint.ShouldBe(NamespaceCure);
        result.ShouldSatisfyAllConditions(
            () => result.MeasuredInFull.ShouldBeFalse(),
            () => result.UnmatchedEntriesTested.ShouldBeTrue());
    }

    [Fact]
    public void MustOnlyReference_DeadNameInANamedList_WarnsNamingTheOneNameSpelling()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/name")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustOnlyReference(arch.Types.Named("Controller", "Helper", "Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types named `Ghost`");
    }

    [Fact]
    public void MustOnlyReference_DeadGlobInAnUnderList_WarnsNamingTheOneGlobSpelling()
    {
        RuleResult result = Checker.Run(Sources.FolderModel, Sources.FolderRoot, arch =>
                arch.Rule("only/glob")
                    .Enforce(arch.Types.Named("Writer").MustOnlyReference(arch.Types.Under("src/Console", "src/Gone")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types under `src/Gone`");
    }

    [Fact]
    public void MustOnlyBeReferencedBy_DeadPattern_Warns()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/inbound")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustOnlyBeReferencedBy(arch.Namespace("App.Domain.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void MustOnlyReference_AbsentTypeofOperand_IsSilent()
    {
        // A typeof the code never mentions allows nothing, and the compiler has already checked its name.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/typeof")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustOnlyReference(arch.Namespace("App.Web.*"), arch.Type(typeof(Guid))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_DeadPartOfAUnionOperand_Warns()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/union")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustOnlyReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*"))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void FamilyAllowList_DeadPattern_WarnsOnce()
    {
        // The operands are resolved once for every cell, so a dead one is named once, not per cell.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/family")
                    .Enforce(arch.Each(arch.Layer("Web", "App.Web.*"), arch.Layer("Domain", "App.Domain.*"))
                        .MustOnlyReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void BoundaryOnlyVia_DeadName_WarnsOnTheContainmentRule()
    {
        // The facade was renamed and the boundary still names it by string: every reference into the scope reds
        // under the caller's name, and the warning is what names the stale facade.
        RuleResult containment = Containment("IGhostFacade", "Ghost");

        containment.ShouldHaveFailedWithEdges(
            ViolationKind.Reference, ["App.Client.User -> App.Legacy.IFacade", "App.Client.User -> App.Legacy.Internal"]);
        containment.ShouldHaveWarnedStricterOnOperand("types named `IGhostFacade` or `Ghost`");
        containment.ShouldSatisfyAllConditions(
            () => containment.MeasuredInFull.ShouldBeFalse(),
            () => containment.UnmatchedEntriesTested.ShouldBeTrue());
    }

    [Fact]
    public void BoundaryOnlyVia_DeadNameBesideTheLiveFacade_WarnsNamingThatName()
    {
        RuleResult containment = Containment("IFacade", "Ghost");

        containment.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Client.User -> App.Legacy.Internal"]);
        containment.ShouldHaveWarnedStricterOnOperand("types named `Ghost`");
        containment.Warnings.Single()
            .Hint.ShouldBe(NameCure);
    }

    [Fact]
    public void BoundaryOnlyVia_LiveFacade_IsSilent()
    {
        RuleResult containment = Containment("IFacade", "Impl");

        containment.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Client.User -> App.Legacy.Internal"]);
        containment.Warnings.ShouldBeEmpty();
    }
}
