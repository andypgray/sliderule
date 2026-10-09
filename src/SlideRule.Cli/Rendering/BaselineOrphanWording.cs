using SlideRule.Baselines;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The lines that name what the orphan scan found, held once so <c>status</c> and <c>baseline init</c> word an
///     orphan the same way.
/// </summary>
internal static class BaselineOrphanWording
{
    /// <summary>
    ///     The line for a section filed under an ID no rule declares, ending in the rename that would move it to
    ///     the rule it was renamed to.
    /// </summary>
    internal static string Line(BaselineOrphan orphan)
    {
        string rename = orphan.RenameCommand("<new-id>");
        return $"orphan: {orphan.Describe()}; if a rule was renamed from it, run '{rename}'";
    }

    /// <summary>The line for a baseline file the scan could not read, with the reason.</summary>
    internal static string Line(UnreadableBaseline unreadable)
    {
        return $"unreadable baseline: {unreadable.Path} — {unreadable.Reason}";
    }
}
