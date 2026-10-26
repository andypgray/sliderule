using Shouldly;
using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     What the reader promises about text it did not write: a damaged file is a refusal and never an
///     exception, and whatever it does accept it can write again.
/// </summary>
public sealed class ModelDocumentReaderRobustnessTests
{
    [Fact]
    public void Read_TheSampleWithAnyOneCharacterDeletedOrReplaced_AnswersWithoutThrowing()
    {
        string valid = ModelFileSpecs.SampleText();
        valid.ShouldReadBack();

        var refused = 0;
        var accepted = 0;
        foreach (string damaged in DamagedText.OneCharacterAway(valid))
            ShouldAnswer(damaged, ref refused, ref accepted);

        // Most damage breaks the file and some only changes a word of prose, so both answers must occur:
        // a sweep that only ever refused, or only ever accepted, would not have exercised the other path.
        refused.ShouldBeGreaterThan(valid.Length);
        accepted.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void Read_TheSampleCutShortAtEveryLength_RefusesWithoutThrowing()
    {
        string valid = ModelFileSpecs.SampleText();

        foreach (string cut in DamagedText.CutShort(valid))
        {
            ModelReadResult result = ModelDocumentReader.Read(cut);

            result.Document.ShouldBeNull($"A file cut to {cut.Length} characters read as a whole document.");
            result.Refusal.ShouldNotBeNull();
        }
    }

    [Fact]
    public void Read_AScopeCarryingADragonsDocument_ReadsThePathBack()
    {
        string text = ModelFileSpecs.TextOf(arch => arch.Scope("legacy/billing")
            .Caution(arch.Namespace("MyApp.Legacy.Billing.*"))
            .DragonsDoc("arch/billing-dragons.md")
            .Because("b"));

        text.ShouldContain("\"dragonsDoc\": \"arch/billing-dragons.md\"");
        ModelDocument parsed = text.ShouldReadBack();
        parsed.Rules.ShouldHaveSingleItem()
            .Scope.ShouldNotBeNull()
            .DragonsDoc.ShouldBe("arch/billing-dragons.md");
        ModelDocumentComposer.Compose(parsed)
            .ShouldBe(text);
    }

    [Fact]
    public void Read_ABannedMember_KeepsItsNameAndAnchorButHasNoReflectedTypeToGive()
    {
        string text = ModelFileSpecs.TextOf(arch => arch.Rule("time/no-wall-clock")
            .Enforce(arch.Types.MustNotUse(arch.Member(typeof(DateTime), nameof(DateTime.Now))))
            .Because("b"));

        ModelDocument parsed = text.ShouldReadBack();
        Member member = parsed.Rules.ShouldHaveSingleItem()
            .Constraint.ShouldNotBeNull()
            .MemberOperands.ShouldHaveSingleItem();

        member.Name.ShouldBe("Now");
        member.IsMethod.ShouldBeFalse();
        member.DeclaringAnchor.FullName.ShouldBe("System.DateTime");

        // Only spec validation reads the reflected type, and a model that came from a file is never
        // validated; a reader that asked anyway must not be handed a null.
        Should.Throw<InvalidOperationException>(() => member.DeclaringType);
    }

    [Fact]
    public void MemberSelectionsOf_AKindOutsideTheEnum_ThrowsRatherThanGuessingAClass()
    {
        Selection source = Checker.OneRule(arch => arch.Types.MustBeSealed())
            .Constraint!.Subject!;

        Should.Throw<InvalidOperationException>(() => MemberSelections.Of(source, (MemberKindFilter)99, []));
    }

    [Fact]
    public void Compose_ADocumentBuiltByHandAroundARuleItCannotWrite_ThrowsNamingTheRule()
    {
        ArchitectureModel model = Checker.Model(arch => arch.Rule("doors/front")
            .Enforce(arch.Types.Must(_ => true, "be a type somebody names"))
            .Because("b"));
        ModelDocument.From(model, "Spec", [])
            .Rules.ShouldBeEmpty();
        var byHand = new ModelDocument("Spec", [], model.Layers, model.Rules, []);

        var thrown = Should.Throw<InvalidOperationException>(() => ModelDocumentComposer.Compose(byHand));

        thrown.Message.ShouldContain("doors/front");
        thrown.Message.ShouldContain("A Must predicate is code, and this file carries data only.");
    }

    // A damaged file must produce exactly one answer, and an accepted one must be writable again and
    // stable once written: whatever the reader lets through is a document like any other.
    private static void ShouldAnswer(string damaged, ref int refused, ref int accepted)
    {
        ModelReadResult result = ModelDocumentReader.Read(damaged);

        if (result.Refusal is not null)
        {
            result.Document.ShouldBeNull();
            refused++;
            return;
        }

        string rewritten = ModelDocumentComposer.Compose(result.Document.ShouldNotBeNull());
        accepted++;
        if (string.Equals(rewritten, damaged, StringComparison.Ordinal)) return;

        ModelDocument reread = rewritten.ShouldReadBack();
        ModelDocumentComposer.Compose(reread)
            .ShouldBe(rewritten);
    }
}
