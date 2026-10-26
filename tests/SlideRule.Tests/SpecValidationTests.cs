using Shouldly;
using SlideRule.Hosting;
using SlideRule.Validation;
using Xunit;
using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Tests;

/// <summary>
///     The spec-build validation catalog (GRAMMAR §8): one failing spec per code, pinned by code
///     plus rule ID with one representative message, and one all-errors spec proving every problem
///     is reported in a single pass.
/// </summary>
public class SpecValidationTests
{
    private static SpecValidationException ShouldFailToBuild(params IArchitectureSpec[] specs)
    {
        return Should.Throw<SpecValidationException>(() => ArchModelBuilder.Build(specs));
    }

    // The catalog's data arms: one failing spec per code, where the reported (code, rule ID) pair is the
    // whole claim. The facts below are the arms that pin something more — a message, a null rule ID, or
    // several errors from one pass.
    [Theory]
    [InlineData(typeof(DanglingRuleSpec), nameof(Code.MissingPosture), "area/dangling")]
    [InlineData(typeof(MissingBecauseScopeSpec), nameof(Code.MissingBecause), "legacy/billing")]
    [InlineData(typeof(MissingDragonsSpec), nameof(Code.MissingDragons), "legacy/billing")]
    [InlineData(typeof(BlankDescriptionSpec), nameof(Code.BlankProse), "area/rule")]
    [InlineData(typeof(MultiLineBecauseSpec), nameof(Code.MultiLineProse), "area/rule")]
    [InlineData(typeof(MalformedIdSpec), nameof(Code.MalformedId), "Bad_Id")]
    [InlineData(typeof(ForeignSelectionSpec), nameof(Code.ForeignSelection), "area/rule")]
    // A scope's sanctioned surface rides the same walks its quarantined interior does, so the boundary's
    // foreign-Arch and blank-pattern reports need no arm of their own — they are the scope's, by its ID.
    [InlineData(typeof(ForeignBoundarySpec), nameof(Code.ForeignSelection), "legacy/billing")]
    [InlineData(typeof(BlankBoundaryPatternSpec), nameof(Code.BlankPattern), "legacy/billing")]
    // The extended prose walk reaches a member Where description and a member Must description
    // (GRAMMAR §8 item 5, §4.6).
    [InlineData(typeof(BlankMemberWhereSpec), nameof(Code.BlankProse), "area/rule")]
    [InlineData(typeof(BlankMemberMustSpec), nameof(Code.BlankProse), "area/rule")]
    // An expression-minted member is Owner-stamped like the typeof form, so the foreign-Arch check
    // (which precedes the poison short-circuit) catches it (GRAMMAR §8 item 13).
    [InlineData(typeof(ForeignExpressionMemberSpec), nameof(Code.ForeignMember), "area/rule")]
    // SpecValidator blank-pattern arms (GRAMMAR §8 item 15). Each arm — the shape/naming verb's own glob,
    // a subject-side adjective, and their member analogs — routes through CheckPatterns and emits the one
    // shared Code.BlankPattern; a distinct spec per arm walks each ConstraintPatterns / SelectionPatterns /
    // MemberAdjectivePatterns code path.
    [InlineData(typeof(BlankTypeNameMatchingVerbSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankTypeNameMatchingAdjectiveSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankTypePrefixAdjectiveSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankMemberNameMatchingAdjectiveSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankMemberPrefixAdjectiveSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankMemberNameMatchingVerbSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankMemberPrefixVerbSpec), nameof(Code.BlankPattern), "area/rule")]
    // MustNotConstruct's foreign-target reach. The verb overrides Operands (the dependency-verb walk hook,
    // like the reference verbs), so the §8 item 10 foreign-selection walk (Selections →
    // CheckForeign) reaches a construct target minted on another Arch without an arm of its own.
    [InlineData(typeof(ForeignConstructTargetSpec), nameof(Code.ForeignSelection), "area/rule")]
    // Surface union: a union carries adjectives of its own (GRAMMAR §5.1), so every §8 walk must reach them
    // and not stop at the operands — including the union's own Except payload, not just its operands.
    [InlineData(typeof(UnionBlankWhereSpec), nameof(Code.BlankProse), "area/rule")]
    [InlineData(typeof(UnionBlankPatternSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(UnionForeignExceptPayloadSpec), nameof(Code.ForeignSelection), "area/rule")]
    [InlineData(typeof(UnionForeignOperandSpec), nameof(Code.ForeignSelection), "area/rule")]
    // The project stratum (GRAMMAR §4.10). A project selection carries its own Arch — it has no underlying
    // type selection to borrow one from — so §8 item 22 is its own walk, and it reaches an Except payload
    // exactly as the type-side foreign walk does. The blank checks (items 23–24) take their own codes
    // rather than the shared BlankPattern, because a blank project name matches nothing while a blank glob
    // matches everything and the catalog has to be able to say which. Blank project escape-hatch
    // descriptions ride BlankProse, which covers a description anywhere (item 5).
    [InlineData(typeof(ForeignProjectExceptPayloadSpec), nameof(Code.ForeignProjectSelection), "area/rule")]
    [InlineData(typeof(BlankProjectPatternSpec), nameof(Code.BlankProjectPattern), "project/blank-in-except")]
    [InlineData(typeof(BlankProjectWhereSpec), nameof(Code.BlankProse), "area/rule")]
    [InlineData(typeof(BlankProjectMustSpec), nameof(Code.BlankProse), "area/rule")]
    // A Baseline path is in the item-5 walk beside DragonsDoc, on both the rule and the scope that can
    // carry one. Nothing downstream would report a blank: it resolves to the solution directory rather
    // than failing, so check would silently read no baseline and the rule would red saying nothing about
    // the path.
    [InlineData(typeof(MultiLineBaselineSpec), nameof(Code.MultiLineProse), "area/rule")]
    [InlineData(typeof(BlankScopeBaselineSpec), nameof(Code.BlankProse), "legacy/billing")]
    // The family (GRAMMAR §5.1). Its cells and its own adjectives ride the walks a union's operands do, so a
    // foreign cell, a blank glob on the family and the project stratum inside a project family need no
    // new codes at all.
    [InlineData(typeof(ForeignFamilyCellSpec), nameof(Code.ForeignSelection), "area/rule")]
    [InlineData(typeof(BlankFamilyGlobSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankProjectFamilyGlobSpec), nameof(Code.BlankProjectPattern), "area/rule")]
    [InlineData(typeof(ForeignProjectFamilySpec), nameof(Code.ForeignProjectSelection), "area/rule")]
    // A member Except payload is a member selection with a type selection of its own, and every walk the
    // subject takes reaches it: its source's Arch, a family standing there, its source's Where descriptions,
    // its blank names and its blank member Where descriptions (GRAMMAR §8 items 5, 10, 15, 27).
    [InlineData(typeof(ForeignMemberExceptPayloadSpec), nameof(Code.ForeignSelection), "area/rule")]
    [InlineData(typeof(FamilyInMemberExceptPayloadSpec), nameof(Code.FamilyMisplaced), "area/rule")]
    [InlineData(typeof(BlankSourceWhereInMemberExceptPayloadSpec), nameof(Code.BlankProse), "area/rule")]
    [InlineData(typeof(BlankNameInMemberExceptPayloadSpec), nameof(Code.BlankPattern), "area/rule")]
    [InlineData(typeof(BlankWhereInMemberExceptPayloadSpec), nameof(Code.BlankProse), "area/rule")]
    public void Validate_FailingSpec_ReportsItsCodeAndRuleId(Type specType, string codeName, string ruleId)
    {
        // By name, because the error-code enum is internal and a public theory cannot take it as a parameter.
        var code = Enum.Parse<Code>(codeName);

        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = ShouldFailToBuild(spec);

        ex.ShouldHaveError(code, ruleId);
    }

    [Fact]
    public void DuplicateId_AcrossTwoSpecClasses_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new DuplicateIdSpecA(), new DuplicateIdSpecB());

        ex.ShouldHaveError(Code.DuplicateId, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:18: Duplicate rule ID 'area/rule'.");
    }

    [Fact]
    public void DuplicateId_TwoRulesInOneMethod_IsReportedAtTheFirstDeclaration()
    {
        SpecValidationException ex = ShouldFailToBuild(new DuplicateRuleIdInOneMethodSpec());

        ex.ShouldHaveError(Code.DuplicateId, "area/twice")
            .Message.ShouldBe("SpecValidationSpecs.cs:1408: Duplicate rule ID 'area/twice'.");
    }

    [Fact]
    public void DuplicateId_TwoScopesInOneMethod_IsReportedUnderBothIdsTheScopeReserves()
    {
        // A scope reserves both of the rules it could contribute whatever its posture, so one scope ID
        // declared twice collides twice — under two IDs that appear nowhere in the source, both rendered at
        // the first declaration.
        SpecValidationException ex = ShouldFailToBuild(new DuplicateScopeIdInOneMethodSpec());

        ex.ShouldHaveError(Code.DuplicateId, "legacy/twice/containment")
            .Message.ShouldBe("SpecValidationSpecs.cs:1421: Duplicate rule ID 'legacy/twice/containment'.");
        ex.ShouldHaveError(Code.DuplicateId, "legacy/twice/tripwire")
            .Message.ShouldBe("SpecValidationSpecs.cs:1421: Duplicate rule ID 'legacy/twice/tripwire'.");
    }

    [Fact]
    public void IdExtendsScope_ReservedNamespace_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new IdExtendsScopeSpec());

        ex.ShouldHaveError(Code.IdExtendsScope, "legacy/billing/foo")
            .Message.ShouldContain("extends scope 'legacy/billing'");
    }

    [Fact]
    public void MissingBecause_OnRule_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new MissingBecauseRuleSpec());

        ex.ShouldHaveError(Code.MissingBecause, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:52: 'area/rule' is missing a required .Because(...).");
    }

    [Fact]
    public void RepeatedCall_BecauseTwice_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new RepeatedBecauseSpec());

        ex.ShouldHaveError(Code.RepeatedCall, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:92: Repeated .Because(...) on 'area/rule'.");
    }

    [Fact]
    public void RepeatedPosture_PostureVerbTwiceViaStoredRuleBuilder_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new DoublePostureRuleSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:111: Rule 'area/rule' has more than one posture; call .Enforce(...) or .Migrate(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_QuarantineTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new DoubleQuarantineScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "legacy/billing")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:123: Scope 'legacy/billing' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_CautionTwiceViaStoredScopeBuilder_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new DoubleCautionScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1025: Scope 'shared/utilities' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void RepeatedPosture_QuarantineThenCautionViaStoredScopeBuilder_IsReported()
    {
        // Two different verbs, not two of one: the count is what the check reads, so mixing them is the
        // same overwrite and reports the same way.
        SpecValidationException ex = ShouldFailToBuild(new MixedPostureScopeSpec());

        ex.ShouldHaveError(Code.RepeatedPosture, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1037: Scope 'shared/utilities' has more than one posture; call .Quarantine(...) or .Caution(...) exactly once.");
    }

    [Fact]
    public void DanglingScope_NoPostureVerb_IsReportedNamingBothVerbs()
    {
        // The dangling report reads the posture field rather than the selection, because a caution sets
        // both and "no posture" is the thing the author has to fix.
        SpecValidationException ex = ShouldFailToBuild(new MultipleProblemsSpec());

        ex.ShouldHaveError(Code.MissingPosture, "other/scope")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:509: Scope 'other/scope' has no posture; call .Quarantine(...) or .Caution(...).");
    }

    [Fact]
    public void MissingDragons_OnACaution_NamesTheCautionedScope()
    {
        // The message names the posture the scope was declared under, because the author's next move is to
        // find that verb in the spec and add the clause beneath it.
        SpecValidationException ex = ShouldFailToBuild(new MissingDragonsCautionSpec());

        ex.ShouldHaveError(Code.MissingDragons, "shared/utilities")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1047: Cautioned scope 'shared/utilities' is missing .Dragons(...) or .DragonsDoc(...).");
    }

    [Fact]
    public void EmptyBoundary_BoundaryOnlyViaWithNoTypes_IsReported()
    {
        // That EmptyBoundarySpec compiles at all is half the pin: BoundaryOnlyVia() binds uniquely to the
        // Type overload because the Selection form takes (first, params more). Flatten that form to plain
        // params and the call is ambiguous under CS0121, taking this hint out of reach (GRAMMAR §7).
        SpecValidationException ex = ShouldFailToBuild(new EmptyBoundarySpec());

        ex.ShouldHaveError(Code.EmptyBoundary, "legacy/billing")
            .Message.ShouldContain("omit the call for a hermetic quarantine");
    }

    [Fact]
    public void DuplicateLayerName_IsReportedSpecWide()
    {
        SpecValidationException ex = ShouldFailToBuild(new DuplicateLayerSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.DuplicateLayerName);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Duplicate layer name 'Dup'.");
    }

    [Theory]
    [InlineData(typeof(FamilyAsOperandSpec), "an operand")]
    [InlineData(typeof(FamilyAsExceptPayloadSpec), "an Except payload")]
    [InlineData(typeof(FamilyAsUnionOperandSpec), "a union operand")]
    [InlineData(typeof(FamilyAsMembershipSpec), "an operand")]
    [InlineData(typeof(FamilyAsCounterpartAmongSpec), "an operand")]
    [InlineData(typeof(FamilyAsExemplarSpec), "an exemplar")]
    public void FamilyMisplaced_OnARule_NamesThePositionItStandsIn(Type specType, string position)
    {
        // One sentence for every position, varying on the position alone: what an author has to move is
        // the family, and where it stands is the whole of what they need told (GRAMMAR §8 item 27).
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = ShouldFailToBuild(spec);

        ex.ShouldHaveError(Code.FamilyMisplaced, "area/rule")
            .Message.ShouldEndWith(
                $"A family (`arch.Each`) used as {position} by 'area/rule'; a family may stand only as a rule subject.");
    }

    [Theory]
    [InlineData(typeof(FamilyAsScopedSelectionSpec), "a scoped selection")]
    [InlineData(typeof(FamilyAsBoundarySpec), "a boundary")]
    public void FamilyMisplaced_OnAScope_NamesThePositionItStandsIn(Type specType, string position)
    {
        var spec = (IArchitectureSpec)Activator.CreateInstance(specType)!;

        SpecValidationException ex = ShouldFailToBuild(spec);

        ex.ShouldHaveError(Code.FamilyMisplaced, "legacy/modules")
            .Message.ShouldEndWith(
                $"A family (`arch.Each`) used as {position} by 'legacy/modules'; "
                + "a family may stand only as a rule subject.");
    }

    [Fact]
    public void FamilyMisplaced_AsALayerDefinition_IsReportedSpecWideNamingTheLayer()
    {
        // A definition is use-independent, so its error is the layer's: null ID, no location, named by
        // layer — the same terms every other layer error reports on.
        SpecValidationException ex = ShouldFailToBuild(new FamilyAsLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.FamilyMisplaced);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe(
            "A family (`arch.Each`) used as a layer definition by layer 'Modules'; "
            + "a family may stand only as a rule subject.");
    }

    [Fact]
    public void EachOtherWithoutFamily_OnAPlainSubject_NamesTheVerbTheAuthorMeant()
    {
        // The cross-cell ban's targets are the subject's own cells, so a cell-free subject names nothing
        // to forbid — and the plain-selection verb has a name (GRAMMAR §8 item 28).
        SpecValidationException ex = ShouldFailToBuild(new EachOtherWithoutFamilySpec());

        ex.ShouldHaveError(Code.EachOtherWithoutFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotReferenceEachOther` on 'area/rule' needs a family subject (`arch.Each`); "
                + "over a plain selection write `MustNotReference`.");
    }

    [Fact]
    public void CircularReferencesNeedLayerFamily_OnAPlainSubject_SaysThereAreNoLayers()
    {
        // The cycle gate's nodes are the subject's own cells, so a cell-free subject has no graph at all
        // (GRAMMAR §8 item 29).
        SpecValidationException ex = ShouldFailToBuild(new CircularReferencesOnPlainSubjectSpec());

        ex.ShouldHaveError(Code.CircularReferencesNeedLayerFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotHaveCircularReferences` on 'area/rule' needs a family of layers (`arch.Each`); "
                + "over a plain selection there are no layers to reference each other.");
    }

    [Fact]
    public void CircularReferencesNeedLayerFamily_OnAProjectFamily_NamesTheVerbsTheAuthorMeant()
    {
        // The build forbids circular project references, so over a family of projects the law holds by
        // construction — a rule that cannot red is a false promise, and the two verbs that can say
        // something are named (GRAMMAR §8 item 29).
        SpecValidationException ex = ShouldFailToBuild(new CircularReferencesOnProjectFamilySpec());

        ex.ShouldHaveError(Code.CircularReferencesNeedLayerFamily, "area/rule")
            .Message.ShouldEndWith(
                "`MustNotHaveCircularReferences` on 'area/rule' needs a family of layers; projects cannot "
                + "have circular references, so over a family of projects the law holds by construction. "
                + "Write `MustNotReferenceEachOther` or an ordering rule.");
    }

    [Fact]
    public void AllErrors_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new MultipleProblemsSpec());

        ex.ShouldHaveError(Code.MalformedId);
        ex.ShouldHaveError(Code.MissingBecause);
        ex.ShouldHaveError(Code.MissingPosture);
    }

    [Fact]
    public void BlankMemberName_OnMustNotUse_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankMemberNameSpec());

        ex.ShouldHaveError(Code.BlankMemberName, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:161: Blank member name on a member of 'System.DateTime' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_TypoName_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new TypoMemberSpec());

        ex.ShouldHaveError(Code.MemberNotDeclared, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:169: 'System.DateTime' does not declare a member named 'Nows' (used by 'area/rule').");
    }

    [Fact]
    public void MemberNotDeclared_MemberOnBaseType_NamesBaseAndTypeof()
    {
        SpecValidationException ex = ShouldFailToBuild(new BaseTypeMemberSpec());

        ex.ShouldHaveError(Code.MemberNotDeclared, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:178: 'System.Threading.Tasks.Task<TResult>' does not declare 'Wait'; it is declared on base type " +
                      "'System.Threading.Tasks.Task' — use typeof(Task) (used by 'area/rule').");
    }

    [Fact]
    public void ForeignMember_MemberFromAnotherArch_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new ForeignMemberSpec());

        ex.ShouldHaveError(Code.ForeignMember, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:187: A member used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void ForeignProjectSelection_ProjectSubjectFromAnotherArch_IsReported()
    {
        // Named as a PROJECT selection rather than folded into the type-side message: an author whose rule
        // mixes strata needs the report to say which one was foreign to find it.
        SpecValidationException ex = ShouldFailToBuild(new ForeignProjectSelectionSpec());

        ex.ShouldHaveError(Code.ForeignProjectSelection, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:884: A project selection used by 'area/rule' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void BlankProjectPattern_BlankNameAndBlankGlob_AreReportedByKind()
    {
        // One code, two labels: the message names which operand was left empty, because a blank name
        // matches nothing while a blank glob matches everything.
        SpecValidationException ex = ShouldFailToBuild(new BlankProjectPatternSpec());

        ex.ShouldHaveError(Code.BlankProjectPattern, "project/blank-name")
            .Message.ShouldBe("SpecValidationSpecs.cs:903: Blank project name on 'project/blank-name'.");
        ex.ShouldHaveError(Code.BlankProjectPattern, "project/blank-glob")
            .Message.ShouldBe("SpecValidationSpecs.cs:904: Blank project name pattern on 'project/blank-glob'.");
    }

    [Fact]
    public void BlankTargetFramework_OnMustOnlyTarget_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankTargetFrameworkSpec());

        ex.ShouldHaveError(Code.BlankTargetFramework, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:915: Blank target framework on 'area/rule'.");
    }

    [Fact]
    public void BlankTargetFramework_OnMustTarget_IsReported()
    {
        // The floor's one moniker rides the ceiling's slot: a blank one would require a framework no project
        // targets, and red every evaluated project.
        SpecValidationException ex = ShouldFailToBuild(new BlankRequiredTargetFrameworkSpec());

        ex.ShouldHaveError(Code.BlankTargetFramework, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1521: Blank target framework on 'area/rule'.");
    }

    [Fact]
    public void BlankPackagePattern_OnEitherPackageVerb_IsReported()
    {
        // A blank glob matches no package: it narrows an allow-list and empties a ban, so both verbs report it.
        SpecValidationException ex = ShouldFailToBuild(new BlankPackagePatternSpec());

        ex.ShouldHaveError(Code.BlankPackagePattern, "area/allow")
            .Message.ShouldBe("SpecValidationSpecs.cs:1529: Blank package pattern on 'area/allow'.");
        ex.ShouldHaveError(Code.BlankPackagePattern, "area/ban")
            .Message.ShouldBe("SpecValidationSpecs.cs:1530: Blank package pattern on 'area/ban'.");
    }

    [Fact]
    public void BlankProperty_NameOrValue_IsReportedUnderItsOwnLabel()
    {
        // One code for both strings, since either blank asks for a property no project can declare, and the
        // label says which was left out.
        SpecValidationException ex = ShouldFailToBuild(new BlankPropertySpec());

        ex.ShouldHaveError(Code.BlankProperty, "area/name")
            .Message.ShouldBe("SpecValidationSpecs.cs:1538: Blank property name on 'area/name'.");
        ex.ShouldHaveError(Code.BlankProperty, "area/value")
            .Message.ShouldBe("SpecValidationSpecs.cs:1539: Blank property value on 'area/value'.");
    }

    [Fact]
    public void ValidMemberUse_MustNotUseSpec_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberUseSpec()));
    }

    [Fact]
    public void MemberReturningClosedGeneric_ClosedAnchor_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new ClosedGenericReturningSpec());

        ex.ShouldHaveError(Code.MemberReturningClosedGeneric, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:212: 'System.Threading.Tasks.Task<System.Int32>' is a closed generic; .Returning matches definition-level — " +
                      "use typeof(Task<>) (used by 'area/rule').");
    }

    [Fact]
    public void ValidMemberSubject_AsyncSuffixSpec_BuildsWithoutError()
    {
        // Non-generic + open-generic Returning anchors are both accepted (only closed generics fail).
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberSubjectSpec()));
    }

    [Fact]
    public void BlankPattern_BlankNamespaceGlob_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankNamespaceGlobSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:251: Blank namespace pattern on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankSuffix_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankSuffixSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:259: Blank suffix on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankTypeName_IsReported()
    {
        // The exact-name adjective carries a list, so the walk yields one pattern per name and the blank one
        // reports under its own label. Blankness is the whole check: a name carries no structure to check.
        SpecValidationException ex = ShouldFailToBuild(new BlankTypeNamedAdjectiveSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:963: Blank type name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankMemberSubjectAffix_IsReported()
    {
        // The member-subject adjective walk (parallel to the member prose/Returning walks) reaches a
        // blank member .WithSuffix (GRAMMAR §8 item 15, §4.6).
        SpecValidationException ex = ShouldFailToBuild(new BlankMemberSuffixSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:267: Blank member suffix on 'area/rule'.");
    }

    [Fact]
    public void MemberExceptKindDisjoint_FieldsFromMethods_IsRefusedWithTheCure()
    {
        // GRAMMAR §8 item 34: no member is both a field and a method, so the exclusion removes nothing; the
        // message names both kinds and the two projections that would make it mean something.
        SpecValidationException ex = ShouldFailToBuild(new MemberExceptKindClashSpec());

        ex.ShouldHaveError(Code.MemberExceptKindDisjoint, "area/rule")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1555: `Except` on 'area/rule' takes fields from methods, so it removes "
                + "nothing: no member is both. Project the payload with `.Methods` or `.Members`.");
    }

    [Fact]
    public void BlankPattern_BlankPathGlob_IsReported()
    {
        // A path glob rides the type-side selection walk under its own label (GRAMMAR §8 item 15).
        SpecValidationException ex = ShouldFailToBuild(new BlankPathGlobSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1604: Blank path glob on 'area/rule'.");
    }

    [Fact]
    public void MalformedPathGlob_EachShapeTheMatcherRefuses_IsReportedWithItsReason()
    {
        // GRAMMAR §8 item 35: refused, never normalized, so the model carries what the author wrote. The reason is
        // the matcher's own, so what it refuses and what the catalog reports cannot drift.
        SpecValidationException ex = ShouldFailToBuild(new MalformedPathGlobSpec());

        ex.ShouldHaveError(Code.MalformedPathGlob, "area/rooted")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1612: The path glob '/src' on 'area/rooted' is rooted; a path glob is relative "
                + "to the solution directory.");
        ex.ShouldHaveError(Code.MalformedPathGlob, "area/dotted")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1613: The path glob 'src/../lib' on 'area/dotted' has a `.` or `..` segment; "
                + "name the directories from the solution directory down.");
        ex.ShouldHaveError(Code.MalformedPathGlob, "area/doubled")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1614: The path glob 'src/**Widgets' on 'area/doubled' puts `**` inside a "
                + "segment; `**` stands alone between slashes.");
    }

    [Fact]
    public void BlankPattern_BlankMemberName_IsReported()
    {
        // A member Named list's names ride the same member-subject adjective walk (GRAMMAR §8 item 15), under
        // the "member name" label that item 11 reports under a code of its own.
        SpecValidationException ex = ShouldFailToBuild(new BlankMemberNamedSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1547: Blank member name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankLayerGlob_IsReportedSpecWide()
    {
        // The layer flavor of §8 item 15: like the dead-subtree layer case below, layer globs are
        // validated at their declaration (spec-wide, null rule ID, named by layer), used or not.
        SpecValidationException ex = ShouldFailToBuild(new BlankLayerGlobSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankPattern);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank namespace pattern on layer 'Bad'.");
    }

    [Fact]
    public void BlankProse_BlankLayerPurpose_IsReportedSpecWide()
    {
        // A layer purpose is prose like any other (§8 item 5), reported on the layer-glob terms above:
        // spec-wide, null rule ID, named by layer, and with no file:line prefix because there is no anchor.
        SpecValidationException ex = ShouldFailToBuild(new BlankLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankProse);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank purpose on layer 'Core'.");
    }

    [Fact]
    public void MultiLineProse_MultiLineLayerPurpose_IsReportedSpecWide()
    {
        SpecValidationException ex = ShouldFailToBuild(new MultiLineLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.MultiLineProse);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Multi-line purpose on layer 'Core'; prose fields are single-line.");
    }

    [Fact]
    public void RepeatedCall_RepeatedLayerPurpose_IsReportedSpecWide()
    {
        SpecValidationException ex = ShouldFailToBuild(new RepeatedLayerPurposeSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.RepeatedCall);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Repeated .Purpose(...) on layer 'Core'.");
    }

    [Fact]
    public void BlankLayerName_IsReportedSpecWide_AndNamesNoLayer()
    {
        // The one layer error that cannot name its layer, because the name is what is missing. It is
        // reported on the same spec-wide terms as the globs and purposes above, and it takes the layer out
        // of the duplicate-name set: a second unnamed layer is a second instance of this, not a collision.
        SpecValidationException ex = ShouldFailToBuild(new BlankLayerNameSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankLayerName);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank layer name; name the layer in the first argument to arch.Layer(...).");
    }

    [Fact]
    public void BlankProse_BlankBaselinePath_IsReportedAgainstItsRule()
    {
        // A Baseline path is walked as prose, DragonsDoc's precedent for a path in that walk, and it is
        // rule-anchored rather than spec-wide, so it lands at a file:line the author can jump to.
        SpecValidationException ex = ShouldFailToBuild(new BlankBaselineSpec());

        ex.ShouldHaveError(Code.BlankProse, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1370: Blank Baseline on 'area/rule'.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_WildcardInSubtreePrefix_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new DeadSubtreeGlobSpec());

        ex.ShouldHaveError(Code.UnanchoredSubtreePattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:283: The namespace pattern 'MyApp.*.Controllers.*' on 'area/rule' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_OnLayerGlob_IsReportedSpecWide()
    {
        SpecValidationException ex = ShouldFailToBuild(new DeadSubtreeLayerSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.UnanchoredSubtreePattern);
        error.RuleId.ShouldBeNull();
        error.Message
            .ShouldBe("The namespace pattern 'MyApp.*.Svc.*' on layer 'Bad' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void ForeignSelection_InALayerDefinition_IsReportedSpecWide()
    {
        // A definition is validated where the layer is declared, on the layer-glob terms: spec-wide, null
        // rule ID, named by layer, and found whether or not any rule ever names the layer (§8 item 10).
        SpecValidationException ex = ShouldFailToBuild(new ForeignLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.ForeignSelection);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe(
            "A selection used by layer 'Foreign' was minted on a different Arch instance; it is not registered with this model.");
    }

    [Fact]
    public void BlankPattern_BlankProjectNameInALayerDefinition_IsReportedSpecWide()
    {
        // The definition takes the same blank-operand walk a rule's selections take (§8 item 15), under the
        // noun the type-side walk names it by.
        SpecValidationException ex = ShouldFailToBuild(new BlankProjectNameLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.BlankPattern);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("Blank project name on layer 'Bad'.");
    }

    [Fact]
    public void UndefinedLifetime_InALayerDefinition_IsReportedSpecWide()
    {
        SpecValidationException ex = ShouldFailToBuild(new UndefinedLifetimeLayerDefinitionSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.UndefinedLifetime);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe("'(Lifetime)7' is not a defined Lifetime — " +
                               "use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by layer 'Wiring').");
    }

    [Fact]
    public void UnanchoredSubtreePattern_InALayerDefinitionsExceptPayload_IsReportedSpecWide()
    {
        // The walk reaches a definition's nesting, not just its head: an Except payload is where a
        // definition hides a second selection.
        SpecValidationException ex = ShouldFailToBuild(new DeadSubtreeLayerDefinitionSpec());

        ex.ShouldHaveError(Code.UnanchoredSubtreePattern)
            .Message
            .ShouldBe("The namespace pattern 'MyApp.*.Svc.*' on layer 'Bad' has a trailing `.*` subtree " +
                      "operator but its literal prefix contains a `*`, which never matches; anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void NamespacePattern_InteriorWildcardWithoutSubtree_BuildsWithoutError()
    {
        // MyApp.*.Orders is legitimate single-segment matching (GRAMMAR §4.2), not a dead subtree pattern.
        Should.NotThrow(() => ArchModelBuilder.Build(new InteriorWildcardSpec()));
    }

    [Fact]
    public void AllErrors_ThreeBadPatterns_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new ThreeBadPatternsSpec());

        // Two dead subtree globs (the subject noun and the verb) plus one blank affix, reported together.
        ex.ShouldHaveErrors(Code.UnanchoredSubtreePattern, 2);
        ex.ShouldHaveErrors(Code.BlankPattern, 1);
    }

    [Fact]
    public void MemberExpression_NonMemberBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new NonMemberBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:318: A member anchor lambda must be a single property, field, or method access " +
                      "(x => x.Member or () => Type.Member); this lambda body is neither (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_MethodGroupBody_IsReportedWithInvocationGuidance()
    {
        // A method-group anchor `w => w.Reset` compiles (converting the method group to object, warning
        // CS8974 — suppressed in the spec) and lowers to a CreateDelegate tree the resolver detects, so the
        // mistake reaches spec build. Steers to the invocation form.
        SpecValidationException ex = ShouldFailToBuild(new MethodGroupBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:327: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_ChainedReceiver_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new ChainedReceiverSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:337: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_StaticMemberInInstanceForm_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new StaticInInstanceFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:346: A typed member anchor arch.Member<T>(x => ...) accesses a static member; anchor statics " +
                      "with the parameterless overload arch.Member(() => Type.Member) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_InstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new InstanceInStaticFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:355: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_IndexerBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new IndexerBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:364: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
                      "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_StaticMethodGroupBody_IsReported()
    {
        // A static method-group anchor `() => AnchorStatics.Beep` compiles (method group to object, warning
        // CS8974 — suppressed in the spec) and lowers to a CreateDelegate tree carrying the MethodInfo as a
        // compiler-emitted constant. Doubles as the regression oracle for that static lowering; steers to
        // the invocation form like the instance case.
        SpecValidationException ex = ShouldFailToBuild(new StaticMethodGroupBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:373: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_UserDefinedConversionReceiver_IsReported()
    {
        // ((AnchorFahrenheit)c).Value reaches its member through a user-defined conversion — following it
        // would silently anchor the post-conversion type, so it is reported (identity casts are the only
        // receiver peel).
        SpecValidationException ex = ShouldFailToBuild(new UserOpConversionReceiverSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:383: A member anchor lambda must reach its member directly on the lambda parameter (an interface " +
                      "cast or as-cast is allowed; a chained access like x => x.A.B, a captured local or field, or a " +
                      "user-defined conversion is not); anchor the declaring type you mean directly (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_CompileTimeConstantBody_IsReported()
    {
        // () => DayOfWeek.Monday inlines the enum member to its value (Convert(Constant(...), object)), so
        // Unwrap peels to a ConstantExpression with no member left to anchor.
        SpecValidationException ex = ShouldFailToBuild(new CompileTimeConstantBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:392: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_MultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new MultiplePoisonedMembersSpec());

        // Two poisoned member anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.ShouldHaveErrors(Code.MemberExpressionUnresolvable, 2);
    }

    [Fact]
    public void ValidExpressionMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidExpressionMemberSpec()));
    }

    // Verb-position poison parity: the same unresolvable-anchor diagnostics the anchor-position
    // twins pin (above) fire when the poisoned lambda is passed bare to MustNotUse's static forms, because
    // the verb desugars each target through the identical MemberExpressionResolver. Six of the eight poison
    // classes are reachable from the static forms; the two instance-form steers have no static-verb spelling.
    // Message strings are copied verbatim from the anchor-position twins — the pinned strings are the spec.
    [Fact]
    public void MemberExpression_VerbNonMemberBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbNonMemberBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:438: A member anchor lambda body is an object creation (a new expression, including target-typed new()); " +
                      "construction is not a member use, so ban the constructed type with the MustNotConstruct verb instead (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbStaticMethodGroupBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbStaticMethodGroupBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:447: A member anchor lambda may not be a method group (x => x.Method or () => Type.Method); " +
                      "write the invocation form (x => x.Method() or () => Type.Method(...)) so the method itself " +
                      "is anchored (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbInstanceMemberInStaticForm_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbInstanceInStaticFormSpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:457: A parameterless member anchor arch.Member(() => ...) must access a static member " +
                      "directly; anchor an instance member with the typed overload arch.Member<T>(x => x.Member) " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbIndexerBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbIndexerBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:466: A member anchor lambda resolves to an indexer accessor (get_Item), which is outside the " +
                      "member-anchor surface (GRAMMAR §4.5); anchor a named property, field, or method " +
                      "(used by 'area/rule').");
    }

    [Fact]
    public void MemberExpression_VerbCompileTimeConstantBody_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbCompileTimeConstantBodySpec());

        ex.ShouldHaveError(Code.MemberExpressionUnresolvable, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:475: A member anchor lambda body is a compile-time constant (a const field, an enum member, or a " +
                      "literal) that the compiler inlines to its value, so no member remains to anchor; name a const " +
                      "or enum member with the typeof form arch.Member(typeof(T), nameof(T.M)) (used by 'area/rule').");
    }

    [Fact]
    public void MemberExpressions_VerbMultiplePoisoned_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new VerbMultiplePoisonedMembersSpec());

        // Two poisoned verb-position anchors on one rule → two errors in one pass (the §8 all-at-once contract).
        ex.ShouldHaveErrors(Code.MemberExpressionUnresolvable, 2);
    }

    [Fact]
    public void ValidVerbMemberUse_BuildsWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidVerbMemberSpec()));
    }

    // Spec-source locations (caller-info diagnostics): the file:line an error carries is its anchor's own,
    // captured where the fixture spells the rule and pinned here verbatim.
    [Fact]
    public void SpecValidationError_NoCapturedLocation_RendersMessageWithNoPrefix()
    {
        // The pre-caller-info degradation seam: an error minted without a source location — as a spec DLL
        // compiled against the previous Core yields — renders the message verbatim, no location prefix
        // and no leading blank. Simulated via the internal ctor since we cannot compile against an older Core.
        var error = new SpecValidationError(Code.MissingPosture, "area/x",
            "Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");

        error.Location.ShouldBeNull();
        error.Message.ShouldBe("Rule 'area/x' has no posture; call .Enforce(...) or .Migrate(...).");
    }

    [Fact]
    public void SpecValidationError_Location_IsFileNameOnlyAndLineCaptured()
    {
        SpecValidationException ex = ShouldFailToBuild(new MissingBecauseRuleSpec());
        SpecValidationError error = ex.ShouldHaveError(Code.MissingBecause);

        SpecSourceLocation location = error.Location.ShouldNotBeNull();
        // File name only — never the machine-specific directory — so goldens stay byte-identical across build
        // machines; the line is the captured 1-based anchor line.
        location.File.ShouldBe("SpecValidationSpecs.cs");
        location.Line.ShouldBeGreaterThan(0);
        error.Message.ShouldStartWith("SpecValidationSpecs.cs:");
    }

    [Fact]
    public void AllErrors_MultiErrorSpec_EachRendersItsAnchorLocation()
    {
        SpecValidationException ex = ShouldFailToBuild(new MultipleProblemsSpec());

        // Every error in the one-pass batch lands at its own anchor's file:line — the rule's malformed ID and
        // missing Because on one line, the dangling scope on the next (two distinct anchor lines).
        ex.Errors.ShouldAllBe(e => e.Location != null && e.Location.File == "SpecValidationSpecs.cs");
        ex.Errors.Select(e => e.Location!.Line)
            .Distinct()
            .ShouldBe([508, 509]);
    }

    [Fact]
    public void MemberPoison_AnchorsToMemberCallSite_RuleErrorAnchorsToRuleAnchor()
    {
        SpecValidationException ex = ShouldFailToBuild(new MemberPoisonBelowRuleSpec());
        SpecValidationError ruleError = ex.ShouldHaveError(Code.MissingBecause);
        SpecValidationError memberError = ex.ShouldHaveError(Code.MemberExpressionUnresolvable);

        SpecSourceLocation ruleAnchor = ruleError.Location.ShouldNotBeNull();
        SpecSourceLocation memberAnchor = memberError.Location.ShouldNotBeNull();
        // The member poison steers to its own arch.Member(...) lambda line — below the arch.Rule(...) anchor
        // the rule-level MissingBecause renders at — proving item-18 steers point at the offending construct,
        // not the consuming rule (GRAMMAR §8).
        memberAnchor.Line.ShouldBeGreaterThan(ruleAnchor.Line);
    }

    // GRAMMAR §8 item 19: an undefined Lifetime value on an arch.Registered noun used by a rule. The check rides the shared
    // RuleSelections walk, reaching the Registered subject with no new selection plumbing.
    [Fact]
    public void UndefinedLifetime_CastToUndefinedValue_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new UndefinedLifetimeSpec());

        ex.ShouldHaveError(Code.UndefinedLifetime, "di/lifetimes")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:605: '(Lifetime)7' is not a defined Lifetime — " +
                      "use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by 'di/lifetimes').");
    }

    // GRAMMAR §8 item 20: a closed-generic MustAcceptParameter anchor on a method selection. Parameter-type matching is
    // definition-level, so a closed construction is refused with the open-definition steer; the check mirrors
    // the item-14 .Returning refusal, reading as its sibling with the verb named in place of .Returning.
    [Fact]
    public void MemberAcceptParameterClosedGeneric_ClosedAnchor_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new ClosedGenericAcceptParameterSpec());

        ex.ShouldHaveError(Code.MemberAcceptParameterClosedGeneric, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:616: 'System.IProgress<System.Int32>' is a closed generic; MustAcceptParameter matches definition-level — " +
                      "use typeof(IProgress<>) (used by 'area/rule').");
    }

    [Fact]
    public void MemberAcceptParameterClosedGeneric_BesideAnotherError_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new AcceptParameterAllAtOnceSpec());

        // The closed-generic parameter anchor and the rule's missing Because report together (the §8
        // all-at-once contract).
        ex.ShouldHaveError(Code.MemberAcceptParameterClosedGeneric, "area/rule");
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidAcceptParameter_NonGenericAndOpenGenericAnchors_BuildWithoutError()
    {
        // A non-generic anchor (typeof(CancellationToken)) and an open-generic one (typeof(IProgress<>)) are
        // both accepted — only closed generics fail.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidAcceptParameterSpec()));
    }

    // GRAMMAR §8 item 21: a category-invalid hierarchy anchor, both polarities. One shared code covers
    // the three categories; the check applies to the positives' single anchor and every anchor in a
    // negative's list, all reported in the same all-at-once pass with the rule's spec-source file:line.
    [Fact]
    public void HierarchyAnchorWrongCategory_NonInterfaceOnMustNotImplement_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new NonInterfaceImplementAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:650: 'System.Exception' is not an interface; MustNotImplement requires an interface anchor — " +
                      "use MustNotDeriveFrom for a base class (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_InterfaceOnMustDeriveFrom_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new InterfaceDeriveFromAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:661: 'System.IDisposable' is an interface; MustDeriveFrom requires a non-interface anchor — " +
                      "use MustImplement for an interface (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_NonAttributeOnMustNotBeAttributedWith_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new NonAttributeAttributedAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:672: 'System.Attribute' does not derive from System.Attribute; MustNotBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_InvalidAnchorInNegativeList_BesideMissingBecause_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new HierarchyAllAtOnceSpec());

        // The invalid anchor is the SECOND in the negative's list (the first is a valid interface), and the
        // rule omits .Because — the category error and the missing Because report together (the §8 all-at-once
        // contract, and proof the check walks every anchor in a negative's list).
        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule");
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidHierarchyAnchors_CategoryCorrectPositivesAndNegatives_BuildWithoutError()
    {
        // Interface anchors on (Must[Not])Implement, non-interface anchors on (Must[Not])DeriveFrom, and
        // Attribute-derived anchors on (Must[Not])BeAttributedWith are all accepted — only wrong-category fails.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidHierarchyAnchorsSpec()));
    }

    // ---- String attribute anchors (GRAMMAR §5.2–§5.3). Blank is the ONLY well-formedness a definition FQN has, and it
    //      reports through the shared Code.BlankPattern family under the "attribute name" label (§8 item 15) —
    //      no new code. The item-21 category check deliberately does not apply: there is no category to read
    //      off a string, so a nonsense name builds clean and matches nothing. ----

    [Fact]
    public void BlankPattern_BlankAttributeNameOnAdjective_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankAttributeAdjectiveSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:749: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNameOnMustBeAttributedWith_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankAttributeVerbSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:759: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNameOnMustNotBeAttributedWith_IsReported()
    {
        // The blank is the SECOND anchor in the negative's list — proof the walk covers every anchor.
        SpecValidationException ex = ShouldFailToBuild(new BlankAttributeNegativeVerbSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:769: Blank attribute name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankAttributeNamesAcrossAdjectiveAndVerb_BesideMissingBecause_AreReportedInOnePass()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankAttributeAllAtOnceSpec());

        ex.ShouldHaveErrors(Code.BlankPattern, 2);
        ex.ShouldHaveError(Code.MissingBecause, "area/rule");
    }

    [Fact]
    public void ValidStringAttributeAnchors_NonsenseNames_BuildWithoutError()
    {
        // No category check applies to a string: a non-attribute FQN, a dotless name, a suffix-less name, and
        // even "System.Attribute" — the one spelling whose typeof form item 21 refuses — all build. They name
        // no declared attribute, so they match nothing; that is the hatch's stated honesty boundary.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringAttributeAnchorSpec()));
    }

    // ---- The member attribute axis (GRAMMAR §5.7). The two VERBS carry the item-21 category check with the type
    //      side's message verbatim; the ADJECTIVE deliberately does not — a wrong-category adjective empties
    //      the subject, which the fail-on-empty gate reds loudly (pinned in MemberSubjectVerbTests), whereas
    //      the always-passing MustNot verb is the silent slip the check exists to catch. ----

    [Fact]
    public void HierarchyAnchorWrongCategory_NonAttributeOnMemberMustBeAttributedWith_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new MemberNonAttributeAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:800: 'System.Exception' does not derive from System.Attribute; MustBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void HierarchyAnchorWrongCategory_AttributeItselfOnMemberMustNotBeAttributedWith_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new MemberAttributeItselfAnchorSpec());

        ex.ShouldHaveError(Code.HierarchyAnchorWrongCategory, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:810: 'System.Attribute' does not derive from System.Attribute; MustNotBeAttributedWith " +
                      "requires an attribute anchor (used by 'area/rule').");
    }

    [Fact]
    public void ValidMemberAttributeAnchors_AndAnUncheckedAdjective_BuildWithoutError()
    {
        // Attribute-derived anchors on both member verbs are accepted — and so is a wrong-category anchor on
        // the member ADJECTIVE, which is category-checked on neither axis.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidMemberAttributeAnchorsSpec()));
    }

    [Fact]
    public void BlankPattern_BlankAttributeNamesOnTheMemberNodes_AreReportedInOnePass()
    {
        // One blank per member node — the adjective, the positive verb, and the SECOND anchor of a negative's
        // list — all three reported together under the shared "attribute name" label.
        SpecValidationException ex = ShouldFailToBuild(new BlankMemberAttributeNameSpec());

        ex.ShouldHaveErrors(Code.BlankPattern, 3);
        ex.ShouldHaveError(Code.BlankPattern)
            .Message
            .ShouldBe("SpecValidationSpecs.cs:830: Blank attribute name on 'member/adjective'.");
    }

    // ---- String hierarchy anchors (GRAMMAR §5.2–§5.3, §8 items 15 and 21).
    //      Blankness is the whole of a string anchor's well-formedness, and it is checked on the adjectives as
    //      well as the verbs; the item-21 CATEGORY check reaches neither, because a string carries no category
    //      to read and refusing a spelling the host cannot load would break the hatch. ----

    [Fact]
    public void BlankPattern_BlankHierarchyNamesOnTheStringAnchors_AreReportedInOnePass()
    {
        // Six blanks across both families and all three positions each — adjective, positive verb, and the
        // SECOND anchor of a negative's list — under two labels that name which kind of anchor was left empty.
        SpecValidationException ex = ShouldFailToBuild(new BlankHierarchyNameSpec());

        IReadOnlyList<SpecValidationError> blanks = ex.ShouldHaveErrors(Code.BlankPattern, 6);
        blanks.ShouldContain(e => e.Message.Contains("Blank interface name"), expectedCount: 3);
        blanks.ShouldContain(e => e.Message.Contains("Blank base type name"), expectedCount: 3);
        blanks[0]
            .Message.ShouldBe("SpecValidationSpecs.cs:840: Blank interface name on 'hierarchy/implementing'.");
    }

    [Fact]
    public void ValidStringHierarchyAnchors_NonsenseNames_BuildWithoutError()
    {
        // No category check applies to a string, so the spellings item 21 refuses in typeof form all build: a
        // class named where an interface belongs, an interface named where a base class belongs, a dotless
        // name, and a constructed generic. Each names no declared type or the wrong one, so it matches
        // nothing — loud on a positive (always red), silent on a negative. That is the hatch's stated cost.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringHierarchyAnchorSpec()));
    }

    // ---- The member type anchors (GRAMMAR §5.7, §8 items 14–15 and 20). Blankness is the whole of what a
    //      string anchor is validated for here too, under labels that name which position was left empty. The
    //      closed-generic refusals items 14 and 20 carry are typeof-arm facts — nothing is inferred from a
    //      string's shape — so a constructed spelling builds and matches nothing. ----

    [Fact]
    public void BlankPattern_BlankReturnTypeName_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankReturnTypeNameSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1286: Blank return type name on 'area/rule'.");
    }

    [Fact]
    public void BlankPattern_BlankParameterTypeName_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankParameterTypeNameSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1296: Blank parameter type name on 'area/rule'.");
    }

    [Fact]
    public void ValidStringMemberTypeAnchors_ConstructedSpellingIsNotRefused()
    {
        // The spellings items 14 and 20 refuse in typeof form both build as strings: those checks read a
        // reflected type and a string carries none. Each names no definition, so it matches nothing — loud
        // when it is .Returning's sole anchor, silent beside one that matches. The hatch's stated cost.
        Should.NotThrow(() => ArchModelBuilder.Build(new NonsenseStringMemberTypeAnchorSpec()));
    }

    // ---- Project names (GRAMMAR §8 item 15). A project name carries no glob structure, so blank is the whole
    //      of its well-formedness and it reports through the shared Code.BlankPattern family under its own
    //      label. One SelectionPatterns arm covers the noun wherever it stands — subject, operand, Except
    //      payload, quarantined scope — and one ConstraintPatterns arm covers the verb. ----

    [Fact]
    public void BlankPattern_BlankProjectNames_AreReportedInOnePass()
    {
        // One blank per position — the noun as a subject, the noun as a target operand, and the verb's own
        // name — all three reported together, each steered to the rule that spells it.
        SpecValidationException ex = ShouldFailToBuild(new BlankProjectNameSpec());

        ex.ShouldHaveErrors(Code.BlankPattern, 3);
        ex.ShouldHaveError(Code.BlankPattern, "project/noun-subject")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:864: Blank project name on 'project/noun-subject'.");
        ex.ShouldHaveError(Code.BlankPattern, "project/noun-operand")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:865: Blank project name on 'project/noun-operand'.");
        ex.ShouldHaveError(Code.BlankPattern, "project/verb")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:866: Blank project name on 'project/verb'.");
    }

    // ---- The correspondence verb's name template (GRAMMAR §8 items 25–26). Two failures, deliberately under
    //      two codes: a blank template is an empty operand like any other, while a template with no {Name} in
    //      it is well-formed text that states a cardinality law instead of a correspondence one. ----

    [Fact]
    public void BlankPattern_BlankCounterpartTemplate_IsReported()
    {
        // Blankness is the slot's first verdict and draws the blank sentence every slot shares, so the
        // label is the only thing that says which operand was left empty.
        SpecValidationException ex = ShouldFailToBuild(new BlankCounterpartTemplateSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:943: Blank counterpart name template on 'area/rule'.");
    }

    [Fact]
    public void CounterpartTemplateWithoutPlaceholder_ConstantName_IsReported()
    {
        // A template with no {Name} derives one fixed name for every subject, so the rule asks "does exactly
        // one IService exist" rather than "does each subject have its own counterpart" — green or red for the
        // whole subject set at once. The message names the template, says what the rule would actually mean,
        // and shows the spelling — which is also how a '{name}' typo is found, the match being ordinal.
        SpecValidationException ex = ShouldFailToBuild(new PlaceholderFreeCounterpartTemplateSpec());

        ex.ShouldHaveError(Code.CounterpartTemplateWithoutPlaceholder, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:953: The counterpart name template 'IService' on 'area/rule' contains no "
                      + "'{Name}' placeholder, so every subject derives the same fixed name — a cardinality claim, not a "
                      + "correspondence; use a template such as 'I{Name}' (substitution is case-sensitive).");
    }

    // ---- A rule's citation (GRAMMAR §8 items 5, 6 and 30). ----

    [Fact]
    public void RepeatedCall_CitationTwice_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new RepeatedCitationSpec());

        ex.ShouldHaveError(Code.RepeatedCall, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1319: Repeated .Citation(...) on 'area/rule'.");
    }

    [Fact]
    public void BlankProse_BlankCitation_IsReportedAsProseAndNotAsMalformed()
    {
        // A whitespace citation is an empty authored field, which item 5 already names; reporting it a second
        // time as a malformed URL would say the same slip twice in different words.
        SpecValidationException ex = ShouldFailToBuild(new BlankCitationSpec());

        ex.ShouldHaveError(Code.BlankProse, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1331: Blank Citation on 'area/rule'.");
        ex.ShouldHaveErrors(Code.MalformedCitation, 0);
    }

    [Theory]
    [InlineData("citation/page-title", 1342, "Reuse HttpClient")]
    [InlineData("citation/relative-path", 1346, "docs/httpclient.md")]
    [InlineData("citation/other-scheme", 1350, "ftp://example.com/guidance.txt")]
    public void MalformedCitation_NotAnAbsoluteHttpUrl_IsReported(string ruleId, int line, string citation)
    {
        // The three shapes a hand-written citation actually takes when it is wrong: the page's title pasted
        // instead of its address, a path into the repository, and a well-formed URI on a scheme no consuming
        // surface can follow. Each names the offending value, because the fix is to replace that text.
        SpecValidationException ex = ShouldFailToBuild(new MalformedCitationSpec());

        ex.ShouldHaveError(Code.MalformedCitation, ruleId)
            .Message
            .ShouldBe($"SpecValidationSpecs.cs:{line}: Malformed citation on '{ruleId}': '{citation}' is not an "
                      + "absolute http(s) URL.");
    }

    [Fact]
    public void ValidProjectNames_BuildWithoutError()
    {
        // Blankness is the whole check: a name is never held against the codebase here, because validation
        // runs before any workspace exists. A name no project carries is a rule that reds at check time — the
        // namespace-glob precedent — not a spec that refuses to build.
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidProjectNamesSpec()));
    }

    // ---- A rule's exemplar (GRAMMAR §8 items 5, 6, 10, 15, 16, 19 and 27). ----

    [Fact]
    public void RepeatedCall_ExemplarTwice_IsReported()
    {
        // Several types to copy go through one union, so a second call is a slip rather than a list.
        SpecValidationException ex = ShouldFailToBuild(new RepeatedExemplarSpec());

        ex.ShouldHaveError(Code.RepeatedCall, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1440: Repeated .Exemplar(...) on 'area/rule'.");
    }

    [Fact]
    public void ForeignSelection_InAnExemplar_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new ForeignExemplarSpec());

        ex.ShouldHaveError(Code.ForeignSelection, "area/rule")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1466: A selection used by 'area/rule' was minted on a different Arch "
                + "instance; it is not registered with this model.");
    }

    [Fact]
    public void BlankPattern_BlankNameInAnExemplar_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new BlankExemplarNameSpec());

        ex.ShouldHaveError(Code.BlankPattern, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1477: Blank type name on 'area/rule'.");
    }

    [Fact]
    public void UnanchoredSubtreePattern_InAnExemplar_IsReported()
    {
        // A glob that can never match would leave the exemplar dead from the first check onwards, and the
        // warning it raised would be the only sign; the spec refuses it where the same glob anywhere else
        // would be refused.
        SpecValidationException ex = ShouldFailToBuild(new DeadExemplarPatternSpec());

        ex.ShouldHaveError(Code.UnanchoredSubtreePattern, "area/rule")
            .Message
            .ShouldBe("SpecValidationSpecs.cs:1488: The namespace pattern 'MyApp.*.Repositories.*' on 'area/rule' has a "
                      + "trailing `.*` subtree operator but its literal prefix contains a `*`, which never matches; "
                      + "anchor the subtree on a literal prefix.");
    }

    [Fact]
    public void BlankProse_BlankWhereDescriptionInAnExemplar_IsReported()
    {
        // The exemplar is printed as written, so a Where description in it is prose a reader sees.
        SpecValidationException ex = ShouldFailToBuild(new BlankExemplarWhereSpec());

        ex.ShouldHaveError(Code.BlankProse, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1499: Blank description on 'area/rule'.");
    }

    [Fact]
    public void UndefinedLifetime_RegisteredNounInAnExemplar_IsReported()
    {
        SpecValidationException ex = ShouldFailToBuild(new UndefinedLifetimeExemplarSpec());

        ex.ShouldHaveError(Code.UndefinedLifetime, "area/rule")
            .Message.ShouldBe("SpecValidationSpecs.cs:1510: '(Lifetime)7' is not a defined Lifetime — " +
                              "use Lifetime.Singleton, Lifetime.Scoped, or Lifetime.Transient (used by 'area/rule').");
    }

    // ---- GRAMMAR §8 item 36: an empty operand list, which only a sequence overload can pass ----

    [Fact]
    public void EmptyOperandList_EachCallThatTakesAList_IsReportedWithWhatItWouldDo()
    {
        // One error per rule, each naming the call and what an empty one does: "empty" alone does not say why
        // the spec is wrong, and the six calls fail in different ways.
        SpecValidationException ex = ShouldFailToBuild(new EmptyOperandListSpec());

        ex.Errors.Select(error => error.Code)
            .ShouldBe(Enumerable.Repeat(Code.EmptyOperandList, 7));
        ex.ShouldHaveError(Code.EmptyOperandList, "area/any-of")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1632: `arch.AnyOf` on 'area/any-of' was given an empty list, so it selects "
                + "nothing; pass at least one operand.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/any-of-types")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1633: `arch.AnyOf` on 'area/any-of-types' was given an empty list, so it "
                + "selects nothing; pass at least one operand.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/project-named")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1634: `Named` on 'area/project-named' was given an empty list, so it selects "
                + "no project; pass at least one project name.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/project-matching")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1635: `Matching` on 'area/project-matching' was given an empty list, so it "
                + "selects no project; pass at least one project name glob.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/member-named")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1636: `Named` on 'area/member-named' was given an empty list, so it selects "
                + "no member; pass at least one member name.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/member-except")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1637: `Except` on 'area/member-except' was given an empty list, so it removes "
                + "nothing; pass at least one member selection.");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/must-not-use")
            .Message.ShouldBe(
                "SpecValidationSpecs.cs:1638: `MustNotUse` on 'area/must-not-use' was given an empty list, so it bans "
                + "nothing; pass at least one member.");
    }

    [Fact]
    public void EmptyOperandList_WhereverTheListNests_IsReportedAtTheStatementThatReachesIt()
    {
        // Every walk the other checks take reaches an empty list too: a union nested in another, a type Except
        // payload, an exemplar, a member Except payload's own adjectives, the projects of a family, a scope.
        SpecValidationException ex = ShouldFailToBuild(new EmptyOperandListPositionsSpec());

        ex.Errors.Select(error => error.RuleId)
            .ShouldBe([
                "area/nested", "area/except-payload", "area/exemplar", "area/member-payload",
                "area/project-family", "legacy/hollow"
            ]);
        ex.ShouldHaveError(Code.EmptyOperandList, "area/nested")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1647: `arch.AnyOf` on 'area/nested' was given");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/except-payload")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1648: `arch.AnyOf` on 'area/except-payload' was given");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/exemplar")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1649: `arch.AnyOf` on 'area/exemplar' was given");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/member-payload")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1650: `Named` on 'area/member-payload' was given");
        ex.ShouldHaveError(Code.EmptyOperandList, "area/project-family")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1651: `Named` on 'area/project-family' was given");
        ex.ShouldHaveError(Code.EmptyOperandList, "legacy/hollow")
            .Message.ShouldStartWith("SpecValidationSpecs.cs:1652: `arch.AnyOf` on 'legacy/hollow' was given");
    }

    [Fact]
    public void EmptyOperandList_AsALayerDefinition_IsReportedSpecWide()
    {
        // A layer's definition is validated at its declaration, used or not, on the spec-wide terms every layer
        // error takes: no rule ID, no location, the layer named outright.
        SpecValidationException ex = ShouldFailToBuild(new EmptyUnionLayerSpec());

        SpecValidationError error = ex.ShouldHaveError(Code.EmptyOperandList);
        error.RuleId.ShouldBeNull();
        error.Message.ShouldBe(
            "`arch.AnyOf` on layer 'Hollow' was given an empty list, so it selects nothing; pass at least one operand.");
    }

    [Fact]
    public void BlankName_InASequenceOverload_IsReportedAsAtItsParamsSibling()
    {
        // A list a helper built rides the walk a params list rides, under the same labels and codes.
        SpecValidationException ex = ShouldFailToBuild(new BlankNameInSequenceSpec());

        ex.ShouldHaveError(Code.BlankProjectPattern, "area/project-names")
            .Message.ShouldBe("SpecValidationSpecs.cs:1668: Blank project name on 'area/project-names'.");
        ex.ShouldHaveError(Code.BlankPattern, "area/member-names")
            .Message.ShouldBe("SpecValidationSpecs.cs:1669: Blank member name on 'area/member-names'.");
    }

    [Fact]
    public void ValidSequenceOverloads_BuildWithoutError()
    {
        Should.NotThrow(() => ArchModelBuilder.Build(new ValidSequenceOverloadsSpec()));
    }
}
