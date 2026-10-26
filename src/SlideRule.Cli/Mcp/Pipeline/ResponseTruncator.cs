namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The last line of defence on a tool response's character count, so a large result cannot exhaust the
///     client's context window.
/// </summary>
/// <remarks>
///     It is only a backstop: a document cut at a line boundary is corrupt JSON, and an agent handed one was
///     measured abandoning the tool for the CLI rather than reading less.
/// </remarks>
internal static class ResponseTruncator
{
    private const int ClientDefaultTokens = 25_000;
    private const int ClientPersistenceThreshold = 50_000;

    // arch_status, arch_explain and arch_context are absent on purpose: they have no knob, so a hint would be
    // noise at the exact moment a reader is looking for something to do.
    //
    // Neither hint names a grain. Both tools coarsen their own grain against this same budget before a
    // response can reach here, so anything that still overruns has already been through the ladder and
    // overview: true would only name a rung it took. What is left in each case is the subject.
    //
    // Neither offers to redirect the CLI to a file either: text at the moment of failure is what a reader acts
    // on, so a reader offered a redirect pages the whole document through the shell rather than narrowing it.
    private const string GraphNarrowingHint =
        "Narrow the subject rather than read half a survey: the grain ladder is already exhausted, so "
        + "projects: \"<name globs>\" surveys part of the solution and is the knob left. On the CLI, "
        + "sliderule graph --projects <globs> --json.";

    private const string CheckNarrowingHint =
        "Narrow the subject rather than read half a report: the grain ladder is already exhausted, so "
        + "rules: \"<rule-id globs>\" checks part of the spec and is the knob left — and arch_explain "
        + "returns one rule whole. On the CLI, sliderule check --rules <globs> --json.";

    /// <summary>
    ///     Resolves the character cap from the MCP client's
    ///     <see cref="SlideRule.Roslyn.Hosting.SlideRuleEnvVars.MaxMcpOutputTokens" /> budget: four
    ///     characters for every whole pair of tokens, and never more than 50,000. An unset, blank, unparseable
    ///     or non-positive value reads as the client's own 25,000-token default, which gives 50,000 exactly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Because only whole pairs count, an odd budget lands two characters below twice itself, and the
    ///         floor is intended: the client's token gate, described in the method body, is read from half the
    ///         budget, so a cap that floors can never be on the wrong side of it, while a cap that rounded up
    ///         could be.
    ///     </para>
    ///     <para>
    ///         This rule is mirrored out of process: nothing reports the effective budget, so a tool that derives
    ///         what a reply was allowed to be carries its own copy of the arithmetic. A change here breaks that
    ///         copy, which this repository cannot edit, and the <c>ComputeMaxChars_*</c> rows are the tripwire
    ///         that says so.
    ///     </para>
    /// </remarks>
    internal static int ComputeMaxChars(string? maxMcpOutputTokens)
    {
        int tokens = int.TryParse(maxMcpOutputTokens, out int parsed) && parsed > 0 ? parsed : ClientDefaultTokens;

        // The cap has to clear both of Claude Code's gates for any content. The token gate never counts a reply
        // whose estimate, a quarter of its length, is within half the budget, so two characters per token passes
        // however densely a document tokenizes. The persistence gate files away any MCP reply over 50,000
        // characters whatever the budget. Measured on 2.1.270: 50,000 characters arrive inline with LF and CRLF
        // alike, and 50,001 come back as a file. Computed in long, because a budget near int.MaxValue overflows
        // int before the cap applies.
        long uncounted = tokens / 2 * 4L;

        return (int)Math.Min(uncounted, ClientPersistenceThreshold);
    }

    /// <summary>
    ///     Returns <paramref name="text" /> unchanged when it fits within <paramref name="maxChars" />;
    ///     otherwise returns a truncated copy with a "RESPONSE TRUNCATED" footer. When
    ///     <paramref name="toolName" /> is one of the tools that can be narrowed, the footer ends with the
    ///     hint naming its knobs and their CLI twins.
    /// </summary>
    public static string TruncateIfNeeded(string text, string? toolName, int maxChars)
    {
        if (text.Length <= maxChars) return text;

        int cutPoint = maxChars > 0 ? text.LastIndexOf('\n', maxChars - 1) : 0;
        if (cutPoint <= 0) cutPoint = maxChars;

        // Never cut between the high and low half of a UTF-16 surrogate pair: that strands a lone surrogate, a
        // broken astral char such as an emoji.
        if (cutPoint > 0 && char.IsHighSurrogate(text[cutPoint - 1]) && char.IsLowSurrogate(text[cutPoint]))
            cutPoint--;

        string truncated = text[..cutPoint];
        int droppedChars = text.Length - cutPoint;
        string hint = Hint(toolName);

        return $"{truncated}\n\n--- RESPONSE TRUNCATED ---\nOutput was {text.Length:N0} characters, limit is {maxChars:N0} ({droppedChars:N0} characters omitted).\nThe results above are incomplete.{hint}";
    }

    private static string Hint(string? toolName)
    {
        return toolName switch
        {
            ArchToolNames.Graph => $"\n{GraphNarrowingHint}",
            ArchToolNames.Check => $"\n{CheckNarrowingHint}",
            _ => string.Empty
        };
    }
}
