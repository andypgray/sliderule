namespace SlideRule.Checking;

/// <summary>Which way a run came to see less than the solution (<see cref="NarrowedUniverse" />).</summary>
internal enum NarrowingKind
{
    /// <summary>A solution filter answered the run over part of the solution.</summary>
    SolutionFilter,

    /// <summary>One project's compilation is all the run was given.</summary>
    OneProject
}
