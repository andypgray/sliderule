namespace SlideRule.Roslyn.Solutions;

/// <summary>Where <see cref="SolutionDiscovery.Choose" /> says discovery starts.</summary>
/// <remarks><paramref name="Path" /> is the absolute solution file to check or directory to search from.</remarks>
internal sealed record SolutionStart(string Path, SolutionStartKind Kind);

/// <summary>What a <see cref="SolutionStart" /> path is, and so what a missing file is reported as.</summary>
internal enum SolutionStartKind
{
    /// <summary>A directory to search upwards from: a directory argument, or the working directory.</summary>
    Search,

    /// <summary>A solution file the argument named.</summary>
    ArgumentFile,

    /// <summary>A solution file <c>SLIDERULE_SOLUTION_PATH</c> named.</summary>
    VariableFile
}
