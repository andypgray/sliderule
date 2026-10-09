using System.Text.Json;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Model;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     The SARIF renderer's per-site mapping (<see cref="SarifReportRenderer.Serialize" />, the workspace-
///     free seam), driven over in-memory reports: each result's level, baseline state, suppressions,
///     fingerprint, location and message, each rule descriptor's level and help, and the notifications a
///     load's diagnostics become. The full-report byte shape is pinned separately by the
///     <c>violated-check.sarif</c> golden.
/// </summary>
public sealed class SarifReportRendererTests
{
    private const string ExemplarPage = "https://learn.microsoft.com/dotnet/exemplar";

    // Alpha sits inside the quarantined scope and User outside it, referencing nothing — so the containment
    // twin stays green and the only finding in the log is the tripwire's.
    private static readonly CodebaseModel QuarantinedCodebase = CompilationFactory.Extract(
        "App",
        ("App.Legacy/Alpha.cs", "namespace App.Legacy { public class Alpha {} }"),
        ("App.Client/User.cs", "namespace App.Client { public class User {} }"));

    // The diff that arms the tripwire: one changed file, inside the scope.
    private static readonly DiffContext TouchedAlpha = new("/repo", ["App.Legacy/Alpha.cs"]);

    // A rule whose target selection matches no type in the codebase, so checking it produces the inert-target
    // warning and nothing else — the one finding with no file to point at.
    private static readonly CheckReport InertTargetReport =
        Checker.Run("namespace App { public class Page {} }", NoGhosts);

    [Fact]
    public void Serialize_RedReference_EmitsErrorLevelNewBaselineStateNoSuppressions()
    {
        // An uncaptured Enforce reference violation: every site is a red error at baselineState new, and —
        // nothing grandfathers it — carries no suppressions property at all (null-omitted, not an empty array).
        CheckReport report = Checker.Run(Sources.OneController, arch =>
            arch.Rule("layer/no-data")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly."));

        string json = report.ToSarif();

        IReadOnlyList<JsonElement> results = json.SarifResults();
        results.ShouldNotBeEmpty();
        foreach (JsonElement result in results)
        {
            result.GetProperty("level")
                .GetString()
                .ShouldBe("error");
            result.GetProperty("baselineState")
                .GetString()
                .ShouldBe("new");
            result.TryGetProperty("suppressions", out _)
                .ShouldBeFalse();
        }
    }

    [Fact]
    public void Serialize_MultiSiteSameFile_AssignsDistinctFingerprintOrdinalsPerSite()
    {
        // One violation (Page -> Db) touched at several sites in one file: the per-file ordinal counter resets
        // per violation and increments per site, so the sites get consecutive 0-based ordinals and thus
        // distinct fingerprints — the (ruleId, fingerprint) alert key stays unique when code motion shifts lines.
        const string source = """
                              namespace App.Web
                              {
                                  public class Page
                                  {
                                      public App.Data.Db First() => new App.Data.Db();
                                      public App.Data.Db Second() => new App.Data.Db();
                                  }
                              }
                              namespace App.Data { public class Db {} }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("layer/no-data")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly."));

        string json = report.ToSarif();

        IReadOnlyList<JsonElement> results = json.SarifResults();
        results.Count.ShouldBeGreaterThan(1); // genuinely multi-site
        // All sites belong to the one Page -> Db violation, so the fingerprints share every slot but the ordinal.
        IReadOnlyList<string> fingerprints = results.Select(Fingerprint)
            .ToList();
        fingerprints.ShouldAllBe(f => f.StartsWith("v1|T:App.Web.Page|T:App.Data.Db|", StringComparison.Ordinal));
        List<int> ordinals = fingerprints.Select(f => int.Parse(f.Split('|')[^1]))
            .ToList();
        ordinals.ShouldBe(Enumerable.Range(0, ordinals.Count)); // consecutive, 0-based, distinct
    }

    [Fact]
    public void Serialize_TwoPackagesDeclaredInOneFile_GetDistinctFingerprints()
    {
        // The counterpart to the ordinal fact above, for the collision an ordinal cannot separate:
        // MustReferenceNoPackages mints one violation per package, each carrying the project's identity
        // (project:{Name}, no source, no target) and a single site. Two packages declared in one file are
        // then the same subject, the same path and ordinal 0 apiece, so the two results carried one
        // fingerprint and a consumer keyed on (ruleId, fingerprint) merged two findings into one alert.
        // The package name in the otherwise empty target slot is what separates them.
        CodebaseModel solution = ProjectFacts.Solution(
            ProjectFacts.Project(
                "Zphil.Domain",
                packageReferences:
                [
                    ProjectFacts.Package("Newtonsoft.Json", "Domain.csproj", 9),
                    ProjectFacts.Package("Serilog", "Domain.csproj", 10)
                ]));
        CheckReport report = Checker.Run(solution, arch => arch.Rule("packaging/domain-pure")
            .Enforce(arch.Projects.Matching("*").MustReferenceNoPackages())
            .Because("The domain must not take a dependency the rest of the estate has to carry."));

        IReadOnlyList<JsonElement> results = report.ToSarif()
            .SarifResults();

        results.Count.ShouldBe(2);
        IReadOnlyList<string> fingerprints = results.Select(Fingerprint)
            .ToList();
        fingerprints.ShouldBeUnique();
        // Not the whole string: the path slot depends on relativizing a bare relative site against the
        // current directory. The target slot is the fix, and it reads in render order.
        IReadOnlyList<string> targets = fingerprints.Select(f => f.Split('|')[2])
            .ToList();
        targets.ShouldBe(["Newtonsoft.Json", "Serilog"]);
    }

    [Fact]
    public void Serialize_CatchViolation_EmitsCatchesMessageText()
    {
        // A red MustNotCatch violation drives the SARIF MessageText catch arm (a missing arm renders empty text
        // and reads green): message `Source catches Target`, level error (GRAMMAR §4.8).
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App { public class Handler { public void Run() { try { } catch (Errors.DbError) { } } } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/no-catch")
                .Enforce(arch.Namespace("App.*").MustNotCatch(arch.Namespace("Errors.*")))
                .Because("Catch specific exceptions, not the domain base."));

        string json = report.ToSarif();

        json.ShouldBeOneErrorSaying("App.Handler catches Errors.DbError");
    }

    [Fact]
    public void Serialize_ExposeViolation_EmitsExposesMessageText()
    {
        // A red MustNotExpose violation drives the SARIF MessageText expose arm (a missing arm renders empty text
        // and reads green): message `Source exposes Target`, level error (GRAMMAR §4.9).
        const string source = """
                              namespace Secrets { public class Data {} }
                              namespace App { public class Facade { public void Take(Secrets.Data d) {} } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("api/no-expose")
                .Enforce(arch.Namespace("App.*").MustNotExpose(arch.Namespace("Secrets.*")))
                .Because("Keep internal types off the public API."));

        string json = report.ToSarif();

        json.ShouldBeOneErrorSaying("App.Facade exposes Secrets.Data");
    }

    [Fact]
    public void Serialize_ThrowViolation_EmitsThrowsMessageText()
    {
        // A red MustOnlyThrow violation drives the SARIF MessageText throw arm: an external, unlisted throw is
        // red (no external exemption), message `Source throws Target`, level error (GRAMMAR §4.8). The allow-set
        // is a typeof the code never throws, which allows nothing and warns nothing, so the one result is the throw.
        const string source = """
                              namespace App { public class Service { public void Run() => throw new System.InvalidOperationException(); } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/only-throw")
                .Enforce(arch.Namespace("App.*").MustOnlyThrow(typeof(ArgumentException)))
                .Because("Throw only the sanctioned exception types."));

        string json = report.ToSarif();

        json.ShouldBeOneErrorSaying("App.Service throws System.InvalidOperationException");
    }

    [Fact]
    public void Serialize_UnfilteredCatchViolation_EmitsOneResultAtTheUnfilteredSite()
    {
        // MustNotCatchUnfiltered reuses the Catch arm, so SARIF needs no new message form. The evidence-subset
        // dividend shows here end to end: the edge has two catch sites but the violation carries only the
        // unfiltered one, so exactly ONE result is emitted, and it points at line 11 — never at the sanctioned
        // `when`-filtered clause on line 9. The filter law itself rides the descriptor's shortDescription (the
        // rule's own sentence), so a standalone viewer reads it beside the message.
        const string source = """
                              namespace Errors { public class DbError : System.Exception {} }
                              namespace App
                              {
                                  public class Handler
                                  {
                                      public void Run(bool flag)
                                      {
                                          try { }
                                          catch (Errors.DbError) when (flag) { }
                                          try { }
                                          catch (Errors.DbError) { }
                                      }
                                  }
                              }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/filter-catches")
                .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                .Because("A broad catch names what it expects."));

        string json = report.ToSarif();

        JsonElement result = json.ShouldBeOneErrorSaying("App.Handler catches Errors.DbError");
        StartLine(result)
            .ShouldBe(11);

        // The filter law is not in the message, so it has to be somewhere a standalone viewer looks: the rule
        // descriptor's shortDescription, which carries the law sentence verbatim.
        string sentence = report.Results.Single()
            .Rule.Sentence;
        sentence.ShouldContain("without a `when` filter");
        json.SarifRules()
            .Single()
            .GetProperty("shortDescription")
            .GetProperty("text")
            .GetString()
            .ShouldBe(sentence);
    }

    [Fact]
    public void Serialize_ForbiddenThrowViolation_EmitsThrowsMessageText()
    {
        // A red MustNotThrow violation drives the same SARIF MessageText throw arm the allow-list drives —
        // the ban polarity adds no arm, so there is no new way for a red to render as nothing.
        const string source = """
                              namespace App { public class Service { public void Run() => throw new System.Exception(); } }
                              """;
        CheckReport report = Checker.Run(source, arch =>
            arch.Rule("ex/no-bare-throws")
                .Enforce(arch.Namespace("App.*").MustNotThrow(typeof(Exception)))
                .Because("Throw a type a caller can dispatch on."));

        string json = report.ToSarif();

        json.ShouldBeOneErrorSaying("App.Service throws System.Exception");
    }

    [Fact]
    public void Serialize_EmptySubjectAndRuleError_ProduceNoResults()
    {
        // EmptySubject and RuleError violations are site-less by construction (the model deliberately carries no
        // spec-source location for them), so they contribute zero results — they still gate via the exit code
        // and show in human/JSON output, but SARIF is a per-site artifact. The rule metadata is still emitted.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule("naming/empty"), RuleStatus.Failed,
                [
                    Violation.EmptySubject(
                        "The subject selection matched no solution-declared types.", "a cure SARIF never carries")
                ]),
            new RuleResult(
                Checker.Rule("ref/error"), RuleStatus.Failed,
                [Violation.RuleError("a closed-generic backstop message")])
        ]);

        string json = report.ToSarif();

        json.SarifResults()
            .ShouldBeEmpty();
        json.SarifRules()
            .Select(r => r.GetProperty("id")
                .GetString())
            .ShouldBe(["naming/empty", "ref/error"]);
    }

    [Fact]
    public void Serialize_TripwireEmptySentence_OmitsShortDescription()
    {
        // A Quarantine tripwire carries no law sentence (empty string), so its reportingDescriptor omits
        // shortDescription entirely rather than emitting an empty one, while still carrying its Because as
        // fullDescription. This is the quarantine-tripwire pin the golden's last rule also holds.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule(
                    "legacy/tripwire", Posture.Quarantine, because: "Replacement scheduled; not worth stabilizing.",
                    sentence: ""),
                RuleStatus.Skipped, [], [], "no diff context", [])
        ]);

        string json = report.ToSarif();

        JsonElement rule = json.SarifRules()
            .Single();
        rule.TryGetProperty("shortDescription", out _)
            .ShouldBeFalse();
        rule.GetProperty("fullDescription")
            .GetProperty("text")
            .GetString()
            .ShouldBe("Replacement scheduled; not worth stabilizing.");
    }

    [Fact]
    public void Serialize_CitedRule_CarriesHelpUriAndAnAutolinkedHelpMarkdown()
    {
        // The citation lands twice on purpose. helpUri is where the standard puts it; help.markdown is what
        // GitHub code scanning actually displays, and a link that appeared only in helpUri would be invisible
        // exactly where a reader is standing over the alert. The angle brackets keep the sentence's period out
        // of the link target; help.text carries the same sentence with no markup, which is also what fills
        // GitHub's required help.text for a rule that has no fix of its own.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule(
                    "http/reuse-httpclient", because: "A new client per call exhausts sockets.",
                    fix: "Inject IHttpClientFactory.",
                    citation: "https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines"),
                RuleStatus.Passed, [])
        ]);

        JsonElement rule = report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem();

        rule.GetProperty("helpUri")
            .GetString()
            .ShouldBe("https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines");
        rule.GetProperty("help")
            .GetProperty("text")
            .GetString()
            .ShouldBe(
                "Inject IHttpClientFactory. "
                + "See https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines.");
        rule.GetProperty("help")
            .GetProperty("markdown")
            .GetString()
            .ShouldBe(
                "Inject IHttpClientFactory. "
                + "See <https://learn.microsoft.com/dotnet/fundamentals/networking/http/httpclient-guidelines>.");
    }

    [Fact]
    public void Serialize_CitedRuleWithoutFix_MakesTheCitationTheWholeHelp()
    {
        // A rule with no fix used to carry no help at all, which GitHub marks as a required field. The
        // citation fills it on its own rather than being dropped for want of a fix to join.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule(
                    "http/reuse-httpclient", because: "A new client per call exhausts sockets.",
                    citation: "https://learn.microsoft.com/dotnet/first"),
                RuleStatus.Passed, [])
        ]);

        JsonElement help = report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem()
            .GetProperty("help");

        help.GetProperty("text")
            .GetString()
            .ShouldBe("See https://learn.microsoft.com/dotnet/first.");
        help.GetProperty("markdown")
            .GetString()
            .ShouldBe("See <https://learn.microsoft.com/dotnet/first>.");
    }

    [Fact]
    public void Serialize_UncitedRule_OmitsHelpUriAndTheMarkdownTwin()
    {
        // What keeps the SARIF golden stable: a rule that cites nothing renders help as the bare fix in text
        // alone, and helpUri is absent rather than null.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule("naming/x", fix: "Rename it."),
                RuleStatus.Passed, [])
        ]);

        JsonElement rule = report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem();

        rule.TryGetProperty("helpUri", out _)
            .ShouldBeFalse();
        rule.GetProperty("help")
            .TryGetProperty("markdown", out _)
            .ShouldBeFalse();
        rule.GetProperty("help")
            .GetProperty("text")
            .GetString()
            .ShouldBe("Rename it.");
    }

    [Theory]
    [InlineData(
        "Inject the repository.", ExemplarPage,
        "Inject the repository. Exemplar: OrderService (Domain/OrderService.cs:5). See " + ExemplarPage + ".",
        "Inject the repository. Exemplar: OrderService (Domain/OrderService.cs:5). See <" + ExemplarPage + ">.")]
    [InlineData(
        null, ExemplarPage,
        "Exemplar: OrderService (Domain/OrderService.cs:5). See " + ExemplarPage + ".",
        "Exemplar: OrderService (Domain/OrderService.cs:5). See <" + ExemplarPage + ">.")]
    [InlineData(
        "Inject the repository.", null,
        "Inject the repository. Exemplar: OrderService (Domain/OrderService.cs:5).",
        null)]
    [InlineData(
        null, null,
        "Exemplar: OrderService (Domain/OrderService.cs:5).",
        null)]
    public void Serialize_RuleWithAResolvedExemplar_PutsItsPointerBetweenTheFixAndTheCitation(
        string? fix, string? citation, string text, string? markdown)
    {
        // The pointer follows the advice it points at and comes before the page the reason rests on; with no
        // fix it stands alone, and the markdown twin is still written only when there is a link to render.
        var report = new CheckReport(
        [
            new RuleResult(
                Checker.Rule("data-access/no-inline-sql", fix: fix, citation: citation),
                RuleStatus.Passed, [],
                exemplar: [SyntheticNodes.Type("OrderService", new SourceLocation("Domain/OrderService.cs", 5))])
        ]);

        JsonElement help = report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem()
            .GetProperty("help");

        help.GetProperty("text")
            .GetString()
            .ShouldBe(text);
        if (markdown is null)
            help.TryGetProperty("markdown", out _)
                .ShouldBeFalse();
        else
            help.GetProperty("markdown")
                .GetString()
                .ShouldBe(markdown);
    }

    [Fact]
    public void Serialize_RuleWhoseExemplarResolvedNothing_HelpsWithTheFixAlone()
    {
        // Help reads the run's resolved types, not the spec's phrase: a dead exemplar has nothing to point at,
        // and the warning result is where it is reported.
        CheckReport report = Checker.Run("namespace App { public class Page {} }", arch =>
            arch.Rule("naming/pages")
                .Enforce(arch.Namespace("App.*").MustHaveSuffix("Page"))
                .Because("b")
                .Fix("Rename it.")
                .Exemplar(arch.Types.Named("Ghost")));

        report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem()
            .GetProperty("help")
            .GetProperty("text")
            .GetString()
            .ShouldBe("Rename it.");
    }

    [Fact]
    public void Serialize_DeadExemplarAndDeadOperandNamingOneType_GetDistinctFingerprints()
    {
        // Both warnings quote the same reference, carry no file and take ordinal 0, so with the source slot
        // empty for both a consumer keyed on the fingerprint would merge two findings into one alert. The
        // literal `exemplar` in the source slot is what keeps them apart.
        CheckReport report = Checker.Run("namespace App { public class Page {} }", arch =>
            arch.Rule("layer/no-ghosts")
                .Enforce(arch.Namespace("App.*")
                    .MustNotReference(arch.Namespace("App.*"), arch.Types.Named("Ghost")))
                .Because("Nothing may reach the ghost.")
                .Exemplar(arch.Types.Named("Ghost")));

        IReadOnlyList<string> fingerprints = report.ToSarif()
            .SarifResults()
            .Select(Fingerprint)
            .ToList();

        fingerprints.ShouldBe(["v1||types named `Ghost`|||0", "v1|exemplar|types named `Ghost`|||0"]);
    }

    [Fact]
    public void Serialize_TripwireDescriptor_DeclaresWarningLevelWhileItsContainmentTwinStaysError()
    {
        // A tripwire warns and can never fail, so its descriptor has to say `warning`: at `error` it advertises
        // to a scanning service an alert it has no way to raise, and any touch it does report would be filed at
        // the severity of a broken law. Its containment twin, desugared from the same scope statement, is a red
        // law and stays where it was — which is why the level is read off the rule's own payload rather than
        // off the posture the two share.
        CheckReport report = Checker.Run(QuarantinedCodebase, BaselineIndex.Empty, TouchedAlpha, QuarantinedScope);

        string json = report.ToSarif();

        json.SarifRules()
            .ToDictionary(
                rule => rule.GetProperty("id")
                    .GetString()!,
                rule => rule.GetProperty("defaultConfiguration")
                    .GetProperty("level")
                    .GetString())
            .ShouldBe(new Dictionary<string, string?>
            {
                ["legacy/quarantined/containment"] = "error",
                ["legacy/quarantined/tripwire"] = "warning"
            });
    }

    [Fact]
    public void Serialize_TripwireWarning_EmitsAWarningResultAtTheChangedFileWithNoRegion()
    {
        // The finding a tripwire produces is a warning, and before this it produced no SARIF result at all: the
        // walk covered violations and grandfathered entries only, so the one rule in the vocabulary that
        // reports by warning reported nothing at all through this channel. The location is the changed file and
        // nothing narrower — the finding is that the file was touched, and a start line would anchor the alert
        // wherever the renderer chose rather than where anything happened.
        CheckReport report = Checker.Run(QuarantinedCodebase, BaselineIndex.Empty, TouchedAlpha, QuarantinedScope);

        string json = report.ToSarif();

        JsonElement result = json.SarifResults()
            .ShouldHaveSingleItem();
        result.GetProperty("ruleId")
            .GetString()
            .ShouldBe("legacy/quarantined/tripwire");
        result.GetProperty("level")
            .GetString()
            .ShouldBe("warning");
        MessageText(result)
            .ShouldContain("App.Legacy/Alpha.cs");
        JsonElement location = result.GetProperty("locations")
            .EnumerateArray()
            .ToList()
            .ShouldHaveSingleItem()
            .GetProperty("physicalLocation");
        location.GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString()
            .ShouldBe("App.Legacy/Alpha.cs");
        location.TryGetProperty("region", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Serialize_CautionDescriptor_DeclaresWarningLevelAndCarriesItsOwnPosture()
    {
        // The renderer has no code of its own for this posture: the level is keyed on the tripwire role rather
        // than the posture, and the posture rides the model's own enum through the shared lower-casing. The
        // pin keeps both right.
        CheckReport report = Checker.Run(QuarantinedCodebase, BaselineIndex.Empty, TouchedAlpha, CautionedScope);

        JsonElement rule = report.ToSarif()
            .SarifRules()
            .ShouldHaveSingleItem();

        rule.GetProperty("id")
            .GetString()
            .ShouldBe("legacy/cautioned/tripwire");
        rule.GetProperty("defaultConfiguration")
            .GetProperty("level")
            .GetString()
            .ShouldBe("warning");
        rule.GetProperty("properties")
            .GetProperty("posture")
            .GetString()
            .ShouldBe("caution");
    }

    [Fact]
    public void Serialize_CautionWarning_EmitsAWarningResultAtTheChangedFile()
    {
        CheckReport report = Checker.Run(QuarantinedCodebase, BaselineIndex.Empty, TouchedAlpha, CautionedScope);

        JsonElement result = report.ToSarif()
            .SarifResults()
            .ShouldHaveSingleItem();

        result.GetProperty("ruleId")
            .GetString()
            .ShouldBe("legacy/cautioned/tripwire");
        result.GetProperty("level")
            .GetString()
            .ShouldBe("warning");
        result.GetProperty("locations")
            .EnumerateArray()
            .ToList()
            .ShouldHaveSingleItem()
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString()
            .ShouldBe("App.Legacy/Alpha.cs");
    }

    [Fact]
    public void Serialize_InertTargetWarning_EmitsAWarningResultWithNoLocation()
    {
        // The other warning kind, and the one that proves the location is read off the warning rather than
        // assumed: an inert target is a fact about the rule's own operand — a pattern that matched no type —
        // so there is no file to point at and the result carries an empty locations array rather than a
        // fabricated one.
        string json = InertTargetReport.ToSarif();

        JsonElement result = json.SarifResults()
            .ShouldHaveSingleItem();
        result.GetProperty("level")
            .GetString()
            .ShouldBe("warning");
        result.GetProperty("locations")
            .EnumerateArray()
            .ShouldBeEmpty();
    }

    [Fact]
    public void Serialize_InertTargetWarning_CarriesTheMessageWithoutTheAuthoringCure()
    {
        // The warning carries a cure for whoever writes the rule, and this document does not: its reader is
        // code scanning, where an alert stands in front of the whole team rather than the spec's author. The
        // empty-subject cure could not join it in any case — those violations are site-less and mint no
        // result — so carrying one here would land half a family's advice and hide the rest.
        InertTargetReport.Single()
            .Warnings.Single()
            .Hint.ShouldNotBeNull();

        string json = InertTargetReport.ToSarif();

        JsonElement warning = json.SarifResults()
            .ShouldHaveSingleItem();

        MessageText(warning)
            .ShouldBe("This rule is inert: its target selection matched no types.");
    }

    [Fact]
    public void Serialize_TwoDeadTargetPatterns_GetDistinctFingerprintsKeyedOnEachPattern()
    {
        // Every warning result takes ordinal 0 and none of these carries a file, so without the pattern in the
        // key two dead patterns on one rule would mint one fingerprint, and a consumer keyed on it would merge
        // two findings into one alert. The pattern sits in the target slot, where an edge puts its target.
        CheckReport report = Checker.Run("namespace App { public class Page {} }", arch =>
            arch.Rule("layer/no-ghosts")
                .Enforce(arch.Namespace("App.*")
                    .MustNotReference(arch.Namespace("App.*"), arch.Namespace("Ghost.*"), arch.Namespace("Phantom.*")))
                .Because("Nothing may reach the ghost layers."));

        IReadOnlyList<string> fingerprints = report.ToSarif()
            .SarifResults()
            .Select(Fingerprint)
            .ToList();

        fingerprints.ShouldBe(["v1||types in `Ghost.*`|||0", "v1||types in `Phantom.*`|||0"]);
    }

    [Fact]
    public void Serialize_OrphanedBaselineWarnings_SitAtEachOrphansFileKeyedOnItsRuleId()
    {
        // Two orphans on one rule are two alerts: the orphan's ID takes the target slot, as a dead pattern's
        // reference does, and its baseline file is both the location and the path slot.
        CheckReport report = Checker.Run(
            Sources.OneControllerModel, BaselineIndex.Empty.WithOrphanScan(Sources.TwoOrphans), Sources.NoDataAccess);

        List<JsonElement> warnings = report.ToSarif()
            .SarifResults()
            .Where(result => result.GetProperty("level")
                .GetString() == "warning")
            .ToList();

        warnings.Select(Fingerprint)
            .ShouldBe(["v1||data/old-x||arch/baselines/data/old-x.json|0", "v1||legacy/old||arch/shared.json|0"]);
        warnings.Select(result => result.GetProperty("locations")[0]
                .GetProperty("physicalLocation")
                .GetProperty("artifactLocation")
                .GetProperty("uri")
                .GetString())
            .ShouldBe(["arch/baselines/data/old-x.json", "arch/shared.json"]);
    }

    [Fact]
    public void Serialize_GrandfatheredWithoutAttribution_FallsBackToTheBaselinePathInSuppressionAndMessage()
    {
        // A grandfathered Migrate violation whose baseline entry has no `because`: the attribution falls back to
        // the generic `grandfathered in {conventional baseline path}` form (note level, unchanged state), and
        // reaches the message as well as the suppression.
        BaselineIndex index = Checker.Baselines("data/x", BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db"));
        CheckReport report = Checker.Run(Sources.OneController, index, Sources.NoDataAccess);

        string json = report.ToSarif();

        ShouldAttributeEveryNoteWith(json, "grandfathered in arch/baselines/data/x.json");
    }

    [Fact]
    public void Serialize_GrandfatheredWithBecause_CarriesTheEntrysBecauseInSuppressionAndMessage()
    {
        // The Core path: RuleBaseline.TryMatch recovers the stored entry's attribution (identity equality
        // excludes Because), and RuleResult.GrandfatheredEntries carries it index-aligned into the report, so the
        // attribution is the operator's own `because`, not the generic fallback. Prefixed rather than bare
        // because the one string is also the message's tail, where it has to say what it is.
        const string because = "Legacy Active Record; scheduled for removal in Q3.";
        BaselineEntry entry = BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
            .WithBecause(because);
        CheckReport report = Checker.Run(Sources.OneController, Checker.Baselines("data/x", entry), Sources.NoDataAccess);

        string json = report.ToSarif();

        ShouldAttributeEveryNoteWith(json, $"grandfathered: {because}");
    }

    [Fact]
    public void Results_GrownPair_AreErrorLevelUpdatedAndUnsuppressed()
    {
        // A grandfathered pair carrying more sites than its entry records is red like anything else red, but
        // not `new`: code scanning already has an alert for this pair from the run that baselined it, and
        // `new` would ask for a second one. Unsuppressed, because the finding IS that the suppression the
        // entry granted no longer covers what is there — a note carrying the operator's own justification
        // would close the alert on the strength of the attribution the growth just outgrew. The message says
        // so too: a reader standing over the alert sees the violation and no grandfathering beside it.
        BaselineEntry entry = BaselineEntry.ForEdge("T:App.Web.OldController", "T:App.Data.Db")
            .WithSiteCount(1)
            .WithBecause("Legacy Active Record; scheduled for removal in Q3.");
        CheckReport report = Checker.Run(Sources.TwoSiteController, Checker.Baselines("data/x", entry), Sources.NoDataAccess);

        string json = report.ToSarif();

        IReadOnlyList<JsonElement> results = json.SarifResults();
        results.Count.ShouldBe(2); // one per site of the grown pair, and nothing grandfathered beside them
        foreach (JsonElement result in results)
        {
            result.GetProperty("level")
                .GetString()
                .ShouldBe("error");
            result.GetProperty("baselineState")
                .GetString()
                .ShouldBe("updated");
            result.TryGetProperty("suppressions", out _)
                .ShouldBeFalse();
            MessageText(result)
                .ShouldBe("App.Web.OldController references App.Data.Db");
        }
    }

    [Fact]
    public void Serialize_PackageAudit_IsOneWarningNotificationPerAdvisoryAheadOfTheStream()
    {
        // In the order stderr writes them: each advisory once, naming its projects, then the stream and its
        // note. However many lines the load raised an advisory in, code scanning is told about it once.
        string app = CapturedAuditLines.FramedFor("App", CapturedAuditLines.BuildTasksGitAdvisory);
        string tests = CapturedAuditLines.FramedFor("App.Tests", CapturedAuditLines.BuildTasksGitAdvisory);
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [app, tests, app, "a load failure", tests]
        };

        string sarif = InertTargetReport.ToSarif(diagnostics);

        IReadOnlyList<JsonElement> notifications = sarif.SarifNotifications();
        notifications.Select(MessageText)
            .ShouldBe([
                $"{CapturedAuditLines.BuildTasksGitAdvisory} (2 projects: 'App' and 'App.Tests')",
                "a load failure",
                MsBuildBootstrap.SelectionNote()
            ]);
        notifications.ShouldAllBe(notification => notification.GetProperty("level").GetString() == "warning");
    }

    [Fact]
    public void Serialize_AnAdvisoryNoFrameNamedAProjectFor_IsANotificationOfTheMessageAlone()
    {
        // A line that named no project reports none, rather than an empty list or a project guessed from the
        // path inside the message.
        WorkspaceDiagnostics diagnostics = WorkspaceDiagnostics.None with
        {
            LoadFailures = [CapturedAuditLines.CodedLine]
        };

        string sarif = InertTargetReport.ToSarif(diagnostics);

        sarif.ShouldCarryOneAuditNotification(CapturedAuditLines.CodedLine, null);
    }

    // The scope QuarantinedCodebase is checked against: one statement desugaring into the containment law and
    // the tripwire, which is what makes the two halves comparable in one log.
    private static void QuarantinedScope(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha is structural.")
            .Because("Replacement scheduled; not worth stabilizing.");
    }

    // The same region under the posture with no containment law: one statement, one rule, and no red half
    // for the log to carry.
    private static void CautionedScope(Arch arch)
    {
        arch.Scope("legacy/cautioned")
            .Caution(arch.Namespace("App.Legacy.*"))
            .Dragons("Alpha is structural.")
            .Because("Every caller depends on the exact behaviour.");
    }

    // The rule InertTargetReport is checked against: `Ghost.*` names no type, so the rule is inert.
    private static void NoGhosts(Arch arch)
    {
        arch.Rule("layer/no-ghosts")
            .Enforce(arch.Namespace("App.*")
                .MustNotReference(arch.Namespace("Ghost.*")))
            .Because("Nothing may reach the ghost layer.");
    }

    /// <summary>
    ///     Asserts the render carries at least one grandfathered result, and that every one of them is an
    ///     <c>unchanged</c> note landing <paramref name="attribution" /> in both places it belongs: the
    ///     justification of its single external suppression, and the parenthetical tail of its message —
    ///     the second being the landing a code-scanning alert shows. One helper over the pair, because a
    ///     rendering asserted on its own is a rendering that can be dropped on its own.
    /// </summary>
    private static void ShouldAttributeEveryNoteWith(string json, string attribution)
    {
        IReadOnlyList<JsonElement> notes = Notes(json);
        notes.ShouldNotBeEmpty();
        foreach (JsonElement note in notes)
        {
            note.GetProperty("baselineState")
                .GetString()
                .ShouldBe("unchanged");
            JsonElement suppression = note.GetProperty("suppressions")
                .EnumerateArray()
                .Single();
            suppression.GetProperty("kind")
                .GetString()
                .ShouldBe("external");
            suppression.GetProperty("justification")
                .GetString()
                .ShouldBe(attribution);
            MessageText(note)
                .ShouldEndWith($" ({attribution})");
        }
    }

    // The grandfathered results — every note-level result carries a suppression by construction.
    private static IReadOnlyList<JsonElement> Notes(string json)
    {
        return json.SarifResults()
            .Where(r => r.GetProperty("level")
                .GetString() == "note")
            .ToList();
    }

    // The alert's title, which is the one field every code-scanning UI shows.
    private static string MessageText(JsonElement result)
    {
        return result.GetProperty("message")
            .GetProperty("text")
            .GetString()!;
    }

    private static string Fingerprint(JsonElement result)
    {
        return result.GetProperty("partialFingerprints")
            .GetProperty("slideRuleViolationIdentity/v1")
            .GetString()!;
    }

    // The 1-based source line of a single-location result — the site the reader is sent to.
    private static int StartLine(JsonElement result)
    {
        return result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region")
            .GetProperty("startLine")
            .GetInt32();
    }
}
