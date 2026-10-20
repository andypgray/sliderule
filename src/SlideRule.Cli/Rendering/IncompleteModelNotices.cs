using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI writes an incomplete-model block: the refusal a gating verb writes instead of its
///     answer, and the caveat a verb that never gates writes above one.
/// </summary>
/// <remarks>
///     The wording is <see cref="IncompleteModelGate" />'s, which each factory comes from; what lives here is the
///     CLI's half, the part every verb would otherwise spell for itself. The exit code stays with the caller:
///     <see cref="Refused" /> reports whether it refused, and the verb returns the 2.
/// </remarks>
internal static class IncompleteModelNotices
{
    /// <summary>
    ///     Writes the verb's incomplete-model refusal to <paramref name="error" />, stderr and never stdout, and
    ///     reports that it fired, or writes nothing and reports that the run may go on.
    /// </summary>
    internal static bool Refused(
        TextWriter error,
        WorkspaceDiagnostics diagnostics,
        bool allowWorkspaceDiagnostics,
        Func<WorkspaceDiagnostics, string> factory)
    {
        if (!diagnostics.Gates(allowWorkspaceDiagnostics)) return false;

        LineBlocks.Write(error, factory(diagnostics));
        return true;
    }

    /// <summary>
    ///     Writes the verb's incomplete-model caveat to <paramref name="output" />, stdout, above its answer, then
    ///     the blank line that separates the two, or nothing at all when the model is complete.
    /// </summary>
    internal static void Caveat(
        TextWriter output, WorkspaceDiagnostics diagnostics, Func<WorkspaceDiagnostics, string> factory)
    {
        if (!diagnostics.IsIncomplete) return;

        string caveat = factory(diagnostics);
        LineBlocks.WriteStamp(output, caveat);
    }
}
