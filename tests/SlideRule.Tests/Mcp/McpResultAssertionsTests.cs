using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Tests.Mcp.TestDoubles;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The cases for <see cref="McpResultAssertions.ShouldHaveAnswered" />: an answer's text comes back, and a
///     refusal reds with its text and everything the server logged, the faulting exception's stack included.
/// </summary>
public sealed class McpResultAssertionsTests
{
    private const string Refusal =
        "An exception of type System.IO.FileNotFoundException was thrown: Unable to find the specified file.";

    [Fact]
    public void ShouldHaveAnswered_OnARefusal_PrintsEverythingTheServerLoggedWithTheFaultsStack()
    {
        using CapturingLoggerProvider log = new();
        log.CreateLogger("Session")
            .LogInformation("Structural change detected, reloading: {Path}", "/w/MyApp.Web.csproj");
        RemoteFaultException fault = new(Refusal);
        log.CreateLogger("Filter")
            .LogWarning(fault, "Tool '{ToolName}' failed", "arch_check");
        CallToolResult result = new() { IsError = true, Content = [new TextContentBlock { Text = Refusal }] };

        string red = Should.Throw<ShouldAssertException>(() => result.ShouldHaveAnswered(log))
            .Message;

        string nl = Environment.NewLine;
        string serverLog = "the server logged:" + nl
                                                + "Information Session: Structural change detected, reloading: /w/MyApp.Web.csproj" + nl
                                                + "Warning Filter: Tool 'arch_check' failed" + nl
                                                + fault;
        red.ShouldSatisfyAllConditions(
            () => red.ShouldContain(serverLog, customMessage: red),
            () => red.ShouldContain("at Example.BuildHost.LoadProjectFileAsync()", customMessage: red),
            () => red.ShouldContain("the tool refused:" + nl + Refusal, customMessage: red));
    }

    [Fact]
    public void ShouldHaveAnswered_OnARefusalWithAnEmptyLog_SaysNothingWasLogged()
    {
        using CapturingLoggerProvider log = new();
        CallToolResult result = new() { IsError = true, Content = [new TextContentBlock { Text = Refusal }] };

        string red = Should.Throw<ShouldAssertException>(() => result.ShouldHaveAnswered(log))
            .Message;

        red.ShouldContain("the server logged:" + Environment.NewLine + "(nothing logged)", customMessage: red);
    }

    [Fact]
    public void ShouldHaveAnswered_OnAnAnswer_ReturnsItsText()
    {
        using CapturingLoggerProvider log = new();
        CallToolResult result = new() { Content = [new TextContentBlock { Text = "{}" }] };

        result.ShouldHaveAnswered(log)
            .ShouldBe("{}");
    }

    /// <summary>Stands in for the MSBuild workspace's remote fault, whose stack is the build host's.</summary>
    private sealed class RemoteFaultException(string message) : Exception(message)
    {
        public override string StackTrace => "   at Example.BuildHost.LoadProjectFileAsync()";
    }
}
