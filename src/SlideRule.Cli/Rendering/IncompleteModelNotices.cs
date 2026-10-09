using SlideRule.Roslyn.Diagnostics;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The one place the CLI writes an incomplete-model block: the refusal a gating verb writes instead of its
///     answer, and the caveat a verb that never gates writes above one.
/// </summary>
/// <remarks>
///     <para>
///         Every refusing verb takes the same three steps — ask <see cref="WorkspaceDiagnostics.Gates" />
///         whether the model is incomplete and the operator did not opt out, write the verb's refusal to
///         stderr as a line block, exit 2 — and differs only in the wording that follows. That wording stays
///         in <see cref="IncompleteModelGate" />, because the xUnit adapter and the MCP surface state the
///         same condition and have no writer to hand; what lives here is the CLI's half, the part every verb
///         would otherwise spell for itself. <see cref="Caveat" /> is <see cref="Refused" />'s never-a-gate
///         twin, as <see cref="NarrowingNotices.Stamp" /> is <see cref="NarrowingNotices.Refusal" />'s: it
///         guards itself and trails the blank line that separates it from the answer it qualifies.
///     </para>
///     <para>
///         The exit code stays with the caller, as it does for <see cref="NarrowingNotices.Refusal" />: this
///         reports whether it refused and the verb returns the 2. Where in a verb the refusal fires is a
///         per-verb decision: a verb that renders its answer first lets the verdict ride the document, and a
///         verb that writes committed files refuses before a byte is written.
///     </para>
/// </remarks>
internal static class IncompleteModelNotices
{
    /// <summary>
    ///     Writes the verb's incomplete-model refusal and reports that it fired, or writes nothing and
    ///     reports that the run may go on.
    /// </summary>
    /// <param name="error">The verb's stderr writer — a refusal never reaches stdout.</param>
    /// <param name="diagnostics">The load's own verdict, which decides whether the gate fires.</param>
    /// <param name="allowWorkspaceDiagnostics">Whether the operator opted into the partial model.</param>
    /// <param name="factory">The verb's refusal wording, from <see cref="IncompleteModelGate" />.</param>
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
    ///     Writes the verb's incomplete-model caveat above its answer, then the blank line that separates the two,
    ///     or nothing at all when the model is complete.
    /// </summary>
    /// <param name="output">The verb's stdout writer — the caveat qualifies what follows it there.</param>
    /// <param name="diagnostics">The load's own verdict, which decides whether there is anything to say.</param>
    /// <param name="factory">The verb's caveat wording, from <see cref="IncompleteModelGate" />.</param>
    internal static void Caveat(
        TextWriter output, WorkspaceDiagnostics diagnostics, Func<WorkspaceDiagnostics, string> factory)
    {
        if (!diagnostics.IsIncomplete) return;

        string caveat = factory(diagnostics);
        LineBlocks.WriteStamp(output, caveat);
    }
}
