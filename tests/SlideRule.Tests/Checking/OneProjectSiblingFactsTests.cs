using System.Text.RegularExpressions;
using Shouldly;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Holds the checker to reading only facts a type from another project can answer the same way
///     whichever form that project was referenced in — which is what lets one project's compilation be
///     checked on its own and reach the verdict a check over the whole solution reaches.
/// </summary>
/// <remarks>
///     <para>
///         A type another project declares is carried into such a view with its scalar facts and its
///         hierarchy and nothing else: no members, because the inventory read from a compiled image is not
///         the one read from source; and, of the hierarchy, only the transitive forms — a type's
///         <c>Interfaces</c>, <c>Attributes</c> and <c>BaseType</c> carry the direct ones, which an image
///         states differently, and <c>IsRecord</c> is a source fact a <c>record struct</c> loses on the way
///         into metadata.
///     </para>
///     <para>
///         Over the tracked source text rather than over references, because the claim is about a whole
///         directory rather than about one caller, and because a mention in a comment is a reader being
///         told the slot is available there. The search is asserted to still find the place a type's
///         hierarchy is legitimately read, so a pattern that stopped matching anything cannot pass as
///         "nothing reads it".
///     </para>
/// </remarks>
public sealed class OneProjectSiblingFactsTests
{
    // Where a type's hierarchy is populated, and so the proof that the pattern below still matches
    // something when it matches nothing under Checking/.
    private const string HierarchyIsPopulated = "src/SlideRule.Extraction/FragmentMerger.cs";

    // The four slots, each read off its receiver so a MEMBER's own attributes are told from a type's: a
    // member is only ever reached through a subject's own inventory, which is this compilation's.
    private static readonly Regex SiblingUnsafeRead =
        new(@"(?<receiver>[A-Za-z_][A-Za-z0-9_]*)?\.(?<slot>Interfaces|Attributes|BaseType|IsRecord)\b");

    private static readonly string[] MemberReceivers = ["member", "IMemberInfo"];

    // Every source under Checking/ that names Members at all: MemberSelectionEvaluator alone, which reads a
    // type's inventory, and only the SUBJECT's, which is never a sibling's, because a sibling carries none
    // and so matches no member selection. A second file here is what a maintainer has to look at.
    private static readonly string[] MemberReadingSources =
    [
        CheckerSources.Root + "MemberSelectionEvaluator.cs"
    ];

    private static readonly Regex MemberRead = new(@"\.Members\b");

    [Fact]
    public void NoCheckingSource_ReadsAFactATypeFromAnotherProjectCannotAnswer()
    {
        // Arrange
        IReadOnlyList<string> checkingSources = CheckerSources.All;

        // Act
        List<string> reads = checkingSources.SelectMany(SiblingUnsafeReads)
            .ToList();

        // Assert: the floor first, so a sweep that reached no file says so rather than reporting nothing.
        checkingSources.Count.ShouldBeGreaterThan(5, "the sweep reached the checker's own sources");
        SiblingUnsafeReads(HierarchyIsPopulated)
            .ShouldNotBeEmpty("the search no longer finds the place a type's hierarchy is populated");
        reads.ShouldReportNothing(
            "The checker now reads a type's Interfaces, Attributes, BaseType or IsRecord. A type from "
            + "another project carries those differently depending on how that project was referenced, so a "
            + "check over one project's compilation would give the build a different verdict from a check "
            + "over the whole solution");
    }

    [Fact]
    public void TheOnlyCheckingSourceThatReadsMembers_IsTheOneRecordedHere()
    {
        // Arrange
        IReadOnlyList<string> checkingSources = CheckerSources.All;

        // Act
        List<string> reading = checkingSources.Where(path => MemberRead.IsMatch(RepoRoot.ReadText(path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        // Assert: the recorded set is reached at all, then nothing outside it.
        reading.ShouldContain(CheckerSources.Root + "MemberSelectionEvaluator.cs");
        reading.Where(path => !MemberReadingSources.Contains(path, StringComparer.Ordinal))
            .ToList()
            .ShouldReportNothing(
                "A checking source outside the recorded set now reads Members. A type from another project "
                + "carries none, so if this is a type's inventory rather than an admitted set, a check over "
                + "one project's compilation would give the build a different verdict from a check over the "
                + "whole solution");
    }

    // Each read of one of the four slots in one tracked file, as path:line, minus the reads whose receiver
    // is a member rather than a type.
    private static IEnumerable<string> SiblingUnsafeReads(string trackedPath)
    {
        string[] lines = RepoRoot.ReadLines(trackedPath);

        for (var index = 0; index < lines.Length; index++)
            foreach (Match match in SiblingUnsafeRead.Matches(lines[index]))
            {
                string receiver = match.Groups["receiver"].Value;
                if (MemberReceivers.Contains(receiver, StringComparer.Ordinal)) continue;

                yield return $"{trackedPath}:{index + 1} ({match.Value.Trim()})";
            }
    }
}
