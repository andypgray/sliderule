using Xunit;
using SlideRule.Tests.ModelFile;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The one-project split of the layer-card fixture spec, which is the corpus's only spec outside this
///     repository's own whose layers are defined by a project.
/// </summary>
/// <remarks>
///     Here to hold the project-defined form from a second direction. <c>Web</c> is
///     <c>arch.Project("MyApp.Web")</c> and <c>Reporting</c> is a refinement of it, so a change to how the
///     classifier reads a layer definition moves this list as well as the self-spec's — and the two beds
///     differ enough that a change that moves only one is a change worth looking at twice.
/// </remarks>
public sealed class MyAppLayerSpecDecidabilityTests
{
    /// <summary>
    ///     The layer fixture's rules whose verdict takes the whole solution, ordinally sorted: the two
    ///     laws over the Reporting-and-Billing family, and the quarantined scope's tripwire. Its three
    ///     layering bans are all decidable — <c>Web</c> through the project noun that defines it,
    ///     <c>Reporting</c> through the refinement of <c>Web</c> that defines it, <c>Billing</c> through a
    ///     namespace glob — as is the quarantine's desugared containment.
    /// </summary>
    private static readonly string[] NotDecidable =
    [
        "layering/leaves-independent",
        "layering/leaves-not-circular",
        "legacy/billing/tripwire"
    ];

    [Fact]
    public void Classify_TheLayerFixtureSpec_LeavesExactlyTheseRulesToTheWholeSolution()
    {
        ModelFileCorpus.ModelOf("MyAppLayerSpec")
            .ShouldLeaveExactlyTheseToTheWholeSolution(NotDecidable);
    }
}
