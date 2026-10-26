using Shouldly;
using SlideRule.Cli.Hooks;
using SlideRule.Cli.Verbs;
using SlideRule.Tests.Cli;

namespace SlideRule.Tests.Hooks;

/// <summary>
///     The Shouldly surface over a <see cref="CliResult" /> from the <c>hook</c> verb: each assertion names the exit
///     contract in Claude Code's terms (the stop stands, in silence or with the check's text handed on as context; the
///     stop is refused; SlideRule's own error is reported) rather than the integer that encodes it, and carries both
///     output channels into its failure message.
/// </summary>
/// <remarks>
///     The CLI's own assertions read its own codes, where 2 is a refusal; under the hook verb 2 is a block and a
///     refusal is 1, so reusing them would state the contract backwards. A fragment is looked for in its channel as
///     written: the hook writes both channels with LF on every OS. Deliberately <em>not</em> attributed
///     <c>[ShouldlyMethods]</c>, for the reason given on <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
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
        string err = result.Err;
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
    ///     Asserts the stop stood with <paramref name="context" /> handed to the agent: exit 0, nothing on stderr, and
    ///     stdout the one document naming <paramref name="hookEvent" /> and carrying exactly that context.
    /// </summary>
    internal static CliResult ShouldProceedWithContext(this CliResult result, string hookEvent, string context)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(HookExitCodes.Proceed, report);
        result.Err.ShouldBeEmpty(report);
        result.ShouldHaveHookAdditionalContext(hookEvent)
            .ShouldBe(context, report);

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
        string err = result.Err;
        err.ShouldStartWith(HookRunner.ConfigErrorLine, customMessage: report);
        foreach (string fragment in errFragments) err.ShouldContain(fragment, customMessage: report);

        return result;
    }
}
