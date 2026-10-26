using System.Reflection;
using System.Text.Json;
using Shouldly;
using SlideRule.Cli.Mcp.Pipeline;
using Xunit;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Tests for <see cref="UnknownParameterGuard" />: a JSON argument key matching no declared parameter of an
///     <c>arch_*</c> tool, or a required parameter with no key, is surfaced as an actionable, self-correcting error
///     instead of being dropped by the SDK or failed inside its binder. The guard is handed each tool as a real
///     <c>tools/list</c> advertises it.
/// </summary>
public sealed class UnknownParameterGuardTests(AdvertisedToolsFixture advertised) : IClassFixture<AdvertisedToolsFixture>
{
    // Value is never inspected by the guard (only keys are), so a single shared dummy suffices. It is never
    // disposed: disposing a JsonDocument invalidates the elements it handed out.
    private static readonly JsonElement DummyValue = JsonDocument.Parse("null")
        .RootElement;

    /// <summary>
    ///     Gets the argument keys of arch_context calls that leave out its required <c>path</c>: no arguments at all, and
    ///     an empty object.
    /// </summary>
    public static TheoryData<string[]?> KeysWithoutPath => new() { (string[]?)null, { [] } };

    [Fact]
    public void Validate_UnknownKeyOnRealTool_NamesBadKeyToolAndValidList()
    {
        // Act — "rule" is the classic typo of arch_explain's "ruleId" parameter.
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool("arch_explain"),
            new Dictionary<string, JsonElement> { ["rule"] = DummyValue });

        // Assert
        message.ShouldBe("Unknown parameter \"rule\" on \"arch_explain\". Valid: ruleId, type, path.");
    }

    [Fact]
    public void Validate_EveryReflectedParameter_IsAcceptedOnItsAdvertisedTool()
    {
        // Arrange — every parameter the tool methods bind from JSON, read by reflection rather than from the
        // schema the guard reads, so a parameter the schema stopped advertising shows up here.
        List<string> failures = [];

        foreach ((string toolName, IReadOnlyList<ParameterInfo> parameters) in LiveToolParameters.ByTool())
        {
            Dictionary<string, JsonElement> arguments = parameters.ToDictionary(p => p.Name!, _ => DummyValue);

            string? message = UnknownParameterGuard.Validate(advertised.ShouldHaveTool(toolName), arguments);
            if (message is not null) failures.Add($"{toolName}: {message}");
        }

        // Assert
        failures.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_WrongCaseKey_NamesItAndTheValidList()
    {
        // Act — the SDK binds each argument by its parameter's exact name, so a casing slip binds nothing.
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool("arch_explain"),
            new Dictionary<string, JsonElement> { ["RuleId"] = DummyValue });

        // Assert
        message.ShouldBe("Unknown parameter \"RuleId\" on \"arch_explain\". Valid: ruleId, type, path.");
    }

    [Theory]
    [InlineData("arch_check")]
    [InlineData("arch_graph")]
    public void Validate_RetiredGrainBooleans_AreUnknownAndTheRefusalNamesGrain(string tool)
    {
        // Act — overview, skeleton and index are the retired grain booleans, so a client still sending them is
        // told the grain parameter's name rather than silently served a full document.
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool(tool),
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

    [Theory]
    [MemberData(nameof(KeysWithoutPath))]
    public void Validate_ContextWithoutPath_NamesTheMissingKeyAndTheValidList(string[]? keys)
    {
        // Act
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool("arch_context"),
            keys?.ToDictionary(key => key, _ => DummyValue));

        // Assert
        message.ShouldBe("Missing required parameter \"path\" on \"arch_context\". Valid: path.");
    }

    [Fact]
    public void Validate_RequiredKeyPresentWithNullValue_ReturnsNull()
    {
        // Act — the key is present, so the tool answers the null value itself, not the guard.
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool("arch_context"),
            new Dictionary<string, JsonElement> { ["path"] = DummyValue });

        // Assert
        message.ShouldBeNull();
    }

    [Fact]
    public void Validate_MisspeltRequiredKey_IsReportedAsUnknownRatherThanMissing()
    {
        // Act — "file" also leaves "path" out, and a misspelt key usually is the missing one.
        string? message = UnknownParameterGuard.Validate(
            advertised.ShouldHaveTool("arch_context"),
            new Dictionary<string, JsonElement> { ["file"] = DummyValue });

        // Assert
        message.ShouldBe("Unknown parameter \"file\" on \"arch_context\". Valid: path.");
    }

    [Fact]
    public void Validate_NullArgumentsOnAToolWithNoRequiredParameter_ReturnsNull()
    {
        UnknownParameterGuard.Validate(advertised.ShouldHaveTool("arch_check"), null)
            .ShouldBeNull();
    }

    [Fact]
    public void Validate_EmptyArgumentsOnAToolWithNoRequiredParameter_ReturnsNull()
    {
        UnknownParameterGuard.Validate(advertised.ShouldHaveTool("arch_check"), new Dictionary<string, JsonElement>())
            .ShouldBeNull();
    }
}
