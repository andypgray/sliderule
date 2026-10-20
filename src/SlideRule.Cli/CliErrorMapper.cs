using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Validation;

namespace SlideRule.Cli;

/// <summary>
///     Maps a pipeline exception to stderr text and <see cref="ExitCodes.Error" />: an expected error renders
///     message-only; anything else is a bug and gets a full stack trace.
/// </summary>
internal static class CliErrorMapper
{
    /// <summary>
    ///     The multi-line user-facing message for an expected error: the identical text the CLI and the MCP
    ///     <c>GlobalCallToolFilter</c> render. Null for an unexpected bug.
    /// </summary>
    /// <remarks>
    ///     A <see cref="UserErrorException" />'s message verbatim, or a <see cref="SpecValidationException" />'s
    ///     "Spec validation failed:" header followed by one indented line per error, so every validation error is
    ///     fixable in one pass.
    /// </remarks>
    public static string? UserFacingMessage(Exception exception)
    {
        return exception switch
        {
            UserErrorException => exception.Message,
            SpecValidationException validation =>
                "Spec validation failed:\n" + string.Join("\n", validation.Errors.Select(e => $"  - {e.Message}")),
            _ => null
        };
    }

    public static int Write(Exception exception, TextWriter error)
    {
        string? userFacing = UserFacingMessage(exception);
        if (userFacing is null)
        {
            error.WriteLine(exception.ToString());
            return ExitCodes.Error;
        }

        // Line by line, as every composed block is written, so this reads the same on a CRLF console as the
        // MCP surface renders it after normalization.
        LineBlocks.Write(error, userFacing);
        return ExitCodes.Error;
    }
}
