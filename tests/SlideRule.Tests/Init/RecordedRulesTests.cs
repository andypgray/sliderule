using Shouldly;
using SlideRule.Cli.Init;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Init;

/// <summary>
///     <see cref="RecordedRules" />: which projects <c>init --from-graph</c> records a boundary between, and the
///     statements it writes for them.
/// </summary>
public sealed class RecordedRulesTests
{
    private static readonly DateOnly RecordedOn = new(2026, 11, 1);

    [Fact]
    public void Statements_TwoUnconnectedProjects_RecordsARuleEachWay()
    {
        GraphSummary summary = Summary([Project("Acme.Billing"), Project("Acme.Shipping")]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBe(
        [
            "// Recorded by sliderule init --from-graph on 2026-11-01 from the project graph, not reasoned: rewrite each",
            "// rule's reason, or delete the rule, once someone has decided whether the boundary is meant.",
            "Layer acmeBilling = arch.Layer(\"Acme.Billing\", arch.Project(\"Acme.Billing\"));",
            "Layer acmeShipping = arch.Layer(\"Acme.Shipping\", arch.Project(\"Acme.Shipping\"));",
            "",
            "arch.Rule(\"layering/acme-billing-independent\")",
            "    .Enforce(acmeBilling.MustNotReference(acmeShipping))",
            "    .Because(\"Recorded from the project graph on 2026-11-01: no reference either way then. Replace this with the reason the boundary exists.\");",
            "",
            "arch.Rule(\"layering/acme-shipping-independent\")",
            "    .Enforce(acmeShipping.MustNotReference(acmeBilling))",
            "    .Because(\"Recorded from the project graph on 2026-11-01: no reference either way then. Replace this with the reason the boundary exists.\");"
        ]);
    }

    [Fact]
    public void Statements_ADeclaredReferenceEitherWay_ConnectsThePair()
    {
        GraphSummary summary = Summary([Project("Acme.Web", ["Acme.Domain"]), Project("Acme.Domain")]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBeEmpty();
    }

    [Fact]
    public void Statements_APathThroughAThirdProject_ConnectsThePair()
    {
        // Web reaches Data only through Domain, so the two ends are connected and neither gets a rule naming the
        // other; nothing else is unconnected either.
        GraphSummary summary = Summary(
        [
            Project("Acme.Web", ["Acme.Domain"]),
            Project("Acme.Domain", ["Acme.Data"]),
            Project("Acme.Data")
        ]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBeEmpty();
    }

    [Fact]
    public void Statements_AnEdgeOnlyTheCodeShows_ConnectsThePair()
    {
        // No declared reference either way, but the code references across: the declared graph can omit one.
        GraphSummary summary = Summary(
            [Project("Acme.Web"), Project("Acme.Domain")],
            [new ProjectEdgeSummary("Acme.Web", "Acme.Domain", 3)]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBeEmpty();
    }

    [Fact]
    public void Statements_AnUndeclaredReferenceTheModelInfers_ConnectsThePair()
    {
        ProjectSummary web = new(
            "Acme.Web", [], 1, 0, [], solutionMember: true, undeclaredProjectReferences: ["Acme.Domain"]);
        GraphSummary summary = Summary([web, Project("Acme.Domain")]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBeEmpty();
    }

    [Fact]
    public void Statements_LeavesOutTestsPassengersAndProjectsWithNoTypes()
    {
        // Each of the three would otherwise be unconnected from Acme.Billing and get a rule: a test project
        // references what it tests, a passenger is not the solution's, and an empty subject fails its rule.
        ProjectSummary tests = new(
            "Acme.Tests", [], 4, 0, [], solutionMember: true,
            packageReferences: [ProjectFacts.Package("xunit.v3", "Acme.Tests.csproj", 5)]);
        ProjectSummary passenger = new("Vendor.Lib", [], 4, 0, [], solutionMember: false);
        ProjectSummary empty = new("Acme.Empty", [], 0, 0, [], solutionMember: true);
        GraphSummary summary = Summary([Project("Acme.Billing"), tests, passenger, empty]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldBeEmpty();
    }

    [Fact]
    public void Statements_NamesWhoseSpellingsCollide_GetDistinctLocalsAndIds()
    {
        GraphSummary summary = Summary([Project("Acme.Web"), Project("Acme_Web"), Project("class")]);

        IReadOnlyList<string> statements = RecordedRules.Statements(summary, RecordedOn);

        statements.ShouldContain("Layer acmeWeb = arch.Layer(\"Acme.Web\", arch.Project(\"Acme.Web\"));");
        statements.ShouldContain("Layer acmeWeb2 = arch.Layer(\"Acme_Web\", arch.Project(\"Acme_Web\"));");
        statements.ShouldContain("Layer layerClass = arch.Layer(\"class\", arch.Project(\"class\"));");
        statements.ShouldContain("arch.Rule(\"layering/acme-web-independent\")");
        statements.ShouldContain("arch.Rule(\"layering/acme-web-independent-2\")");
    }

    private static ProjectSummary Project(string name, IReadOnlyList<string>? references = null)
    {
        return new ProjectSummary(name, references ?? [], 1, 0, [], solutionMember: true);
    }

    private static GraphSummary Summary(
        IReadOnlyList<ProjectSummary> projects, IReadOnlyList<ProjectEdgeSummary>? edges = null)
    {
        return new GraphSummary(projects, edges ?? [], [], [], []);
    }
}
