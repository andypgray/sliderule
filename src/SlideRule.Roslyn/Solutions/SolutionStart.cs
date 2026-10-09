namespace SlideRule.Roslyn.Solutions;

/// <summary>Where <see cref="SolutionDiscovery.Choose" /> says discovery starts.</summary>
/// <param name="Path">The absolute solution file to check, or the absolute directory to search upwards from.</param>
/// <param name="Kind">Which of the three it is.</param>
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
