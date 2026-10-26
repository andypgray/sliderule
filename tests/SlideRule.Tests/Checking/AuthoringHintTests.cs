using Shouldly;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The cure that rides an empty subject, an inert target, a dead target pattern, a dead name in a
///     <c>Named</c> list, a dead allow-list piece and a dead membership or <c>among:</c> part: which advice each
///     selection shape earns, that the signals ask different things of the same shape, that a partial model
///     replaces every one of those cures rather than trailing them, and that a solution filter's narrowing
///     replaces the cures on a target or an allow-list.
/// </summary>
public sealed class AuthoringHintTests
{
    private const string NamespaceFragment = "`Legacy*` never crosses a dot";

    private const string ProjectFragment = "named by its csproj file name";

    private const string TypeFragment = "typeof anchor reaches only a type this solution declares";

    private const string NarrowingFragment = "term narrows, so the set is no wider than the last one applied";

    private const string NamedFragment = "is the whole simple name, matched exactly";

    private const string NamedClause =
        "A name in a `Named` list is the whole simple name, matched exactly, without its namespace, generic "
        + "arity or containing type";

    private const string SubjectAction = "check the selection against what the solution declares";

    private const string InertAction = "passes forever, so decide before keeping it";

    private const string InertOperandAction = "a pattern that matches nothing bans nothing, so decide before keeping it";

    // The action clauses of the per-piece cures, spelled from the verb on, so one fragment holds for the
    // pattern tail and the name tail alike.
    private const string HoldsNoTypeAction =
        "matches nothing holds no type, so each subject it was meant to hold is reported as a violation";

    private const string HoldsNoCounterpartAction =
        "matches nothing holds no counterpart, so a subject whose counterpart it was meant to hold reads as having "
        + "none, and one it was meant to make ambiguous passes";

    private const string NameCure = NamedClause + "; a name that matches nothing bans nothing, so decide before keeping it.";

    [Fact]
    public void TypesNarrowedByOneInNamespace_EarnsTheGlobSemanticsToo()
    {
        // The namespace noun's region spelled the other way round: RegionOf reads it off arch.Types plus a
        // single InNamespace exactly as it reads it off the noun.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.InNamespace("App.Nowhere.*"), null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void GlobLayer_EarnsTheGlobSemantics()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Domain", "App.Nowhere.*"), null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void LayerDefinedByASelection_IsCuredAsWhateverDefinesIt()
    {
        // A layer is transparent to its definition, so a layer that IS a project earns the project's cure
        // rather than glob semantics it has no glob to apply to.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Core", arch.Project("App.Core")), null)
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void ProjectNoun_EarnsTheProjectNamingRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Project("App.Nowhere"), null)
            .ShouldContain(ProjectFragment);
    }

    [Fact]
    public void TypeofNoun_EarnsTheDeclaredOnlyRule()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Type(typeof(AuthoringHintTests)), null)
            .ShouldContain(TypeFragment);
    }

    [Fact]
    public void ANarrowedTypesSubject_FallsBackToTheNarrowingAdvice()
    {
        // Nothing here globs a namespace, so glob semantics would be a false sentence: the cure is the one
        // true of every selection.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.WithSuffix("Service"), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ARegisteredSubject_FallsBackToTheNarrowingAdvice()
    {
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Registered(Lifetime.Singleton), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void AUnionSubject_FallsBackToTheNarrowingAdvice()
    {
        // A union answers no noun, and its parts may disagree about shape — but each empty part is cured on
        // its own terms anyway, so the union's own sentence never has to choose between them.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.AnyOf(arch.Namespace("App.A.*"), arch.Project("App.B")), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ANamedList_EarnsTheExactNameRuleFromTheSubjectAndBothInertTargetHints()
    {
        // A list that came up empty failed on its names, whichever signal reports it, so ForSubject,
        // ForInertTarget and a target's ForDeadOperand all say what an exact name is; only the action clause differs.
        var arch = new Arch();
        Selection named = arch.Types.Named("Orders.Line", "Acme.Widget");

        AuthoringHints.ForSubject(named, null)
            .ShouldBe(NamedClause + "; check the selection against what the solution declares.");
        AuthoringHints.ForInertTarget([named], null, null)
            .ShouldBe(NamedClause + "; a rule whose target matches nothing passes forever, so decide before keeping it.");
        AuthoringHints.ForDeadOperand(DeadOperandRole.Target, named, null, null)
            .ShouldBe(NamedClause + "; a name that matches nothing bans nothing, so decide before keeping it.");
    }

    [Fact]
    public void AnUnderList_EarnsThePathGlobRule_WithThePatternTails()
    {
        // A glob list that came up empty failed on a path, so every signal says what a path glob is; a glob is a
        // pattern, so a dead one beside live ones takes the pattern tail rather than the name tail.
        var arch = new Arch();
        Selection under = arch.Namespace("App.*").Under("src/Widgets", "src/Console");
        const string pathClause =
            "A path glob is relative to the solution directory with `/` between segments, and reaches a type when any "
            + "file declaring it is under the glob";

        AuthoringHints.ForSubject(under, null)
            .ShouldBe(pathClause + "; check the selection against what the solution declares.");
        AuthoringHints.ForDeadOperand(DeadOperandRole.Target, under, null, null)
            .ShouldBe(pathClause + "; a pattern that matches nothing bans nothing, so decide before keeping it.");
    }

    [Fact]
    public void ANamedListNarrowedFromANamespace_EarnsTheExactNameRuleOverTheGlob()
    {
        // The region is not what failed: a Named list inside one matched nothing because of a name, so glob
        // semantics would send the reader to the one part of the selection that was right.
        var arch = new Arch();
        string[] hints =
        [
            AuthoringHints.ForSubject(arch.Namespace("App.*").Named("Ghost"), null),
            AuthoringHints.ForSubject(arch.Types.InNamespace("App.*").Named("Ghost"), null)
        ];

        hints.ShouldAllBe(hint => hint.Contains(NamedFragment) && !hint.Contains(NamespaceFragment));
    }

    [Fact]
    public void ANamedListInsideAnExceptPayload_IsNotReadAsTheShape()
    {
        // An exemption's names exempt; the selection that came up empty is the one the exemption narrows.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Types.WithSuffix("Service").Except(arch.Types.Named("Ghost")), null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ALayerDefinedByANamedList_IsCuredAsAName()
    {
        // The layer arm reaches its definition, and the definition is a list.
        var arch = new Arch();

        AuthoringHints.ForSubject(arch.Layer("Core", arch.Types.Named("Ghost")), null)
            .ShouldContain(NamedFragment);
    }

    [Fact]
    public void AWhollyDeadNamedTarget_CarriesTheNameCureThroughTheChecker()
    {
        // The whole-target grain on a list none of whose names matched: the cure says what a name is, and the
        // action stays the whole-target one, because this rule really does pass forever.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/named")
                    .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Types.Named("Ghost", "Phantom")))
                    .Because("b"))
            .Single();

        result.Warnings.ShouldHaveSingleItem()
            .Hint.ShouldBe(NamedClause + "; a rule whose target matches nothing passes forever, so decide before keeping it.");
    }

    [Fact]
    public void TheSameShape_AsksDifferentThingsOfASubjectAndOfATarget()
    {
        // One vocabulary, two signals: the semantics are shared and what to do about them is not. A subject
        // that matched nothing has already failed; an inert target has not, which is the whole difference.
        var arch = new Arch();
        Selection pattern = arch.Namespace("App.Nowhere.*");

        string subject = AuthoringHints.ForSubject(pattern, null);
        string inert = AuthoringHints.ForInertTarget([pattern], null, null);

        subject.ShouldSatisfyAllConditions(
            () => subject.ShouldContain(NamespaceFragment),
            () => subject.ShouldContain(SubjectAction),
            () => subject.ShouldNotContain(InertAction));

        inert.ShouldSatisfyAllConditions(
            () => inert.ShouldContain(NamespaceFragment),
            () => inert.ShouldContain(InertAction),
            () => inert.ShouldNotContain(SubjectAction));
    }

    [Fact]
    public void AnInertTarget_IsCuredByThePatternOperandThatRaisedTheWarning()
    {
        // The gate fires on the first PATTERN operand, so the sentence has to describe that one rather than
        // a bare typeof sibling, whose absence is the win condition and never warns at all.
        var arch = new Arch();
        Selection[] operands = [arch.Type(typeof(AuthoringHintTests)), arch.Namespace("App.Ghost.*")];

        AuthoringHints.ForInertTarget(operands, null, null)
            .ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void AnInertTargetWithNoPatternOperand_FallsBackToTheNarrowingAdvice()
    {
        // Unreachable through the checker, whose gate warns only when some operand is a pattern — but the
        // floor has to hold anyway, because it is what makes the chooser total. Advice true of every
        // selection is the only answer that cannot be false when there is no shape to read.
        AuthoringHints.ForInertTarget([], null, null)
            .ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void ADeadAllowListPiece_TakesTheAllowListTailOfItsOwnShape()
    {
        // A dead allowed piece errs the other way, so its cure says where the reds come from. Like the ban's, it
        // calls the piece a pattern or a name by the piece's own shape, and a one-name list is a name.
        var arch = new Arch();

        AuthoringHints.ForDeadOperand(DeadOperandRole.AllowList, arch.Namespace("App.Ghost.*"), null, null)
            .ShouldBe(
                "A trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses a "
                + "dot; a pattern that matches nothing allows nothing, so what it was meant to allow is reported as "
                + "a violation.");
        AuthoringHints.ForDeadOperand(DeadOperandRole.AllowList, arch.Types.Named("Ghost"), null, null)
            .ShouldBe(
                NamedClause + "; a name that matches nothing allows nothing, so what it was meant to allow is "
                            + "reported as a violation.");
    }

    [Fact]
    public void ADeadMembershipOrAmongPiece_TakesItsOwnTailOfItsOwnShape()
    {
        // A membership errs as an allow-list does, but what reds is the subject itself; an among part errs both
        // ways, and its tail says so. Each calls the piece a pattern or a name by the piece's own shape.
        var arch = new Arch();

        AuthoringHints.ForDeadOperand(DeadOperandRole.Membership, arch.Namespace("App.Ghost.*"), null, null)
            .ShouldBe(
                "A trailing `.*` covers the namespace itself and everything under it, and `Legacy*` never crosses a "
                + "dot; a pattern that matches nothing holds no type, so each subject it was meant to hold is "
                + "reported as a violation.");
        AuthoringHints.ForDeadOperand(DeadOperandRole.Membership, arch.Types.Named("Ghost"), null, null)
            .ShouldBe(NamedClause + "; a name that " + HoldsNoTypeAction + ".");
        AuthoringHints.ForDeadOperand(DeadOperandRole.Among, arch.Namespace("App.Ghost.*"), null, null)
            .ShouldEndWith("; a pattern that " + HoldsNoCounterpartAction + ".");
        AuthoringHints.ForDeadOperand(DeadOperandRole.Among, arch.Types.Named("Ghost"), null, null)
            .ShouldBe(NamedClause + "; a name that " + HoldsNoCounterpartAction + ".");
    }

    [Fact]
    public void EveryHint_ReadsAsOneSentenceInTheHouseVoice()
    {
        // The registers these share: one line, a semicolon parting diagnosis from cure, a full stop, and
        // never the second person. Cheap insurance on text only ever read at a moment of failure.
        IEnumerable<string> subjectCures = EveryShape()
            .Select(shape => AuthoringHints.ForSubject(shape.Pattern, null));
        IEnumerable<string> inertCures = EveryShape()
            .SelectMany(shape => EveryInertCure().Select(cure => cure.HintFor(shape.Pattern, null, null)));
        IEnumerable<string> subjectSideCures = EveryShape()
            .SelectMany(shape => EverySubjectSideCure().Select(cure => cure.HintFor(shape.Pattern, null)));
        string[] hints =
        [
            .. subjectCures,
            .. inertCures,
            .. subjectSideCures,
            AuthoringHints.ForMemberSubject(null),
            AuthoringHints.ForProjectSubject(null)
        ];

        hints.ShouldAllBe(hint => !hint.Contains('\n'));
        hints.ShouldAllBe(hint => hint.EndsWith('.'));
        hints.ShouldAllBe(hint => char.IsUpper(hint[0]));
        hints.ShouldAllBe(hint => hint.Contains(';'));
        hints.ShouldAllBe(hint => !hint.Contains(" you ") && !hint.Contains(" your "));
    }

    [Fact]
    public void AnEmptySubjectOfAnotherShape_CarriesThatShapesCure()
    {
        // The wiring proves the point of the feature: this rule fails on the same kind as a namespace subject
        // and gets a different cure, because the cure is read off the selection rather than off the kind.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("empty/y")
                    .Enforce(arch.Types.WithSuffix("NoSuchSuffix").MustBeSealed())
                    .Because("b"))
            .Single();

        string? hint = result.Violations.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(NarrowingFragment);
    }

    [Fact]
    public void AnInertTarget_CarriesItsCureThroughTheChecker()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, InertTargetRule)
            .Single();

        string? hint = result.Warnings.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(InertAction);
    }

    [Fact]
    public void ADeadTargetPatternBesideALiveOne_CarriesItsOwnCureThroughTheChecker()
    {
        // The whole-target cure would be false here: the live pattern still decides the rule, so it does not
        // pass forever. What the dead one costs is its own part of the ban, and the cure is read off that
        // pattern rather than off the first one listed.
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inert/operand")
                    .Enforce(arch.Namespace("App.Web.*")
                        .MustNotReference(arch.Namespace("App.Domain.*"), arch.Project("App.Ghost")))
                    .Because("b"))
            .Single();

        string? hint = result.Warnings.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldSatisfyAllConditions(
            () => hint.ShouldContain(ProjectFragment),
            () => hint.ShouldContain(InertOperandAction),
            () => hint.ShouldNotContain("passes forever"));
    }

    [Fact]
    public void ADeadNameInANamedList_CarriesTheNameCureThroughTheChecker()
    {
        // The operand's own shape advice would describe the operand, whose other names still match. What
        // failed is one exact name, so the cure says what an exact name is.
        RuleResult result = Checker.Run(Sources.LayeredModel, DeadNameRule)
            .Single();

        string? hint = result.Warnings.Single()
            .Hint;

        hint.ShouldBe(NameCure);
    }

    [Fact]
    public void ADeadNameInANamedList_CarriesThePartialModelAndTheNarrowingThroughTheChecker()
    {
        // The name cure takes the same two replacements as the other inert cures, and the evaluator is what hands
        // it the run's facts: a call that passed neither would keep every row above green.
        IncompleteModel partial = Checker.PartialModel;

        RuleResult broken = Checker.Run(Sources.LayeredModel, DeadNameRule, incompleteModel: partial)
            .Single();
        RuleResult filtered = Checker.Run(Sources.LayeredModel, DeadNameRule, narrowing: Checker.FilterNarrowing)
            .Single();

        broken.Warnings.ShouldHaveSingleItem()
            .Hint.ShouldBe(Checker.PartialModelCure);
        filtered.Warnings.ShouldHaveSingleItem()
            .Hint.ShouldBe(Checker.FilterInertCure);
    }

    [Fact]
    public void AnInertTargetUnderANarrowing_CarriesTheNarrowingThroughTheChecker()
    {
        // AnInertTarget_CarriesItsCureThroughTheChecker over a run a solution filter narrowed. The subject loaded,
        // so the rule is measured and still warns inert, which is true of the run; the cure names the filter where
        // that row advises on how the target was spelled.
        RuleResult result = Checker.Run(Sources.LayeredModel, InertTargetRule, narrowing: Checker.FilterNarrowing)
            .Single();

        string? hint = result.Warnings.Single()
            .Hint;

        hint.ShouldBe(Checker.FilterInertCure);
    }

    [Fact]
    public void APartialModel_ReplacesEveryShapeCureRatherThanTrailingIt()
    {
        // Every subject, target, allow-list, membership and among signal: advice to check a selection against what
        // the solution declares is about the wrong thing when a project never loaded, so it is replaced rather than
        // appended — and a sentence carrying both would be two cures for one diagnosis, past the single semicolon
        // these clauses are built to keep.
        //
        // Every shape the chooser reads, rather than one of them twice: the replacement is unconditional,
        // and a cure narrowed to namespace nouns would go on reading green against a glob row while a
        // project- or typeof-shaped empty subject advised a spec fix for a load failure. That is the
        // half-fix the member and project row below exists to prevent, one stratum over.
        IncompleteModel partial = Checker.PartialModel;

        foreach ((string shape, Selection pattern) in EveryShape())
        {
            AuthoringHints.ForSubject(pattern, partial)
                .ShouldBe(Checker.PartialModelCure, shape);

            foreach ((string cure, InertCure hintFor) in EveryInertCure())
                hintFor(pattern, partial, null)
                    .ShouldBe(Checker.PartialModelCure, $"{cure} on {shape}");

            foreach ((string cure, SubjectSideCure hintFor) in EverySubjectSideCure())
                hintFor(pattern, partial)
                    .ShouldBe(Checker.PartialModelCure, $"{cure} on {shape}");
        }
    }

    [Fact]
    public void APartialModel_ReachesTheMemberAndProjectCuresToo()
    {
        // The two fixed clauses read off no selection shape, so nothing about them would have made anyone
        // route them through the fact — and a failed project empties a member or a project subject exactly
        // as it empties a type one. Leaving these two alone would be the half-fix this row exists to prevent.
        IncompleteModel partial = Checker.PartialModel;

        AuthoringHints.ForMemberSubject(partial)
            .ShouldBe(Checker.PartialModelCure);
        AuthoringHints.ForProjectSubject(partial)
            .ShouldBe(Checker.PartialModelCure);
    }

    [Fact]
    public void APartialModel_ReachesBothSignalsThroughTheChecker()
    {
        // The wiring, end to end on one model: a subject that matched nothing fails carrying the fact, and a
        // target that matched nothing warns carrying the same fact. Without it, a rule whose edge was never
        // extracted would report itself inert with a cure about globs.
        IncompleteModel partial = Checker.PartialModel;

        RuleResult empty = Checker.Run(Sources.LayeredModel, EmptySubjectRule, incompleteModel: partial)
            .Single();
        RuleResult inert = Checker.Run(Sources.LayeredModel, InertTargetRule, incompleteModel: partial)
            .Single();

        empty.Violations.ShouldHaveSingleItem()
            .Hint.ShouldBe(Checker.PartialModelCure);
        inert.Warnings.ShouldHaveSingleItem()
            .Hint.ShouldBe(Checker.PartialModelCure);
    }

    [Fact]
    public void AWholeModel_KeepsTheShapeCure()
    {
        // The control on the whole feature: a run whose projects all loaded gets the shape cure rather than the
        // partial model's reason.
        RuleResult result = Checker.Run(Sources.LayeredModel, EmptySubjectRule)
            .Single();

        string? hint = result.Violations.Single()
            .Hint;

        hint.ShouldNotBeNull();
        hint.ShouldContain(NamespaceFragment);
    }

    [Fact]
    public void ANarrowing_ReplacesEveryInertShapeCure()
    {
        // A solution filter's narrowing takes over every cure on a target or an allow-list, for the partial model's
        // reason: a project the filter left out may declare what the selection names, so advice about how it was
        // spelled is advice about the wrong thing. Every shape the chooser reads, for the same reason as there.
        foreach ((string shape, Selection pattern) in EveryShape())
        foreach ((string cure, InertCure hintFor) in EveryInertCure())
            hintFor(pattern, null, Checker.FilterNarrowing)
                .ShouldBe(Checker.FilterInertCure, $"{cure} on {shape}");
    }

    [Fact]
    public void APartialModel_OutranksANarrowing()
    {
        // A filtered run can also be a broken one. The narrowing's cure sends the reader to re-check against
        // the whole solution, which loads the same broken model, so the load's repair has to come first —
        // for every cure a narrowing replaces, whatever the shape of what came up empty.
        IncompleteModel partial = Checker.PartialModel;

        foreach ((string shape, Selection pattern) in EveryShape())
        foreach ((string cure, InertCure hintFor) in EveryInertCure())
            hintFor(pattern, partial, Checker.FilterNarrowing)
                .ShouldBe(Checker.PartialModelCure, $"{cure} on {shape}");
    }

    // A namespace subject no type occupies, so the rule fails empty and its cure is read off the selection.
    private static void EmptySubjectRule(Arch arch)
    {
        arch.Rule("empty/x")
            .Enforce(arch.Namespace("Nope.Nowhere.*").MustHaveSuffix("X"))
            .Because("b");
    }

    // A ban whose one target pattern matches nothing, so the rule passes inert and warns.
    private static void InertTargetRule(Arch arch)
    {
        arch.Rule("inert/x")
            .Enforce(arch.Namespace("App.Domain.*").MustNotReference(arch.Namespace("App.Ghost.*")))
            .Because("b");
    }

    // A ban whose Named target holds one live name and one dead one.
    private static void DeadNameRule(Arch arch)
    {
        arch.Rule("inert/name")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Types.Named("Service", "Ghost")))
            .Because("b");
    }

    // Every shape the cure chooser reads, so a row over this table makes its claim of every shape rather than
    // of one. One table for every row that walks it: a new shape has to reach them together, and a shape added
    // to one alone would leave the others reading green.
    private static (string Shape, Selection Pattern)[] EveryShape()
    {
        var arch = new Arch();
        return
        [
            ("a namespace glob", arch.Namespace("App.Nowhere.*")),
            ("a project name", arch.Project("App.Nowhere")),
            ("a typeof anchor", arch.Type(typeof(AuthoringHintTests))),
            ("a narrowed types selection", arch.Types.WithSuffix("Service")),
            ("a Named list", arch.Types.Named("Nowhere"))
        ];
    }

    private delegate string InertCure(Selection piece, IncompleteModel? model, NarrowedUniverse? narrowing);

    // The cures on a target or an allow-list, the entry points a narrowing reaches. Walked rather than listed
    // per row, for EveryShape's reason: an entry point left off a hand-kept list is one whose replacements and voice
    // nothing checks.
    private static (string Cure, InertCure HintFor)[] EveryInertCure()
    {
        return
        [
            (nameof(AuthoringHints.ForInertTarget),
                (piece, model, narrowing) => AuthoringHints.ForInertTarget([piece], model, narrowing)),
            DeadOperandCure(DeadOperandRole.Target),
            DeadOperandCure(DeadOperandRole.AllowList)
        ];
    }

    private delegate string SubjectSideCure(Selection piece, IncompleteModel? model);

    // The two cures on a membership or an among part. Kept apart from EveryInertCure because a narrowed run
    // raises neither warning, so they take no narrowing to replace their advice with.
    private static (string Cure, SubjectSideCure HintFor)[] EverySubjectSideCure()
    {
        return
        [
            ($"{nameof(AuthoringHints.ForDeadOperand)}({DeadOperandRole.Membership})",
                (piece, model) => AuthoringHints.ForDeadOperand(DeadOperandRole.Membership, piece, model, null)),
            ($"{nameof(AuthoringHints.ForDeadOperand)}({DeadOperandRole.Among})",
                (piece, model) => AuthoringHints.ForDeadOperand(DeadOperandRole.Among, piece, model, null))
        ];
    }

    private static (string Cure, InertCure HintFor) DeadOperandCure(DeadOperandRole role)
    {
        return ($"{nameof(AuthoringHints.ForDeadOperand)}({role})",
            (piece, model, narrowing) => AuthoringHints.ForDeadOperand(role, piece, model, narrowing));
    }
}
