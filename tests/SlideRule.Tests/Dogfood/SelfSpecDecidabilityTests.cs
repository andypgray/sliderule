using SlideRule.Tests.Checking;
using Xunit;

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
    ///     sorted: the residency rule, the four front doors, whose coverage verb is held out whole, the
    ///     packaging rules, which read project-file facts, the cross-cell ban and the cycle gate, the
    ///     registration noun, and the two scopes' tripwires. Not one of them is here for a project noun,
    ///     though every layer in this spec is defined by a project or cut out of one that is: a project noun
    ///     is out only where it spares a type by plain membership, and no rule here spells one inside an
    ///     <c>Except</c>. Every other rule that names a layer is decidable, a layer being read where it
    ///     stands.
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
        "packaging/compiler-hosted-netstandard",
        "packaging/compiler-hosted-take-only-the-compiler",
        "packaging/core-carries-nothing",
        "packaging/core-netstandard-only",
        "packaging/mcp-sdk-in-cli-only",
        "packaging/only-the-named-ship",
        "packaging/shipping-carries-docs",
        "packaging/shipping-locks-restore",
        "packaging/xunit-in-adapter-only",
        "roslyn/msbuild-bootstrap/tripwire"
    ];

    [Fact]
    public void Classify_ThisRepositorysOwnSpec_LeavesExactlyTheseRulesToTheWholeSolution()
    {
        SelfSpecTests.SelfModel.ShouldLeaveExactlyTheseToTheWholeSolution(NotDecidable);
    }
}
