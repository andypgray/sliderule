using System.Reflection;
using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Mcp.Pipeline;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Tests for <see cref="UnknownParameterGuard" />: a JSON argument key matching no declared parameter
///     of an <c>arch_*</c> tool is surfaced as an actionable, self-correcting error instead of being
///     silently dropped by the SDK's <c>UnmappedMemberHandling = Skip</c>.
/// </summary>
public sealed class UnknownParameterGuardTests
{
    // Value is never inspected by the guard (only keys are), so a single shared dummy suffices.
    private static readonly JsonElement DummyValue = JsonDocument.Parse("null")
        .RootElement;

    [Fact]
    public void Validate_UnknownKeyOnRealTool_NamesBadKeyToolAndValidList()
    {
        // Act — "rule" is the classic typo of arch_explain's "ruleId" parameter.
        string? message = UnknownParameterGuard.Validate(
            "arch_explain",
            new Dictionary<string, JsonElement> { ["rule"] = DummyValue });

        // Assert — names the bad key (quoted), the tool, and the real parameter list.
        message.ShouldNotBeNull();
        message.ShouldContain("\"rule\"");
        message.ShouldContain("arch_explain");
        message.ShouldContain("ruleId");
    }

    [Fact]
    public void Validate_EveryDeclaredParameter_ReturnsNull()
    {
        // Arrange — independently reflect every tool's JSON parameter names. Services arrive via primary
        // constructors, so the only context-bound method parameter is CancellationToken; IsJsonBound
        // encodes exactly that, independently of the guard's own predicate. A newly introduced
        // context-bound parameter type will (correctly) trip this test.
        List<string> failures = [];

        foreach ((string toolName, MethodInfo method) in ToolAttributeDiscovery.GetNamedToolMethods())
        {
            Dictionary<string, JsonElement> arguments = method.GetParameters()
                .Where(IsJsonBound)
                .ToDictionary(p => p.Name!, _ => DummyValue);

            string? message = UnknownParameterGuard.Validate(toolName, arguments);
            if (message is not null) failures.Add($"{toolName}: {message}");
        }

        // Assert — every real parameter name is accepted; any failure is schema drift.
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WrongCaseKey_NamesItAndTheValidList()
    {
        // Act — the SDK binds each argument by its parameter's exact name, so a casing slip binds nothing.
        string? message = UnknownParameterGuard.Validate(
            "arch_explain",
            new Dictionary<string, JsonElement> { ["RuleId"] = DummyValue });

        // Assert
        message.ShouldBe("Unknown parameter \"RuleId\" on \"arch_explain\". Valid: ruleId, type, path.");
    }

    [Fact]
    public void Validate_WrongCaseToolName_ReturnsNull()
    {
        // Act — the SDK dispatches by exact name too and answers an unknown tool itself. A parameter error
        // here would send the caller to fix a key on a tool the call can never reach.
        string? message = UnknownParameterGuard.Validate(
            "Arch_Explain",
            new Dictionary<string, JsonElement> { ["rule"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_KnownKeysOnCheck_ReturnsNull()
    {
        // Act — arch_check's real optional parameters: the tripwire diff ref, the rule-ID filter, and the
        // grain.
        string? message = UnknownParameterGuard.Validate(
            "arch_check",
            new Dictionary<string, JsonElement>
            {
                ["diffBase"] = DummyValue,
                ["rules"] = DummyValue,
                ["grain"] = DummyValue
            });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_KnownKeysOnGraph_ReturnsNull()
    {
        // Act — arch_graph's real optional parameters: the opt-out into surveying a partially-loaded model,
        // and the two narrowing knobs (grain and subject).
        string? message = UnknownParameterGuard.Validate(
            "arch_graph",
            new Dictionary<string, JsonElement>
            {
                ["allowWorkspaceDiagnostics"] = DummyValue,
                ["grain"] = DummyValue,
                ["projects"] = DummyValue
            });

        // Assert
        message.ShouldBeNull();
    }

    [Theory]
    [InlineData("arch_check")]
    [InlineData("arch_graph")]
    public void Validate_RetiredGrainBooleans_AreUnknownAndTheRefusalNamesGrain(string tool)
    {
        // Act — overview, skeleton and index were three booleans until one grain parameter replaced them, so a
        // client still sending them is told the new name rather than silently served a full document.
        string? message = UnknownParameterGuard.Validate(
            tool,
            new Dictionary<string, JsonElement>
            {
                ["overview"] = DummyValue,
                ["skeleton"] = DummyValue,
                ["index"] = DummyValue
            });

        // Assert
        message.ShouldNotBeNull();
        message.ShouldContain("\"overview\", \"skeleton\", \"index\"");
        message.ShouldContain("grain");
    }

    [Fact]
    public void Validate_UnknownToolName_ReturnsNull()
    {
        // Act — unknown-tool dispatch is the SDK's concern; the guard never blocks it.
        string? message = UnknownParameterGuard.Validate(
            "no_such_tool",
            new Dictionary<string, JsonElement> { ["whatever"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_NullArguments_ReturnsNull()
    {
        UnknownParameterGuard.Validate("arch_check", null)
            .ShouldBeNull();
    }

    [Fact]
    public void Validate_EmptyArguments_ReturnsNull()
    {
        UnknownParameterGuard.Validate("arch_check", new Dictionary<string, JsonElement>())
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("file")]
    [InlineData("filePath")]
    [InlineData("directory")]
    public void Validate_HallucinatedKeyOnContext_ReturnsError(string hallucinatedKey)
    {
        // Act — the keys a model reaches for instead of arch_context's real "path" parameter.
        string? message = UnknownParameterGuard.Validate(
            "arch_context",
            new Dictionary<string, JsonElement> { [hallucinatedKey] = DummyValue });

        // Assert
        message.ShouldNotBeNull();
        message.ShouldContain($"\"{hallucinatedKey}\"");
        message.ShouldContain("arch_context");
        message.ShouldContain("path");
    }

    // Independent oracle for "is this a JSON-bound parameter": the only context-bound method-parameter
    // type in this server is CancellationToken. Deliberately NOT calling the guard's own predicate.
    private static bool IsJsonBound(ParameterInfo p)
    {
        return p.Name is not null && p.ParameterType != typeof(CancellationToken);
    }
}
