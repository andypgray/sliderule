using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     The single seam through which the MCP server reads process environment variables. Everything
///     else is a pure function of these values, which is what lets the xUnit suite drive the pipeline
///     without ever mutating real process state.
/// </summary>
internal interface IEnvironment
{
    string? GetVariable(string name);
}

/// <summary>
///     The readings a run takes off the <see cref="IEnvironment" /> seam, so each is spelled once.
/// </summary>
internal static class EnvironmentReadings
{
    /// <summary>The value of <paramref name="name" />, or <c>null</c> when it is unset or blank.</summary>
    internal static string? NonBlankVariable(this IEnvironment environment, string name)
    {
        string? value = environment.GetVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    ///     The operator's cache-root override (<see cref="SlideRuleEnvVars.CacheDirectory" />), or
    ///     <c>null</c> when it is unset or blank.
    /// </summary>
    /// <remarks>
    ///     One owner because a run's binlog capture store and its extraction-fragment cache have to agree on
    ///     where the cache is: they read the same variable through the same seam, and two normalizations of
    ///     "blank means unset" are two chances for them to disagree.
    /// </remarks>
    internal static string? CacheRootOverride(this IEnvironment environment)
    {
        return environment.NonBlankVariable(SlideRuleEnvVars.CacheDirectory);
    }
}
