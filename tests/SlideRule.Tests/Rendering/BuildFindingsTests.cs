using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Rendering;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The projection from a check report to the warnings a build prints (<see cref="BuildFindings" />) and
///     the one line each carries (<see cref="BuildFindingMessage" />). Placement is the human report's — one
///     finding per site for the seven edge kinds, one at the first site for the three shape kinds, none for
///     the two kinds that carry no site — and a violation whose first shape site the caller does not own
///     yields nothing rather than sliding to a later one. Only red violations count: grandfathered ones, a
///     warned rule, a passing rule and a skipped rule all yield nothing. The message is pinned whole: the
///     rule ID and what broke it, the grown pair's allowance, the law sentence, the reason, the fix and the
///     exemplar as the spec names it, each ended as a sentence and separated by one space. Pinned strings are
///     the spec.
/// </summary>
public sealed class BuildFindingsTests
{
    private const string RuleId = "probe/wording";

    private const string DomainSource = """
                                        namespace MyApp.Web { public class HomeController {} }
                                        namespace MyApp.Domain { public class OrderService { public MyApp.Web.HomeController Home; } }
                                        """;

    // Sources.TwoSiteController with a third writing of the same pair, so an allowance above one has
    // something to be exceeded by — which is what puts the inflected noun's plural arm on the page.
    private const string ThreeSiteController = """
                                               namespace App.Web
                                               {
                                                   public class OldController
                                                   {
                                                       public App.Data.Db A() => new App.Data.Db();
                                                       public App.Data.Db B() => new App.Data.Db();
                                                       public App.Data.Db C() => new App.Data.Db();
                                                   }
                                               }
                                               namespace App.Data { public class Db {} }
                                               """;

    // The ten kinds a finding can be placed at: the seven edges and the three shapes. EmptySubject and
    // RuleError are deliberately absent — carrying no site is the whole of what the rows below ask of them.
    private static readonly IReadOnlyList<ViolationKind> PlaceableKinds =
    [
        ViolationKind.Reference, ViolationKind.MemberUse, ViolationKind.Construction, ViolationKind.Injection,
        ViolationKind.Catch, ViolationKind.Throw, ViolationKind.Expose,
        ViolationKind.Shape, ViolationKind.MemberShape, ViolationKind.ProjectShape
    ];

    private static readonly SourceLocation OneSite = new("Src.cs", 7);

    // Owns every file: the default for the rows whose subject is what a violation produces rather than which
    // files the caller holds.
    private static readonly Func<string, bool> EveryFile = _ => true;

    /// <summary>The ten placeable kinds, one theory case each.</summary>
    public static TheoryData<string> PlaceableKindCases => [.. PlaceableKinds.Select(static kind => kind.ToString())];

    [Theory]
    [MemberData(nameof(PlaceableKindCases))]
    public void Message_OfEveryPlaceableKind_OpensWithTheRuleIdAndTheHumanReportsOwnWords(string kindName)
    {
        // By name, because ViolationKind is internal and a public theory cannot take it as a parameter.
        var kind = Enum.Parse<ViolationKind>(kindName);

        RuleResult result = Failing(ViolationOfKind(kind, OneSite));

        BuildFinding finding = FindingsOf(result)
            .ShouldHaveSingleItem();

        finding.Message.ShouldStartWith($"{RuleId}: {ShouldHaveHumanWording(result)}.");
    }

    [Fact]
    public void Message_OfAReferenceViolationOfARuleWithAFix_IsTheWholeComposedLine()
    {
        RuleResult result = DomainRuleWithFix();

        BuildFinding finding = FindingsOf(result)
            .ShouldHaveSingleItem();

        // The law sentence is read off the built rule rather than typed, so the model stays its one author;
        // every other segment is typed literally, because the template is what this row pins.
        finding.Message.ShouldBe(
            "layering/domain-independent: MyApp.Domain.OrderService references MyApp.Web.HomeController. "
            + result.Rule.Sentence
            + " Because: Domain is UI-agnostic; transaction boundaries live in services."
            + " Fix: Define an abstraction in Domain and implement it in Web.");
    }

    [Fact]
    public void Message_OfARuleWithAFixAndAnExemplar_EndsWithTheExemplarAsTheSpecNamesIt()
    {
        // The phrase, never a site: a build sees one project's compilation, and the type to copy may well be
        // declared in another.
        RuleResult result = Checker.Run(DomainSource, arch => arch.Rule("layering/domain-independent")
                .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                .Fix("Define an abstraction in Domain and implement it in Web.")
                .Exemplar(arch.Types.Named("OrderService")))
            .Single();

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Message.ShouldBe(
                "layering/domain-independent: MyApp.Domain.OrderService references MyApp.Web.HomeController. "
                + result.Rule.Sentence
                + " Because: Domain is UI-agnostic; transaction boundaries live in services."
                + " Fix: Define an abstraction in Domain and implement it in Web."
                + " Exemplar: types named `OrderService`.");
    }

    [Fact]
    public void Message_OfARuleWithAnExemplarAndNoFix_PutsTheExemplarAfterTheBecauseSegment()
    {
        RuleResult result = Checker.Run(DomainSource, arch => arch.Rule("layering/domain-independent")
                .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                .Because("Domain is UI-agnostic.")
                .Exemplar(arch.Types.Named("OrderService")))
            .Single();

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Message.ShouldEndWith(" Because: Domain is UI-agnostic. Exemplar: types named `OrderService`.");
    }

    [Fact]
    public void Message_OfARuleWithNoFix_EndsAfterTheBecauseSegment()
    {
        ArchRule rule = Checker.Rule(
            RuleId, because: "Domain is UI-agnostic.",
            sentence: "The Domain layer must not reference the Web layer.");
        var result = new RuleResult(rule, RuleStatus.Failed, [ViolationOfKind(ViolationKind.Shape, OneSite)]);

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Message.ShouldBe(
                "probe/wording: App.Orphan. The Domain layer must not reference the Web layer. "
                + "Because: Domain is UI-agnostic.");
    }

    [Fact]
    public void Message_OfARuleWithAnEmptySentence_OmitsThatSegmentWithoutDoublingTheSpace()
    {
        // A rule asserting nothing about the code has no sentence to state, and a blank one would leave two
        // spaces between what broke and why the rule exists.
        var result = new RuleResult(
            Checker.Rule(RuleId, sentence: string.Empty), RuleStatus.Failed,
            [ViolationOfKind(ViolationKind.Shape, OneSite)]);

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Message.ShouldBe("probe/wording: App.Orphan. Because: b.");
    }

    [Theory]
    [InlineData("A reader cannot find the type", "Because: A reader cannot find the type.")]
    [InlineData("A reader cannot find the type.", "Because: A reader cannot find the type.")]
    [InlineData("Why would a reader look here?", "Because: Why would a reader look here?")]
    [InlineData("Nobody should ever do this!", "Because: Nobody should ever do this!")]
    public void Message_BecauseSegment_GainsAStopOnlyWhenItEndsNoSentence(string because, string expected)
    {
        var result = new RuleResult(
            Checker.Rule(RuleId, because: because, sentence: string.Empty), RuleStatus.Failed,
            [ViolationOfKind(ViolationKind.Shape, OneSite)]);

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Message.ShouldBe($"probe/wording: App.Orphan. {expected}");
    }

    [Fact]
    public void Findings_OfAGrownPair_AreOnePerSite_EachNamingTheAllowanceAndWhatWasFound()
    {
        // Every site of a grown pair is red, so every one carries the sentence that says why a pair the
        // baseline names is red at all — the allowance beside what the run measured.
        RuleResult result = Grown(1, Sources.TwoSiteControllerModel);

        IReadOnlyList<BuildFinding> findings = FindingsOf(result);

        findings.Count.ShouldBe(2);
        findings.Select(finding => finding.Message)
            .Distinct()
            .ShouldHaveSingleItem()
            .ShouldContain("The baseline holds 1 site of this pair and 2 were found.");
    }

    [Fact]
    public void Findings_OfAGrownPairAllowedMoreThanOneSite_InflectTheNounForTheAllowance()
    {
        RuleResult result = Grown(2, CompilationFactory.Extract(ThreeSiteController));

        IReadOnlyList<BuildFinding> findings = FindingsOf(result);

        findings.Count.ShouldBe(3);
        findings.Select(finding => finding.Message)
            .Distinct()
            .ShouldHaveSingleItem()
            .ShouldContain("The baseline holds 2 sites of this pair and 3 were found.");
    }

    [Fact]
    public void Findings_OfAMigrateRule_AreNewDebt()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, Sources.NoDataAccess)
            .Single();

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(BuildFindingKind.NewDebt);
    }

    [Fact]
    public void Findings_OfAnEnforceRule_AreABrokenRule()
    {
        RuleResult result = Checker.Run(Sources.OneControllerModel, arch => arch.Rule("data/enforced")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("Repository pattern for testability."))
            .Single();

        FindingsOf(result)
            .ShouldHaveSingleItem()
            .Kind.ShouldBe(BuildFindingKind.BrokenRule);
    }

    [Fact]
    public void Findings_OfAQuarantinesContainmentRule_AreABrokenRule_ThoughItRatchets()
    {
        // Containment ratchets like a Migrate rule and is law all the same: a new crossing of a quarantine
        // boundary is not debt anyone scheduled, so the kind is keyed on the posture rather than on the
        // baseline.
        RuleResult containment = Checker.Run(Sources.ContainmentModel, BaselineIndex.Empty, Sources.HermeticContainment)
            .ForRule("legacy/quarantined/containment");

        IReadOnlyList<BuildFinding> findings = FindingsOf(containment);

        containment.Rule.IsRatcheted.ShouldBeTrue();
        findings.ShouldNotBeEmpty();
        findings.Select(finding => finding.Kind)
            .Distinct()
            .ShouldHaveSingleItem()
            .ShouldBe(BuildFindingKind.BrokenRule);
    }

    [Fact]
    public void Findings_OfAGrandfatheredViolation_AreNone()
    {
        BaselineIndex index = Checker.Baselines(
            "data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));
        RuleResult result = Checker.Run(Sources.OneControllerModel, index, Sources.NoDataAccess)
            .Single();

        result.ShouldHaveGrandfathered(1);
        FindingsOf(result)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Findings_OfRulesThatDidNotFail_AreNone()
    {
        // The skipped result is given a violation it would never carry in a real run, because a status
        // filter that is only ever handed empty lists is a filter no row has measured.
        var passed = new RuleResult(Checker.Rule("r/pass"), RuleStatus.Passed, []);
        var warned = new RuleResult(
            Checker.Rule("r/warn"), RuleStatus.Passed, [],
            [CheckWarning.ScopeTouched(caution: true, "Helpers.cs", "shared/utilities", "shared/utilities/tripwire")]);
        var skipped = new RuleResult(
            Checker.Rule("r/skip"), RuleStatus.Skipped, [ViolationOfKind(ViolationKind.Shape, OneSite)], [],
            "'BillingOnly.slnf' narrowed this run.", []);

        BuildFindings.From(ReportOf(passed, warned, skipped), EveryFile)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Findings_OfAnEmptySubjectOrARuleError_AreNone()
    {
        RuleResult empty = Failing(Violation.EmptySubject("matched nothing", "THE-REAL-CURE"));
        RuleResult errored = Failing(Violation.RuleError("boom"));

        BuildFindings.From(ReportOf(empty, errored), EveryFile)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Finding_OfAnEdgeViolation_CarriesTheIdentitysSourceAndTargetAndNoSubject()
    {
        Violation violation = ViolationOfKind(ViolationKind.Reference, OneSite);
        BaselineEntry identity = violation.BaselineIdentity()
            .ShouldNotBeNull();

        BuildFinding finding = FindingsOf(Failing(violation))
            .ShouldHaveSingleItem();

        finding.ShouldSatisfyAllConditions(
            () => finding.Source.ShouldBe(identity.Source),
            () => finding.Target.ShouldBe(identity.Target),
            () => finding.Source.ShouldNotBeNull(),
            () => finding.Target.ShouldNotBeNull(),
            () => finding.Subject.ShouldBeNull());
    }

    [Fact]
    public void Finding_OfAShapeViolation_CarriesTheIdentitysSubjectAndNoEdge()
    {
        Violation violation = ViolationOfKind(ViolationKind.Shape, OneSite);
        BaselineEntry identity = violation.BaselineIdentity()
            .ShouldNotBeNull();

        BuildFinding finding = FindingsOf(Failing(violation))
            .ShouldHaveSingleItem();

        finding.ShouldSatisfyAllConditions(
            () => finding.Subject.ShouldBe(identity.Subject),
            () => finding.Subject.ShouldNotBeNull(),
            () => finding.Source.ShouldBeNull(),
            () => finding.Target.ShouldBeNull());
    }

    [Fact]
    public void Findings_OfAShapeViolationWhoseFirstSiteIsUnowned_AreNoneThoughALaterSiteIsOwned()
    {
        // The whole-solution report places a shape violation at its first site, and the two must agree on
        // where a violation is: sliding to a later site would move it.
        var first = new SourceLocation("Elsewhere.cs", 3);
        var second = new SourceLocation("Owned.cs", 9);
        RuleResult result = Failing(ViolationOfKind(ViolationKind.Shape, first, second));

        BuildFindings.From(ReportOf(result), path => path == "Owned.cs")
            .ShouldBeEmpty();
        // The control: the same violation does yield a finding when its FIRST site is the owned one, so the
        // emptiness above is the placement rule rather than a predicate that rejects everything.
        BuildFindings.From(ReportOf(result), path => path == "Elsewhere.cs")
            .ShouldHaveSingleItem()
            .Line.ShouldBe(3);
    }

    [Fact]
    public void Findings_OfAnEdgeViolation_AreOnePerOwnedSiteInSiteOrder()
    {
        RuleResult result = Failing(ViolationOfKind(
            ViolationKind.Reference,
            new SourceLocation("A.cs", 5), new SourceLocation("Skipped.cs", 6), new SourceLocation("B.cs", 7)));

        IReadOnlyList<BuildFinding> findings = BuildFindings.From(ReportOf(result), path => path != "Skipped.cs");

        findings.Select(Place)
            .ShouldBe(["A.cs:5", "B.cs:7"]);
    }

    [Fact]
    public void Finding_FilePath_IsTheViolationsOwnStringUnchanged()
    {
        // Mixed separators and an unfolded `..`: the caller looks a syntax tree up by this exact string, so
        // relativizing or normalizing here would lose the finding rather than move it. The predicate is
        // asked the same string, which is what makes the round trip a single fact.
        const string path = @"C:\repo\src\..\src/App\Caller.cs";
        RuleResult result = Failing(ViolationOfKind(ViolationKind.Reference, new SourceLocation(path, 12)));

        BuildFindings.From(ReportOf(result), candidate => candidate == path)
            .ShouldHaveSingleItem()
            .FilePath.ShouldBe(path);
    }

    [Fact]
    public void Findings_AreInReportOrderThenViolationOrderThenSiteOrder()
    {
        // The first violation's sites descend, so nothing here can be passing by accident of a sort.
        var first = new RuleResult(
            Checker.Rule("a/first"), RuleStatus.Failed,
            [
                ViolationOfKind(ViolationKind.Reference, new SourceLocation("A.cs", 2), new SourceLocation("A.cs", 1)),
                ViolationOfKind(ViolationKind.Shape, new SourceLocation("B.cs", 9))
            ]);
        var second = new RuleResult(
            Checker.Rule("b/second"), RuleStatus.Failed,
            [ViolationOfKind(ViolationKind.Catch, new SourceLocation("C.cs", 4))]);

        IReadOnlyList<BuildFinding> findings = BuildFindings.From(ReportOf(first, second), EveryFile);

        findings.Select(finding => $"{finding.RuleId} {Place(finding)}")
            .ShouldBe(["a/first A.cs:2", "a/first A.cs:1", "a/first B.cs:9", "b/second C.cs:4"]);
    }

    [Fact]
    public void Messages_OfEveryFindingTheseRowsProduce_HoldNoLineBreak()
    {
        List<RuleResult> results = PlaceableKinds
            .Select(kind => Failing(ViolationOfKind(kind, OneSite)))
            .ToList();
        results.Add(Grown(1, Sources.TwoSiteControllerModel));
        results.Add(DomainRuleWithFix());

        IReadOnlyList<BuildFinding> findings = BuildFindings.From(ReportOf([.. results]), EveryFile);

        // The corpus reaches every placeable kind, a grown pair's extra sentence, and a rule carrying a fix —
        // the three shapes the message is composed from — so the claim below is about all of them.
        findings.Select(finding => finding.ViolationKind)
            .Distinct()
            .ShouldBe(PlaceableKinds, ignoreOrder: true);
        findings.Select(finding => finding.Message)
            .ShouldContain(message => message.Contains("The baseline holds", StringComparison.Ordinal));
        findings.Select(finding => finding.Message)
            .ShouldContain(message => message.Contains("Fix: ", StringComparison.Ordinal));
        findings.Where(finding => finding.Message.Contains('\n') || finding.Message.Contains('\r'))
            .ShouldBeEmpty();
    }

    [Fact]
    public void PlaceableKinds_AreEveryViolationKindButTheTwoThatCarryNoSite()
    {
        // The guard on every corpus above: a new violation kind lands here rather than being silently absent
        // from the rows that walk the list. Order-insensitive — the list is grouped by edge and shape for a
        // reader, which is not the order the enum declares them in.
        IEnumerable<ViolationKind> siteless = [ViolationKind.EmptySubject, ViolationKind.RuleError];

        PlaceableKinds.ShouldBe(Enum.GetValues<ViolationKind>().Except(siteless), ignoreOrder: true);
    }

    // Everything the ten placeable kinds need, built shallow: the caller, the far end, the banned member and
    // the offending declared member all being placeholders nothing under test reads but their names.
    private static Violation ViolationOfKind(ViolationKind kind, params SourceLocation[] sites)
    {
        TypeNode source = SyntheticNodes.Type("App.Caller");
        TypeNode target = SyntheticNodes.Type("Infra.Widget");
        MemberReference bannedMember = SyntheticNodes.Member(
            "System.Threading.Tasks.Task", "Wait", "M:System.Threading.Tasks.Task.Wait", MemberKind.Method);
        MemberNode subjectMember = SyntheticNodes.MemberSubject(
            "App.Service", "Name", "P:App.Service.Name", MemberKind.Property);

        return kind switch
        {
            ViolationKind.Reference => Violation.Reference(source, target, sites),
            ViolationKind.MemberUse => Violation.MemberUse(source, bannedMember, sites),
            ViolationKind.Construction => Violation.Construction(source, target, sites),
            ViolationKind.Injection => Violation.Injection(source, target, sites),
            ViolationKind.Catch => Violation.Catch(source, target, sites),
            ViolationKind.Throw => Violation.Throw(source, target, sites),
            ViolationKind.Expose => Violation.Expose(source, target, sites),
            ViolationKind.Shape => Violation.Shape(SyntheticNodes.Type("App.Orphan"), sites),
            ViolationKind.MemberShape => Violation.MemberShape(subjectMember, sites),
            ViolationKind.ProjectShape => Violation.ProjectShape(new ProjectNode("Zphil.Internal", []), sites),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "carries no site")
        };
    }

    // The layering rule over DomainSource, carrying a fix: the row that pins the whole composed line and the
    // no-line-break corpus both run exactly this. A method rather than a field, so a red inside the run stays
    // the calling row's instead of failing the whole class at type initialization.
    private static RuleResult DomainRuleWithFix()
    {
        return Checker.Run(DomainSource, arch => arch.Rule("layering/domain-independent")
                .Enforce(arch.Layer("Domain", "MyApp.Domain.*").MustNotReference(arch.Layer("Web", "MyApp.Web.*")))
                .Because("Domain is UI-agnostic; transaction boundaries live in services.")
                .Fix("Define an abstraction in Domain and implement it in Web."))
            .Single();
    }

    // The ratchet's grown shape from real source: a baseline allowing `allowed` sites of the one pair, met by
    // code that writes it more times than that.
    private static RuleResult Grown(int allowed, CodebaseModel codebase)
    {
        BaselineIndex index = Checker.Baselines(
            "data/x",
            BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
                .WithSiteCount(allowed));

        return Checker.Run(codebase, index, Sources.NoDataAccess)
            .Single();
    }

    // The very text the human report prints for this result's one violation, read back out of its own output
    // rather than typed a second time — so the report a developer reads and the line a build prints cannot
    // drift into two spellings of one violation.
    private static string ShouldHaveHumanWording(RuleResult result)
    {
        string block = result.HumanBlock();
        // The header line carries the same separator between the rule ID and its sentence, so it is dropped:
        // what is wanted is the one site line under it.
        List<string> siteLines = block.Split('\n')
            .Skip(1)
            .Where(line => line.Contains(" — ", StringComparison.Ordinal))
            .ToList();
        string siteLine = siteLines.ShouldHaveSingleItem(block);
        int separator = siteLine.IndexOf(" — ", StringComparison.Ordinal);

        return siteLine.Substring(separator + 3);
    }

    private static RuleResult Failing(Violation violation)
    {
        return new RuleResult(Checker.Rule(RuleId), RuleStatus.Failed, [violation]);
    }

    private static CheckReport ReportOf(params RuleResult[] results)
    {
        return new CheckReport(results);
    }

    private static IReadOnlyList<BuildFinding> FindingsOf(RuleResult result)
    {
        return BuildFindings.From(ReportOf(result), EveryFile);
    }

    private static string Place(BuildFinding finding)
    {
        return $"{finding.FilePath}:{finding.Line}";
    }
}
