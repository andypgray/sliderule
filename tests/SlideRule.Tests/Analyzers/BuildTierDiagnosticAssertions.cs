using Microsoft.CodeAnalysis;
using Shouldly;

namespace SlideRule.Tests.Analyzers;

/// <summary>
///     The Shouldly surface over what the build-tier analyzer reported: which warnings came back and where
///     each one sits. What both analyzers' tests assert alike is in <see cref="DiagnosticAssertions" />.
/// </summary>
/// <remarks>
///     Every failure message carries the whole reported set — each warning's ID, position and message —
///     because the first question a red raises is what the analyzer actually said, and a count answers
///     none of it. Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason
///     <c>RuleResultAssertions</c> records: leaving it off makes an inner failure name the read under test
///     rather than the caller's local.
/// </remarks>
internal static class BuildTierDiagnosticAssertions
{
    /// <summary>
    ///     Asserts every warning reported carries <paramref name="id" />, and returns them in the order they
    ///     came back.
    /// </summary>
    internal static IReadOnlyList<Diagnostic> ShouldAllBe(this IReadOnlyList<Diagnostic> reported, string id)
    {
        List<string> strangers = reported.Where(diagnostic => diagnostic.Id != id)
            .Select(diagnostic => diagnostic.Id)
            .ToList();
        strangers.ShouldBeEmpty(DiagnosticAssertions.Describe(reported, $"Expected every warning to be {id}"));

        return reported;
    }

    /// <summary>
    ///     Asserts the warning sits in <paramref name="path" /> at the 1-based <paramref name="line" />,
    ///     and at <paramref name="column" /> where one is given.
    /// </summary>
    /// <remarks>
    ///     The column is optional because the two kinds of warning are placed differently and only one of
    ///     them is about a column: a rule warning's span covers the reference the extractor recorded — a
    ///     qualified type name, a <c>new</c> through its arguments, a <c>catch</c> through its declaration,
    ///     a <c>throw</c> to the end of its line, an identifier for a shape — which <c>ShouldCover</c>
    ///     states in the words of the code, while a refusal in a file the build handed over is placed at the
    ///     exact character the read stopped on.
    /// </remarks>
    internal static void ShouldSitAt(this Diagnostic diagnostic, string path, int line, int? column = null)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        span.Path.ShouldBe(path);
        (span.StartLinePosition.Line + 1).ShouldBe(line, $"the line {diagnostic.Id} sits on");
        if (column is { } expected)
            (span.StartLinePosition.Character + 1).ShouldBe(expected, $"the column {diagnostic.Id} sits at");
    }

    /// <summary>Asserts the warning's span covers exactly <paramref name="text" /> of the file it is in.</summary>
    internal static void ShouldCover(this Diagnostic diagnostic, string text)
    {
        SyntaxTree tree = diagnostic.Location.SourceTree
            .ShouldNotBeNull($"{diagnostic.Id} should sit in a syntax tree");
        tree.GetText()
            .ToString(diagnostic.Location.SourceSpan)
            .ShouldBe(text);
    }
}
