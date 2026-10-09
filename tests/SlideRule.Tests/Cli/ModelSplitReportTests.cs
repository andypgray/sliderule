using Shouldly;
using Xunit;
using SlideRule.Cli.Rendering;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The rule split <c>render</c> prints beneath a written model file: the counts line, and the two
///     groups that name the rules a reader cannot settle from one project and the rules the file could not
///     carry at all.
/// </summary>
/// <remarks>
///     Whole lines, because every one of them is stdout a reader acts on. The reasons are the classifier's
///     and the document's own words — a report that composed its own would be a second answer, free to
///     drift from the one in the file.
/// </remarks>
public sealed class ModelSplitReportTests
{
    private const string Tripwire =
        "A tripwire compares against a diff, which one project's compilation does not carry.";

    private const string Registered =
        "Reads dependency-injection registrations, which are collected across the whole solution.";

    [Fact]
    public void Lines_OneDecidableRule_IsTheCountsLineAloneAndSingular()
    {
        // Nothing left to the solution and nothing omitted, so both headers would sit over nothing — and a
        // colon that promises a block there is how a report reads as truncated.
        ModelDocument document = Document([Decidable("naming/interfaces")]);

        ModelSplitReport.Lines(document)
            .ShouldBe(["  1 rule: 1 decidable from one project, 0 left to the whole solution, 0 not in the file"]);
    }

    [Fact]
    public void Lines_SeveralRules_PluralizesTheCountedNoun()
    {
        ModelDocument document = Document([Decidable("naming/interfaces"), Decidable("naming/handlers")]);

        ModelSplitReport.Lines(document)
            .First()
            .ShouldBe("  2 rules: 2 decidable from one project, 0 left to the whole solution, 0 not in the file");
    }

    [Fact]
    public void Lines_ARuleTheSolutionDecides_HeadsItsOwnGroupWithTheClassifiersReason()
    {
        ModelDocument document = Document([Decidable("naming/interfaces"), Registration("di/registered")]);

        ModelSplitReport.Lines(document)
            .ShouldBe([
                "  2 rules: 1 decidable from one project, 1 left to the whole solution, 0 not in the file",
                "  left to the whole solution:",
                $"    di/registered: {Registered}"
            ]);
    }

    [Fact]
    public void Lines_AnOmittedRule_HeadsItsOwnGroupWithTheDocumentsReason()
    {
        // The omitted reason comes off the document rather than out of the classifier: a rule can be
        // perfectly decidable from one project and still be code the file cannot hold.
        ModelDocument document = Document([Decidable("naming/interfaces")], new OmittedRule("api/front-door", "Held code."));

        ModelSplitReport.Lines(document)
            .ShouldBe([
                "  2 rules: 1 decidable from one project, 0 left to the whole solution, 1 not in the file",
                "  not in the file:",
                "    api/front-door: Held code."
            ]);
    }

    [Fact]
    public void Lines_BothGroups_PrintsTheSolutionGroupFirstAndEachRowInModelOrder()
    {
        ModelDocument document = Document(
            [Tripwired("legacy/billing"), Decidable("naming/interfaces"), Registration("di/registered")],
            new OmittedRule("api/front-door", "Held code."),
            new OmittedRule("state/no-static-mutable", "Held a predicate."));

        ModelSplitReport.Lines(document)
            .ShouldBe([
                "  5 rules: 1 decidable from one project, 2 left to the whole solution, 2 not in the file",
                "  left to the whole solution:",
                $"    legacy/billing/tripwire: {Tripwire}",
                $"    di/registered: {Registered}",
                "  not in the file:",
                "    api/front-door: Held code.",
                "    state/no-static-mutable: Held a predicate."
            ]);
    }

    // ── harness ───────────────────────────────────────────────────────────────────────────────────────────

    private static ModelDocument Document(IReadOnlyList<ArchRule> rules, params OmittedRule[] omitted)
    {
        return new ModelDocument("Spec", [], [], rules, omitted);
    }

    // A rule of each classification, built through the real fluent surface so the classifier answers about a
    // real node graph rather than about a stub nothing else in the product could produce.
    private static ArchRule Decidable(string id)
    {
        return Rule(arch => arch.Rule(id)
            .Enforce(arch.Namespace("App.Domain.*")
                .MustNotReference(arch.Namespace("App.Web.*")))
            .Because("b"));
    }

    private static ArchRule Registration(string id)
    {
        return Rule(arch => arch.Rule(id)
            .Enforce(arch.Registered()
                .MustBeSealed())
            .Because("b"));
    }

    // A caution scope desugars to its tripwire alone, so one rule comes out of it and the id gains the
    // suffix the product gives it.
    private static ArchRule Tripwired(string id)
    {
        return Rule(arch => arch.Scope(id)
            .Caution(arch.Types.Named("RetryPolicy"))
            .Dragons("d")
            .Because("b"));
    }

    private static ArchRule Rule(Action<Arch> define)
    {
        return Checker.Model(define)
            .Rules.Single();
    }
}
