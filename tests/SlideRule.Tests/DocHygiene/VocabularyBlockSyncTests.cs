using Shouldly;
using SlideRule.Rendering;
using SlideRule.Tests.TestSupport;
using SlideRule.Tests.Vocabulary;
using Xunit;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The gate over the generated vocabulary table: the body between the managed-block markers in
///     <c>GRAMMAR.md</c> and in the <c>derive_spec</c> recipe must be exactly what
///     <see cref="VocabularyBlockComposer.Compose" /> renders from the shipped API today.
/// </summary>
/// <remarks>
///     One body serves both hosts, so the grammar and the recipe can never teach two different tables. The
///     suite never rewrites either committed file: on drift it writes the whole file, regenerated, beside the
///     test assembly and names the copy that would land it, so a person reads every changed row first.
/// </remarks>
public sealed class VocabularyBlockSyncTests
{
    /// <summary>The two files that carry the table, by repository-relative path.</summary>
    public static TheoryData<string> Hosts => [DocPaths.Grammar, DocPaths.DeriveSpecPrompt];

    [Theory]
    [MemberData(nameof(Hosts))]
    public void TheCommittedTable_IsWhatTheShippedApiRenders(string path)
    {
        // Arrange
        string committed = RepoRoot.ReadText(path);
        ManagedBlocks.MarkerPairCount(committed)
            .ShouldBe(1, $"{path} must carry exactly one pair of managed-block markers around its vocabulary table.");
        string committedBody = ManagedBlock.ExtractBody(committed)!;
        string composedBody = VocabularyBlockComposer.Compose();
        List<string> drift = new();

        // Act
        if (!string.Equals(committedBody, composedBody, StringComparison.Ordinal))
        {
            IReadOnlyList<string> moved = RowDrift(committedBody, composedBody);
            string spliced = ManagedBlock.Splice(committed, composedBody);
            drift.AddRange(RegeneratedFiles.DriftReport(path, moved, spliced));
        }

        // Assert
        drift.ShouldReportNothing(
            $"{path}'s vocabulary table is not what the shipped API renders. Read every changed row, and once "
            + "each is intended, copy the regenerated file over the committed one");
    }

    [Fact]
    public void RowDrift_ListsATwinRowTheRenderDropped()
    {
        // Arrange
        string committed = string.Join("\n", "| Verb |", "|---|", "| `.MustBeStatic` |", "|---|", "| `.MustBeStatic` |", "");
        string composed = string.Join("\n", "| Verb |", "|---|", "| `.MustBeStatic` |", "|---|", "");

        // Act
        IReadOnlyList<string> drift = RowDrift(committed, composed);

        // Assert
        drift.ShouldBe(["- | `.MustBeStatic` |"]);
    }

    [Fact]
    public void RowDrift_NamesAReorderingOfTheSameRows()
    {
        // Arrange
        string committed = string.Join("\n", "| `.MustBeStatic` |", "| `.MustBeSealed` |", "");
        string composed = string.Join("\n", "| `.MustBeSealed` |", "| `.MustBeStatic` |", "");

        // Act
        IReadOnlyList<string> drift = RowDrift(committed, composed);

        // Assert
        drift.ShouldBe(["the table holds exactly these rows, in a different order"]);
    }

    // What moved between the committed table and the render: + for each copy of a row the render has beyond the
    // committed count, - for the reverse. The table repeats rows (every separator, the header rows, a verb listed
    // under two subjects), so the diff counts copies; a set diff cannot see a verb lose one of its twin rows.
    private static IReadOnlyList<string> RowDrift(string committed, string composed)
    {
        // A checkout that rewrote the file to CRLF would otherwise report every row as both added and removed.
        string[] before = committed.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string[] after = composed.Split('\n');

        List<string> drift = [.. Surplus(after, before, '+'), .. Surplus(before, after, '-')];
        if (drift.Count == 0 && !before.SequenceEqual(after, StringComparer.Ordinal))
            drift.Add("the table holds exactly these rows, in a different order");

        return drift;
    }

    // Each row of lines once for every copy beyond its count in other, in the order the rows stand.
    private static IEnumerable<string> Surplus(string[] lines, string[] other, char sign)
    {
        Dictionary<string, int> unmatched = other.CountBy(static line => line, StringComparer.Ordinal)
            .ToDictionary(StringComparer.Ordinal);

        foreach (string line in lines)
            if (unmatched.TryGetValue(line, out int left) && left > 0)
                unmatched[line] = left - 1;
            else
                yield return $"{sign} {(line.Length == 0 ? "(blank line)" : line)}";
    }
}
