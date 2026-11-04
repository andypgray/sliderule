using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     The warning text <see cref="CrossLanguageReferences" /> feeds the load. <c>Cli/SolutionEntriesE2ETests</c> drives
///     the detection through a real load, over a VB project that compiles and one that does not.
/// </summary>
public sealed class CrossLanguageReferencesTests
{
    [Fact]
    public void Message_NamesTheReferrerTheTargetAndTheMissingEdges()
    {
        CrossLanguageReferences.Message(new CrossLanguageReferences.Finding("/repo/App/App.csproj", "/repo/Legacy/Legacy.vbproj"))
            .ShouldBe(
                "'/repo/App/App.csproj' references '/repo/Legacy/Legacy.vbproj', whose assembly did not reach its "
                + "compilation; types from that project are unbound, so edges to them are absent from the model.");
    }
}
