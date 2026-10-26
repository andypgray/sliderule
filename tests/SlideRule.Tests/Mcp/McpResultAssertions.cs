using System.Text.Json;
using ModelContextProtocol.Protocol;
using Shouldly;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     The Shouldly surface over MCP results: asserts a result carries its one block of text and hands
///     the text back, failing by name — the block that is not text, or the result that carries none —
///     rather than by cast. Covers both shapes this suite sees: the SDK's typed results, and the raw
///     JSON-RPC frame the real-child stdio suites read, where no typed result exists.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason given on
///         <see cref="SlideRule.Tests.Checking.RuleResultAssertions" />.
///     </para>
///     <para>
///         The frame overloads take a <see cref="string" /> receiver, which reaches only files in this
///         namespace. None of them may ever be named for one of the case-sensitive string assertions the
///         suite's root namespace shadows — a nearer applicable method wins the extension lookup, so such a
///         name would silently return this folder to Shouldly's case-insensitive default.
///     </para>
/// </remarks>
internal static class McpResultAssertions
{
    /// <summary>Asserts the tool result has exactly one content block, a text one, and returns its text.</summary>
    internal static string ShouldHaveTextContent(this CallToolResult result)
    {
        return result.Content
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TextContentBlock>()
            .Text;
    }

    /// <summary>Asserts the tool refused: an error result carrying one text block, whose text is handed back.</summary>
    internal static string ShouldHaveRefused(this CallToolResult result)
    {
        string text = result.ShouldHaveTextContent();
        result.IsError.ShouldBe(true, $"the tool answered rather than refused:{Environment.NewLine}{text}");

        return text;
    }

    /// <summary>Asserts the tool answered: no error flag, and one text block, whose text is handed back.</summary>
    internal static string ShouldHaveAnswered(this CallToolResult result)
    {
        string text = result.ShouldHaveTextContent();
        result.IsError.ShouldNotBe(true, $"the tool refused:{Environment.NewLine}{text}");

        return text;
    }

    /// <summary>Asserts the prompt result has at least one message, text-first, and returns that text.</summary>
    internal static string ShouldHaveTextContent(this GetPromptResult result)
    {
        result.Messages.ShouldNotBeEmpty();

        return result.Messages[0]
            .Content
            .ShouldBeOfType<TextContentBlock>()
            .Text;
    }

    /// <summary>
    ///     Asserts <paramref name="actual" /> is <paramref name="expected" />'s text, CRLF folded to LF and the ends
    ///     trimmed on both sides: the parity a tool's answer is held to against a CLI channel or another answer.
    ///     Hands <paramref name="actual" /> back.
    /// </summary>
    internal static string ShouldBeTheSameTextAs(this string actual, string expected)
    {
        actual.NormalizedTrimmed()
            .ShouldBe(expected.NormalizedTrimmed());

        return actual;
    }

    /// <summary>
    ///     Asserts <paramref name="actual" /> differs from <paramref name="other" /> under the same folding.
    /// </summary>
    internal static string ShouldNotBeTheSameTextAs(this string actual, string other)
    {
        actual.NormalizedTrimmed()
            .ShouldNotBe(other.NormalizedTrimmed());

        return actual;
    }

    /// <summary>
    ///     The text payload of a <c>tools/call</c> response frame. A JSON-RPC error always fails — the tool
    ///     never ran — but a tool-level <c>isError</c> does not, because for some callers a refusal
    ///     is the subject and for others a rule the fixture spec cannot answer is legitimate. Read
    ///     that flag separately with <see cref="McpChildHarness.IsToolError" />.
    /// </summary>
    internal static string ShouldHaveToolText(this string frame, string toolName)
    {
        using JsonDocument document = JsonDocument.Parse(frame);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"{toolName} returned a JSON-RPC error rather than a tool result: {error}"
                           + Environment.NewLine + Describe(frame));

        return document.RootElement.GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }

    /// <summary>
    ///     The <c>instructions</c> an <c>initialize</c> response frame carries — one of the two in-band
    ///     channels a client actually reads, the other being a tool call's error result.
    /// </summary>
    internal static string ShouldHaveInstructions(this string handshake)
    {
        using JsonDocument document = JsonDocument.Parse(handshake);
        document.RootElement.TryGetProperty("error", out JsonElement error)
            .ShouldBeFalse($"the server returned a JSON-RPC error to initialize: {error}"
                           + Environment.NewLine + Describe(handshake));

        return document.RootElement.GetProperty("result")
            .GetProperty("instructions")
            .GetString() ?? string.Empty;
    }

    /// <summary>The whole frame — what the child actually answered, which is what a red asks first.</summary>
    private static string Describe(string frame)
    {
        return $"frame:{Environment.NewLine}{frame.TrimEnd()}";
    }
}
