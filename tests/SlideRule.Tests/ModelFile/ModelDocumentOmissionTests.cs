using Shouldly;
using SlideRule.ModelFile;
using SlideRule.Tests.Stubs;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     Pins the whole closed set of reasons a rule is named in a model file rather than carried by it,
///     each with its sentence in full, plus which reason wins when a rule meets two.
/// </summary>
/// <remarks>
///     These sentences are the only thing a reader with no spec assembly has to go on, so they are read
///     by people rather than matched by machines — which is why each row asserts the whole string rather
///     than a fragment of it.
/// </remarks>
public sealed class ModelDocumentOmissionTests
{
    [Fact]
    public void From_ARuleWhoseSubjectNarrowsByAPredicate_OmitsItAsCode()
    {
        ShouldOmitOneRule(arch => arch.Rule("area/predicated")
                .Enforce(arch.Types.Where(t => t.Name.Length > 3, description: "whose name is long")
                    .MustBeSealed())
                .Because("b"))
            .ShouldBe("A Where predicate is code, and this file carries data only.");
    }

    [Fact]
    public void From_ARuleWhoseVerbIsAPredicate_OmitsItAsCode()
    {
        ShouldOmitOneRule(arch => arch.Rule("area/predicate-verb")
                .Enforce(arch.Types.Must(t => t.Name.Length > 3, description: "have a long name"))
                .Because("b"))
            .ShouldBe("A Must predicate is code, and this file carries data only.");
    }

    [Fact]
    public void From_ARuleWhoseExemplarNarrowsByAPredicate_OmitsItAsCode()
    {
        // A predicate is code, wherever in the rule it sits: the exemplar is a selection like any other, so a
        // rule whose exemplar narrows with Where is left out with the reason every other Where gets.
        ShouldOmitOneRule(arch => arch.Rule("area/pointed")
                .Enforce(arch.Types.MustBeSealed())
                .Because("b")
                .Exemplar(arch.Types.Where(type => type.Name.Length > 3, description: "whose name is long")))
            .ShouldBe("A Where predicate is code, and this file carries data only.");
    }

    [Fact]
    public void From_ARuleAnchoredOnAClosedGeneric_OmitsItAsAnErrorTheCheckerWouldReport()
    {
        ShouldOmitOneRule(arch => arch.Rule("area/closed-generic")
                .Enforce(arch.Type(typeof(IHandler<string>))
                    .MustHaveSuffix("Handler"))
                .Because("b"))
            .ShouldBe("`IHandler<String>` is a closed generic construction where the rule matches on a "
                      + "definition, so `check` reports this rule as an error.");
    }

    [Fact]
    public void From_ARuleAnchoredOnATypeWithNoSourceLevelName_OmitsItAsAnErrorTheCheckerWouldReport()
    {
        ShouldOmitOneRule(arch => arch.Rule("area/unrepresentable")
                .Enforce(arch.Namespace("MyApp.Domain.*")
                    .MustNotReference(typeof(int*)))
                .Because("b"))
            .ShouldBe("`Int32*` has no source-level name to match on, so `check` reports this rule as an error.");
    }

    [Fact]
    public void From_ARuleNamingALayerTheFileCannotCarry_OmitsItQuotingThatLayersReason()
    {
        ModelDocument document = ModelFileSpecs.DocumentOf(arch =>
        {
            Layer odd = arch.Layer("Odd", arch.Types.Where(t => t.IsSealed, description: "that are sealed"));
            arch.Rule("area/uses-odd")
                .Enforce(odd.MustBeSealed())
                .Because("b");
        });

        document.Layers.ShouldBeEmpty();
        document.Rules.ShouldBeEmpty();
        document.Omitted.ShouldHaveSingleItem()
            .Reason.ShouldBe("Layer `Odd` is not in this file: A Where predicate is code, and this file "
                             + "carries data only.");
    }

    [Fact]
    public void From_ARuleThatMeetsAPredicateAdjectiveAndAPredicateVerb_ReportsTheAdjective()
    {
        // The walk is the writer's own and runs in file order, so the subject is met before the verb.
        ShouldOmitOneRule(arch => arch.Rule("area/both")
                .Enforce(arch.Types.Where(t => t.IsSealed, description: "that are sealed")
                    .Must(t => t.Name.Length > 3, description: "have a long name"))
                .Because("b"))
            .ShouldBe("A Where predicate is code, and this file carries data only.");
    }

    [Fact]
    public void From_AnOmittedRule_IsNamedInTheFileRatherThanDroppedFromIt()
    {
        ModelDocument document = ModelFileSpecs.Document();

        document.Rules.Select(rule => rule.Id)
            .ShouldNotContain("naming/long-names-are-sealed");
        document.Omitted.Select(entry => entry.Id)
            .ShouldContain("naming/long-names-are-sealed");
    }

    // The reason of the one rule a one-rule spec could not carry — asserted to be the only one, so a row
    // cannot pass by reading a second rule's reason.
    private static string ShouldOmitOneRule(Action<Arch> define)
    {
        ModelDocument document = ModelFileSpecs.DocumentOf(define);

        document.Rules.ShouldBeEmpty();
        return document.Omitted.ShouldHaveSingleItem()
            .Reason;
    }
}
