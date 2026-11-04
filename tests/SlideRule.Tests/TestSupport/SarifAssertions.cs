using System.Text.Json;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The SARIF render of an in-memory <see cref="CheckReport" /> through the workspace-free
///     <see cref="SarifReportRenderer.Serialize" /> seam, and the Shouldly surface over it.
/// </summary>
/// <remarks>
///     <para>
///         Shared because every row driving the renderer directly makes the same five-argument call and
///         then walks the same <c>runs[0]</c>, whatever the row's subject is — the per-site mapping, or a
///         project violation's message text.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />: leaving it off is what
///         makes an inner failure name the SARIF property under test — <c>level</c>, <c>message.text</c> —
///         rather than the caller's local.
///     </para>
/// </remarks>
internal static class SarifAssertions
{
    /// <summary>
    ///     <paramref name="report" /> rendered as SARIF over <paramref name="diagnostics" />' load, streamed
    ///     as <c>check</c> streams it — or, by default, over a healthy load: nothing on the diagnostics
    ///     stream and no incomplete-model evidence, so the invocation block is the one a clean run writes.
    ///     The populated shape is the gate suites' subject, and the golden's.
    /// </summary>
    /// <remarks>
    ///     No caller chooses the solution directory. An in-memory report's site paths are already relative
    ///     — a bare <c>Test.cs</c> from the MSBuild-free source factory, a bare <c>Internal.csproj</c> from
    ///     the project-facts one — so every directory relativizes them to themselves. The URIs read from the
    ///     solution directory too unless <paramref name="uriRoot" /> names another directory, as a repository's
    ///     top level above the solution would be.
    /// </remarks>
    internal static string ToSarif(
        this CheckReport report, WorkspaceDiagnostics? diagnostics = null, string? uriRoot = null)
    {
        WorkspaceDiagnostics load = diagnostics ?? WorkspaceDiagnostics.None;
        string solutionDirectory = Directory.GetCurrentDirectory();
        WorkspaceTrustStamp trust = WorkspaceTrustStamp.From(load, solutionDirectory);
        return SarifReportRenderer.Serialize(
            report, solutionDirectory, uriRoot ?? solutionDirectory, true, load.RenderedWithMergeNotes, trust);
    }

    /// <summary>
    ///     Asserts <paramref name="sarif" /> carries exactly one result, at <c>error</c> level, saying
    ///     <paramref name="message" /> — and hands it back for any further per-site read.
    /// </summary>
    internal static JsonElement ShouldBeOneErrorSaying(this string sarif, string message)
    {
        JsonElement result = sarif.SarifResults()
            .ShouldHaveSingleItem();
        result.SarifLevel()
            .ShouldBe("error");
        result.SarifMessageText()
            .ShouldBe(message);

        return result;
    }

    /// <summary>
    ///     Asserts <paramref name="sarif" /> carries exactly one tool-execution notification, at <c>warning</c>
    ///     level, reporting the NuGet audit advisory <paramref name="message" /> for <paramref name="projects" />
    ///     — one per advisory however many lines the load raised it in, and no MSBuild note beside it. Null
    ///     projects is the advisory no frame named a project for, which reports the message alone.
    /// </summary>
    internal static void ShouldCarryOneAuditNotification(
        this string sarif, string message, IReadOnlyList<string>? projects)
    {
        JsonElement notification = sarif.SarifNotifications()
            .ShouldHaveSingleItem(sarif);
        notification.SarifLevel()
            .ShouldBe("warning", sarif);
        notification.SarifMessageText()
            .ShouldBe(new PackageAuditEntry(message, projects).Describe(), sarif);
    }

    /// <summary>
    ///     Asserts <paramref name="sarif" /> carries exactly one tool-execution notification whose message contains
    ///     <paramref name="lede" />, at <paramref name="level" />, and hands it back for any further read.
    /// </summary>
    internal static JsonElement ShouldCarryNotification(this string sarif, string level, string lede)
    {
        JsonElement notification = sarif.SarifNotifications()
            .Where(candidate => candidate.SarifMessageText()
                .Contains(lede, StringComparison.Ordinal))
            .ShouldHaveSingleItem(sarif);
        notification.SarifLevel()
            .ShouldBe(level, sarif);

        return notification;
    }

    /// <summary>
    ///     Whether the run's one invocation recorded success: the verdict code scanning reads in place of an exit
    ///     code.
    /// </summary>
    internal static bool SarifExecutionSuccessful(this string sarif)
    {
        return Invocation(sarif)
            .GetProperty("executionSuccessful")
            .GetBoolean();
    }

    /// <summary>The run's results, in render order — one per violation site.</summary>
    internal static IReadOnlyList<JsonElement> SarifResults(this string sarif)
    {
        return Run(sarif)
            .GetProperty("results")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>
    ///     The driver's reporting descriptors, in model order — every rule the spec declared, not just the
    ///     ones that produced a result.
    /// </summary>
    internal static IReadOnlyList<JsonElement> SarifRules(this string sarif)
    {
        return Run(sarif)
            .GetProperty("tool")
            .GetProperty("driver")
            .GetProperty("rules")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>
    ///     The run's tool-execution notifications, in render order — the invocation channel a partial
    ///     survey warns on, this log format having neither an exit code nor prose to carry it.
    /// </summary>
    internal static IReadOnlyList<JsonElement> SarifNotifications(this string sarif)
    {
        return Invocation(sarif)
            .GetProperty("toolExecutionNotifications")
            .EnumerateArray()
            .ToList();
    }

    /// <summary>
    ///     The run's tool-configuration notifications, in render order: the check warnings about the spec itself,
    ///     which name no file for a result to sit on. Throws, quoting the invocation, when it carries none.
    /// </summary>
    internal static IReadOnlyList<JsonElement> SarifConfigurationNotifications(this string sarif)
    {
        JsonElement invocation = Invocation(sarif);

        return invocation.TryGetProperty("toolConfigurationNotifications", out JsonElement notifications)
            ? notifications.EnumerateArray()
                .ToList()
            : throw new KeyNotFoundException($"The invocation carries no configuration notifications: {invocation}");
    }

    /// <summary>The rule a notification names as its associated rule; throws, quoting it, when it names none.</summary>
    internal static string SarifAssociatedRuleId(this JsonElement notification)
    {
        return notification.TryGetProperty("associatedRule", out JsonElement rule)
            ? rule.GetProperty("id")
                .GetString()!
            : throw new KeyNotFoundException($"The notification names no associated rule: {notification}");
    }

    /// <summary>A result's or notification's message text: the alert title every code-scanning UI shows.</summary>
    internal static string SarifMessageText(this JsonElement result)
    {
        return result.GetProperty("message")
            .GetProperty("text")
            .GetString()!;
    }

    /// <summary>The violation-identity fingerprint a result carries.</summary>
    internal static string SarifFingerprint(this JsonElement result)
    {
        return result.GetProperty("partialFingerprints")
            .GetProperty("slideRuleViolationIdentity/v1")
            .GetString()!;
    }

    /// <summary>A result's or notification's own <c>level</c>: <c>error</c>, <c>warning</c> or <c>note</c>.</summary>
    internal static string SarifLevel(this JsonElement result)
    {
        return result.GetProperty("level")
            .GetString()!;
    }

    /// <summary>The artifact URI of a single-location result: the file code scanning puts the alert on.</summary>
    internal static string SarifUri(this JsonElement result)
    {
        return result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("artifactLocation")
            .GetProperty("uri")
            .GetString()!;
    }

    /// <summary>The 1-based source line of a single-location result: the site the reader is sent to.</summary>
    internal static int SarifStartLine(this JsonElement result)
    {
        return result.GetProperty("locations")[0]
            .GetProperty("physicalLocation")
            .GetProperty("region")
            .GetProperty("startLine")
            .GetInt32();
    }

    // The single run a SlideRule SARIF log always carries.
    private static JsonElement Run(string sarif)
    {
        return JsonDocument.Parse(sarif)
            .RootElement.GetProperty("runs")[0];
    }

    // The run's one invocation.
    private static JsonElement Invocation(string sarif)
    {
        return Run(sarif)
            .GetProperty("invocations")[0];
    }
}
