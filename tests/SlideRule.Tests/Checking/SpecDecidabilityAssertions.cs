using Shouldly;
using SlideRule.Checking;
using SlideRule.Hosting;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The Shouldly surface over one spec's one-project split: which of its rules a reader that sees one
///     project's compilation cannot decide.
/// </summary>
/// <remarks>
///     <para>
///         A list rather than a count, because the interesting question when a split moves is
///         <em>which</em> rule moved: a rule rewritten into a predicate, or a layer respelled from a
///         project to a namespace glob, leaves its spec silently less checkable early, and the name is
///         what says so. The reasons ride in the failure text, because the first question a move raises
///         is why.
///     </para>
///     <para>
///         Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason
///         <c>RuleResultAssertions</c> records: leaving it off makes an inner failure name the read under
///         test rather than the caller's local.
///     </para>
/// </remarks>
internal static class SpecDecidabilityAssertions
{
    /// <summary>
    ///     Asserts <paramref name="model" /> leaves exactly <paramref name="notDecidable" /> to the whole
    ///     solution, ordinally sorted.
    /// </summary>
    /// <param name="model">The spec to classify, rule by rule.</param>
    /// <param name="notDecidable">The rule IDs a one-project reader cannot settle, ordinally sorted.</param>
    internal static void ShouldLeaveExactlyTheseToTheWholeSolution(
        this ArchitectureModel model, IReadOnlyList<string> notDecidable)
    {
        List<(string Id, string Reason)> undecidable = model.Rules
            .Select(rule => (rule.Id, OneProjectDecidability.Classify(rule).Reason))
            .Where(rule => rule.Reason is not null)
            .Select(rule => (rule.Id, Reason: rule.Reason!))
            .OrderBy(rule => rule.Id, StringComparer.Ordinal)
            .ToList();

        string reasons = string.Join("\n", undecidable.Select(rule => $"{rule.Id} — {rule.Reason}"));
        undecidable.Select(rule => rule.Id)
            .ShouldBe(notDecidable, $"What the classifier says today:\n{reasons}");
    }
}
