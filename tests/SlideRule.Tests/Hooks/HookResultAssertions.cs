using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Cli;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The Shouldly surface over a <see cref="CliResult" /> from the <c>hook</c> verb: each assertion names the exit
///     contract in Claude Code's terms (the stop stands, in silence or with the check's document passed through; the
///     stop is refused; SlideRule's own error is reported) rather than the integer that encodes it, and carries both
///     output channels into its failure message.
/// </summary>
/// <remarks>
///     The CLI's own assertions read its own codes, where 2 is a refusal; under the hook verb 2 is a block and a
///     refusal is 1, so reusing them would state the contract backwards. A fragment is looked for in its channel with
///     CRLF folded to LF, so a multi-line fragment written with <c>\n</c> matches whichever line ending the report was
///     written with. Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///     <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
/// </remarks>
internal static class HookResultAssertions
{
    /// <summary>
    ///     Asserts the hook refused the stop: exit 2, nothing on stdout, and every one of
    ///     <paramref name="errFragments" /> in the report on stderr.
    /// </summary>
    internal static CliResult ShouldBlockWith(this CliResult result, params string[] errFragments)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(HookExitCodes.Block, report);
        result.Out.ShouldBeEmpty(report);
        string err = result.Err.NormalizedLines();
        foreach (string fragment in errFragments) err.ShouldContain(fragment, customMessage: report);

        return result;
    }

    /// <summary>Asserts the stop stood without a word: exit 0, and nothing on either channel.</summary>
    internal static CliResult ShouldProceedSilently(this CliResult result)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(HookExitCodes.Proceed, report);
        result.Out.ShouldBeEmpty(report);
        result.Err.ShouldBeEmpty(report);

        return result;
    }

    /// <summary>
    ///     Asserts the stop stood with the check's document passed through: exit 0, nothing on stderr, and every one
    ///     of <paramref name="outFragments" /> on stdout.
    /// </summary>
    internal static CliResult ShouldProceedWith(this CliResult result, params string[] outFragments)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(HookExitCodes.Proceed, report);
        result.Err.ShouldBeEmpty(report);
        string output = result.Out.NormalizedLines();
        foreach (string fragment in outFragments) output.ShouldContain(fragment, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the hook reported SlideRule's own error without blocking: exit 1, stderr opening with the
    ///     config-error line and carrying every one of <paramref name="errFragments" />, and nothing on stdout.
    /// </summary>
    internal static CliResult ShouldReportConfigError(this CliResult result, params string[] errFragments)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(HookExitCodes.Report, report);
        result.Out.ShouldBeEmpty(report);
        string err = result.Err.NormalizedLines();
        err.ShouldStartWith(HookRunner.ConfigErrorLine, customMessage: report);
        foreach (string fragment in errFragments) err.ShouldContain(fragment, customMessage: report);

        return result;
    }
}
