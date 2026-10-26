using System.Text.Json;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Prose;
using SlideRule.Rendering;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.ModelFile;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Checking;

/// <summary>Wraps an <see cref="Action{Arch}" /> as a one-off spec so tests can build models inline.</summary>
internal sealed class InlineSpec(Action<Arch> define) : IArchitectureSpec
{
    public void Define(Arch arch)
    {
        define(arch);
    }
}

/// <summary>Fast-path checker harness: extract a codebase from source, build a one-rule model, check.</summary>
internal static class Checker
{
    // The spec name every model file this harness writes records. A one-off spec has no name of its own,
    // and none of these runs is about what the file says it came from.
    private const string RoundTripSpecName = "Spec";

    /// <summary>
    ///     Checks over a run given any of the conditions a host hands the checker, each defaulting to the whole,
    ///     unconditioned run: the baselines, a diff, a filter's narrowing, a partial model, the solution root.
    /// </summary>
    public static CheckReport Run(
        CodebaseModel codebase, Action<Arch> define, BaselineIndex? baselines = null, DiffContext? diff = null,
        NarrowedUniverse? narrowing = null, IncompleteModel? incompleteModel = null, SolutionRoot? root = null)
    {
        BaselineIndex index = baselines ?? BaselineIndex.Empty;
        return Pinned(
            define, codebase,
            rules => Harness.Check(rules, codebase, index, diff, narrowing, incompleteModel, root));
    }

    /// <summary>The same run, extracting <paramref name="source" /> first.</summary>
    public static CheckReport Run(
        string source, Action<Arch> define, BaselineIndex? baselines = null, NarrowedUniverse? narrowing = null)
    {
        return Run(CompilationFactory.Extract(source), define, baselines, narrowing: narrowing);
    }

    public static CheckReport Run(CodebaseModel codebase, BaselineIndex baselines, Action<Arch> define)
    {
        return Pinned(define, codebase, rules => Harness.Check(rules, codebase, baselines, null));
    }

    public static CheckReport Run(string source, BaselineIndex baselines, Action<Arch> define)
    {
        return Run(CompilationFactory.Extract(source), baselines, define);
    }

    /// <summary>The line a rule reports when <see cref="FilterNarrowing" /> emptied its subject.</summary>
    /// <remarks>
    ///     A stand-in for the sentence a filtered run composes, deliberately not the real wording: the filter
    ///     kind's wording is pinned where it is minted, the rows that narrow by a filter are about which rules
    ///     take it, and pinning the sentence twice would make one of the two pins the wrong place to look.
    /// </remarks>
    internal const string FilterSkipReason = "'Leaf.slnf' narrowed this run: 2 projects were not checked.";

    /// <summary>The cure an inert target reports under <see cref="FilterNarrowing" />.</summary>
    /// <remarks>
    ///     A stand-in for the sentence a filtered run composes, for <see cref="FilterSkipReason" />'s reason.
    /// </remarks>
    internal const string FilterInertCure = "'Leaf.slnf' narrowed this run; check the whole solution.";

    /// <summary>The narrowing a filtered run hands the checker — the kind that cuts nothing.</summary>
    internal static NarrowedUniverse FilterNarrowing { get; } = new(FilterSkipReason, FilterInertCure);

    /// <summary>
    ///     The cure a rule reports when <see cref="PartialModel" /> stood behind a selection that matched nothing.
    /// </summary>
    /// <remarks>
    ///     A stand-in for the sentence a partial model composes, for <see cref="FilterSkipReason" />'s reason.
    /// </remarks>
    internal const string PartialModelCure = "THE-PARTIAL-MODEL-CURE";

    /// <summary>The partial-model fact handed to the checker by a run part of whose model never loaded.</summary>
    internal static IncompleteModel PartialModel { get; } = new(PartialModelCure);

    /// <summary>
    ///     The report <paramref name="check" /> gives over the model <paramref name="define" /> builds, having
    ///     first put that model through its own model file and required the file's rules to reach the same
    ///     verdict as the live ones — so every check this harness runs is also a check that the file carries
    ///     the model whole.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every <c>Run</c> overload comes through here, with <paramref name="check" /> closing over the
    ///         codebase and every condition that overload was given, so the parsed rules are judged under exactly
    ///         the conditions the live ones were.
    ///     </para>
    ///     <para>
    ///         Deliberately not on the path of <see cref="HandBuilt" />, whose models bypass spec-build
    ///         validation and hold nodes no file can carry — its callers reach
    ///         <see cref="Harness.Check(ArchitectureModel,CodebaseModel)" /> directly and must keep
    ///         doing so.
    ///     </para>
    /// </remarks>
    private static CheckReport Pinned(
        Action<Arch> define, CodebaseModel codebase, Func<IReadOnlyList<ArchRule>, CheckReport> check)
    {
        ArchitectureModel model = Model(define);
        CheckReport report = check(model.Rules);

        model.ShouldCheckIdentically(
            RoundTripSpecName, ModelDocumentProject.ListOf(codebase), check, report);

        return report;
    }

    /// <summary>The reified model of a one-off spec — for the tests whose subject is the model, not the check.</summary>
    public static ArchitectureModel Model(Action<Arch> define)
    {
        return ArchModelBuilder.Build(new InlineSpec(define));
    }

    /// <summary>
    ///     The reified canonical sample (GRAMMAR §12), built once — the model is immutable and every
    ///     caller only reads it.
    /// </summary>
    public static readonly ArchitectureModel Canonical = ArchModelBuilder.Build(new ArchitectureSpec());

    /// <summary>
    ///     The tripwire a scope spec desugars to — a caution's only child, or a quarantine's second.
    /// </summary>
    public static ArchRule Tripwire(Action<Arch> define)
    {
        return SingleMatch(
            Model(define).Rules, rule => rule.IsTripwire, rule => rule.Id,
            "the scope's tripwire");
    }

    /// <summary>The rule <paramref name="id" /> names, for the tests that reach into a multi-rule model.</summary>
    public static ArchRule Rule(this ArchitectureModel model, string id)
    {
        return SingleMatch(model.Rules, rule => rule.Id == id, rule => rule.Id, $"rule '{id}'");
    }

    /// <summary>
    ///     The members <paramref name="rule" />'s constraint names as operands, each as its declaring type, name
    ///     and whether it is a method, in authoring order; empty for a rule with no constraint.
    /// </summary>
    /// <remarks>
    ///     The resolved member is what two rules must agree on. Its location is left out: it is diagnostic only,
    ///     so the same member captured from two different files still compares equal.
    /// </remarks>
    public static IReadOnlyList<string> MemberAnchors(this ArchRule rule)
    {
        if (rule.Constraint is not { } constraint) return [];

        return constraint.MemberOperands
            .Select(member => $"{member.DeclaringType.FullName}.{member.Name} (method: {member.IsMethod})")
            .ToList();
    }

    /// <summary>
    ///     The one result of a one-rule spec carrying <paramref name="constraint" />, checked over
    ///     <paramref name="codebase" /> — for the rows that differ by their constraint alone, whose rule id and
    ///     reason nothing reads.
    /// </summary>
    public static RuleResult Verdict(CodebaseModel codebase, Func<Arch, Constraint> constraint)
    {
        return Run(codebase, OneRuleSpec(constraint))
            .Single();
    }

    /// <summary>The same verdict, extracting <paramref name="source" /> first.</summary>
    public static RuleResult Verdict(string source, Func<Arch, Constraint> constraint)
    {
        return Verdict(CompilationFactory.Extract(source), constraint);
    }

    /// <summary>
    ///     The one rule of a one-rule spec enforcing <paramref name="constraint" />, for the rows whose rule id
    ///     and reason nothing reads.
    /// </summary>
    public static ArchRule OneRule(Func<Arch, Constraint> constraint)
    {
        return Model(OneRuleSpec(constraint))
            .Rules.Single();
    }

    // One enforced rule whose id and reason are placeholders nothing reads.
    private static Action<Arch> OneRuleSpec(Func<Arch, Constraint> constraint)
    {
        return arch => arch.Rule("area/rule")
            .Enforce(constraint(arch))
            .Because("b");
    }

    /// <summary>
    ///     The rendered sentence of a one-rule spec carrying <paramref name="constraint" />, for the rows that pin
    ///     one.
    /// </summary>
    /// <remarks>
    ///     Two constraints that render one sentence need not reify alike;
    ///     <see cref="ShouldReifyAlike(Func{Arch, Constraint}, Func{Arch, Constraint})" /> compares the model as well.
    /// </remarks>
    public static string Sentence(Func<Arch, Constraint> constraint)
    {
        return OneRule(constraint).Sentence;
    }

    /// <summary>
    ///     Asserts the one-rule specs enforcing <paramref name="actual" /> and <paramref name="expected" /> reify
    ///     to one thing, compared as <see cref="ShouldReifyAlike(Constraint, Constraint)" /> compares them.
    /// </summary>
    public static void ShouldReifyAlike(Func<Arch, Constraint> actual, Func<Arch, Constraint> expected)
    {
        Constraint actualConstraint = OneRule(actual).Constraint!;
        Constraint expectedConstraint = OneRule(expected).Constraint!;

        ShouldReifyAlike(actualConstraint, expectedConstraint);
    }

    /// <summary>
    ///     Asserts <paramref name="actual" /> and <paramref name="expected" /> reify to one thing: the same
    ///     sentence and the same model-file text, which spells every node and field the checker reads.
    /// </summary>
    /// <remarks>
    ///     The sentence alone cannot carry the claim: a one-operand union, a repeated adjective and a string
    ///     anchor each render exactly like a model they are not.
    /// </remarks>
    public static void ShouldReifyAlike(Constraint actual, Constraint expected)
    {
        SentenceRenderer.Sentence(actual)
            .ShouldBe(SentenceRenderer.Sentence(expected));
        ModelText(actual)
            .ShouldBe(ModelText(expected));
    }

    // A refused write is compared by its reason, so two constraints refused for different reasons still differ.
    private static string ModelText(Constraint constraint)
    {
        var writer = new NodeWriter(new Dictionary<string, string>());
        writer.WriteConstraint(constraint);

        return writer.Refusal ?? writer.Text;
    }

    /// <summary>
    ///     The subjects <paramref name="select" /> matches over <paramref name="codebase" />, in report
    ///     order — for the rows whose whole question is which types a selection reaches.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read back through a probe rule nothing can satisfy: no fixture type is named <c>ZZZ*</c>, so
    ///         every selected type lands as a shape violation and the violation list IS the selected set. The
    ///         verb is incidental — only the selection is under test — which is why it is spelled once here
    ///         rather than scaffolded at each site, and explained once rather than in a comment per site.
    ///     </para>
    ///     <para>
    ///         Its callers therefore keep this raw list rather than stating
    ///         <c>ShouldHaveFailedWithSubjects</c>: they hold no <see cref="RuleResult" />, and the probe
    ///         rule's redness is the incidental fact above — a row that asserted it would be
    ///         claiming the scaffolding.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string> Selects(CodebaseModel codebase, Func<Arch, Selection> select)
    {
        Action<Arch> probe = Probe(select);
        return Run(codebase, probe)
            .Single()
            .ShapeSubjects();
    }

    /// <summary>
    ///     The same reading over a run given the directory path globs are read against, for <c>Under</c>.
    /// </summary>
    public static IReadOnlyList<string> Selects(CodebaseModel codebase, SolutionRoot root, Func<Arch, Selection> select)
    {
        Action<Arch> probe = Probe(select);
        return Run(codebase, probe, root: root)
            .Single()
            .ShapeSubjects();
    }

    // The probe rule both readings run, explained on Selects.
    private static Action<Arch> Probe(Func<Arch, Selection> select)
    {
        return arch => arch.Rule("probe/selection")
            .Enforce(select(arch).MustHavePrefix("ZZZ"))
            .Because("b");
    }

    /// <summary>A baseline index carrying <paramref name="entries" /> under one rule — the ratchet's input.</summary>
    public static BaselineIndex Baselines(string ruleId, params BaselineEntry[] entries)
    {
        return new BaselineIndex(new Dictionary<string, RuleBaseline>(StringComparer.Ordinal)
        {
            [ruleId] = new(entries)
        });
    }

    /// <summary>
    ///     A one-rule model built by hand, bypassing spec-build validation so a check-time backstop can be
    ///     reached. Sentence and because are placeholders: a caller that needed either would build a spec.
    /// </summary>
    public static ArchitectureModel HandBuilt(string ruleId, Constraint constraint)
    {
        return new ArchitectureModel(
            [new ArchRule(ruleId, Posture.Enforce, "b", null, "sentence", constraint, null, null)], []);
    }

    /// <summary>
    ///     A metadata-only rule built by hand — no constraint, migrate or scope payload — for the renderer
    ///     rows whose subject is what a rule carries rather than what checking it produced.
    ///     <paramref name="because" /> and <paramref name="sentence" /> are placeholders; a row that asserts
    ///     on either says so by passing it.
    /// </summary>
    public static ArchRule Rule(
        string id, Posture posture = Posture.Enforce, string because = "b", string? fix = null,
        string sentence = "s", string? citation = null)
    {
        return new ArchRule(id, posture, because, fix, sentence, null, null, null, citation);
    }

    /// <summary>The single rule result — most specs under test carry exactly one rule.</summary>
    public static RuleResult Single(this CheckReport report)
    {
        return SingleMatch(report.Results, _ => true, result => result.Rule.Id, "the report's one rule");
    }

    /// <summary>The rule result for a given ID (for multi-rule specs, e.g. a desugared Quarantine scope).</summary>
    public static RuleResult ForRule(this CheckReport report, string ruleId)
    {
        return SingleMatch(
            report.Results, result => result.Rule.Id == ruleId, result => result.Rule.Id, $"rule '{ruleId}'");
    }

    // LINQ's Single, with a message that names what was sought and the id of every item there is: the facts that
    // settle whether the spec moved or the test's expectation did.
    private static T SingleMatch<T>(
        IReadOnlyList<T> items, Func<T, bool> wanted, Func<T, string> idOf, string sought)
    {
        List<T> matching = items.Where(wanted)
            .ToList();
        if (matching.Count == 1) return matching[0];

        string present = string.Join(", ", items.Select(idOf));
        throw new InvalidOperationException($"Looking for {sought}; there are: {present}.");
    }

    /// <summary>
    ///     The violations of one <paramref name="kind" />, each rendered by <paramref name="identify" />, in
    ///     report order — the one walk every violation-listing reader below is a naming of.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A row reaches for this reader and the ones below only where its subject is the projection
    ///         itself: report order, an arbitrary slot no named reader offers, or two reads cross-compared.
    ///         Every other claim about a result is an outcome verb of <see cref="RuleResultAssertions" />.
    ///     </para>
    ///     <para>
    ///         The readers stay in the harness because
    ///         <see cref="Selects(CodebaseModel, Func{Arch, Selection})" /> reads <see cref="ShapeSubjects" />:
    ///         beside the assertions they would make the harness depend on the assertions rather than the other
    ///         way round.
    ///     </para>
    /// </remarks>
    public static IReadOnlyList<string> Violators(
        this RuleResult result, ViolationKind kind, Func<Violation, string> identify)
    {
        return result.Violations.Where(v => v.Kind == kind)
            .Select(identify)
            .ToList();
    }

    /// <summary>Reference violations rendered as <c>Source -&gt; Target</c>, in report order.</summary>
    public static IReadOnlyList<string> ReferencePairs(this RuleResult result)
    {
        return result.Violators(ViolationKind.Reference, Pair);
    }

    /// <summary>Shape-violation subject FullNames, in report order.</summary>
    public static IReadOnlyList<string> ShapeSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.Shape, v => v.Subject!.FullName);
    }

    /// <summary>Member-shape violation subject SymbolIds, in report order (§4.6).</summary>
    public static IReadOnlyList<string> MemberShapeSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.MemberShape, v => v.SubjectMember!.SymbolId);
    }

    /// <summary>Member-use violation target SymbolIds (the member used), in report order.</summary>
    public static IReadOnlyList<string> MemberUseTargets(this RuleResult result)
    {
        return result.Violators(ViolationKind.MemberUse, v => v.Member!.SymbolId);
    }

    /// <summary>Project-shape violation subject names, in report order (§4.10).</summary>
    public static IReadOnlyList<string> ProjectSubjects(this RuleResult result)
    {
        return result.Violators(ViolationKind.ProjectShape, v => v.SubjectProject!.Name);
    }

    /// <summary>
    ///     The projects <paramref name="select" /> matches over <paramref name="codebase" />, in report
    ///     order — the project-stratum twin of <see cref="Selects(CodebaseModel, Func{Arch, Selection})" /> and
    ///     read back the same way, through a probe rule nothing can satisfy.
    /// </summary>
    /// <remarks>
    ///     The probe is the escape hatch with a constant-false predicate rather than a naming verb, because
    ///     every packaging verb passes a project whose facts nothing evaluated — which would make the probe
    ///     silently under-report exactly the fixtures the absent-fact rows are built from.
    /// </remarks>
    public static IReadOnlyList<string> SelectsProjects(CodebaseModel codebase, Func<Arch, ProjectSelection> select)
    {
        return Run(codebase, arch => arch.Rule("probe/projects")
                .Enforce(select(arch).Must(_ => false, description: "be selected by the probe"))
                .Because("b"))
            .Single()
            .ProjectSubjects();
    }

    /// <summary>
    ///     This result rendered as the human failure block, relative to the current directory — the text a
    ///     developer reads from the CLI or the xUnit adapter.
    /// </summary>
    public static string HumanBlock(this RuleResult result)
    {
        return HumanReportRenderer.RuleBlock(result, Directory.GetCurrentDirectory());
    }

    /// <summary>
    ///     This report rendered as the check verb's <c>--json</c> document at <paramref name="grain" /> —
    ///     the product's wire format, full by default.
    /// </summary>
    /// <remarks>
    ///     The solution directory is the current directory, and the solution and spec names are fixed
    ///     placeholders every wire-shape pin shares: none of them is what such an assertion is about, so
    ///     spelling them once here keeps the pins reading as the slot claims they are. The grain is the one
    ///     slot a caller does vary, so it is a parameter rather than a second spelling of the whole call.
    /// </remarks>
    public static string JsonReport(this CheckReport report, DocumentGrain grain = DocumentGrain.Full)
    {
        string solutionDirectory = Directory.GetCurrentDirectory();
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(WorkspaceDiagnostics.None, solutionDirectory);

        return JsonReportRenderer.Document(
            report, solutionDirectory, "S.sln", "Spec.dll", null, [], trust, [], grain);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as an edge: <paramref name="kind" /> over
    ///     <paramref name="source" /> and <paramref name="target" /> with sites, and every member and subject
    ///     slot omitted rather than null (which is what holds the schema at version 3).
    /// </summary>
    public static void ShouldRenderEdgeViolation(this CheckReport report, string kind, string source, string target)
    {
        JsonElement violation = ShouldRenderFirstViolationAs(report, kind);
        violation.GetProperty("source")
            .GetString()
            .ShouldBe(source);
        violation.GetProperty("target")
            .GetString()
            .ShouldBe(target);
        ShouldCarrySlot(violation, "targetMember", null);
        ShouldCarrySlot(violation, "subject", null);
        ShouldCarrySlot(violation, "subjectMember", null);
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a member subject: <paramref name="kind" />
    ///     over <paramref name="subjectMember" /> with sites, and the type-level and target slots omitted.
    /// </summary>
    public static void ShouldRenderMemberShapeViolation(this CheckReport report, string kind, string subjectMember)
    {
        JsonElement violation = ShouldRenderFirstViolationAs(report, kind);
        violation.GetProperty("subjectMember")
            .GetString()
            .ShouldBe(subjectMember);
        ShouldCarrySlot(violation, "subject", null);
        ShouldCarrySlot(violation, "target", null);
        ShouldCarrySlot(violation, "targetMember", null);
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a use of a member: <paramref name="kind" />
    ///     over <paramref name="source" /> and <paramref name="targetMember" /> with sites, and the type-level
    ///     target and subject slots omitted. The third distinct slot combination the wire format carries.
    /// </summary>
    public static void ShouldRenderTargetMemberViolation(
        this CheckReport report, string kind, string source, string targetMember)
    {
        JsonElement violation = ShouldRenderFirstViolationAs(report, kind);
        violation.GetProperty("source")
            .GetString()
            .ShouldBe(source);
        violation.GetProperty("targetMember")
            .GetString()
            .ShouldBe(targetMember);
        ShouldCarrySlot(violation, "target", null);
        ShouldCarrySlot(violation, "subject", null);
        violation.GetProperty("sites")
            .GetArrayLength()
            .ShouldBeGreaterThan(0);
    }

    /// <summary>
    ///     Asserts the first violation of the first rule renders as a project subject:
    ///     <paramref name="kind" /> over <paramref name="subjectProject" />, with <c>package</c> and
    ///     <c>framework</c> each present only when <paramref name="package" /> or <paramref name="framework" />
    ///     is given, <c>property</c> and <c>propertyValue</c> omitted, and every type- and member-level slot
    ///     omitted. The fourth distinct slot combination the wire format carries.
    /// </summary>
    public static void ShouldRenderProjectShapeViolation(
        this CheckReport report, string kind, string subjectProject, string? package = null, string? framework = null)
    {
        JsonElement violation = ShouldRenderFirstViolationAs(report, kind);
        violation.GetProperty("subjectProject")
            .GetString()
            .ShouldBe(subjectProject);

        ShouldCarrySlot(violation, "package", package);
        ShouldCarrySlot(violation, "framework", framework);
        ShouldCarrySlot(violation, "property", null);
        ShouldCarrySlot(violation, "propertyValue", null);

        ShouldCarrySlot(violation, "source", null);
        ShouldCarrySlot(violation, "target", null);
        ShouldCarrySlot(violation, "subject", null);
        ShouldCarrySlot(violation, "subjectMember", null);
    }

    // One optional slot of a violation's wire form: omitted when expected is null, otherwise present and holding it.
    // The slot rides as the message because inside a helper Shouldly prints the expression, not the literal.
    private static void ShouldCarrySlot(JsonElement violation, string slot, string? expected)
    {
        var reason = $"the {slot} slot";
        if (expected is null)
        {
            violation.TryGetProperty(slot, out _)
                .ShouldBeFalse(reason);
            return;
        }

        violation.GetProperty(slot)
            .GetString()
            .ShouldBe(expected, customMessage: reason);
    }

    private static string Pair(Violation v)
    {
        return $"{v.Source!.FullName} -> {v.Target!.FullName}";
    }

    // The first violation of the report's first rule, once the document is held at schema version 3 and the
    // violation at kind; cloned, because the document is disposed before the caller reads its slots.
    private static JsonElement ShouldRenderFirstViolationAs(CheckReport report, string kind)
    {
        using JsonDocument document = JsonDocument.Parse(report.JsonReport());
        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
        JsonElement violation = document.RootElement.GetProperty("rules")[0]
            .GetProperty("violations")[0]
            .Clone();
        violation.GetProperty("kind")
            .GetString()
            .ShouldBe(kind);

        return violation;
    }
}
