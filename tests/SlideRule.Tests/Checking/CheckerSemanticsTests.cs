using System.Text;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Tests.Checking.Targets;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Checker semantics not tied to one verb: empty-subject failure, the inert-target warning scope,
///     RuleError paths, and deterministic violation ordering.
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

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void InertTarget_DeadOperandBesideALiveOne_WarnsNamingThatOperand()
    {
        // The grain the whole-target gate cannot reach: `App.Domain.*` matched, so the folded operand
        // set is non-empty and the per-rule gate above stays quiet however dead the sibling is. Without
        // this report the rule would read as banning two things and ban one, forever and in silence — the
        // same masking GRAMMAR §4.1 refuses on the subject side.
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
        // A Named list is one operand, so a list with one rotted name is a live operand, and without this
        // warning the rule would read as banning both while banning one. Every type adjective filters one
        // type at a time, so the list names what the union of its one-name spellings names, and the dead name
        // is that union's dead operand (GRAMMAR §4.1). Not measured in full, like any partly inert rule.
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
    public void DeadNameInATargetException_WarnsTheNameAloneAsStricterThanWritten()
    {
        // A name inside an Except that removes nothing spares nothing: the ban reaches what the sentence says it
        // spares. The name that removed something is not named beside it.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/except")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*").Except(arch.Types.Named("Model", "Ghost"))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedStricterException("types in `App.Domain.*`", "types named `Ghost`");
    }

    [Fact]
    public void TargetException_OfAPackageTypeThroughArchTypes_KeepsTheViolationAndNamesTheSpellingsThatReachIt()
    {
        // arch.Types holds only solution-declared types, so the exception cannot reach StringBuilder, a type the
        // solution only references: the edge it was meant to spare stays red, and the warning says why.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("text/no-builders")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("System.Text").Except(arch.Types.Named("StringBuilder"))))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Domain.Service -> System.Text.StringBuilder"]);
        result.ShouldHaveWarnedStricterException("types in `System.Text`", "types named `StringBuilder`")
            .Hint.ShouldBe(
                "`arch.Types` holds only the types this solution declares, and a package's type carries no bases, "
                + "interfaces, attributes or files here, so name a package's type with `typeof(...)` or "
                + "`arch.Namespace(...).Named(...)`; an exception that removes nothing spares nothing, so what it was "
                + "meant to spare is reported as a violation.");
    }

    [Fact]
    public void TargetException_ByAHierarchyAdjectiveOverAPackageType_WarnsAsStricterThanWritten()
    {
        // A package's type carries no base list here, so DerivedFrom never matches it, whatever the noun.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("text/no-builders")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("System.Text")
                            .Except(arch.Namespace("System.Text").DerivedFrom(typeof(object)))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException(
                "types in `System.Text`", "types in `System.Text` derived from `Object`")
            .Hint!.ShouldStartWith("`arch.Types` holds only the types this solution declares");
    }

    [Theory]
    [InlineData("typeof")]
    [InlineData("namespace")]
    public void TargetException_OfAPackageTypeSpelledToReachIt_SparesItsEdgeSilently(string spelling)
    {
        // The two spellings the warning's hint gives: each removes StringBuilder, so only the Web edges stay red.
        // The union keeps the target live once StringBuilder is removed, so no inert warning stands beside them.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("text/no-builders")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("System.Text"))
                            .Except(spelling == "typeof"
                                ? arch.Type(typeof(StringBuilder))
                                : arch.Namespace("System.Text").Named("StringBuilder"))))
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
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TargetException_ABareTypeofTheCodebaseNeverReferences_StaysSilent()
    {
        // Uri is a real type the layered source never references, so the model holds no node for it: there is
        // nothing to remove until the code names it, as with a bare typeof target.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/web")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Web.*").Except(typeof(Uri))))
                    .Because("b"))
            .Single();

        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TargetException_ABareTypeofTheCodebaseHoldsOutsideTheTarget_Warns()
    {
        // StringBuilder is in the model, but not among the Web types the exception is written on.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/web")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("App.Web.*").Except(typeof(StringBuilder))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException("types in `App.Web.*`", "`StringBuilder`");
    }

    [Fact]
    public void SubjectException_ByAKindNoTypeHasToday_StaysSilent()
    {
        // "except static types" states a policy: with no static type in the subject it removes nothing, and the
        // sentence still holds, since no static type is reported.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/domain")
                    .Enforce(arch.Namespace("App.Domain.*").Except(arch.Types.ThatAreStatic())
                        .MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void TargetException_ByAKindOverAPackageType_WarnsSinceTheKindCannotSeeIt()
    {
        // StringBuilder is sealed, so "except sealed types" promises to spare it, but arch.Types holds no package
        // type: the kind is blind to the very type the sentence names.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("text/no-builders")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("System.Text").Except(arch.Types.ThatAreSealed())))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException("types in `System.Text`", "sealed types");
    }

    [Fact]
    public void TargetException_WhollyDeadNamedList_IsWarnedOnceAsAWhole()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("text/no-builders")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("System.Text").Except(arch.Types.Named("StringBuilder", "Encoding"))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException("types in `System.Text`", "types named `StringBuilder` or `Encoding`");
    }

    [Fact]
    public void TargetException_InsideALayerDefinition_IsRead()
    {
        // A layer is transparent to its definition, so an Except written there is the target's own.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
            {
                Layer web = arch.Layer("Web", arch.Namespace("App.Web.*").Except(arch.Types.Named("Ghost")));
                arch.Rule("layering/web")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(web))
                    .Because("b");
            })
            .Single();

        result.ShouldHaveWarnedStricterException("types in `App.Web.*`", "types named `Ghost`");
    }

    [Fact]
    public void TargetException_OfASolutionTypeOutsideTheTarget_SaysItRemovesOnlyWhatTheTargetHolds()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/web")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.Namespace("App.Web.*").Except(arch.Types.Named("Model"))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException("types in `App.Web.*`", "types named `Model`")
            .Hint!.ShouldStartWith("An exception removes only what the selection it is written on holds; ");
    }

    [Fact]
    public void InjectionTargetException_ThatRemovesNothing_Warns()
    {
        // MustNotInject raises no inert warning of its own, since a registration absent is its win condition; an
        // exception the sentence prints is a promise all the same.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("di/no-web")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotInject(arch.Namespace("App.Web.*").Except(arch.Types.Named("Ghost"))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedStricterException("types in `App.Web.*`", "types named `Ghost`");
    }

    [Fact]
    public void TargetException_ThatRemovesNothing_IsSilentOnANarrowedRun()
    {
        // A run over part of the solution cannot tell a dead exception from one whose types sit in a project it
        // never loaded.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                    arch.Rule("inert/except")
                        .Enforce(arch.Namespace("App.Web.*")
                            .MustNotReference(arch.Namespace("App.Domain.*").Except(arch.Types.Named("Ghost"))))
                        .Because("b"),
                narrowing: Checker.FilterNarrowing)
            .Single();

        result.Warnings.ShouldBeEmpty();
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

        result.ShouldHaveFailedWithSingle(ViolationKind.RuleError);
    }

    [Fact]
    public void UnrepresentableType_IsRuleError()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("ptr/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(int*)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingle(ViolationKind.RuleError);
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
