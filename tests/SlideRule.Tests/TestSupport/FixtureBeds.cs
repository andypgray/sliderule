using System.Reflection;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Where the fixture beds and the built fixture assemblies sit in the test output: the solution beds the
///     build copies under <c>Fixtures/TestSolutions</c>, and the DLL paths it bakes into this assembly's
///     metadata.
/// </summary>
internal static class FixtureBeds
{
    /// <summary>The directory the build copies every fixture tree under.</summary>
    internal static string FixturesRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    /// <summary>The directory every solution bed is copied under.</summary>
    internal static string TestSolutionsRoot => Path.Combine(FixturesRoot, "TestSolutions");

    /// <summary>The MyApp bed's directory: its solution, its projects and its <c>arch/</c> baselines.</summary>
    internal static string MyAppRoot => Path.Combine(TestSolutionsRoot, "MyApp");

    /// <summary>The MyApp bed's solution.</summary>
    internal static string MyAppSolution => Path.Combine(MyAppRoot, "MyApp.sln");

    /// <summary>The polyglot bed's solution, which declares an F# project beside a C# one.</summary>
    internal static string PolyglotAppSolution => Path.Combine(TestSolutionsRoot, "PolyglotApp", "PolyglotApp.slnx");

    /// <summary>The drifted-copies bed's solution, whose projects declare drifted copies of one type name.</summary>
    internal static string DriftedAppSolution => Path.Combine(TestSolutionsRoot, "DriftedApp", "DriftedApp.slnx");

    /// <summary>The multi-targeting bed's directory, whose core project targets two frameworks.</summary>
    internal static string MultiTfmRoot => Path.Combine(TestSolutionsRoot, "MultiTfm");

    /// <summary>The multi-targeting bed's solution.</summary>
    internal static string MultiTfmSolution => Path.Combine(MultiTfmRoot, "MultiTfm.sln");

    /// <summary>
    ///     The Razor bed's solution, whose web project compiles generated view types beside authored ones.
    /// </summary>
    internal static string RazorAppSolution => Path.Combine(TestSolutionsRoot, "RazorApp", "RazorApp.slnx");

    /// <summary>
    ///     The shadowed-name bed's solution, whose test project declares a full name a referenced package also
    ///     supplies.
    /// </summary>
    internal static string ShadowedAppSolution => Path.Combine(TestSolutionsRoot, "ShadowedApp", "ShadowedApp.slnx");

    /// <summary>
    ///     The linked-source bed's solution, whose tool project compiles a source file of the core project's.
    /// </summary>
    internal static string LinkedAppSolution => Path.Combine(TestSolutionsRoot, "LinkedApp", "LinkedApp.slnx");

    /// <summary>
    ///     The solution-directory bed's solution, whose projects reach their references through
    ///     <c>$(SolutionDir)</c>.
    /// </summary>
    internal static string SolutionDirAppSolution =>
        Path.Combine(TestSolutionsRoot, "SolutionDirApp", "SolutionDirApp.sln");

    /// <summary>
    ///     The file path the build baked into this assembly's metadata under <paramref name="key" />. Throws
    ///     <see cref="InvalidOperationException" /> when the key is absent or empty, or names no file on disk.
    /// </summary>
    internal static string BakedPath(string key)
    {
        string? value = typeof(FixtureBeds).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == key)
            ?.Value;

        if (string.IsNullOrEmpty(value))
            throw new InvalidOperationException($"Assembly metadata '{key}' was not baked in by the build.");

        if (!File.Exists(value))
            throw new InvalidOperationException($"Assembly metadata '{key}' names '{value}', which does not exist.");

        return value;
    }
}
