using System.Reflection;
using Shouldly;
using SlideRule.Validation;
using Xunit;
using Code = SlideRule.Validation.SpecValidationErrorCode;

namespace SlideRule.Tests;

/// <summary>
///     The owners of the spec catalog's literal-decidable verdicts, held directly rather than through the
///     validator: the slot inventory, the two sentence frames, and the two several-literal walks whose
///     emission order and tags the validator's own error list depends on.
/// </summary>
public sealed class SpecVerdictOwnerTests
{
    [Fact]
    public void All_HoldsEverySlotOfTheAuthoringSurface()
    {
        // Nineteen pattern slots, nine prose slots, and seven that are neither.
        SpecSlot.All.Count
            .ShouldBe(35);
    }

    [Fact]
    public void All_IsEveryDeclaredSlot_EachNamedForItsOwnField()
    {
        // Name is how a caller holding no reference to the instance says which slot it means, so a field
        // renamed away from its own nameof would leave that caller naming nothing.
        List<FieldInfo> declared = typeof(SpecSlot)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.FieldType == typeof(SpecSlot))
            .ToList();
        List<string> fieldNames = declared.Select(field => field.Name).ToList();
        List<string> slotNames = SpecSlot.All.Select(slot => slot.Name).ToList();

        foreach (FieldInfo field in declared)
        {
            var slot = (SpecSlot)field.GetValue(null)!;
            slot.Name.ShouldBe(field.Name);
        }

        slotNames.ShouldBe(fieldNames, ignoreOrder: true);
        slotNames.ShouldBeUnique();
    }

    [Fact]
    public void All_CarriesTheNounEachSlotsMessagesNameItsValueBy()
    {
        // Every blank-operand message is "Blank {label} on {subject}.", so the label is the only thing that
        // says which operand an author left empty. Pinned whole and in order: a label is read by anyone
        // holding the warning and by nobody holding the code, so a silent edit to one renames the value in
        // every message it appears in.
        List<string> vocabulary = SpecSlot.All
            .Select(slot => $"{slot.Name}: {slot.Label}")
            .ToList();

        vocabulary.ShouldBe([
            "NamespacePattern: namespace pattern",
            "NamePattern: name pattern",
            "ExactTypeName: type name",
            "Suffix: suffix",
            "Prefix: prefix",
            "MemberNamePattern: member name pattern",
            "MemberSuffix: member suffix",
            "MemberPrefix: member prefix",
            "ExactMemberName: member name",
            "AttributeName: attribute name",
            "InterfaceName: interface name",
            "BaseTypeName: base type name",
            "ReturnTypeName: return type name",
            "ParameterTypeName: parameter type name",
            "CounterpartTemplate: counterpart name template",
            "ProjectName: project name",
            "ProjectNamePattern: project name pattern",
            "ProjectNameOperand: project name",
            "Because: Because",
            "Fix: Fix",
            "Citation: Citation",
            "MigrateFrom: Migrate from",
            "Baseline: Baseline",
            "Dragons: Dragons",
            "DragonsDoc: DragonsDoc",
            "Description: description",
            "Purpose: purpose",
            "TargetFramework: target framework",
            "PackagePattern: package pattern",
            "PropertyName: property name",
            "PropertyValue: property value",
            "MemberName: member name",
            "RuleId: rule ID",
            "ScopeId: scope ID",
            "LayerName: layer name"
        ]);
    }

    [Fact]
    public void ByName_RoundTripsEverySlot()
    {
        foreach (SpecSlot slot in SpecSlot.All)
            SpecSlot.ByName(slot.Name)
                .ShouldBeSameAs(slot);

        SpecSlot.ByName("NotASlot")
            .ShouldBeNull();
    }

    [Fact]
    public void About_NamesTheSubjectInOneFrameAndLeavesItOutOfTheOther()
    {
        LiteralVerdict verdict = LiteralVerdict.About(Code.BlankPattern, "Blank suffix", ".");

        verdict.NamesSubject.ShouldBeTrue();
        verdict.On("'area/rule'")
            .ShouldBe("Blank suffix on 'area/rule'.");
        verdict.AtTheLiteral.ShouldBe("Blank suffix.");
    }

    [Fact]
    public void Whole_ReadsTheSameInBothFrames()
    {
        // The value the subject clause would name is the one that is wrong, so there is nowhere to put a
        // subject and nothing is lost by leaving it out.
        LiteralVerdict verdict = LiteralVerdict.Whole(Code.MalformedId, "Malformed ID 'Bad_Id'.");

        verdict.NamesSubject.ShouldBeFalse();
        verdict.On("'Bad_Id'")
            .ShouldBe("Malformed ID 'Bad_Id'.");
        verdict.AtTheLiteral.ShouldBe("Malformed ID 'Bad_Id'.");
        verdict.Tail.ShouldBe(string.Empty);
    }

    [Fact]
    public void IdCollisions_TwoRulesWithOneId_IsOneVerdictTaggedAtBothDeclarations()
    {
        // Tags[0] is the first occurrence, which is where the catalog reports the collision.
        IReadOnlyList<CollisionVerdict> verdicts = SpecIdCollisions.Decide([Rule("area/rule", 0), Rule("area/rule", 1)]);

        CollisionVerdict verdict = verdicts.ShouldHaveSingleItem();
        verdict.Code.ShouldBe(Code.DuplicateId);
        verdict.Key.ShouldBe("area/rule");
        verdict.Sentence.ShouldBe("Duplicate rule ID 'area/rule'.");
        verdict.Tags.ShouldBe([0, 1]);
    }

    [Fact]
    public void IdCollisions_ThreeRulesWithOneId_IsStillOneVerdict()
    {
        // One verdict per colliding key, however many declarations carry it: the ID names one thing the
        // model has to be able to find, and it is taken once.
        IReadOnlyList<CollisionVerdict> verdicts =
            SpecIdCollisions.Decide([Rule("area/rule", 0), Rule("area/rule", 1), Rule("area/rule", 2)]);

        verdicts.ShouldHaveSingleItem()
            .Tags.ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void IdCollisions_TwoScopesWithOneId_NamesBothChildrenTheScopeDesugarsInto()
    {
        // Every scope reserves both children whatever its posture, so a scope ID declared twice collides
        // twice, on two IDs that appear nowhere in the spec source.
        IReadOnlyList<CollisionVerdict> verdicts =
            SpecIdCollisions.Decide([Scope("legacy/billing", 0), Scope("legacy/billing", 1)]);

        verdicts.Select(verdict => verdict.Key)
            .ShouldBe(["legacy/billing/containment", "legacy/billing/tripwire"]);
        verdicts.ShouldAllBe(verdict => verdict.Code == Code.DuplicateId);
        verdicts[0].Sentence.ShouldBe("Duplicate rule ID 'legacy/billing/containment'.");
        verdicts[0].Tags.ShouldBe([0, 1]);
    }

    [Fact]
    public void IdCollisions_RuleBeneathAScopesPrefix_IsReportedByTheMoreSpecificCode()
    {
        IReadOnlyList<CollisionVerdict> verdicts =
            SpecIdCollisions.Decide([Scope("legacy/billing", 0), Rule("legacy/billing/extra", 1)]);

        CollisionVerdict verdict = verdicts.ShouldHaveSingleItem();
        verdict.Code.ShouldBe(Code.IdExtendsScope);
        verdict.Key.ShouldBe("legacy/billing/extra");
        verdict.Sentence.ShouldBe("Rule ID 'legacy/billing/extra' extends scope 'legacy/billing'; "
                                  + "the 'legacy/billing/…' namespace is reserved for its generated children.");
        verdict.Tags.ShouldBe([1, 0]);
    }

    [Fact]
    public void IdCollisions_RuleExtendingAScope_TakesNoPartInTheDuplicateSet()
    {
        // Both rules reach into the reserved namespace, so each is reported once by the more specific code
        // and neither is reported again as a duplicate of the other.
        IReadOnlyList<CollisionVerdict> verdicts = SpecIdCollisions.Decide(
            [Scope("legacy/billing", 0), Rule("legacy/billing/extra", 1), Rule("legacy/billing/extra", 2)]);

        verdicts.Select(verdict => verdict.Code)
            .ShouldBe([Code.IdExtendsScope, Code.IdExtendsScope]);
        verdicts.Select(verdict => verdict.Tags[0])
            .ShouldBe([1, 2]);
    }

    [Fact]
    public void IdCollisions_BothKinds_ReportsEveryExtendsScopeBeforeEveryDuplicate()
    {
        // The duplicate is declared first and still reports second, because the two kinds are emitted in
        // blocks rather than interleaved by declaration.
        IReadOnlyList<CollisionVerdict> verdicts = SpecIdCollisions.Decide([
            Rule("area/rule", 0),
            Rule("area/rule", 1),
            Scope("legacy/billing", 2),
            Rule("legacy/billing/extra", 3)
        ]);

        verdicts.Select(verdict => verdict.Code)
            .ShouldBe([Code.IdExtendsScope, Code.DuplicateId]);
        verdicts.Select(verdict => verdict.Key)
            .ShouldBe(["legacy/billing/extra", "area/rule"]);
    }

    [Fact]
    public void LayerNames_ThreeLayersNamedAlike_IsOneVerdictPerRepeat()
    {
        // The opposite shape to an ID collision: each re-use of a name is its own slip, reported at the
        // repeat with the first occurrence named second.
        IReadOnlyList<CollisionVerdict> verdicts = SpecLayerNames.Decide(["Dup", "Dup", "Dup"]);

        verdicts.Count.ShouldBe(2);
        verdicts.ShouldAllBe(verdict => verdict.Code == Code.DuplicateLayerName);
        verdicts[0].Sentence.ShouldBe("Duplicate layer name 'Dup'.");
        verdicts[0].Tags.ShouldBe([1, 0]);
        verdicts[1].Tags.ShouldBe([2, 0]);
    }

    [Fact]
    public void LayerNames_BlankNames_NeverCollide()
    {
        // Two unnamed layers are two instances of one mistake, which the layer-name slot reports under its
        // own code; "Duplicate layer name ''." would name neither of them.
        IReadOnlyList<CollisionVerdict> verdicts = SpecLayerNames.Decide(["", "   ", "\t"]);

        verdicts.ShouldBeEmpty();
    }

    private static IdDeclaration Rule(string id, int tag)
    {
        return new IdDeclaration(id, IdRole.Rule, tag);
    }

    private static IdDeclaration Scope(string id, int tag)
    {
        return new IdDeclaration(id, IdRole.Scope, tag);
    }
}
