using Shouldly;
using Xunit;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The one-project narrowing: before a rule is evaluated over a single project's compilation, its
///     subject is cut down to the parts that project can see, so an empty part of a union, an empty cell of a
///     family and a name of a <c>Named</c> list that project lacks are absences rather than authoring errors —
///     and a subject nothing survives is a skip carrying the one-project reason.
/// </summary>
/// <remarks>
///     The guards either side are the point. Under no narrowing, and under the solution-filter kind, the
///     same spec over the same codebase reaches the verdict it always did: nothing is cut, and an empty
///     part still empties the whole subject. The cut belongs to the checker, so the evaluator's gates keep
///     reading an empty part as a spec defect for every universe but this one.
/// </remarks>
public sealed class OneProjectNarrowingTests
{
    // One controller reaching the data layer — a real violation any rule here can ask for — beside an
    // interface, so a row can prove an adjective survived the cut by which of the two it reds on. Both
    // namespaces exist, so an empty part is always the selection's own doing.
    private const string WebAndData = """
                                      namespace App.Web
                                      {
                                          public interface IHome {}
                                          public class HomeController { public App.Data.Db Load() => new App.Data.Db(); }
                                      }
                                      namespace App.Data { public class Db {} }
                                      """;

    // Two cells with an edge each way between them, and nothing else: a third, empty cell is what the cut
    // removes, and what is left has to be judged — for the cross-cell law and for the partition alike.
    private const string Plugins = """
                                   namespace Plugin.A { public class Alpha { public Plugin.B.Beta Cross; } }
                                   namespace Plugin.B { public class Beta { public Plugin.A.Alpha Back; } }
                                   """;

    // The one-project skip reason, pinned whole: this is where it is minted, so a row that read it off the
    // value under test would pin nothing.
    private const string OneProjectSkipReason =
        "This run checked one project's compilation, and this rule's subject matched no type in it, so the "
        + "rule was not measured here and this is not a clean result for it; a check over the whole solution "
        + "answers it.";

    // Its inert-target twin, pinned whole for the same reason.
    private const string OneProjectInertTargetHint =
        "This run checked one project's compilation, so this rule's target may match types in a project it "
        + "did not see; check the rule over the whole solution before deciding whether to keep it.";

    private static readonly CodebaseModel WebAndDataModel = CompilationFactory.Extract(WebAndData);

    private static readonly CodebaseModel PluginsModel = CompilationFactory.Extract(Plugins);

    [Fact]
    public void Check_AUnionWithAnEmptyPartUnderOneProject_RunsTheVerbOverTheRest()
    {
        // The commonest shape on a one-project run: most of a union's layers live in projects this view
        // never saw, and the one that is here has a finding to report.
        RuleResult result = Checker.Run(
                WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, UnionWithAnEmptyPart)
            .Single();

        // Exactly one violation, so the empty part raised none of its own.
        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
    }

    [Fact]
    public void Check_TheSameUnionWithNoNarrowing_StillRedsOnTheEmptyPart()
    {
        // The invariant the cut is measured against: over the whole solution a union operand that matched
        // nothing is a spec defect, named in its own right (GRAMMAR §4.1), and the verb never runs.
        // The cast names which of the harness's two three-argument overloads this row wants: over a
        // CodebaseModel a bare null is ambiguous between the diff context and the narrowing.
        RuleResult result = Checker.Run(
                WebAndDataModel, BaselineIndex.Empty, (NarrowedUniverse?)null, UnionWithAnEmptyPart)
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage("types in `App.Nowhere.*`"));
    }

    [Fact]
    public void Check_TheSameUnionUnderAFilterNarrowing_SkipsAsItAlwaysDid()
    {
        // The other guard: a solution filter cuts nothing. The empty operand still empties the whole
        // subject, the verb still never runs, and the rule reaches the filter's skip rather than the
        // finding the one-project kind reports over the very same codebase.
        RuleResult result = Checker.Run(
                WebAndDataModel, BaselineIndex.Empty, Checker.FilterNarrowing, UnionWithAnEmptyPart)
            .Single();

        result.ShouldHaveSkipped(Checker.FilterSkipReason);
    }

    [Fact]
    public void Check_AUnionWhoseEveryPartIsEmpty_SkipsWithTheOneProjectReason()
    {
        // Nothing survives the cut, so the subject goes to the evaluator whole and reaches the empty-subject
        // verdict an unnarrowed run would — which the checker turns into the skip, not into a red.
        RuleResult result = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.AnyOf(arch.Namespace("App.Nowhere.*"), arch.Namespace("App.Elsewhere.*"))
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(OneProjectSkipReason);
    }

    [Fact]
    public void Check_ASingleNounSubjectEmptyUnderOneProject_SkipsWithTheOneProjectReason()
    {
        // No cut is involved at all — a plain noun has no parts — so this is the kind behaving as a
        // narrowing, exactly as the filter kind does.
        RuleResult result = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Namespace("App.Nowhere.*").MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveSkipped(OneProjectSkipReason);
    }

    [Fact]
    public void Cut_ANestedUnion_IsKeptWholeRatherThanOpened()
    {
        // A nested union is one top-level part. Opening it would change which parts the evaluator's
        // per-operand gate sees and would drop the adjectives the inner union carries, so the cut keeps or
        // drops it entire — here keeping it while the empty sibling goes.
        var arch = new Arch();
        var selections = new SelectionEvaluator(WebAndDataModel);
        Selection inner = arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
            .OfKind(TypeKind.Class);
        Selection subject = arch.AnyOf(inner, arch.Namespace("App.Elsewhere.*"));

        Selection cut = OneProjectSubject.Cut(selections, subject);

        cut.ShouldBeOfType<UnionSelection>()
            .Parts.ShouldHaveSingleItem()
            .ShouldBeSameAs(inner);
    }

    [Fact]
    public void Check_ANestedUnionsEmptyInnerPart_IsReportedNowhere()
    {
        // What the evaluator does with the kept nested union: its per-operand gate reads top-level parts
        // only, so an empty part one level down raises nothing — as it raises nothing on an unnarrowed run.
        // The rule is measured over what the inner union does hold.
        RuleResult result = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.AnyOf(
                            arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
                                .OfKind(TypeKind.Class),
                            arch.Namespace("App.Elsewhere.*"))
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
    }

    [Fact]
    public void Check_AUnionCarryingAnAdjective_StillFiltersByItAfterTheCut()
    {
        // The adjective is the union's own, not its parts' (GRAMMAR §5.1), so a rebuilt union that lost it
        // would range one type wider. The interface in the bed is what says whether it did.
        RuleResult result = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("shape/x")
                    .Enforce(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
                        .OfKind(TypeKind.Class)
                        .MustHavePrefix("ZZZ"))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithSubjects(["App.Web.HomeController"]);
    }

    [Fact]
    public void Check_AFamilyWithAnEmptyCell_RunsTheVerbOverTheCellsThatRemain()
    {
        RuleResult result = Checker.Run(PluginsModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("plugins/independent")
                    .Enforce(arch.Each(
                            arch.Layer("A", "Plugin.A.*"), arch.Layer("B", "Plugin.B.*"),
                            arch.Layer("Ghost", "Nowhere.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference,
        [
            "Plugin.A.Alpha -> Plugin.B.Beta",
            "Plugin.B.Beta -> Plugin.A.Alpha"
        ]);
        result.Violators(ViolationKind.EmptySubject, violation => violation.Detail!)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Check_AFamilyWhoseRemainingCellsOverlap_StillRefusesOnThePartition()
    {
        // The cell checks run over the cut family, not the declared one: with the empty cell gone, the two
        // that remain are held to the partition discipline exactly as a two-cell family always was.
        RuleResult result = Checker.Run(PluginsModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("plugins/overlapping")
                    .Enforce(arch.Each(
                            arch.Layer("Wide", "Plugin.*"), arch.Layer("A", "Plugin.A.*"),
                            arch.Layer("Ghost", "Nowhere.*"))
                        .MustNotReferenceEachOther())
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithDetail(ViolationKind.RuleError,
            "Type `Plugin.A.Alpha` sits in both the Wide and A cells of the family; "
            + "a family's cells must not overlap. Leave the type out of one of the two layer definitions.");
    }

    [Fact]
    public void Check_AnInertTargetUnderOneProject_CarriesTheOneProjectCure()
    {
        // The subject loaded, so the rule is measured and still warns inert — which is true of this run.
        // The cure sends the reader to the whole solution rather than advising on how the target was spelled.
        RuleResult result = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("inert/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Ghost.*")))
                    .Because("b"))
            .Single();

        result.Warnings.ShouldHaveSingleItem()
            .Hint.ShouldBe(OneProjectInertTargetHint);
    }

    [Fact]
    public void Cut_DroppingNothingOrEverything_ReturnsTheSubjectItself()
    {
        // Rebuilding only where something went is what keeps an unnarrowed shape reaching the very node the
        // spec built; and a subject nothing survives has to arrive at the evaluator whole, so that its
        // empty-subject verdict is the one an unnarrowed run would reach.
        var arch = new Arch();
        var selections = new SelectionEvaluator(WebAndDataModel);
        Selection whole = arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Data.*"));
        Selection nothing = arch.AnyOf(arch.Namespace("App.Nowhere.*"), arch.Namespace("App.Elsewhere.*"));

        OneProjectSubject.Cut(selections, whole)
            .ShouldBeSameAs(whole);
        OneProjectSubject.Cut(selections, nothing)
            .ShouldBeSameAs(nothing);
    }

    [Fact]
    public void Check_AfterARunThatCutTheSubject_TheRulesOwnSubjectIsTheSameNode()
    {
        // A parsed model is shared by every compilation of a long-lived compiler process, so a cut that
        // wrote into it would leave the next project checking a subject the previous one narrowed.
        ArchitectureModel model = Checker.Model(UnionWithAnEmptyPart);
        Selection subject = model.Rules.Single()
            .Constraint!.Subject!;
        IReadOnlyList<Selection> parts = subject.ShouldBeOfType<UnionSelection>()
            .Parts;

        RuleResult result = ArchChecker
            .Check(model.Rules, WebAndDataModel, BaselineIndex.Empty, null, NarrowedUniverse.OneProject, null)
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
        Selection after = model.Rules.Single()
            .Constraint!.Subject!;
        after.ShouldBeSameAs(subject);
        after.ShouldBeOfType<UnionSelection>()
            .Parts.ShouldBeSameAs(parts);
        parts.Count.ShouldBe(2);
    }

    [Fact]
    public void Check_ANamedSubjectWithANameThisProjectLacks_RunsTheVerbOverTheNamesItHolds()
    {
        // The evaluator fails a dead name of a subject list as a union's empty operand (GRAMMAR §4.1), and a
        // name another project declares is dead in this one. Without the cut, the build would report nothing
        // for the controller this project does hold; the single edge says the name raised nothing of its own.
        RuleResult result = Checker.Run(
                WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, NamedListWithAnAbsentName)
            .Single();

        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
    }

    [Fact]
    public void Check_TheSameNamedSubjectWithNoNarrowing_RedsOnTheAbsentName()
    {
        RuleResult result = Checker.Run(
                WebAndDataModel, BaselineIndex.Empty, (NarrowedUniverse?)null, NamedListWithAnAbsentName)
            .Single();

        result.ShouldHaveFailedWithDetail(
            ViolationKind.EmptySubject, ConstraintEvaluator.EmptyOperandMessage("types named `Elsewhere`"));
    }

    [Fact]
    public void Check_AnAbsentNameInAUnionPartOrTheUnionsOwnList_IsCutToo()
    {
        // Both lists the evaluator reads on a union subject: a kept part's, and the union's own.
        RuleResult inPart = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.AnyOf(arch.Types.Named("HomeController", "Elsewhere"), arch.Namespace("App.Nowhere.*"))
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();
        RuleResult onUnion = Checker.Run(WebAndDataModel, BaselineIndex.Empty, NarrowedUniverse.OneProject, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
                        .Named("HomeController", "Elsewhere")
                        .MustNotReference(arch.Namespace("App.Data.*")))
                    .Because("b"))
            .Single();

        inPart.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
        onUnion.ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "App.Web.HomeController", "App.Data.Db");
    }

    [Fact]
    public void Cut_ANamedListWhoseNamesAllMatched_ReturnsTheSubjectItself()
    {
        var arch = new Arch();
        var selections = new SelectionEvaluator(WebAndDataModel);
        Selection named = arch.Types.Named("HomeController", "Db");

        OneProjectSubject.Cut(selections, named)
            .ShouldBeSameAs(named);
    }

    // A list naming the controller this bed holds and a type it does not.
    private static void NamedListWithAnAbsentName(Arch arch)
    {
        arch.Rule("layering/x")
            .Enforce(arch.Types.Named("HomeController", "Elsewhere").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b");
    }

    // "The same union" the guard rows name: one part holding the controller, and one no type occupies.
    private static void UnionWithAnEmptyPart(Arch arch)
    {
        arch.Rule("layering/x")
            .Enforce(arch.AnyOf(arch.Namespace("App.Web.*"), arch.Namespace("App.Nowhere.*"))
                .MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b");
    }
}
