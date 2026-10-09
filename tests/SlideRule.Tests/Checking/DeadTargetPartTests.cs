using Shouldly;
using Xunit;
using SlideRule.Checking;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The partly-inert report below a target operand's own grain (GRAMMAR §4.1): the parts of a union passed
///     as one operand, and the definition of a layer used as a target, each read raw before the enclosing
///     selection's adjectives. A layer used as a subject stays folded.
/// </summary>
public sealed class DeadTargetPartTests
{
    [Fact]
    public void DeadPartOfAUnionOperand_WarnsNamingThatPart()
    {
        // One operand, so the per-operand report used to see only the union, which matched App.Web: the
        // ghost clause of the ban went quiet with nothing said.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/union")
                    .Enforce(arch.Namespace("App.Domain.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*"))))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Ghost.*`");
        result.MeasuredInFull.ShouldBeFalse();
    }

    [Fact]
    public void DeadNameInAUnionPartsList_WarnsNamingTheOneNameSpelling()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/name")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Types.Named("Model", "Ghost"))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost`");
    }

    [Fact]
    public void DeadPartBesideADeadNameOfTheUnionsOwnList_WarnsPartsFirst()
    {
        // The parts are written before the union's adjectives, so they are reported first.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/order")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Ghost.*"))
                            .Named("Controller", "Phantom")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperands(
            ["types in `App.Ghost.*`", "types in `App.Web.*` or `App.Ghost.*` named `Phantom`"]);
    }

    [Fact]
    public void PartEmptiedOnlyByTheUnionsAdjectives_IsLive()
    {
        // App.Domain holds no Controller, but a part is read raw, as the subject gate reads one: it matched
        // types, and what the union's own list keeps of it is the union's business, not the part's.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/raw")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Domain.*"))
                            .Named("Controller")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void UnionEmptiedWholeByItsAdjectives_WarnsAsTheUnionAlone()
    {
        // Every part matched raw, and the union's list kept none of it: the union is the dead operand, and
        // its parts, being live, add nothing.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/whole")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(
                            arch.Namespace("App.Domain.*"),
                            arch.AnyOf(arch.Namespace("App.Domain.*"), arch.Namespace("App.Web.*")).Named("Ghost")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedPartlyInertOnOperand("types in `App.Domain.*` or `App.Web.*` named `Ghost`");
    }

    [Fact]
    public void DeadTypeofPartOfAUnionOperand_IsSilent()
    {
        // A typeof part absent from the codebase is the win condition, as a typeof operand is.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("part/typeof")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.AnyOf(arch.Namespace("App.Domain.*"), arch.Type(typeof(Guid)))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void DeadNameInATargetLayersDefinition_WarnsNamingTheOneNameSpelling()
    {
        // A layer carries no list of its own, so the name sits in its definition, which the target pass opens.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layer/target")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Layer("Core", arch.Types.Named("Model", "Ghost"))))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.ShouldHaveWarnedPartlyInertOnOperand("types named `Ghost`");
    }

    [Fact]
    public void DeadNameInASubjectLayersDefinition_DoesNotFail()
    {
        // The subject side leaves a layer folded: written bare, the same list fails the rule on Ghost, and behind
        // a layer it does not.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layer/subject")
                    .Enforce(arch.Layer("Core", arch.Types.Named("Model", "Ghost"))
                        .MustNotReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }
}
