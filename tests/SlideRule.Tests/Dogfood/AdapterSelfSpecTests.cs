using SlideRule.Architecture;
using SlideRule.Xunit;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     The adapter dogfood: SlideRule's own self-spec, run through the xUnit adapter. Each of its rules
///     surfaces as an individually named test (the rule ID is the display name) and is green. The
///     <see cref="ArchRuleTests{TSpec}" /> default
///     <c>ExcludeProjectName</c> drops the spec's own project (<c>SlideRule.Architecture</c>) from the
///     checked universe, exactly as the CLI self-check does. Its <c>SolutionPath</c> resolves through the
///     shipped <c>FindSolutionUp</c> helper, so the dogfood runs the exact line a consumer writes.
/// </summary>
public sealed class AdapterSelfSpecTests : ArchRuleTests<SlideRuleArchitectureSpec>
{
    protected override string SolutionPath => FindSolutionUp("SlideRule.slnx");
}
