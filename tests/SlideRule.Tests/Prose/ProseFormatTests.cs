using Shouldly;
using SlideRule.Prose;
using Xunit;

namespace SlideRule.Tests.Prose;

/// <summary>
///     The GRAMMAR §6 reference-list join, pinned directly on
///     <see cref="ProseFormat.JoinReferences(IReadOnlyList{string})" />: a final "or" and no Oxford comma. The
///     closing-comma overload beside it is pinned through the sentences that use it, in
///     <c>SentenceAssemblyTests</c>.
/// </summary>
public sealed class ProseFormatTests
{
    [Fact]
    public void JoinReferences_EmptyList_ReturnsEmptyString()
    {
        ProseFormat.JoinReferences([])
            .ShouldBe(string.Empty);
    }

    [Fact]
    public void JoinReferences_TwoReferences_JoinsWithOr()
    {
        ProseFormat.JoinReferences(["`A`", "`B`"])
            .ShouldBe("`A` or `B`");
    }

    [Fact]
    public void JoinReferences_ThreeReferences_UsesNoOxfordComma()
    {
        ProseFormat.JoinReferences(["`A`", "`B`", "`C`"])
            .ShouldBe("`A`, `B` or `C`");
    }
}
