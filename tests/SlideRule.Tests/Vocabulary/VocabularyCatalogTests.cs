using Shouldly;
using SlideRule.Tests.Checking;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using SlideRule.Validation;
using Xunit;

namespace SlideRule.Tests.Vocabulary;

/// <summary>
///     Holds <see cref="VocabularyCatalog" /> to the shipped fluent vocabulary in both directions: every
///     name the reflection sweep finds has a row, every row names something the sweep finds, and every verb
///     row's example is a spec that builds.
/// </summary>
/// <remarks>
///     The generated table's text is held to its hosts elsewhere; this is what keeps that table complete, so a
///     name that ships without a row reds here rather than reaching the documents without its fragment.
/// </remarks>
public sealed class VocabularyCatalogTests
{
    // The one shipped name with no row: a trailer the adjective sweep sees only because it hands the layer
    // back, and taught beside the other trailers rather than in the vocabulary table.
    private static readonly VocabularyKey Exempt = new(VocabularyStratum.Types, PartOfSpeech.Adjective, nameof(Layer.Purpose));

    [Fact]
    public void EveryShippedName_HasARow_AndEveryRowNamesAShippedName()
    {
        // Act
        IReadOnlyList<string> drift = CatalogDrift(VocabularyCatalog.Rows);

        // Assert
        drift.ShouldReportNothing("The vocabulary catalog and the shipped fluent API disagree");
    }

    [Fact]
    public void TheExemption_StillShips_AndHasNoRow()
    {
        // Arrange: the exemption is two-sided, so it cannot outlive the trailer it excuses.
        IEnumerable<VocabularyKey> shipped = ShippedVocabulary.Entries.Select(entry => entry.Key);
        IEnumerable<VocabularyKey> catalogued = VocabularyCatalog.Rows.Select(row => row.Key);

        // Assert
        shipped.ShouldContain(Exempt, $"{Exempt} is exempt from needing a row, but the sweep no longer ships it.");
        catalogued.ShouldNotContain(Exempt, $"{Exempt} is exempt from needing a row, but the catalog carries one.");
    }

    [Fact]
    public void TheCatalog_IsNotVacuous()
    {
        // Arrange: an empty sweep against an empty catalog would agree perfectly and prove nothing.
        List<string> thin = new();

        // Act
        AddFloor(thin, "verb", Count(VocabularyCatalog.Rows, PartOfSpeech.Verb), 50);
        AddFloor(thin, "adjective", Count(VocabularyCatalog.Rows, PartOfSpeech.Adjective), 20);
        AddFloor(thin, "noun and projection",
            Count(VocabularyCatalog.Rows, PartOfSpeech.Noun) + Count(VocabularyCatalog.Rows, PartOfSpeech.Projection), 12);

        // Assert
        thin.ShouldReportNothing("The vocabulary catalog is thinner than the shipped vocabulary can be");
    }

    [Fact]
    public void TheComparison_NamesTheRowTheCatalogLost()
    {
        // Arrange: negative control. With one row dropped, the comparison must report that key and only it.
        VocabularyRow dropped = VocabularyCatalog.Rows.Single(row => row.Name == nameof(SelectionConstraints.MustNotReferenceEachOther));
        List<VocabularyRow> rows = VocabularyCatalog.Rows
            .Where(row => row != dropped)
            .ToList();

        // Act
        IReadOnlyList<string> drift = CatalogDrift(rows);

        // Assert
        drift.ShouldBe([$"no row for {dropped.Key}"]);
    }

    [Fact]
    public void EveryVerbRow_BuildsAValidSpec()
    {
        // Arrange: the table must never show a combination the spec builder refuses.
        List<string> refused = new();
        IEnumerable<VocabularyRow> verbs = VocabularyCatalog.Rows.Where(row => row.PartOfSpeech == PartOfSpeech.Verb);

        // Act
        foreach (VocabularyRow row in verbs)
            try
            {
                Checker.Model(arch => arch.Rule("vocabulary/example")
                    .Enforce((Constraint)row.Build(arch))
                    .Because("The vocabulary table shows only legal combinations."));
            }
            catch (SpecValidationException exception)
            {
                refused.Add($"{row.Key}: {exception.Message}");
            }

        // Assert
        refused.ShouldReportNothing("These verb rows build a spec the spec builder refuses");
    }

    [Fact]
    public void Compose_IsDeterministic()
    {
        // Act
        string first = VocabularyBlockComposer.Compose();
        string second = VocabularyBlockComposer.Compose();

        // Assert
        second.ShouldBe(first);
    }

    [Fact]
    public void Row_RefusesACellCarryingAPipe()
    {
        // Arrange: a '|' inside a cell would end the cell early and shift every column after it.
        VocabularyRow piped = new(
            VocabularyStratum.Types,
            PartOfSpeech.Verb,
            nameof(SelectionConstraints.Must),
            arch => arch.Types.Must(_ => true, description: "be either | or"));

        // Act
        var refusal = Should.Throw<InvalidOperationException>(() => VocabularyBlockComposer.Row(piped));

        // Assert
        refusal.Message.ShouldContain("Types Verb Must");
    }

    // Missing keys first, then rows naming nothing shipped, then duplicate rows, each in a stable order.
    private static IReadOnlyList<string> CatalogDrift(IEnumerable<VocabularyRow> rows)
    {
        List<VocabularyKey> catalogued = rows.Select(row => row.Key)
            .ToList();
        HashSet<VocabularyKey> shipped = ShippedVocabulary.Entries
            .Select(entry => entry.Key)
            .Where(key => key != Exempt)
            .ToHashSet();
        HashSet<VocabularyKey> catalogSet = catalogued.ToHashSet();

        IEnumerable<string> missing = shipped
            .Where(key => !catalogSet.Contains(key))
            .Select(key => $"no row for {key}");
        IEnumerable<string> extra = catalogued
            .Where(key => !shipped.Contains(key))
            .Select(key => $"a row for {key}, which the shipped API does not carry");
        IEnumerable<string> duplicated = catalogued
            .GroupBy(key => key)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Count()} rows for {group.Key}");

        return [.. missing, .. extra, .. duplicated];
    }

    private static int Count(IEnumerable<VocabularyRow> rows, PartOfSpeech partOfSpeech)
    {
        return rows.Count(row => row.PartOfSpeech == partOfSpeech);
    }

    private static void AddFloor(List<string> thin, string kind, int count, int floor)
    {
        if (count <= floor) thin.Add($"{count} {kind} rows, where more than {floor} ship");
    }
}
