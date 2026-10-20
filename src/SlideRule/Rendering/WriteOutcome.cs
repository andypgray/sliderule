namespace SlideRule.Rendering;

/// <summary>Whether a file write changed the target file's bytes.</summary>
internal enum WriteOutcome
{
    Wrote,

    /// <summary>The target file's bytes were already current; no change was made.</summary>
    Unchanged
}
