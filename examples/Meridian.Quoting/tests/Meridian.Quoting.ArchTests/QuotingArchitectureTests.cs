using Meridian.Quoting.Architecture;
using SlideRule.Xunit;

namespace Meridian.Quoting.ArchTests;

/// <summary>
///     The xUnit adapter pattern for the quoting spec: derive from
///     <c>ArchRuleTests&lt;QuotingArchitectureSpec&gt;</c> and every rule in
///     <see cref="QuotingArchitectureSpec" /> becomes its own individually named test — the rule ID is the
///     test's display name, so a violated architecture rule reads as a named failing test whose
///     message is the exact CLI human block.
/// </summary>
public sealed class QuotingArchitectureTests : ArchRuleTests<QuotingArchitectureSpec>
{
    protected override string SolutionPath => FindSolutionUp("Meridian.Quoting.slnx");
}
