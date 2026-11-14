namespace SlideRule.Roslyn.Diagnostics;

/// <summary>Where an SDK resolution failed, which decides what the refusal says helps.</summary>
internal enum SdkRefusalTrigger
{
    /// <summary>
    ///     Roslyn's out-of-process build host, which resolves the SDK from each project's own directory while it
    ///     loads the solution.
    /// </summary>
    BuildHost,

    /// <summary>MSBuild's registration in this process, which resolves the SDK from the working directory.</summary>
    InProcess
}
