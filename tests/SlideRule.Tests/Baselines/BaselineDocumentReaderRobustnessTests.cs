using Shouldly;
using SlideRule.Baselines;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     What the reader promises about text it did not write: a damaged baseline file is an answer and
///     never an exception, and the answer is exactly one of a document and a refusal.
/// </summary>
/// <remarks>
///     Both shipped versions are swept, because they fail in different places — a current file verifies
///     entry by entry as it is read, a legacy one recomputes one digest over all of them after the walk
///     is done, and only a sweep over both reaches the second path with a damaged entry in hand.
/// </remarks>
public sealed class BaselineDocumentReaderRobustnessTests
{
    [Theory]
    [InlineData(BaselineFormat.SchemaVersion)]
    [InlineData(BaselineFormat.LegacySchemaVersion)]
    public void Read_TheSampleWithAnyOneCharacterDeletedOrReplaced_AnswersWithoutThrowing(int schemaVersion)
    {
        string valid = Sample(schemaVersion);
        BaselineDocumentReader.Read(valid)
            .Refusal.ShouldBeNull();

        var refused = 0;
        var accepted = 0;
        foreach (string damaged in DamagedText.OneCharacterAway(valid))
            ShouldAnswer(damaged, ref refused, ref accepted);

        // Most damage breaks the file and some only moves a space, so both answers must occur: a sweep
        // that only ever refused, or only ever accepted, would not have exercised the other path.
        refused.ShouldBeGreaterThan(valid.Length);
        accepted.ShouldBeGreaterThan(0);
    }

    [Theory]
    [InlineData(BaselineFormat.SchemaVersion)]
    [InlineData(BaselineFormat.LegacySchemaVersion)]
    public void Read_TheSampleCutShortAtEveryLength_RefusesWithoutThrowing(int schemaVersion)
    {
        string valid = Sample(schemaVersion);

        foreach (string cut in DamagedText.CutShort(valid))
        {
            BaselineReadResult result = BaselineDocumentReader.Read(cut);

            result.Document.ShouldBeNull($"A file cut to {cut.Length} characters read as a whole document.");
            result.Refusal.ShouldNotBeNull();
        }
    }

    // A real composition rather than a hand-written file, so every seal and the legacy digest are the
    // ones the product computes: an edge entry with both riders, a bare edge, and a subject entry in a
    // second section. The legacy version carries no count, its grammar having none.
    private static string Sample(int schemaVersion)
    {
        BaselineEntry attributed = BaselineEntry.ForEdge("T:App.Web.Old", "T:App.Data.Db")
            .WithBecause("INC-1234");
        BaselineEntry bare = BaselineEntry.ForEdge("T:App.Web.Older", "T:App.Data.Db");
        BaselineEntry subject = BaselineEntry.ForSubject("T:App.Legacy.Thing");

        if (!BaselineFormat.CarriesSiteCount(schemaVersion))
            return BaselineComposer.ComposeLegacy(
                ("data/x", [attributed, bare]), ("legacy/billing/containment", [subject]));

        return BaselineComposer.Compose(
            ("data/x", [attributed.WithSiteCount(2), bare]), ("legacy/billing/containment", [subject]));
    }

    // A damaged file must produce exactly one answer, and producing it must not throw: whatever the
    // reader lets through is a document like any other, and whatever it refuses is a refusal value.
    private static void ShouldAnswer(string damaged, ref int refused, ref int accepted)
    {
        BaselineReadResult result = BaselineDocumentReader.Read(damaged);

        if (result.Refusal is not null)
        {
            result.Document.ShouldBeNull();
            refused++;
            return;
        }

        result.Document.ShouldNotBeNull();
        accepted++;
    }
}
