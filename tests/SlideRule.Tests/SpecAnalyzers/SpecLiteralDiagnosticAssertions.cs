using Microsoft.CodeAnalysis;
using Shouldly;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The Shouldly surface over what the spec-literal analyzer reported: where each warning sits and what it
///     points at. What both analyzers' tests assert alike is in <see cref="DiagnosticAssertions" />.
/// </summary>
/// <remarks>
///     Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason <c>RuleResultAssertions</c>
///     records: leaving it off makes an inner failure name the read under test rather than the caller's local.
/// </remarks>
internal static class SpecLiteralDiagnosticAssertions
{
    /// <summary>
    ///     Asserts the snippet compiles, which every claim about the analyzer rests on: an unbound call
    ///     produces no invocation operation, so a mistyped snippet would report nothing and read as silence.
    /// </summary>
    internal static void ShouldHaveNothingToCompileAbout(
        this Compilation compilation, CancellationToken cancellationToken)
    {
        compilation.GetDiagnostics(cancellationToken)
            .ShouldHaveNothingToCompileAbout();
    }

    /// <summary>The same over the compiler's own diagnostics, already taken.</summary>
    internal static void ShouldHaveNothingToCompileAbout(this IEnumerable<Diagnostic> diagnostics)
    {
        List<string> errors = diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .ToList();

        errors.ShouldBeEmpty("The snippet has to compile before anything the analyzer says about it means "
                             + "something, because an unbound call is silence rather than a diagnostic.");
    }

    /// <summary>
    ///     Asserts the warning sits at the 1-based <paramref name="line" /> and
    ///     <paramref name="column" /> and covers exactly <paramref name="text" /> — the literal's own span,
    ///     which is what tells it from the argument's and from the whole invocation's.
    /// </summary>
    internal static void ShouldSitOn(this Diagnostic diagnostic, int line, int column, string text)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        (span.StartLinePosition.Line + 1).ShouldBe(line, $"the line {diagnostic.Id} sits on");
        (span.StartLinePosition.Character + 1).ShouldBe(column, $"the column {diagnostic.Id} sits at");
        ShouldHaveTextAt(diagnostic.Location)
            .ShouldBe(text, $"the text {diagnostic.Id} covers");
    }

    /// <summary>
    ///     Asserts the warning's one related location sits at the 1-based <paramref name="line" /> and
    ///     <paramref name="column" /> and covers exactly <paramref name="text" />.
    /// </summary>
    internal static void ShouldPointAt(this Diagnostic diagnostic, int line, int column, string text)
    {
        Location related = diagnostic.AdditionalLocations.ToList()
            .ShouldHaveSingleItem($"{diagnostic.Id} should point at exactly one other declaration.");
        FileLinePositionSpan span = related.GetLineSpan();
        (span.StartLinePosition.Line + 1).ShouldBe(line, $"the line {diagnostic.Id} points at");
        (span.StartLinePosition.Character + 1).ShouldBe(column, $"the column {diagnostic.Id} points at");
        ShouldHaveTextAt(related)
            .ShouldBe(text, $"the text {diagnostic.Id} points at");
    }

    /// <summary>Asserts the warning points at nothing else.</summary>
    internal static void ShouldPointAtNothing(this Diagnostic diagnostic)
    {
        diagnostic.AdditionalLocations.ShouldBeEmpty($"{diagnostic.Id} should carry no related location.");
    }

    private static string ShouldHaveTextAt(Location location)
    {
        SyntaxTree tree = location.SourceTree
            .ShouldNotBeNull("The warning should sit in a syntax tree.");

        return tree.GetText()
            .ToString(location.SourceSpan);
    }
}
