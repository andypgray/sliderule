using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Internal;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Settings;

/// <summary>
///     The solution and spec a run is about once a committed <c>sliderule.json</c> has had its say, and the
///     settings that said it. Every surface resolves through <see cref="Resolve" /> before building its request,
///     so one file binds them all the same way.
/// </summary>
/// <param name="Solution">
///     The solution to hand the pipeline: the settings' solution where a settings file applies, otherwise the
///     caller's argument untouched, for the pipeline's own discovery to resolve.
/// </param>
/// <param name="Spec">The explicit <c>--spec</c> when given, else the settings' spec, else null for the convention.</param>
/// <param name="Settings">The settings file that applies, or null.</param>
internal sealed record InvocationTargets(string? Solution, string? Spec, ProjectSettings? Settings)
{
    /// <summary>
    ///     Applies the nearest <c>sliderule.json</c> to a run's solution argument and <c>--spec</c> value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         With no solution argument and no <c>SLIDERULE_SOLUTION_PATH</c>, the walk up from the working
    ///         directory stops at the first directory that holds a settings file or exactly one solution,
    ///         whichever it meets first. A settings file found that way names the solution. A solution found that
    ///         way, or passed explicitly, takes its spec and render targets from the nearest settings file above
    ///         it only when that file names the same solution — which is what keeps a fixture solution staged
    ///         inside another repository's tree from taking that repository's settings.
    ///     </para>
    ///     <para>
    ///         An explicit argument always wins over the file, and so does the environment variable; the file wins
    ///         over the plain walk. A directory argument is searched the way the working directory is.
    ///     </para>
    /// </remarks>
    /// <exception cref="UserErrorException">A settings file that applies is malformed or names a missing solution.</exception>
    public static InvocationTargets Resolve(string? solution, string? spec, string workingDirectory, IEnvironment environment)
    {
        ProjectSettings? settings = SettingsFor(solution, workingDirectory, environment);
        if (settings is null) return new InvocationTargets(solution, spec, null);

        if (!File.Exists(settings.Solution))
            throw new UserErrorException(
                $"{settings.FilePath} names a solution that does not exist: {settings.Solution}. Fix its 'solution', "
                + "or pass the solution as the argument.");

        return new InvocationTargets(settings.Solution, spec ?? settings.Spec, settings);
    }

    // The settings file that applies to this run: the one the walk stopped at, or the one the chosen solution's
    // own tree declares for it. Null when none applies, which leaves the pipeline's own discovery to say so.
    private static ProjectSettings? SettingsFor(string? solution, string workingDirectory, IEnvironment environment)
    {
        string? fromEnvironment = environment.NonBlankVariable(SlideRuleEnvVars.SolutionPath);
        SolutionStart start = SolutionDiscovery.Choose(solution, workingDirectory, fromEnvironment);
        return start.Kind == SolutionStartKind.Search ? Walk(start.Path) : NearestFor(start.Path);
    }

    // Up from start to the first directory holding a settings file or exactly one solution, and the settings that
    // apply there: that file, or the one the lone solution's own tree declares for it. A directory holding several
    // solutions is climbed past, as the pipeline's own walk climbs past it.
    private static ProjectSettings? Walk(string start)
    {
        foreach ((string directory, string[] solutions) in SolutionDiscovery.Ancestors(start))
        {
            if (SettingsIn(directory) is { } settings) return settings;

            if (solutions.Length == 1) return NearestFor(solutions[0]);
        }

        return null;
    }

    // The nearest settings file at or above the solution's directory, when it names that solution. The nearest
    // file governs its tree: one that names another solution means no settings apply, and the walk does not
    // go on past it to a file further up.
    private static ProjectSettings? NearestFor(string solution)
    {
        string wanted = PathCanonicalizer.Resolve(solution);
        for (DirectoryInfo? directory = new FileInfo(wanted).Directory; directory is not null; directory = directory.Parent)
        {
            if (SettingsIn(directory.FullName) is not { } settings) continue;

            return string.Equals(PathCanonicalizer.Resolve(settings.Solution), wanted, PathComparison.Comparison)
                ? settings
                : null;
        }

        return null;
    }

    private static ProjectSettings? SettingsIn(string directory)
    {
        string candidate = Path.Combine(directory, ProjectSettings.FileName);
        return File.Exists(candidate) ? ProjectSettingsReader.Read(candidate) : null;
    }
}
