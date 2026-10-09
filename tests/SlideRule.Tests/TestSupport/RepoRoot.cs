namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Locates this repository's root by walking up from <see cref="AppContext.BaseDirectory" /> to the
///     directory that holds the solution file, so a test can reach this repository's solution and committed
///     tree regardless of the test host's working directory.
/// </summary>
internal static class RepoRoot
{
    private const string SolutionFileName = "SlideRule.slnx";

    /// <summary>The repository root directory.</summary>
    public static string Directory { get; } = Find();

    /// <summary>The absolute path to the solution file.</summary>
    public static string Solution => Path.Combine(Directory, SolutionFileName);

    /// <summary>The absolute path to the dogfood arch-spec csproj (the <c>--spec</c> the self-check passes).</summary>
    public static string ArchitectureSpecCsproj =>
        Path.Combine(Directory, "arch", "SlideRule.Architecture", "SlideRule.Architecture.csproj");

    /// <summary>The absolute path to the committed root <c>AGENTS.md</c>.</summary>
    public static string AgentsMd => Path.Combine(Directory, "AGENTS.md");

    /// <summary>The absolute path to the committed root <c>ARCHITECTURE.md</c> (the rendered diagram).</summary>
    public static string ArchitectureMd => Path.Combine(Directory, "ARCHITECTURE.md");

    /// <summary>
    ///     The absolute path to the committed root <c>sliderule.json</c>, this repository's own settings.
    /// </summary>
    public static string Settings => Path.Combine(Directory, "sliderule.json");

    /// <summary>The absolute native path for a forward-slash repo-relative one.</summary>
    public static string Absolute(string repoRelativePath)
    {
        return Path.Combine([Directory, .. repoRelativePath.Split('/')]);
    }

    /// <summary>The whole text of a committed file, named by its forward-slash repo-relative path.</summary>
    public static string ReadText(string repoRelativePath)
    {
        return File.ReadAllText(Absolute(repoRelativePath));
    }

    /// <summary>The lines of a committed file, named by its forward-slash repo-relative path.</summary>
    public static string[] ReadLines(string repoRelativePath)
    {
        return File.ReadAllLines(Absolute(repoRelativePath));
    }

    /// <summary>The forward-slash repo-relative path for an absolute one — how the repo spells its own files.</summary>
    public static string Relative(string absolutePath)
    {
        return Path.GetRelativePath(Directory, absolutePath)
            .Replace('\\', '/');
    }

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate {SolutionFileName} walking up from {AppContext.BaseDirectory}.");
    }
}
