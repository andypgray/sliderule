using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Tests.Mcp.TestDoubles;

/// <summary>
///     A hand-rolled <see cref="IEnvironment" /> for tests: dictionary-backed variables, whichever ones the
///     subject reads. Using this instead of mutating real process environment variables keeps the parallel
///     test run free of shared-state races.
/// </summary>
internal sealed class FakeEnvironment : IEnvironment
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    /// <summary>
    ///     An environment whose one variable points the extraction cache at <paramref name="cacheRoot" />.
    /// </summary>
    public static FakeEnvironment WithCacheRoot(string cacheRoot)
    {
        return new FakeEnvironment().SetVariable(SlideRuleEnvVars.CacheDirectory, cacheRoot);
    }

    public string? GetVariable(string name)
    {
        return _variables.GetValueOrDefault(name);
    }

    /// <summary>Sets (or, with a <c>null</c> value, clears) an environment variable.</summary>
    /// <returns><c>this</c>, for chaining.</returns>
    public FakeEnvironment SetVariable(string name, string? value)
    {
        if (value is null)
            _variables.Remove(name);
        else
            _variables[name] = value;

        return this;
    }
}
