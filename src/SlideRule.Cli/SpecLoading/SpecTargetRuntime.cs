using System.Reflection;
using SlideRule.Roslyn;

namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     Refuses a spec assembly built for a newer .NET than the runtime the tool is running on, before any of its
///     types load. Without it the spec dies inside discovery on <c>System.Runtime</c>, under a remedy about
///     packages and shared frameworks that does not apply.
/// </summary>
internal static class SpecTargetRuntime
{
    private const string RuntimeContract = "System.Runtime";

    /// <summary>
    ///     Throws the <see cref="Refusal" /> for <paramref name="spec" /> on the running runtime, if there is one.
    /// </summary>
    /// <exception cref="UserErrorException">The spec references a newer <c>System.Runtime</c> major.</exception>
    internal static void Require(Assembly spec, string specDllPath)
    {
        AssemblyName[] references = spec.GetReferencedAssemblies();
        if (Refusal(references, Environment.Version, specDllPath) is { } refusal) throw refusal;
    }

    /// <summary>
    ///     The refusal for a spec whose <paramref name="references" /> name a <c>System.Runtime</c> major above
    ///     <paramref name="runtime" />'s, or null. A spec on .NET Framework or .NET Standard references
    ///     <c>mscorlib</c> or <c>netstandard</c> instead, so it always passes.
    /// </summary>
    internal static UserErrorException? Refusal(IEnumerable<AssemblyName> references, Version runtime, string specDllPath)
    {
        int? target = references
            .Where(reference => string.Equals(reference.Name, RuntimeContract, StringComparison.Ordinal))
            .Select(reference => reference.Version?.Major)
            .Max();
        if (target is not { } major || major <= runtime.Major) return null;

        string spec = Path.GetFileNameWithoutExtension(specDllPath);
        return new UserErrorException(
            $"The spec assembly '{spec}' was built for .NET {major}, and sliderule is running on .NET {runtime}. "
            + $"Install the .NET {major} runtime, since sliderule runs on the newest .NET runtime installed, or "
            + $"retarget the spec project to a framework .NET {runtime.Major} runs, such as net{runtime.Major}.0, "
            + "and rebuild it.");
    }
}
