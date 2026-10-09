using Xunit;
using SlideRule.Tests.Checking;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     What this repository's own spec costs a reader that sees one project's compilation: which of its
///     rules such a reader cannot decide, named one by one.
/// </summary>
/// <remarks>
///     Rewriting a rule is allowed to move this list — in the same change, with the reason read off the
///     table. <c>SpecDecidabilityAssertions</c> carries the claim and says why it is worth making.
/// </remarks>
public sealed class SelfSpecDecidabilityTests
{
    /// <summary>
    ///     The rules of this repository's own spec whose verdict takes the whole solution, ordinally
    ///     sorted: the residency rule, the four front doors, whose coverage verb is held out whole, the two
    ///     other rules that narrow by a predicate, four packaging verbs, the cross-cell ban and the cycle
    ///     gate, the registration noun, and the two scopes' tripwires. Not one of them is here for a
    ///     project noun, though every layer in this spec is defined by a project or cut out of one that is:
    ///     a project noun is out only where it spares a type by plain membership, and no rule here spells
    ///     one inside an <c>Except</c>. Every other rule that names a layer is decidable, a layer being
    ///     read where it stands.
    /// </summary>
    private static readonly string[] NotDecidable =
    [
        "api/core-front-door",
        "api/extraction-front-door",
        "api/host-front-door",
        "api/workspace-front-door",
        "di/no-captive-dependencies",
        "layering/leaves-independent",
        "layering/no-circular-references",
        "mcp/tool-types-in-cli",
        "model/prose-fragments/tripwire",
        "naming/async-suffix",
        "packaging/core-carries-nothing",
        "packaging/core-netstandard-only",
        "packaging/only-the-named-ship",
        "packaging/shipping-locks-restore",
        "roslyn/msbuild-bootstrap/tripwire",
        "state/no-static-mutable"
    ];

    [Fact]
    public void Classify_ThisRepositorysOwnSpec_LeavesExactlyTheseRulesToTheWholeSolution()
    {
        SelfSpecTests.SelfModel.ShouldLeaveExactlyTheseToTheWholeSolution(NotDecidable);
    }
}
