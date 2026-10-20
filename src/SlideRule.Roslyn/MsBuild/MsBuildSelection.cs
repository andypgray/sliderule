namespace SlideRule.Roslyn.MsBuild;

/// <summary>
///     Which MSBuild a run is using. <see cref="Source" /> is a one-line account of what was chosen and why, meant
///     to be logged or printed: it answers "which MSBuild did this run use" on a machine nobody can attach a
///     debugger to.
/// </summary>
internal sealed record MsBuildSelection(string Source);
