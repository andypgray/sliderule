using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Shouldly;
using SlideRule.Internal;

namespace SlideRule.Tests.Internal;

/// <summary>
///     <see cref="PathGlob.Matches" /> stated over generated globs and paths rather than a table of them, beside
///     <see cref="WildcardPropertyTests" />, whose matcher it calls once per segment. Its own algorithm is the
///     <c>**</c> table and the prefix reading, and both fail quietly: a glob that matches too little leaves a
///     type out of a rule's subject and the report still says passed.
/// </summary>
public sealed class PathGlobPropertyTests
{
    // Two letters, one of them also drawn in upper case so case-insensitivity is exercised, and a `*` and a
    // `**` on the glob side. Short segments and short paths make near-misses the common case.
    private static readonly string[] SegmentWords = ["a", "b", "A", "ab"];

    private static readonly string[] GlobWords = ["a", "b", "A", "ab", "*", "a*", "**"];

    /// <summary>The table against the recursive definition of a segment glob with a trailing <c>**</c>.</summary>
    [Property(MaxTest = 1000)]
    public Property Matches_AnyGlobAndPath_AgreesWithTheRecursiveDefinition()
    {
        Gen<(string[] Glob, string[] Path)> cases = Words(GlobWords, 1, 4)
            .SelectMany(_ => Words(SegmentWords, 1, 5), (glob, path) => (Glob: glob, Path: path));

        return Prop.ForAll(
            cases.ToArbitrary(),
            testCase =>
            {
                // Arrange
                string glob = string.Join("/", testCase.Glob);
                bool expected = MatchesByDefinition([.. testCase.Glob, "**"], 0, testCase.Path, 0);

                // Act
                bool matched = new PathGlob(glob).Matches(testCase.Path);

                // Assert
                matched.ShouldBe(
                    expected,
                    $"`{glob}` against \"{string.Join("/", testCase.Path)}\": the table and the recursive "
                    + "definition of `**` and the prefix reading must agree.");
            });
    }

    /// <summary>The prefix reading, stated as the equivalence GRAMMAR promises: a glob means its own <c>/**</c>.</summary>
    [Property(MaxTest = 500)]
    public Property Matches_AGlob_MeansWhatItsTrailingDoubleStarFormMeans()
    {
        Gen<(string[] Glob, string[] Path)> cases = Words(GlobWords, 1, 3)
            .SelectMany(_ => Words(SegmentWords, 1, 5), (glob, path) => (Glob: glob, Path: path));

        return Prop.ForAll(
            cases.ToArbitrary(),
            testCase =>
            {
                string glob = string.Join("/", testCase.Glob);

                new PathGlob(glob).Matches(testCase.Path)
                    .ShouldBe(new PathGlob(glob + "/**").Matches(testCase.Path), $"`{glob}` against `{glob}/**`");
            });
    }

    /// <summary>Case never decides a match, on any operating system.</summary>
    [Property(MaxTest = 500)]
    public Property Matches_IgnoresCase()
    {
        Gen<(string[] Glob, string[] Path)> cases = Words(GlobWords, 1, 3)
            .SelectMany(_ => Words(SegmentWords, 1, 4), (glob, path) => (Glob: glob, Path: path));

        return Prop.ForAll(
            cases.ToArbitrary(),
            testCase =>
            {
                string glob = string.Join("/", testCase.Glob);
                string[] upper = testCase.Path.Select(segment => segment.ToUpperInvariant()).ToArray();

                new PathGlob(glob).Matches(upper)
                    .ShouldBe(new PathGlob(glob).Matches(testCase.Path), $"`{glob}` in either case");
            });
    }

    private static bool MatchesByDefinition(string[] glob, int g, string[] path, int p)
    {
        if (g == glob.Length) return p == path.Length;

        if (glob[g] == "**")
            return MatchesByDefinition(glob, g + 1, path, p) || (p < path.Length && MatchesByDefinition(glob, g, path, p + 1));

        return p < path.Length
               && Wildcard.Match(glob[g], path[p], ignoreCase: true)
               && MatchesByDefinition(glob, g + 1, path, p + 1);
    }

    private static Gen<string[]> Words(string[] words, int minCount, int maxCount)
    {
        return Gen.Choose(minCount, maxCount)
            .SelectMany(count => Gen.Elements(words).ListOf(count))
            .Select(chosen => chosen.ToArray());
    }
}
