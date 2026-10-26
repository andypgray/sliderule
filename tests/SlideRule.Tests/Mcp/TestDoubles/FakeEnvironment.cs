using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Hosting;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Mcp.TestDoubles;

/// <summary>
///     A hand-rolled <see cref="IEnvironment" /> for tests: dictionary-backed variables, whichever ones the
///     subject reads, starting from the run's own cache root. Using this instead of mutating real process
///     environment variables keeps the parallel test run free of shared-state races.
/// </summary>
internal sealed class FakeEnvironment : IEnvironment
{
    // Seeded with the run-scoped root MsBuildInitializer gives the process, and must agree with it: without the
    // seed a cache-wired run handed a bare double falls back to the product's default root under %LOCALAPPDATA%.
    private readonly Dictionary<string, string> _variables = new(StringComparer.Ordinal)
    {
        [SlideRuleEnvVars.CacheDirectory] = TestTempRoot.For("cache")
    };

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
