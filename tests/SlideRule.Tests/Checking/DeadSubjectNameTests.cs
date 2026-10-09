using Shouldly;
using Xunit;
using SlideRule.Checking;

namespace SlideRule.Tests.Checking;

/// <summary>
///     A dead name in a subject's <c>Named</c> list (GRAMMAR §4.1): <c>Named(a, b)</c> names exactly what
///     <c>AnyOf(Named(a), Named(b))</c> names, so a name nothing carries fails the rule as the union's empty
///     operand would, quoted as the subject would read with that name alone — and the two spellings fail
///     with the same text.
/// </summary>
public sealed class DeadSubjectNameTests
{
    private const string GhostOperand = "types named `Ghost`";

    private const string NameCure =
        "A name in a `Named` list is the whole simple name, matched exactly, without its namespace, generic "
        + "arity or containing type; check the selection against what the solution declares.";

    // A stand-in for the sentence a partial model composes; its wording is pinned where it is minted.
    private const string PartialModelCure = "THE-PARTIAL-MODEL-CURE";

    [Fact]
    public void ADeadNameBesideALiveOne_FailsTheRuleNamingIt()
    {
        // Service reaches Web, so a rule that measured the live name would fail on edges; this one fails
        // on the name instead, and the verb never runs, as for a union's empty operand.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/named")
                    .Enforce(arch.Types.Named("Service", "Ghost").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        Violation violation = result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage(GhostOperand));
        violation.Hint.ShouldBe(NameCure);
    }

    [Fact]
    public void TheSameNamesWrittenAsAUnion_FailWithTheSameText()
    {
        // The regression this row holds: two spellings of one subject used to answer differently.
        RuleResult listed = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/named")
                    .Enforce(arch.Types.Named("Service", "Ghost").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();
        RuleResult united = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/named")
                    .Enforce(arch.AnyOf(arch.Types.Named("Service"), arch.Types.Named("Ghost"))
                        .MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        listed.Violations.Select(violation => violation.Detail)
            .ShouldBe(united.Violations.Select(violation => violation.Detail));
    }

    [Fact]
    public void EveryDeadName_FailsInListOrder()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("naming/named")
                    .Enforce(arch.Types.Named("Phantom", "Service", "Ghost").MustBeSealed())
                    .Because("b"))
            .Single();

        result.Violations.Select(violation => violation.Detail)
            .ShouldBe(
            [
                ConstraintEvaluator.EmptyOperandMessage("types named `Phantom`"),
                ConstraintEvaluator.EmptyOperandMessage(GhostOperand)
            ]);
    }

    [Fact]
    public void AListNoneOfWhoseNamesMatched_FailsWholeWithNoPerNameDuplicates()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("naming/named")
                    .Enforce(arch.Types.Named("Phantom", "Ghost").MustBeSealed())
                    .Because("b"))
            .Single();

        Violation violation = result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptySubjectMessage);
        violation.Hint.ShouldBe(NameCure);
    }

    [Fact]
    public void ADeadNameInAUnionPart_FailsNamingItsOneNameSpelling()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("naming/named")
                    .Enforce(arch.AnyOf(arch.Types.Named("Service", "Ghost"), arch.Namespace("App.Web.*"))
                        .MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage(GhostOperand));
    }

    [Fact]
    public void ADeadNameInAUnionsOwnList_FailsTheRule()
    {
        // The union's own list narrows the united set, and is read against it.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("naming/named")
                    .Enforce(arch.AnyOf(arch.Namespace("App.Domain.*"), arch.Namespace("App.Web.*"))
                        .Named("Service", "Ghost")
                        .MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        Violation violation = result.ShouldHaveFailedWithDetailContaining(ViolationKind.EmptySubject, "`Ghost`");
        violation.Detail!.ShouldNotContain("`Service`");
    }

    [Fact]
    public void ADeadNameUnderAMemberSubject_FailsThroughTheSameGate()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("naming/named-members")
                    .Enforce(arch.Types.Named("Service", "Ghost").Fields.MustHaveNameMatching("*"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage(GhostOperand));
    }

    [Fact]
    public void ADeadNameInAnExceptPayload_IsNotRead()
    {
        // An exemption's dead name exempts nothing, which leaves the rule stricter, not unmeasured.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/named")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .Except(arch.Types.Named("Model", "Ghost"))
                        .MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Reference, "App.Domain.Service -> App.Web.Controller");
        result.Violations.ShouldNotContain(violation => violation.Kind == ViolationKind.EmptySubject);
    }

    [Fact]
    public void AListWhoseEveryNameMatched_ReachesTheVerb()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/named")
                    .Enforce(arch.Types.Named("Service", "Apple").MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdgesIncluding(
            ViolationKind.Reference,
            "App.Domain.Service -> App.Web.Controller",
            "App.Domain.Apple -> App.Web.Controller");
    }

    [Fact]
    public void ADeadNameUnderAPartialModel_CarriesThePartialModelCure()
    {
        var partial = new IncompleteModel(PartialModelCure);

        RuleResult result = Checker.Run(Sources.LayeredModel, partial, arch =>
                arch.Rule("naming/named")
                    .Enforce(arch.Types.Named("Service", "Ghost").MustBeSealed())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(
                ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage(GhostOperand))
            .Hint.ShouldBe(PartialModelCure);
    }
}
