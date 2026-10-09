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
///     The two spec→model primitives every workspace command needs: <see cref="DiscoverSolution" /> (which
///     solution a run is about) and <see cref="LoadModel" /> (the ALC-loaded, validated model behind a spec
///     DLL). <see cref="CodebaseSource" /> composes them into the one acquisition ladder — discover, acquire
///     the workspace, resolve the spec, load the model — so both live here rather than in it: solution
///     discovery also runs before any codebase source exists, and <c>explain</c>'s DLL fast path
///     reaches <see cref="LoadModel" /> alone, with no workspace at all
///     (<see cref="SpecResolver.TryResolveWithoutSolution" />).
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
        var context = new SpecLoadContext(specDllPath);
        try
        {
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
        finally
        {
            context.Unload();
        }
    }

    /// <summary>
    ///     The <see cref="UserErrorException" /> text for a <see cref="ReflectionTypeLoadException" /> out of
    ///     spec discovery: the <em>distinct</em> loader messages (deduped, ordinal-sorted so the output is
    ///     deterministic) under a naming/fix frame. Internal so a fabricated exception can pin the shape.
    /// </summary>
    /// <remarks>
    ///     The remedy names both worlds rather than asserting the packaging one, because a shared-framework
    ///     anchor reaches this arm too and no build setting can stage it. It does not pick between them the
    ///     way <see cref="SpecDependencyLoadFailure" /> does: this failure aggregates several loader
    ///     exceptions that need not carry assembly identities at all, so one remedy line cannot honestly
    ///     branch per assembly. Naming both is what that costs.
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
