using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.Cli;
using SlideRule.Tests.Mcp.TestDoubles;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Drives a real MCP client against the server over in-memory pipes to lock down
///     <see cref="SlideRule.Cli.Mcp.Pipeline.GlobalCallToolFilter" />'s branches — silent user-error, logged
///     unexpected-error, truncated success, unknown-parameter guard — end to end.
/// </summary>
/// <remarks>
///     <para>
///         Most rows ride the <c>arch_explain</c> DLL fast path (a built-DLL spec resolves with no
///         workspace), so the whole stack is proven in milliseconds. The two over-budget graph rows need a
///         real survey: one lands on a coarser grain, and the other proves that the narrowing hint, keyed on
///         the tool name, travels. The bool-coercer and wrong-case-key rows call <c>arch_graph</c> or
///         <c>arch_context</c> and fail ahead of any survey: the coercer at binding, the wrong-case keys at the
///         guard.
///     </para>
///     <para>
///         The filter brackets each call with the shared
///         <see cref="SlideRule.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog" /> in-flight counter.
///         Nothing here reads it: the suites that read or reset it are in the
///         <see cref="SerialCollection">"Serial"</see> collection, which starts only once this class has
///         finished.
///     </para>
/// </remarks>
public sealed class GlobalCallToolFilterTests
{
    [Fact]
    public async Task CallTool_UserError_ReturnsErrorResultAndLogsNothing()
    {
        // Arrange — arch_explain of an unknown rule ID throws UserErrorException on the DLL fast path.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "nope/nope" },
            cancellationToken: Ct);

        // Assert — surfaced as an error result with the exact message, and the filter stayed silent.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldStartWith("Unknown rule ID 'nope/nope'.");
        // A bound server pays nothing for the unbound path: no recovery advice, because there is nothing
        // here to recover from.
        text.ShouldNotContain(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_SpecClassWithoutAParameterlessConstructor_ReturnsErrorResultAndLogsNothing()
    {
        // Arrange — a spec whose one class cannot be created, which discovery refuses as the author's error.
        string specDll = NoConstructorSpec.EmitDll("mcp-no-ctor");
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, specDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "any/rule" },
            cancellationToken: Ct);

        // Assert — the user-error path, which logs nothing, rather than the unexpected-fault path, which warns.
        result.IsError.ShouldBe(true);
        result.ShouldHaveTextContent()
            .ShouldBe(NoConstructorSpec.Refusal);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_StringifiedArrayArgument_RefusesOnTheShapeWithoutTheRoster()
    {
        // Arrange — the defect as filed, on the surface it was filed against: a client serialized a JSON
        // array into the string parameter it was binding, so the tool received one token carrying brackets.
        // A real one-element array never reaches the runner (StringCoercerFactory unwraps it); a string that
        // merely looks like one survives untouched, which is the value under test.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = """["layering/domain-independent"]""" },
            cancellationToken: Ct);

        // Assert — the shape named, and no roster. A refusal that lists every other ID beside the one inside the
        // caller's own brackets reads as self-contradictory.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldStartWith(
            "Unknown rule ID '[\"layering/domain-independent\"]'. That is a JSON array written as text; "
            + "pass one rule ID: 'layering/domain-independent'.");
        text.ShouldNotContain("Available rule IDs");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UserErrorWhileUnbound_AppendsTheSessionRecovery()
    {
        // Arrange — a harness flagged unbound over a solution that does resolve. Deliberate, and exactly the
        // filter's contract: it reads the nullness of the binding failure and nothing else, and the real
        // unbound launch cannot run in-process at all (McpUnboundServerTests drives that in a child).
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(),
            Ct,
            bindingFailure: "No .sln, .slnf or .slnx file found.");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "nope/nope" },
            cancellationToken: Ct);

        // Assert — the error still leads with what went wrong, and closes with what the reader can do about
        // it. ShouldEndWith is the placement pin: appended, never interleaved, never ahead of the reason.
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldStartWith("Unknown rule ID 'nope/nope'.");
        text.ShouldEndWith(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnknownParameterWhileUnbound_AlsoCarriesTheRecovery()
    {
        // Arrange — the pre-dispatch guard, which fails before discovery is ever reached. Pinning the coda
        // here is what makes the blanket semantics deliberate rather than incidental: fixing the argument
        // key would only surface the discovery refusal next, so the advice is true of this call too.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(),
            Ct,
            bindingFailure: "No .sln, .slnf or .slnx file found.");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["rule"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert
        result.IsError.ShouldBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("\"rule\"");
        text.ShouldEndWith(ServerInstructions.UnboundCallCoda);
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnexpectedError_LogsExactlyOneWarningNamingTheTool()
    {
        // Arrange — a garbage .dll spec throws BadImageFormatException on load: not a user error. A file with
        // a .dll name but non-PE bytes is what SpecResolver's fast path finds, and what the ALC then rejects.
        using TempDirectory temp = TestTempRoot.Fresh("garbage-spec");
        string garbageDll = temp.PathOf("not-really.dll");
        File.WriteAllText(garbageDll, "this is not a portable executable");

        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.For(CliRunner.MyAppSolution, garbageDll), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "any/thing" },
            cancellationToken: Ct);

        // Assert — surfaced as an error, and logged exactly once as a warning that names the tool.
        result.IsError.ShouldBe(true);
        LogEntry warning = harness.Logs.Warnings.ShouldHaveSingleItem();
        warning.Message.ShouldContain("arch_explain");
    }

    [Fact]
    public async Task CallTool_SuccessOverBudget_TruncatesAndLogsNothing()
    {
        // Arrange — a 10-token budget (20-char cap) forces truncation of a real explain body.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);
        harness.Environment.SetVariable(SlideRuleEnvVars.MaxMcpOutputTokens, "10");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_explain",
            new Dictionary<string, object?> { ["ruleId"] = "layering/domain-independent" },
            cancellationToken: Ct);

        // Assert — a successful result, truncated, unlogged.
        result.IsError.ShouldNotBe(true);
        result.ShouldHaveTextContent()
            .ShouldContain("--- RESPONSE TRUNCATED ---");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_GraphOverBudgetAtEveryGrain_TruncatesWithTheNarrowingHint()
    {
        // Arrange — a 10-token budget (20-char cap) no survey can fit at any grain, so arch_graph walks the
        // whole ladder down to its floor and the truncator still fires. That is the backstop case, and the
        // one that has to teach: with grain exhausted, the footer names the knob that is actually left. It
        // takes a budget this small to reach here, because the floor rung is a project roster, which grows
        // with the solution rather than the codebase.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);
        harness.Environment.SetVariable(SlideRuleEnvVars.MaxMcpOutputTokens, "10");

        // Act
        CallToolResult result = await harness.Client.CallToolAsync("arch_graph", cancellationToken: Ct);

        // Assert — the hint travels the whole pipeline, keyed on the tool name the filter passes through, and
        // points at scope rather than back down a ladder this survey has already reached the bottom of.
        result.IsError.ShouldNotBe(true);
        string text = result.ShouldHaveTextContent();
        text.ShouldContain("--- RESPONSE TRUNCATED ---");
        text.ShouldContain("Narrow the subject");
        text.ShouldContain("sliderule graph --projects <globs> --json");
        text.ShouldNotContain("overview: true");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_UnparseableBool_ReturnsCoercerErrorAndLogsNothing()
    {
        // Arrange — "yes" for a flag is the mis-spelling BoolCoercerFactory exists for. Web defaults read
        // numbers from strings but never booleans, so without it this throws a raw JsonException:
        // CliErrorMapper does not recognise one as a user error, so the filter would classify it as a bug and
        // return a byte position. Binding fails ahead of dispatch, so no workspace is ever opened.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_graph",
            new Dictionary<string, object?> { ["allowWorkspaceDiagnostics"] = "yes" },
            cancellationToken: Ct);

        // Assert — the coercer's actionable message surfaces as an error, unlogged.
        result.IsError.ShouldBe(true);
        result.ShouldHaveTextContent()
            .ShouldContain("Expected true or false");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_WrongCaseRequiredKey_ReturnsGuardErrorAndLogsNothing()
    {
        // Arrange — the SDK binds each argument by its parameter's exact name, so "Path" would reach
        // arch_context as no path at all and fail inside the binder, where the filter can only log it.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_context",
            new Dictionary<string, object?> { ["Path"] = "MyApp.Domain" },
            cancellationToken: Ct);

        // Assert — the guard names the key and the spelling that binds, ahead of the binder.
        result.IsError.ShouldBe(true);
        result.ShouldHaveTextContent()
            .ShouldBe("Unknown parameter \"Path\" on \"arch_context\". Valid: path.");
        harness.Logs.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task CallTool_WrongCaseOptionalKey_IsRefusedRatherThanDropped()
    {
        // Arrange — the silent case: an optional parameter the binder cannot find takes its default, so a
        // "Grain" key would return the full survey and nothing would say the grain went unread.
        await using McpPipelineHarness harness = await McpPipelineHarness.StartAsync(
            McpServerBindings.MyAppWithViolatedSpec(), Ct);

        // Act
        CallToolResult result = await harness.Client.CallToolAsync(
            "arch_graph",
            new Dictionary<string, object?> { ["Grain"] = "overview" },
            cancellationToken: Ct);

        // Assert
        result.IsError.ShouldBe(true);
        result.ShouldHaveTextContent()
            .ShouldStartWith("Unknown parameter \"Grain\" on \"arch_graph\". Valid: ");
        harness.Logs.Warnings.ShouldBeEmpty();
    }
}
