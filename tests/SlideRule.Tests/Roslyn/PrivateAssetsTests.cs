using Shouldly;
using SlideRule.Roslyn.Solutions;
using Xunit;

namespace SlideRule.Tests.Roslyn;

/// <summary>
///     <see cref="PrivateAssets.CoversEveryAsset" /> over the spellings of <c>PrivateAssets</c> NuGet accepts:
///     which of them make a package reference private, and which only look as if they should.
/// </summary>
public sealed class PrivateAssetsTests
{
    [Theory]
    // The spelling nearly every project uses, and the ways NuGet tolerates it: any case, padding, a trailing
    // separator, and a none beside it, which adds nothing rather than cancelling anything.
    [InlineData("all", true)]
    [InlineData("ALL", true)]
    [InlineData(" all ", true)]
    [InlineData("all;", true)]
    [InlineData("none;all", true)]
    // Every asset named one by one is all, and buildtransitive brings build with it, so build itself can go.
    [InlineData("runtime;compile;build;native;contentfiles;analyzers;buildtransitive", true)]
    [InlineData("runtime;compile;native;contentfiles;analyzers;buildtransitive", true)]
    // Nothing written is NuGet's default of build, content files and analyzers, which still hands runtime
    // and compile assets on, so neither a blank value nor that default spelled out is private.
    [InlineData("", false)]
    [InlineData(";", false)]
    [InlineData("analyzers;build;contentfiles", false)]
    // none is the empty set rather than the default, and private least of all.
    [InlineData("none", false)]
    // build does not bring buildtransitive, so dropping that one leaves a gap.
    [InlineData("runtime;compile;build;native;contentfiles;analyzers", false)]
    [InlineData("compile", false)]
    // Only a semicolon separates, and the flags' numeric value is no spelling NuGet reads: both add nothing.
    [InlineData("all,runtime", false)]
    [InlineData("127", false)]
    public void CoversEveryAsset_OverTheSpellingsNuGetAccepts_ClassifiesEachOne(string value, bool expected)
    {
        PrivateAssets.CoversEveryAsset(value)
            .ShouldBe(expected, $"PrivateAssets=\"{value}\"");
    }
}
