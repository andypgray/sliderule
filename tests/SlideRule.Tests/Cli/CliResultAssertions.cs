using System.Text.Json;
using Shouldly;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Cli;

/// <summary>
///     The Shouldly surface over <see cref="CliResult" />: the exit assertions name the contract they
///     check — 0 succeeded, 1 found violations, 2 refused the run — rather than the integer that encodes
///     it, and every assertion carries both output channels into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>Exit.ShouldBe(0)</c> reds with <c>2 should be 0</c> and discards the stderr line that
///         says why. Every assertion here passes <see cref="Transcript" /> as its Shouldly reason instead, so
///         the refusal the CLI actually printed is in front of the reader — and a row asserting on a run
///         directly reaches the same transcript rather than spelling one of its own.
///     </para>
///     <para>
///         The exit assertions match their fragments against the channel with CRLF folded to LF, so a pinned
///         multi-line block matches as written whichever line ending the platform writes.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
/// </remarks>
internal static class CliResultAssertions
{
    /// <summary>Asserts the run succeeded, and that stdout contains every one of <paramref name="outFragments" />.</summary>
    internal static CliResult ShouldSucceed(this CliResult result, params string[] outFragments)
    {
        return ShouldExitWith(result, 0, result.Out, outFragments);
    }

    /// <summary>
    ///     Asserts the run completed and found violations — exit 1, the enforcement red, distinct from the
    ///     exit 2 that means the run never got as far as checking.
    /// </summary>
    internal static CliResult ShouldReportViolations(this CliResult result, params string[] outFragments)
    {
        return ShouldExitWith(result, 1, result.Out, outFragments);
    }

    /// <summary>
    ///     Asserts the CLI refused the run — exit 2, a usage or tool error — and said so on stderr, which
    ///     carries every one of <paramref name="errFragments" />.
    /// </summary>
    internal static CliResult ShouldRefuseWith(this CliResult result, params string[] errFragments)
    {
        return ShouldExitWith(result, 2, result.Err, errFragments);
    }

    /// <summary>
    ///     Asserts the CLI refused the run — exit 2 — and that stderr is <paramref name="message" /> and nothing
    ///     else, line endings and the whitespace around the whole aside.
    /// </summary>
    internal static CliResult ShouldRefuseWithExactly(this CliResult result, string message)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(2, report);
        result.Err.NormalizedTrimmed()
            .ShouldBe(message.NormalizedTrimmed(), report);

        return result;
    }

    /// <summary>
    ///     Asserts stdout is exactly <paramref name="expected" />, line endings and the whitespace around the whole
    ///     aside.
    /// </summary>
    internal static CliResult ShouldPrint(this CliResult result, string expected)
    {
        result.Out.NormalizedTrimmed()
            .ShouldBe(expected.NormalizedTrimmed(), result.Transcript());

        return result;
    }

    /// <summary>Asserts stdout opens with <paramref name="expected" />, line endings aside.</summary>
    internal static CliResult ShouldOpenWith(this CliResult result, string expected)
    {
        result.Out.NormalizedLines()
            .ShouldStartWith(expected, customMessage: result.Transcript());

        return result;
    }

    /// <summary>
    ///     Asserts stdout is <paramref name="expected" /> verbatim, line endings aside. Unlike <see cref="ShouldPrint" />,
    ///     nothing is trimmed: the whitespace around the whole counts, the trailing newline included.
    /// </summary>
    internal static CliResult ShouldPrintVerbatim(this CliResult result, string expected)
    {
        result.Out.NormalizedLines()
            .ShouldBe(expected, result.Transcript());

        return result;
    }

    /// <summary>
    ///     Asserts this run came out byte for byte as <paramref name="expected" /> did — same exit code, same
    ///     stdout, same stderr.
    /// </summary>
    /// <remarks>
    ///     It takes whatever exit code <paramref name="expected" /> reached rather than pinning one, because
    ///     the guarantee it serves is that a replayed run is indistinguishable from the cold one, whatever the
    ///     cold one did.
    /// </remarks>
    internal static CliResult ShouldMatchTheOutputOf(this CliResult actual, CliResult expected)
    {
        string report = $"This run:{Environment.NewLine}{actual.Transcript()}{Environment.NewLine}"
                        + $"The run it should match:{Environment.NewLine}{expected.Transcript()}";

        actual.ShouldSatisfyAllConditions(
            () => actual.Exit.ShouldBe(expected.Exit, report),
            () => actual.Out.ShouldBe(expected.Out, report),
            () => actual.Err.ShouldBe(expected.Err, report));

        return actual;
    }

    /// <summary>
    ///     Asserts stdout is one parseable JSON document and hands it back. The caller owns the
    ///     <see cref="JsonDocument" />.
    /// </summary>
    /// <remarks>
    ///     Parsing rather than sniffing the first character: a truncated report still starts with
    ///     <c>{</c>, and a report cut in half is exactly the failure a stdout-purity guard exists to catch.
    /// </remarks>
    internal static JsonDocument ShouldHaveJsonStdout(this CliResult result)
    {
        try
        {
            return JsonDocument.Parse(result.Out);
        }
        catch (JsonException exception)
        {
            throw new ShouldAssertException(
                $"stdout should have been one JSON document but did not parse: {exception.Message}"
                + Environment.NewLine + result.Transcript());
        }
    }

    /// <summary>
    ///     Asserts stdout is the one hook document, answering for <paramref name="expectedEvent" />, and
    ///     hands back its <c>additionalContext</c> — the text the hook passes through to Claude Code.
    /// </summary>
    /// <remarks>
    ///     The event is named by every caller rather than defaulted, because it decides whether the context
    ///     arrives at all: Claude Code reads <c>additionalContext</c> only from a document naming the event
    ///     it fired, so an envelope for the wrong event is silently dropped at the far end and a row that
    ///     asserted only on the text would stay green through exactly that.
    /// </remarks>
    internal static string ShouldHaveHookAdditionalContext(this CliResult result, string expectedEvent)
    {
        using JsonDocument document = result.ShouldHaveJsonStdout();
        JsonElement output = document.RootElement.GetProperty("hookSpecificOutput");
        output.GetProperty("hookEventName")
            .GetString()
            .ShouldBe(expectedEvent, result.Transcript());

        return output.GetProperty("additionalContext")
            .GetString()!;
    }

    /// <summary>
    ///     Asserts stdout's human report names <paramref name="ruleId" /> and hands back that rule's block: its marker
    ///     line plus the indented lines under it.
    /// </summary>
    /// <remarks>
    ///     Needed wherever an assertion is an absence: a type named by one rule and not another is invisible to a
    ///     whole-report <c>ShouldNotContain</c>.
    /// </remarks>
    internal static string ShouldHaveRuleBlock(this CliResult result, string ruleId)
    {
        string[] lines = result.Out.NormalizedTrimmed()
            .Split('\n');
        int start = Array.FindIndex(lines, line => line.Contains($" {ruleId} —", StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(
            0, $"the report names no rule {ruleId}" + Environment.NewLine + result.Transcript());

        int end = start + 1;
        while (end < lines.Length && lines[end]
                   .StartsWith("  ", StringComparison.Ordinal)) end++;

        return string.Join("\n", lines[start..end]);
    }

    /// <summary>
    ///     Asserts stdout carries a section under <paramref name="heading" />, and returns its lines as written,
    ///     indentation included, up to the first blank line.
    /// </summary>
    /// <remarks>
    ///     A missing heading reds rather than reading as an empty section, so a row pinning a section's lines
    ///     cannot pass on output that never printed the section at all.
    /// </remarks>
    internal static IReadOnlyList<string> ShouldHaveOutSection(this CliResult result, string heading)
    {
        List<string> lines = result.Out.NormalizedLines()
            .Split('\n')
            .ToList();
        int start = lines.FindIndex(line => string.Equals(line.Trim(), heading, StringComparison.Ordinal));
        start.ShouldBeGreaterThanOrEqualTo(
            0, $"stdout has no '{heading}' section" + Environment.NewLine + result.Transcript());

        return lines.Skip(start + 1)
            .TakeWhile(line => line.Trim()
                .Length > 0)
            .ToList();
    }

    /// <summary>
    ///     Asserts stderr carries exactly one <c>warning:</c> line, and that it reports the NuGet audit advisory
    ///     <paramref name="message" /> for <paramref name="projects" /> — the grouped line, once, with no load
    ///     line and no MSBuild note beside it.
    /// </summary>
    internal static CliResult ShouldEchoOneAuditWarning(
        this CliResult result, string message, IReadOnlyList<string> projects)
    {
        string report = result.Transcript();
        string expected = "warning: " + new PackageAuditEntry(message, projects).Describe();
        string[] warnings = result.Err
            .ReplaceLineEndings("\n")
            .Split('\n')
            .Where(line => line.StartsWith("warning:", StringComparison.Ordinal))
            .ToArray();

        warnings.ShouldBe([expected], customMessage: report);

        return result;
    }

    private static CliResult ShouldExitWith(CliResult result, int exit, string channel, string[] fragments)
    {
        string report = result.Transcript();
        result.Exit.ShouldBe(exit, report);

        string text = channel.NormalizedLines();
        Action[] checks = fragments
            .Select<string, Action>(fragment => () => text.ShouldContain(fragment, customMessage: report))
            .ToArray();
        result.ShouldSatisfyAllConditions(checks);

        return result;
    }

    /// <summary>
    ///     The exit code and both output channels — what the run actually did. Both channels, because a run
    ///     that answered wrongly answered for a reason that is on one of them, and which one is not knowable
    ///     in advance.
    /// </summary>
    internal static string Transcript(this CliResult result)
    {
        return $"The CLI exited {result.Exit}.{Environment.NewLine}"
               + $"stderr:{Environment.NewLine}{result.Err.TrimEnd()}{Environment.NewLine}"
               + $"stdout:{Environment.NewLine}{result.Out.TrimEnd()}";
    }
}
