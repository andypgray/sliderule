using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Hosting;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Hooks.Handoff;

/// <summary>
///     The configuration a check's answer can depend on, as one digest per environment variable and one for the
///     <c>global.json</c> that selects the SDK, so a hook and a server can tell whether they would answer alike
///     without either seeing the other's values.
/// </summary>
/// <remarks>
///     MSBuild reads any environment variable as a property, so the set is every variable less the ones named in
///     <see cref="IsDenied" />, never a list of the ones that matter. A variable wrongly left in costs a cold
///     stop; one wrongly denied could serve an answer the hook's own check would not give. The shell's bookkeeping
///     and the search path differ between launchers without changing what a lookup finds, so each side first puts
///     its own environment in one form with <see cref="Canonicalize" />, and the digests then compare the values
///     MSBuild will read.
/// </remarks>
internal sealed class HandoffConfiguration(IReadOnlyDictionary<string, string> digests)
{
    private const string VariablePrefix = "env:";
    private const string GlobalJsonKey = "file:global.json";
    private const string SearchPathVariable = "PATH";

    // Variables that configure the hook or the server's lifetime, none of which a check reads.
    private static readonly HashSet<string> DeniedVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        SlideRuleEnvVars.IdleTimeoutMinutes,
        SlideRuleEnvVars.DisableParentWatch,
        SlideRuleEnvVars.DisableWarmWorkspace,
        SlideRuleEnvVars.LogLevel,
        SlideRuleEnvVars.HookStateDirectory,
        SlideRuleEnvVars.HookTimeout,
        SlideRuleEnvVars.DisableWarmHook,
        SlideRuleEnvVars.MaxMcpOutputTokens
    };

    // The shell's own bookkeeping, which each launcher sets its own way and nothing in a check needs.
    private static readonly HashSet<string> ShellBookkeeping = new(EnvironmentNames) { "SHELL", "PWD", "OLDPWD", "SHLVL", "_" };

    /// <summary>Each fingerprinted input's digest, keyed by a name that says which input it is.</summary>
    public IReadOnlyDictionary<string, string> Digests => digests;

    // Windows reads environment names and directory names without regard to case.
    private static StringComparer EnvironmentNames => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    ///     Puts <paramref name="environment" /> in the form both sides of a hand-off check in: the shell's bookkeeping
    ///     removed, and the search path in its search order, each directory once, in its first spelling, with no
    ///     trailing separator. A composition root takes it through <see cref="Settle" />, which runs it before
    ///     <see cref="Capture" />; either must run before anything registers MSBuild.
    /// </summary>
    public static void Canonicalize(IEnvironment environment)
    {
        foreach ((string name, string value) in environment.Variables())
            if (ShellBookkeeping.Contains(name))
                environment.SetVariable(name, null);
            else if (EnvironmentNames.Equals(name, SearchPathVariable))
                environment.SetVariable(name, SearchOrder(value));
    }

    /// <summary>
    ///     Puts <paramref name="environment" /> in the form both sides of a hand-off check in, as <see cref="Canonicalize" />
    ///     does, and captures the configuration it then holds from <paramref name="workingDirectory" />. Call it before
    ///     anything registers MSBuild.
    /// </summary>
    public static HandoffConfiguration Settle(IEnvironment environment, string workingDirectory)
    {
        Canonicalize(environment);
        return Capture(environment, workingDirectory);
    }

    /// <summary>
    ///     The configuration as <paramref name="environment" /> holds it now, with the nearest <c>global.json</c> at
    ///     or above <paramref name="workingDirectory" />, the process's own working directory.
    /// </summary>
    public static HandoffConfiguration Capture(IEnvironment environment, string workingDirectory)
    {
        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string name, string value) in environment.Variables())
            if (!IsDenied(name))
                captured[VariablePrefix + KeyOf(name)] = FileStamping.HashText(value);

        if (GlobalJsonPin.NearestFile(workingDirectory) is { } globalJson)
            captured[GlobalJsonKey] = FileStamping.HashText(
                $"{globalJson}\n{FileStamping.TryHashFile(globalJson) ?? "unreadable"}");

        return new HandoffConfiguration(captured);
    }

    /// <summary>Whether a check's answer cannot depend on the variable <paramref name="name" />.</summary>
    internal static bool IsDenied(string name)
    {
        // A name that opens with "=" is one of Windows' per-drive working directories, which every process keeps.
        return name.StartsWith("CLAUDE_", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith('=')
               || DeniedVariables.Contains(name);
    }

    /// <summary>
    ///     The inputs whose digests differ from <paramref name="other" />'s, or that only one side has, by name and
    ///     in order; empty when the two would configure a check alike.
    /// </summary>
    public IReadOnlyList<string> DifferingInputs(IReadOnlyDictionary<string, string> other)
    {
        return digests.Keys
            .Union(other.Keys, StringComparer.Ordinal)
            .Where(key => !digests.TryGetValue(key, out string? mine)
                          || !other.TryGetValue(key, out string? theirs)
                          || !string.Equals(mine, theirs, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // A lookup takes the first match in order, so a repeated directory is never reached a second time, and a trailing
    // separator names the same directory: the result finds the same file for every name as the value it came from.
    // Git for Windows' launchers prefix their own directories, the hook's and the server's differently.
    private static string SearchOrder(string value)
    {
        var seen = new HashSet<string>(EnvironmentNames);
        IEnumerable<string> directories = value
            .Split(Path.PathSeparator)
            .Select(static entry => Path.TrimEndingDirectorySeparator(entry))
            .Where(seen.Add);

        return string.Join(Path.PathSeparator, directories);
    }

    // One variable has one key whatever its spelling, on Windows.
    private static string KeyOf(string name)
    {
        return OperatingSystem.IsWindows() ? name.ToUpperInvariant() : name;
    }
}
