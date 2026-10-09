using Shouldly;
using Xunit;
using SlideRule.Checking;
using SlideRule.Tests.Checking.Targets;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Checker semantics not tied to one verb: empty-subject failure, the inert-target warning scope,
///     RuleError paths, the Quarantine containment formula evaluated as an Enforce rule, and
///     deterministic violation ordering.
/// </summary>
public sealed class CheckerSemanticsTests
{
    [Fact]
    public void EmptySubject_FailsWithPinnedMessage()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("empty/x")
                    .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.EmptySubject, "The subject selection matched no solution-declared types.");
    }

    [Fact]
    public void InertTarget_EmptyPatternTarget_WarnsButPasses()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void InertTarget_AbsentTypeofTarget_IsSilentWinCondition()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("no-guid/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(Guid)))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void InertTarget_DeadOperandBesideALiveOne_WarnsNamingThatOperand()
    {
        // The grain the whole-target gate cannot reach: `App.Domain.*` matched, so the folded operand
        // set is non-empty and the per-rule gate above stays quiet however dead the sibling is. Before
        // this report the rule read as banning two things and banned one, forever and in silence — the
        // same masking the subject side has refused since GRAMMAR §4.1.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/operand")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void InertTarget_TwoDeadOperands_WarnOncePerOperandInOperandOrder()
    {
        // One warning per dead pattern rather than one per rule, each naming its own, which is what a reader
        // and a code-scanning fingerprint both need to tell them apart. Listed against alphabetical order, so
        // the row can tell operand order from a sort.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/operands")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(
                            arch.Namespace("App.Phantom.*"),
                            arch.Namespace("App.Domain.*"),
                            arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperands(["types in `App.Phantom.*`", "types in `App.Ghost.*`"]);
    }

    [Fact]
    public void InertTarget_SameDeadOperandListedTwice_WarnsOnce()
    {
        // The warning is keyed on the operand as rendered, and so is everything downstream of it: two
        // warnings saying the same thing would be two alerts with one fingerprint.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/twice")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(
                            arch.Namespace("App.Domain.*"),
                            arch.Namespace("App.Ghost.*"),
                            arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void InertTarget_DeadOperandBesideALiveOneThatBites_FailsAndStillWarns()
    {
        // The warning fails nothing and passes nothing: the verdict is what the live operand found, here
        // every Domain edge into Web, and the dead operand warns beside those violations all the same.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/bites")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(
            ViolationKind.Reference,
            [
                "App.Domain.Apple -> App.Web.Controller",
                "App.Domain.Service -> App.Web.Controller",
                "App.Domain.Service -> App.Web.Helper",
                "App.Domain.Zebra -> App.Web.Controller"
            ]);
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void InertTarget_DeadOperandOfAnInboundBan_WarnsNamingThatOperand()
    {
        // The inbound verb resolves its operands in target position and plays them as the edges' SOURCES, so
        // this row holds the per-operand arm to the operand list rather than to whichever end of an edge the
        // operands happen to sit at.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/inbound")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotBeReferencedBy(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void InertTarget_EveryOperandLive_IsSilent()
    {
        // The negative control for the rows above: two operands, both matching, nothing to say.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("live/operands")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*"), arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void InertTarget_DeadTypeofOperandBesideALiveOne_IsSilentWinCondition()
    {
        // Only a PATTERN operand warns, at either grain. A typeof names a type the compiler resolved, so
        // its absence from the codebase is the ban working — the line GRAMMAR §4.1 draws for the whole
        // target, drawn identically per operand.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("typeof/operand")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*"), arch.Type<Guid>()))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void InertTarget_DeadNameInANamedListBesideALiveName_WarnsNamingThatName()
    {
        // A Named list is one operand, so a list with one rotted name was a live operand and the rule read as
        // banning both while it banned one. Every type adjective filters one type at a time, so the list names
        // what the union of its one-name spellings names, and the dead name is that union's dead operand
        // (GRAMMAR §4.1). Not measured in full, like any partly inert rule.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/name")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Service", "Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost`");
        result.MeasuredInFull.ShouldBeFalse();
    }

    [Fact]
    public void InertTarget_DeadNamesBesideADeadOperand_WarnInOperandOrderThenNameOrder()
    {
        // Listed against alphabetical order inside the list too, so the row can tell authoring order from a sort.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/names")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(
                            arch.Namespace("App.Phantom.*"),
                            arch.Types.Named("Zeta", "Service", "Alpha")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperands(
            ["types in `App.Phantom.*`", "types named `Zeta`", "types named `Alpha`"]);
    }

    [Fact]
    public void InertTarget_NameDeadOnlyInsideItsOperand_WarnsNamingTheOneNameSpelling()
    {
        // `Controller` exists, in App.Web, but not among the types this operand matched: the name is dead
        // where the ban put it, and the warning quotes the operand as it would read with that name alone.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/narrowed")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*").Named("Service", "Controller")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Domain.*` named `Controller`");
    }

    [Fact]
    public void InertTarget_DeadNameOnAUnionOperand_KeepsTheUnionInTheOneNameSpelling()
    {
        // A union owns the adjectives written after it, so the one-name spelling swaps the list on the union
        // itself and keeps both parts, rather than reading the union's noun, which a union does not have.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/union")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Domain.*"), arch.Namespace("App.Web.*"))
                            .Named("Service", "Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Domain.*` or `App.Web.*` named `Ghost`");
    }

    [Fact]
    public void InertTarget_EveryNameLive_IsSilent()
    {
        // The negative control for the rows above: two names, both carried, nothing to say.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("live/names")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Service", "Model")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void InertTarget_EveryNameDead_WarnsTheWholeTargetOnly()
    {
        // The whole-target and per-name arms never both fire for one rule: a list none of whose names matched is an
        // empty target, which the whole-target warning already names, and naming each name beside it would say it
        // twice.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/allnames")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Ghost", "Phantom")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }

    [Fact]
    public void InertTarget_DeadNameBesideTheSameNameAsAnOperand_WarnsOnce()
    {
        // The one-name spelling of a dead name renders exactly as a dead one-name operand does, and every
        // surface downstream keys on that rendering, so the two collapse to one warning.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/samename")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Service", "Ghost"), arch.Types.Named("Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost`");
    }

    [Fact]
    public void InertTarget_DeadOperandBesideTheSameNameInAList_WarnsOnce()
    {
        // The row above in the other order: the one-name operand warns first, and the list's dead name finds the
        // warning already there.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/samename2")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Ghost"), arch.Types.Named("Service", "Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost`");
    }

    [Fact]
    public void InertTarget_DeadNamedListBesideALiveOperand_WarnsTheListOnceAsAWhole()
    {
        // A list none of whose names matched is a dead operand and is named whole. The per-name pass only reads an
        // operand that matched something, so its two names are not named again beside the operand.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/deadlist")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Types.Named("Ghost", "Phantom"), arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost` or `Phantom`");
    }

    [Fact]
    public void InertTarget_DeadNameInAnExceptList_IsSilent()
    {
        // A name inside an Except exempts, and one that matches nothing exempts nothing: the ban is stricter
        // for it, never quieter, so there is no clause gone missing to report.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/except")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*").Except(arch.Types.Named("Model", "Ghost"))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void DeadStringAnchor_OnANegativeVerb_StaysSilent()
    {
        // Pinned, because the silence is the cost and it is not the same cost the operand rows above
        // buy off. A negative anchor verb resolves no target set: the matcher runs per subject, and
        // "no subject matched" is indistinguishable from the ban being satisfied — it is what a working
        // ban looks like. Resolving the name instead does not separate them either, since a typo and a
        // ban on something this codebase has never contained both name a type the model does not know.
        //
        // The model file closes what is left: an anchor is written as a name, an onDefinition flag and
        // a path, never the form it was written in, so a typeof anchor and a string anchor are one node
        // after a round trip — and a report that told them apart would make a live model and a
        // round-tripped one disagree. GRAMMAR §4.5 and §4.6 pin this as an accepted cost.
        RuleResult result = Checker.Run(Sources.HierarchyModel, arch =>
                arch.Rule("inert/anchor")
                    .Enforce(arch.Types.MustNotImplement("SlideRule.Tests.Checking.Targets.INoSuchThing"))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void DeadStringAnchor_BesideAMatchingOne_StaysSilent()
    {
        // The masking twin of the row above, and the closest thing to a real slip this family has:
        // `IThing` bites, so the rule reds and looks like it is working, while the second name has
        // stopped meaning anything. Pinned as silent for the same reason — nothing in the check can
        // tell that name from one deliberately banning what is not here.
        RuleResult result = Checker.Run(Sources.HierarchyModel, arch =>
                arch.Rule("partly/anchor")
                    .Enforce(arch.Types.MustNotImplement(
                        "SlideRule.Tests.Checking.Targets.IThing",
                        "SlideRule.Tests.Checking.Targets.INoSuchThing"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MustOnly_EmptyAllowSet_IsLoudNotInert()
    {
        // The allow-set an author wrote matches nothing, so the only targets left are the ones §4.1 grants:
        // the subject itself, and external types. The Domain to Web edges are neither, which is what keeps
        // an empty allow-set loud rather than turning it into a rule about nothing. The dead pattern is named
        // all the same, as the stricter-than-written warning, and never as the whole-target inert one.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        result.ShouldHaveWarnedStricterOnOperand("types in `App.Ghost.*`");
    }

    [Fact]
    public void ClosedGenericTypeNounTarget_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.HierarchyModel, arch =>
                arch.Rule("gen/x")
                    .Enforce(arch.Types.MustNotReference(typeof(IHandler<Order>)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetailContaining(ViolationKind.RuleError, "closed generic construction");
    }

    [Fact]
    public void ClosedGenericTypeNounSubject_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.HierarchyModel, arch =>
                arch.Rule("gen/x")
                    .Enforce(arch.Type(typeof(IHandler<Order>)).MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
    }

    [Fact]
    public void UnrepresentableType_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("ptr/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(int*)))
                    .Because("b"))
            .Single();

        result.Violations.ShouldHaveSingleItem()
            .Kind.ShouldBe(ViolationKind.RuleError);
    }

    [Fact]
    public void UnrepresentableHierarchyAnchor_IsRuleErrorNamingTheType()
    {
        // A hierarchy position admits a closed generic, so a type with no source-level form is the one
        // anchor it refuses. Validation lets the pointer through: it only asks that a base is no interface.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("ptr/base")
                    .Enforce(arch.Types.DerivedFrom(typeof(int*)).MustHaveSuffix("X"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.RuleError, "Type 'System.Int32*' has no source-level display form the checker can match against.");
    }

    [Fact]
    public void ThrowingPredicate_IsRuleErrorNotCrash()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("throw/x")
                    .Enforce(arch.Types.Must(_ => throw new InvalidOperationException("boom"), "explode"))
                    .Because("b"))
            .Single();

        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Kind.ShouldBe(ViolationKind.RuleError);
        violation.Detail!.ShouldContain("predicate threw");
    }

    [Fact]
    public void ContainmentFormula_AsEnforceRule_RedForInteriorReferenceGreenForFacade()
    {
        // The Quarantine desugaring shape (sel.Except(facade).MustOnlyBeReferencedBy(sel ∪ facade)),
        // hand-built as Enforce so the containment logic is exercised while Quarantine itself skips.
        RuleResult result = Checker.Run(Sources.ContainmentModel, arch =>
                arch.Rule("contain/x")
                    .Enforce(arch.Namespace("App.Legacy.*").Except(arch.Types.WithSuffix("Facade"))
                        .MustOnlyBeReferencedBy(arch.Namespace("App.Legacy.*")))
                    .Because("b"))
            .Single();

        // Exhaustive, which is what carries the green half too: the facade edge is absent from a set that
        // names every inbound reference the containment formula reports.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Client.User -> App.Legacy.Internal"]);
    }

    [Fact]
    public void ViolationOrder_IsOrdinalBySourceThenTarget()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("order/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        // Reads the projection in its own right rather than stating the set through ShouldHaveFailedWithEdges,
        // whose comparison is deliberately unordered: report order is this row's whole subject, and Apple and
        // Zebra exist to bracket it.
        result.ReferencePairs()
            .ShouldBe([
                "App.Domain.Apple -> App.Web.Controller",
                "App.Domain.Service -> App.Web.Controller",
                "App.Domain.Service -> App.Web.Helper",
                "App.Domain.Zebra -> App.Web.Controller"
            ]);
    }
}
