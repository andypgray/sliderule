namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Whether an example solution has been built, read off its spec assembly on disk. Nothing in this suite
///     builds an example, so a test that needs one built skips on this rather than going red on a machine
///     that never built them; CI's examples job builds them before it runs those tests.
/// </summary>
internal static class ExampleBuilds
{
    /// <summary>
    ///     Where a built example puts its spec assembly, for the configuration this test assembly was itself
    ///     built in.
    /// </summary>
    /// <remarks>
    ///     The two are built by separate commands, so a suite built Debug beside an example built Release
    ///     reads as unbuilt, which is what a skip on <see cref="IsBuilt" /> is for.
    /// </remarks>
    /// <param name="specProjectDirectory">The example's spec project directory.</param>
    internal static string SpecOutputDirectory(string specProjectDirectory)
    {
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        string configuration = output.Parent?.Name ?? "Debug";

        return Path.Combine(specProjectDirectory, "bin", configuration);
    }

    /// <summary>
    ///     Whether <paramref name="outputDirectory" /> holds the spec assembly, whatever target framework the
    ///     spec project builds for.
    /// </summary>
    /// <param name="outputDirectory">Where to look, as <see cref="SpecOutputDirectory" /> names it.</param>
    /// <param name="specProjectDirectory">The example's spec project directory, whose name the assembly takes.</param>
    internal static bool IsBuilt(string outputDirectory, string specProjectDirectory)
    {
        var fileName = $"{Path.GetFileName(specProjectDirectory)}.dll";

        return Directory.Exists(outputDirectory)
               && Directory.EnumerateFiles(outputDirectory, fileName, SearchOption.AllDirectories)
                   .Any();
    }
}
