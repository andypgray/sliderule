namespace SlideRule.Validation;

/// <summary>One mistake in a spec.</summary>
// Pinned primarily by Code plus RuleId, with one representative Message per code.
internal sealed class SpecValidationError
{
    internal SpecValidationError(SpecValidationErrorCode code, string? ruleId, string message, SpecSourceLocation? location = null)
    {
        Code = code;
        RuleId = ruleId;
        Location = location;

        // The location rides in front of the diagnostic (file name and line only), so every error is a jump
        // target. With no location captured (a spec DLL built against an older Core), the message renders
        // verbatim, with no prefix and no leading blank.
        Message = location is null ? message : $"{location}: {message}";
    }

    public SpecValidationErrorCode Code { get; }

    /// <summary>
    ///     Gets the ID of the rule or scope the mistake is about, or null when it is about the spec as a
    ///     whole — a duplicate layer name, or a mistake in a layer's definition.
    /// </summary>
    public string? RuleId { get; }

    /// <summary>
    ///     Gets where in the spec source the offending call was written, or null when the mistake is about
    ///     the spec as a whole or when no position was captured.
    /// </summary>
    public SpecSourceLocation? Location { get; }

    public string Message { get; }
}
