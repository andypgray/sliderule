using SlideRule.Cli.Pipeline;

namespace SlideRule.Cli.Mcp.Pipeline;

/// <summary>
///     The MCP server's fitter: walk the ladder and answer with the first rung that fits the client's budget,
///     so an over-budget document comes back whole at a coarser grain instead of cut at a line boundary.
/// </summary>
/// <remarks>
///     It walks the whole ladder, because on a large solution even the overview the full survey degrades to can
///     be over the 50,000-character ceiling that bounds every budget. Past the last rung it returns that rung
///     anyway, and a document still over budget at its coarsest grain lands on the truncator like any other
///     response. The budget is read once per <see cref="Fit" />, once per tool call, and the rungs are pulled one
///     at a time, so a document that fits at the grain the caller asked for costs exactly one serialization.
/// </remarks>
internal sealed class BudgetedResponseFitter(IResponseBudget budget) : IResponseFitter
{
    /// <inheritdoc />
    public string Fit(IEnumerable<string> ladder)
    {
        int maxChars = budget.MaxChars();
        var coarsest = string.Empty;

        foreach (string rung in ladder)
        {
            coarsest = rung;
            if (rung.Length <= maxChars) return rung;
        }

        return coarsest;
    }
}
