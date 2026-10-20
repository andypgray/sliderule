using System.Reflection;

namespace SlideRule.Xunit;

/// <summary>
///     Whether the copy of SlideRule this test process loaded is the adapter's own version. The adapter runs
///     the contract's engine through an internals grant, which binds at run time rather than at compile time, so a
///     test project that resolves the two packages at different versions would fail on whatever member moved
///     between them, with an error that names neither package.
/// </summary>
// Deliberately self-contained: it names one contract type, which every version carries, and nothing else of the
// contract's, because every other member is exactly what a mismatched copy may not have. The analyzers make the
// same check the same way (SR2093).
internal static class ContractVersion
{
    /// <summary>
    ///     The refusal for a test process whose contract is not the adapter's version, or null when they agree.
    /// </summary>
    internal static string? Mismatch()
    {
        return Mismatch(
            InformationalVersionOf(typeof(ContractVersion).Assembly),
            InformationalVersionOf(typeof(IArchitectureSpec).Assembly));
    }

    /// <summary>
    ///     The refusal for an adapter built as <paramref name="adapter" /> beside a contract built as
    ///     <paramref name="contract" /> (both informational versions), or null when they are one release.
    /// </summary>
    internal static string? Mismatch(string? adapter, string? contract)
    {
        string adapterVersion = Released(adapter);
        string contractVersion = Released(contract);
        if (string.Equals(adapterVersion, contractVersion, StringComparison.Ordinal)) return null;

        return $"The SlideRule xUnit adapter checked nothing: it is version {Shown(adapterVersion)}, and the test "
               + $"project loaded version {Shown(contractVersion)} of SlideRule. That happens when a project "
               + "references SlideRule and SlideRule.Xunit at two different versions, so keep the two "
               + "packages on one version.";
    }

    private static string? InformationalVersionOf(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
    }

    // Up to the first '+', which drops the commit a build appends; empty for a missing version, which no real
    // version equals.
    private static string Released(string? informational)
    {
        if (informational is null) return string.Empty;

        int plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    private static string Shown(string version)
    {
        return version.Length == 0 ? "(unknown)" : version;
    }
}
