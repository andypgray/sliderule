namespace SlideRule.Checking;

/// <summary>
///     Which of the two ways a run came to see less than the solution
///     (<see cref="NarrowedUniverse" />): a solution filter chose the projects, or the run was handed one
///     project's compilation.
/// </summary>
internal enum NarrowingKind
{
    /// <summary>A solution filter answered the run over part of the solution.</summary>
    SolutionFilter,

    /// <summary>One project's compilation is all the run was given.</summary>
    OneProject
}
