using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Settings;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     What a warm server answers for and what built it: the solution, the spec, the hand-off protocol's major
///     version and the CLI's build. A hook reaches a server only when the two agree on all four.
/// </summary>
/// <remarks>
///     Both paths are canonical and case-folded where the OS ignores case, so two spellings of one file agree. The
///     server and the hook must compute it through <see cref="Resolve" /> alone: the server is often started with
///     no arguments and takes both paths from <c>sliderule.json</c>, while the hook resolves them from the tree.
/// </remarks>
internal sealed record HandoffIdentity(string Solution, string Spec, int ProtocolMajor, string Build)
{
    /// <summary>The protocol's major version; a change to either frame's shape bumps it.</summary>
    internal const int CurrentProtocolMajor = 1;

    /// <summary>The spec identity of a run that names no spec and takes the convention's.</summary>
    internal const string ConventionSpec = "convention";

    // 96 bits of the identity's digest: short enough for a Unix socket path, ample against collisions.
    private const int EndpointHashLength = 24;

    /// <summary>The name the endpoint for this identity is created and found under.</summary>
    public string EndpointName =>
        "sliderule-" + FileStamping.HashText($"{Solution}\n{Spec}\n{ProtocolMajor}\n{Build}")[..EndpointHashLength];

    /// <summary>
    ///     The identity of a run over <paramref name="solution" /> and <paramref name="spec" /> from
    ///     <paramref name="workingDirectory" />, once the settings file applies, and the discovered solution file
    ///     the run checks.
    /// </summary>
    /// <exception cref="UserErrorException">The settings file is malformed, or discovery finds no solution.</exception>
    public static HandoffIdentity Resolve(
        string? solution, string? spec, string workingDirectory, IEnvironment environment, out string solutionPath)
    {
        InvocationTargets targets = InvocationTargets.Resolve(solution, spec, workingDirectory, environment);
        solutionPath = ModelPipeline.DiscoverSolution(targets.Solution, workingDirectory, environment);
        string specIdentity = targets.Spec is null
            ? ConventionSpec
            : Canonical(Path.GetFullPath(targets.Spec, workingDirectory));

        return new HandoffIdentity(Canonical(solutionPath), specIdentity, CurrentProtocolMajor, BuildFingerprint.Current);
    }

    private static string Canonical(string path)
    {
        string resolved = PathCanonicalizer.Resolve(path);
        return PathComparison.Fold(resolved);
    }
}
