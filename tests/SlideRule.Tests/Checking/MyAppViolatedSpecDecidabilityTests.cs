using Xunit;
using SlideRule.Tests.ModelFile;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The one-project split of the broadest fixture spec: thirty-odd rules across most of the vocabulary,
///     every layer of them a namespace glob.
/// </summary>
/// <remarks>
///     The control beside <c>MyAppLayerSpecDecidabilityTests</c>. How a layer is defined is the whole axis
///     the project-noun rows turn on, so a spec that defines none of its layers by a project is the one
///     that should not move when those rows do — and this list is what says so.
/// </remarks>
public sealed class MyAppViolatedSpecDecidabilityTests
{
    /// <summary>
    ///     The violated fixture's rules whose verdict takes the whole solution, ordinally sorted: two
    ///     registration nouns, two scopes' tripwires, the coverage verb, two family laws over the
    ///     projects, the counterpart verb, residency, the cycle gate, and four packaging verbs. Not one of
    ///     them is here for a project noun, this spec naming none.
    /// </summary>
    private static readonly string[] NotDecidable =
    [
        "di/handlers-registered",
        "di/no-captive-dependencies",
        "domain/retry-budget/tripwire",
        "layering/no-ungoverned-types",
        "layering/projects-independent",
        "layering/projects-reached-only-by-themselves",
        "layering/services-behind-contracts",
        "layering/services-in-web",
        "layering/web-cuts-not-circular",
        "legacy/billing/tripwire",
        "packaging/locked-restore",
        "packaging/no-feed-dependencies",
        "packaging/nothing-published",
        "packaging/supported-frameworks"
    ];

    [Fact]
    public void Classify_TheViolatedFixtureSpec_LeavesExactlyTheseRulesToTheWholeSolution()
    {
        ModelFileCorpus.ModelOf("MyAppViolatedSpec")
            .ShouldLeaveExactlyTheseToTheWholeSolution(NotDecidable);
    }
}
