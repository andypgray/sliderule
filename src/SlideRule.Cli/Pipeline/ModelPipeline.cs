using System.Reflection;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Settings;
using SlideRule.Cli.SpecLoading;
using SlideRule.Discovery;
using SlideRule.Hosting;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Cli.Pipeline;

/// <summary>
///     The two spec→model primitives every workspace command needs, <see cref="DiscoverSolution" /> and
///     <see cref="LoadModel" />, each of which also runs where no <see cref="CodebaseSource" /> exists.
/// </summary>
internal static class ModelPipeline
{
    /// <summary>
    ///     Discovers the target solution file: the argument (a file, or a directory to search), then
    ///     <c>SLIDERULE_SOLUTION_PATH</c> read through <paramref name="environment" />, then a walk-up from the
    ///     working directory. Discovery failures become <see cref="UserErrorException" />.
    /// </summary>
    public static string DiscoverSolution(string? solution, string workingDirectory, IEnvironment environment)
    {
        string? fromEnvironment = environment.NonBlankVariable(SlideRuleEnvVars.SolutionPath);
        try
        {
            return SolutionDiscovery.DiscoverSolution(solution, workingDirectory, fromEnvironment);
        }
        catch (FileNotFoundException ex)
        {
            throw new UserErrorException(ex.Message, ex);
        }
        catch (InvalidOperationException ex) when (ex is not UserErrorException)
        {
            // Only the walk throws this, when it found no solution or too many. A settings file names the solution
            // for every command and the server at once, so this refusal is where it is offered.
            throw new UserErrorException(ex.Message + SettingsHint, ex);
        }
    }

    private const string SettingsHint =
        $"\nOr name it once, for every command and the MCP server, in a {ProjectSettings.FileName} at the root "
        + "of the repository: {\"solution\": \"src/MyApp.sln\"}.";

    /// <summary>
    ///     Loads the spec DLL in a collectible ALC (the <c>SlideRule</c> contract resolves to this
    ///     host's own copy, whatever version the spec was compiled against, so type identity holds),
    ///     discovers and builds the model, then best-effort unloads.
    ///     The returned model roots the spec's <c>Type</c> references, so it stays usable after
    ///     <c>Unload()</c>.
    /// </summary>
    public static ArchitectureModel LoadModel(string specDllPath)
    {
        using var context = new SpecLoadContext(specDllPath);
        Assembly assembly = context.LoadWithoutLocking(specDllPath);
        IReadOnlyList<IArchitectureSpec> specs;
        try
        {
            specs = SpecDiscovery.FindSpecs(assembly);
        }
        catch (SpecDiscoveryException ex)
        {
            throw new UserErrorException(ex.Message, ex);
        }
        catch (Exception ex) when (SpecContractMismatch.SkewCause(ex) is { } cause)
        {
            // A contract member or type this host's copy lacks or keeps internal, met while the spec's types
            // load or a spec class is created.
            throw SpecContractMismatch.MapSkew(assembly, specDllPath, cause);
        }
        catch (ReflectionTypeLoadException ex)
        {
            // GetTypes() couldn't load every type — a missing or version-mismatched dependency of the
            // spec assembly. Surface the distinct loader messages as one user error, not a reflection crash.
            throw new UserErrorException(LoaderFailureMessage(ex, specDllPath), ex);
        }

        try
        {
            return ArchModelBuilder.Build(specs); // SpecValidationException propagates to the top handler
        }
        catch (FileNotFoundException ex) when (SpecDependencyLoadFailure.IsAssemblyLoadFailure(ex))
        {
            throw SpecDependencyLoadFailure.Map(ex, specDllPath);
        }
        catch (Exception ex) when (SpecContractMismatch.SkewCause(ex) is { } cause)
        {
            // A contract member or type this host's copy lacks or keeps internal, met while Define() runs. The
            // bind is version-agnostic by design, so it got this far; what the spec reached for is the one
            // thing that cannot work.
            throw SpecContractMismatch.MapSkew(assembly, specDllPath, cause);
        }
        catch (TypeLoadException ex)
        {
            // A typeof() anchor whose base type or implemented interface lives in a .NET Framework-only
            // assembly: the dependency resolved, the type did not. Without this arm it surfaces raw.
            throw SpecDependencyLoadFailure.Map(ex, specDllPath);
        }
    }

    /// <summary>
    ///     The <see cref="UserErrorException" /> text for a <see cref="ReflectionTypeLoadException" /> out of
    ///     spec discovery: the <em>distinct</em> loader messages (deduped, ordinal-sorted so the output is
    ///     deterministic) under a naming/fix frame.
    /// </summary>
    /// <remarks>
    ///     The remedy names both the package fix and the shared-framework one: a shared-framework anchor reaches
    ///     this arm too, and the aggregated loader exceptions need not carry an assembly identity to branch on.
    /// </remarks>
    internal static string LoaderFailureMessage(ReflectionTypeLoadException ex, string specDllPath)
    {
        List<string> messages = ex.LoaderExceptions
            .Select(inner => inner?.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Select(message => message!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToList();

        string detail = messages.Count > 0
            ? string.Join("\n", messages.Select(message => "  " + message))
            : "  (the runtime reported no loader detail)";

        return $"Could not load spec assembly '{Path.GetFileName(specDllPath)}'; one or more types failed to load:\n"
               + detail
               + "\nBuild the spec project and restore its dependencies, then retry. If it is already built, an"
               + " assembly named above is one the spec's own build does not put where the loader can reach it:"
               + " for a NuGet package, add <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies> to"
               + " the spec .csproj and rebuild; for a .NET shared framework (<FrameworkReference>) or a .NET"
               + " Framework reference assembly, no build setting stages it — name the type as a string rather"
               + " than a typeof(), which needs no assembly load.";
    }
}
