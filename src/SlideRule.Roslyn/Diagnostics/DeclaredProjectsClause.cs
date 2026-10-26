using SlideRule.Internal;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The clause a partial-coverage notice opens with: how many declared projects the run did not reach, and
///     what it did not do to them.
/// </summary>
internal static class DeclaredProjectsClause
{
    // Inflected in both numbers: "1 projects ... were not checked" is the sentence a reader stops trusting.
    // One owner, because a reader comparing two surfaces reads a difference in wording as a difference in fact.
    internal static string Subject(int count, string participle)
    {
        return $"{Plurals.Counted(count, "project")} the solution declares {Plurals.PastVerb(count)} not {participle}";
    }
}
