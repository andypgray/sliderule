using System.Runtime.CompilerServices;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.MsBuild;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests;

/// <summary>
///     Ensures MSBuild is registered once before any test code runs, routed through
///     <see cref="MsBuildBootstrap" /> so tests exercise the same stable-version selection as
///     production.
/// </summary>
internal static class MsBuildInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        MsBuildBootstrap.EnsureInitialized();

        // Point the persisted extraction cache at a fresh per-run temp directory so no run
        // reads a cache another run wrote, and so this process never touches a developer's real
        // %LOCALAPPDATA% cache. Because the cache starts empty each run, the golden E2Es exercise both
        // cache paths (a miss on the first touch of a solution, a hit on later identical touches), which
        // makes the golden suite a continuous cold-vs-hit equality pin: any leak of the cache into
        // observable output shows up as golden churn. Read CLI-side through IEnvironment/SystemEnvironment.
        // Routed through TestTempRoot so the roots earlier runs left behind get swept.
        Environment.SetEnvironmentVariable(SlideRuleEnvVars.CacheDirectory, TestTempRoot.For("cache"));

        // NB: fixture restore is deliberately NOT done here. A [ModuleInitializer] runs during the
        // runner's assembly-info probe too, whose 60s no-response deadline a cold restore blows past
        // (timing out discovery so zero tests run). It runs from FixtureRestoreStartup, an xUnit v3
        // pipeline-startup hook that fires only in the discover/run pass. Keep this fast.
    }
}
