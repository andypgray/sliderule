namespace SlideRule.Tests.TestSupport;

/// <summary>
///     The example solutions on disk, found by walking <c>examples/</c> rather than listed, so a new example
///     arrives on every test that iterates them rather than beside it.
/// </summary>
internal static class ExampleSolutions
{
    /// <summary>The directory the example solutions sit under, relative to the repository root.</summary>
    private const string ExamplesDirectory = "examples";

    /// <summary>The directory beneath an example that holds its spec project and its baselines.</summary>
    internal const string ArchDirectory = "arch";

    /// <summary>What a spec project's directory name ends with, which is how an example's spec is found.</summary>
    internal const string SpecProjectSuffix = "Architecture";

    /// <summary>
    ///     Every example solution on disk: one entry per <c>examples/&lt;name&gt;/&lt;name&gt;.slnx</c>, ordinally
    ///     ordered so the entries come in the order a reader lists the directory in.
    /// </summary>
    internal static IReadOnlyList<ExampleSolution> Discovered()
    {
        string root = RepoRoot.Absolute(ExamplesDirectory);
        List<ExampleSolution> found = new();

        IEnumerable<string> directories = Directory.EnumerateDirectories(root)
            .OrderBy(path => path, StringComparer.Ordinal);

        foreach (string directory in directories)
        {
            IEnumerable<string> solutions = Directory.EnumerateFiles(directory, "*.slnx")
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (string solution in solutions)
                found.Add(new ExampleSolution(
                    Path.GetFileNameWithoutExtension(solution), directory, solution,
                    SpecProjectDirectoryUnder(directory)));
        }

        return found;
    }

    /// <summary>The example solution named <paramref name="name" />.</summary>
    internal static ExampleSolution Named(string name)
    {
        return Discovered()
                   .FirstOrDefault(example => string.Equals(example.Name, name, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"No example solution named '{name}' is on disk.");
    }

    // The one directory under an example's arch tree whose name ends with the spec suffix, or null where an
    // example keeps its spec somewhere else, which the model-file inventory reports rather than skips over.
    private static string? SpecProjectDirectoryUnder(string exampleDirectory)
    {
        string arch = Path.Combine(exampleDirectory, ArchDirectory);
        if (!Directory.Exists(arch)) return null;

        return Directory.EnumerateDirectories(arch)
            .Where(path => Path.GetFileName(path)
                .EndsWith(SpecProjectSuffix, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

/// <summary>One example solution on disk.</summary>
/// <param name="Name">The solution file's name without its extension, which is how a row names it.</param>
/// <param name="SolutionDirectory">The solution's own directory, which is where its model file sits.</param>
/// <param name="SolutionPath">The absolute path to the solution file.</param>
/// <param name="SpecProjectDirectory">The spec project's directory, or null where none was found.</param>
internal sealed record ExampleSolution(
    string Name,
    string SolutionDirectory,
    string SolutionPath,
    string? SpecProjectDirectory);
