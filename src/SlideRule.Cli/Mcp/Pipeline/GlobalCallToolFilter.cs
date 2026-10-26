using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The single point where tool-call exceptions become error results and successful responses are truncated.
///     It also brackets every call with the idle watchdog's in-flight count so a long cold-solution load can never
///     self-trip the timeout.
/// </summary>
internal static class GlobalCallToolFilter
{
    // The SDK's argument-marshalling layer wraps a coercer-thrown UserErrorException one or two
    // JsonExceptions deep; 8 is loose headroom against a pathological chain.
    private const int MaxExceptionChainDepth = 8;

    /// <summary>
    ///     Wraps every <c>tools/call</c> so an expected user-facing error comes back as an error result,
    ///     anything unexpected is logged exactly once, and successful text is truncated to the client's budget.
    /// </summary>
    /// <remarks>
    ///     Only the nullness of <paramref name="bindingFailure" /> is read. On an unbound server every error also
    ///     carries <see cref="ServerInstructions.UnboundCallCoda" />, because the banner that named a recovery was
    ///     delivered once, at the handshake, and a reader arriving at a failed tool call may never have seen it. The
    ///     parameter is required so a forgotten coda cannot leave a silent hole in the only channel an unbound
    ///     session still reads.
    /// </remarks>
    public static IMcpServerBuilder WithGlobalCallToolFilter(this IMcpServerBuilder builder, string? bindingFailure)
    {
        return builder.WithRequestFilters(filters =>
        {
            filters.AddCallToolFilter(next => async (context, cancellationToken) =>
            {
                IdleTimeoutWatchdog.EnterCall();
                try
                {
                    CallToolResult result;
                    try
                    {
                        // Reject unknown or missing argument keys before binding, against the tool the SDK matched;
                        // a call that matched none is left to the SDK's own unknown-tool answer. The guard's message
                        // is a UserErrorException, so it flows through the silent-user-error path below.
                        if (context.MatchedPrimitive is McpServerTool tool
                            && UnknownParameterGuard.Validate(tool.ProtocolTool, context.Params.Arguments) is { } parameterError)
                            throw new UserErrorException(parameterError);

                        result = await next(context, cancellationToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The first user-facing message in the chain surfaces silently, exactly as a directly
                        // thrown one would. Anything else is a bug: log one warning, then surface.
                        if (FindUserFacingMessage(ex) is { } message) return ErrorResult(message, bindingFailure);

                        context.Server.Services?.GetService<ILoggerFactory>()
                            ?.CreateLogger(typeof(GlobalCallToolFilter))
                            .LogWarning(ex, "Tool '{ToolName}' failed", context.Params.Name);

                        return ErrorResult(ex.Message, bindingFailure);
                    }

                    if (result.IsError is not true)
                    {
                        // The budget the tools degrade against, resolved from the request context. Required, not
                        // optional: a GetService fallback would use the default cap whenever the service was
                        // missing, turning a composition bug into a silently-wrong number. The provider cannot be
                        // absent by the time a result exists — the tool that produced it was itself constructed from
                        // it — unlike the logger above, which is best-effort by design.
                        int maxChars = context.Server.Services!.GetRequiredService<IResponseBudget>()
                            .MaxChars();
                        string toolName = context.Params.Name;
                        foreach (ContentBlock contentBlock in result.Content)
                            if (contentBlock is TextContentBlock textBlock)
                                textBlock.Text = ResponseTruncator.TruncateIfNeeded(textBlock.Text, toolName, maxChars);
                    }

                    return result;
                }
                finally
                {
                    IdleTimeoutWatchdog.ExitCall();
                }
            });
        });
    }

    /// <summary>
    ///     The error result for <paramref name="message" />, with the session's recovery appended when the
    ///     server never bound to a solution.
    /// </summary>
    /// <remarks>
    ///     The coda rides every error while unbound, whatever failed: every dispatched call ends in the discovery
    ///     refusal anyway, and the calls that fail earlier (the parameter guard, a coercer) would only reach it once
    ///     the argument was fixed, so the advice is true of all of them. Errors bypass the truncator, so the coda can
    ///     never be the part that gets cut.
    /// </remarks>
    private static CallToolResult ErrorResult(string message, string? bindingFailure)
    {
        string text = bindingFailure is null
            ? message
            : message + "\n\n" + ServerInstructions.UnboundCallCoda;

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text }],
            IsError = true
        };
    }

    /// <summary>
    ///     Walks up to <see cref="MaxExceptionChainDepth" /> inner exceptions for the first one
    ///     <see cref="CliErrorMapper.UserFacingMessage" /> recognizes, returning its rendered message, or
    ///     <see langword="null" /> when the failure is an unexpected error.
    /// </summary>
    private static string? FindUserFacingMessage(Exception? ex)
    {
        for (var depth = 0; ex is not null && depth < MaxExceptionChainDepth; depth++)
        {
            if (CliErrorMapper.UserFacingMessage(ex) is { } message) return message;
            ex = ex.InnerException;
        }

        return null;
    }
}
