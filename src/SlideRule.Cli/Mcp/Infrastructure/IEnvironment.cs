using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Mcp.Infrastructure;

/// <summary>
///     The single seam through which the CLI and the MCP server read process environment variables.
///     Everything else is a pure function of these values, which is what lets a test drive the
///     pipeline without ever mutating real process state.
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
    ///     One owner because a run's binlog capture store and its extraction-fragment cache must agree on where the
    ///     cache is, and two normalizations of "blank means unset" are two chances for them to disagree.
    /// </remarks>
    internal static string? CacheRootOverride(this IEnvironment environment)
    {
        return environment.NonBlankVariable(SlideRuleEnvVars.CacheDirectory);
    }

    /// <summary>
    ///     Whether the opt-out <paramref name="name" /> is set, in the spelling every <c>SLIDERULE_*</c> opt-out
    ///     takes: the literal <c>true</c>, case-insensitively, and nothing else, so a value nobody meant as a flag
    ///     leaves the behaviour on.
    /// </summary>
    internal static bool IsFlagSet(this IEnvironment environment, string name)
    {
        return string.Equals(environment.GetVariable(name), "true", StringComparison.OrdinalIgnoreCase);
    }
}
