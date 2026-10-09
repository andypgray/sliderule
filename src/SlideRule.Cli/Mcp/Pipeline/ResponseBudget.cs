using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The client's response budget in characters — the one number both halves of the over-budget answer are
///     measured against, so a document can never degrade against one cap and then be truncated against another.
/// </summary>
internal interface IResponseBudget
{
    /// <summary>
    ///     The cap right now, read per tool call and never cached. A stdio server's environment is fixed
    ///     once its client starts it, so in a running server the answer never moves; reading per call is
    ///     what lets the tests drive one in-process server at several budgets.
    /// </summary>
    int MaxChars();
}

/// <summary>
///     The budget as the server reads it: the client's
///     <see cref="SlideRuleEnvVars.MaxMcpOutputTokens" /> through the
///     <see cref="IEnvironment" /> seam, converted by
///     <see cref="ResponseTruncator.ComputeMaxChars" /> — which stays the pure computation its own tests pin.
/// </summary>
/// <remarks>
///     A singleton over a seam that is itself read per call. Reading <see cref="IEnvironment" /> once at
///     construction would answer the same in a running server, whose environment its client fixed at
///     start, and would pin the first budget for an in-process server a test drives at several.
/// </remarks>
internal sealed class ResponseBudget(IEnvironment environment) : IResponseBudget
{
    /// <inheritdoc />
    public int MaxChars()
    {
        return ResponseTruncator.ComputeMaxChars(environment.GetVariable(SlideRuleEnvVars.MaxMcpOutputTokens));
    }
}
