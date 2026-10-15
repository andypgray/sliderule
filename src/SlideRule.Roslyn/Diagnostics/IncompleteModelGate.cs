using SlideRule.Internal;
using SlideRule.ModelFile;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     The one answer to a partially-loaded workspace, shared by every surface that consumes the model —
///     the CLI verbs, the MCP tools, and the xUnit adapter.
/// </summary>
/// <remarks>
///     <para>
///         A partially-loaded workspace is never silently treated as the model: a surface either refuses on
///         its error channel (CLI stderr and exit 2, an MCP error result, the adapter's failing
///         <c>Workspace_LoadedCompletely</c>) unless <c>--allow-workspace-diagnostics</c> or
///         <c>AllowWorkspaceDiagnostics</c> opts out, or states the incomplete model on the answer it gives
///         anyway. Whether the gate fires is decided by <see cref="WorkspaceDiagnostics.Gates" />; this class
///         holds only the wording each surface uses once it has.
///     </para>
///     <para>
///         Every message names the projects, and one with both causes puts the load block before the restore
///         block: a project that never loaded is the more broken of the two, and the repairs differ
///         (<c>dotnet build</c> versus <c>dotnet restore</c>). The four CLI verbs print the workspace
///         diagnostics immediately above their refusal, so their tails send the reader to those warnings for
///         why each project failed instead of repeating them. The restore tail does so only for "a restore that
///         ran", because the SDK replays NuGet's own logs into later builds only where a restore actually ran.
///     </para>
/// </remarks>
internal static class IncompleteModelGate
{
    /// <summary>
    ///     The per-rule skip reason when the gate fires: short and constant, because the failed projects
    ///     themselves ride <c>Workspace_LoadedCompletely</c>'s failure — one place to read them, not one copy
    ///     per rule.
    /// </summary>
    internal const string AdapterSkipReason =
        "the workspace did not load completely, so no verdict was reached; see Workspace_LoadedCompletely for "
        + "the projects that failed to load or whose NuGet packages did not resolve.";

    /// <summary>
    ///     The stderr lines <c>check</c> emits before exit 2, printed after the per-project load warnings
    ///     they refer to, and naming the opt-out flag.
    /// </summary>
    internal static string CheckMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so check cannot pass:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To check "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so check cannot pass: package references that resolved to nothing produce no edges, so a rule "
            + "about a package is measured against a model that never saw it:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To check against the partial model anyway, pass --allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>baseline</c> emits before exit 2, having written nothing. A baseline is the
    ///     team's signature on its debt: captured from a partial model it grandfathers "zero debt" for rules
    ///     that were never measured, and <c>accept-reductions</c> deletes real entries as "no longer
    ///     occurring" when the only thing that changed is that a project stopped loading — or that its
    ///     packages stopped resolving.
    /// </summary>
    internal static string BaselineMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so no baseline was "
            + "written: a baseline captured from a partial model signs off debt that was never measured:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To baseline "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so no baseline was written: every violation resting on a package edge is absent from this "
            + "model, so the baseline would sign off debt it could not see:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To baseline against the partial model anyway, pass "
            + "--allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>status</c> emits before exit 2, after rendering the burndown it does have.
    ///     Unloaded projects declare no types, and unresolved packages declare no edges, so every count in
    ///     that burndown reads low either way.
    /// </summary>
    internal static string StatusMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so status cannot report "
            + "the burndown: unloaded projects contribute no violations, so every count reads low:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To report "
            + "against the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so status cannot report the burndown: a rule about a package the model never saw contributes "
            + "no violations, so every count reads low:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To report against the partial model anyway, pass "
            + "--allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr lines <c>render</c> emits before exit 2, having written nothing. Rendered files are
    ///     committed context: a card whose project failed to load resolves no directory and is silently
    ///     dropped rather than written wrong, and <c>--diagram</c> draws the very survey <c>graph</c>
    ///     refuses to print from a partial model.
    /// </summary>
    internal static string RenderMessage(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so nothing was rendered: "
            + "a card whose project failed to load cannot be placed and would be dropped from the committed "
            + "files, and --diagram would draw a survey missing whole projects:",
            "See the warnings above for why, then restore and build the solution (dotnet build). To render "
            + "from the partial model anyway, pass --allow-workspace-diagnostics.",
            $"error: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so nothing was rendered: a card would be committed describing dependencies the model never "
            + "resolved, and --diagram would draw a survey with the external edges missing:",
            "Restore the solution (dotnet restore) — any NuGet errors behind a restore that ran are in the "
            + "warnings above. To render from the partial model anyway, pass --allow-workspace-diagnostics.");
    }

    /// <summary>
    ///     The stderr block <c>render</c> emits before exit 2 when <c>--model</c> was passed explicitly and a
    ///     project failed to load. The model file names every project the spec governs, so one written from a
    ///     partial model would silently leave out exactly the projects that failed — and a reader with no spec
    ///     assembly has nothing to compare it against.
    /// </summary>
    /// <remarks>
    ///     A load failure alone, never a restore one: a project whose restore failed is still in the codebase
    ///     with its name and its assembly names, so the project list the file records is whole.
    /// </remarks>
    internal static string RenderModelRefusal(WorkspaceDiagnostics diagnostics)
    {
        return EvidenceBlock.Compose(
            $"error: the model is incomplete — {Failed(diagnostics)} failed to load, so --model was refused "
            + "and nothing was rendered: the model file names every project the spec governs, and one "
            + "written now would leave out exactly the projects that failed:",
            diagnostics.FailedProjects,
            "See the warnings above for why, then restore and build the solution (dotnet build). "
            + "--allow-workspace-diagnostics renders the block and the cards from the partial model, but "
            + "never the model file; drop --model to do that.");
    }

    /// <summary>
    ///     The one stderr line <c>render</c> emits when a model file already exists and a project failed to
    ///     load: the block and the cards render from the partial model as they always do, and the model file
    ///     is neither compared nor written.
    /// </summary>
    /// <remarks>
    ///     One line rather than this class's usual evidence block, for the reason
    ///     <see cref="EmptySelectionHint" /> is one: nothing is being refused, so there is no remedy to hang a
    ///     project list from — the run carried on, and the projects themselves are in the warnings above it.
    /// </remarks>
    internal static string RenderModelWarning(WorkspaceDiagnostics diagnostics)
    {
        return $"warning: {ModelDocument.FileName} was not refreshed — {Failed(diagnostics)} failed to load, "
               + "and a model file written now would leave out exactly those projects. Build the solution "
               + "(dotnet build), then render again.";
    }

    /// <summary>
    ///     The <c>graph</c> refusal, thrown as a <see cref="UserErrorException" /> before extraction
    ///     rather than written to a stream — so the identical text reaches CLI stderr (exit 2, via
    ///     <c>CliErrorMapper</c>) and an MCP <c>arch_graph</c> error result (via
    ///     <c>GlobalCallToolFilter</c>).
    /// </summary>
    /// <remarks>
    ///     It carries the failed projects inline rather than pointing at warnings printed above, because on
    ///     the MCP surface there is nothing above: <c>arch_graph</c> discards its error writer, so a refusal
    ///     that said "see the warnings" would name evidence the caller cannot reach. <c>graph</c> is also the
    ///     entry point that needs no spec — a stranger's first command on an unfamiliar codebase — so the
    ///     refusal names the fix in both dialects rather than assuming which surface asked.
    /// </remarks>
    internal static string GraphRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"the model is incomplete — {Failed(diagnostics)} failed to load, so graph cannot survey the "
            + "codebase:",
            "Restore and build the solution first (dotnet build), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).",
            $"the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, so graph "
            + "cannot survey the codebase: the external references a survey exists to show are exactly what "
            + "did not resolve:",
            "Restore the solution first (dotnet restore), then retry. To survey the partial model as it "
            + "loaded, pass --allow-workspace-diagnostics (CLI) or allowWorkspaceDiagnostics: true (arch_graph).");
    }

    /// <summary>
    ///     The refusal spec resolution throws when the convention finds no spec project and the model is
    ///     incomplete — the project that would have matched may be one the run could not see, and no
    ///     <c>--spec</c> argument repairs either cause.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Like <see cref="ContextCaveat" /> it gates nothing: it is the same two causes with the same two
    ///         remedies, said where spec resolution meets them. It belongs here rather than at the call site
    ///         because spec resolution runs <em>before</em> any verb's gate can fire, so this is the message a
    ///         reader on a broken tree actually meets.
    ///     </para>
    ///     <para>
    ///         It carries the blamed projects inline rather than pointing at warnings printed above, for
    ///         <see cref="GraphRefusal" />'s reason and a stronger one of its own: this refusal is thrown
    ///         before a runner exists to render any diagnostics beside it, so on <em>both</em> its surfaces
    ///         there is nothing above to point at. The MSBuild selection note rides that block for the same
    ///         reason — a reader who cannot reach the warnings cannot read the selection off them either.
    ///     </para>
    ///     <para>
    ///         Both ledes keep the <c>No spec project found</c> prefix every arm of spec resolution's refusal
    ///         shares: the docs and the derive_spec prompt quote that prefix alone, so the rest of each lede is
    ///         free to change.
    ///     </para>
    /// </remarks>
    internal static string SpecRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"No spec project found: {Failed(diagnostics)} failed to load, so a project that references "
            + "SlideRule.dll may be among them:",
            "Restore and build the solution first (dotnet restore, dotnet build), then retry.",
            $"No spec project found: NuGet packages did not resolve for {Unrestored(diagnostics)}, so a "
            + "project that references SlideRule.dll may have failed to resolve it:",
            "Restore the solution first (dotnet restore), then retry.");
    }

    /// <summary>
    ///     The caveat block <c>context</c> writes above its answer when the model is incomplete. Context
    ///     never gates — it is a lookup an agent runs mid-edit, and a partial answer beats none — but a
    ///     partial load is exactly the state in which "no architecture scope covers this path" can be a
    ///     false all-clear: a card whose project failed to load resolves no directory and places nowhere, and
    ///     a rule about a package the restore never fetched answered without ever measuring anything.
    ///     Stdout is context's only channel (no CLI twin, no <c>--json</c>), so the caveat rides the body,
    ///     the blamed projects inline, ahead of whatever answer the partial model still supports.
    /// </summary>
    internal static string ContextCaveat(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"caveat: the model is incomplete — {Failed(diagnostics)} failed to load, so scope cards from "
            + "them cannot be placed and \"no architecture scope covers\" cannot be trusted for paths under "
            + "them:",
            "Restore and build the solution first (dotnet build), then retry for a whole answer.",
            $"caveat: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so a rule about a package cannot be trusted to have been measured for paths under them:",
            "Restore the solution first (dotnet restore), then retry for a whole answer.");
    }

    /// <summary>
    ///     The caveat block <c>explain --type</c> and <c>explain --path</c> write above their answer when the
    ///     model is incomplete: a type declared only by a project that failed to load is not in the model, and a
    ///     selection that reads a package's base types or attributes was never given them where the restore
    ///     failed. Like <see cref="ContextCaveat" /> it gates nothing and rides the body, because the body is
    ///     <c>arch_explain</c>'s only channel.
    /// </summary>
    internal static string ExplainCaveat(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"caveat: the model is incomplete — {Failed(diagnostics)} failed to load, so a type declared only "
            + "there reads as unknown and a path under them as declaring no type:",
            "Restore and build the solution first (dotnet build), then retry for a whole answer.",
            $"caveat: the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, "
            + "so a selection that reads a package's base types, interfaces or attributes cannot be trusted "
            + "for the types they declare:",
            "Restore the solution first (dotnet restore), then retry for a whole answer.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> failure body — the CLI gate transposed to a test report:
    ///     <c>check</c> gates and emits no verdict, so the adapter emits no rule verdicts. It carries the
    ///     blamed projects inline for <see cref="GraphRefusal" />'s exact reason: in a test report there is
    ///     nothing above to point at.
    /// </summary>
    internal static string AdapterRefusal(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            true,
            $"the model is incomplete — {Failed(diagnostics)} failed to load, so every rule test was skipped:",
            "Every rule would be measured over a codebase missing whole projects — a green run against a "
            + "partial model would sign verdicts that were never reached. Restore and build the solution "
            + "first (dotnet build), then retry. To check the partial model as it loaded, override "
            + "AllowWorkspaceDiagnostics to true on the test class.",
            $"the model is incomplete — NuGet packages did not resolve for {Unrestored(diagnostics)}, so every "
            + "rule test was skipped:",
            "Every rule about a package would be measured over a codebase where that package resolved to "
            + "nothing — a green run against a partial model would sign verdicts that were never reached. "
            + "Restore the solution first (dotnet restore), then retry. To check the partial model as it "
            + "loaded, override AllowWorkspaceDiagnostics to true on the test class.");
    }

    /// <summary>
    ///     The <c>Workspace_LoadedCompletely</c> skip reason under <c>AllowWorkspaceDiagnostics</c>: the rule
    ///     verdicts are live again, but a test by that name cannot pass while the load failures it is named
    ///     for are real — so it skips, and the failed projects ride the skip reason.
    /// </summary>
    internal static string AdapterOptedIn(WorkspaceDiagnostics diagnostics)
    {
        return Message(
            diagnostics,
            false,
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + $"{Failed(diagnostics)} failing to load:",
            null,
            "AllowWorkspaceDiagnostics is true: rule verdicts come from the partial model that loaded, despite "
            + $"NuGet packages not resolving for {Unrestored(diagnostics)}:",
            null);
    }

    /// <summary>
    ///     The cure a rule whose own selection matched nothing reports while the model is partial — an empty
    ///     subject or an inert target alike. It gates nothing, like <see cref="ContextCaveat" />: it is the
    ///     same two causes with the same two remedies, said at the one moment a reader is about to act on
    ///     advice that would otherwise send them after a spec defect that is not there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One sentence rather than this class's usual evidence block, and that is the constraint that
    ///         shapes it: an authoring cure is a single line on a single slot, so it cannot carry a block, a
    ///         project list, or the two-blocks-one-per-cause shape <see cref="Message" /> composes. The causes
    ///         therefore fold into one clause and the remedies into one, keeping load's repair ahead of
    ///         restore's wherever both apply — the same precedence every message here keeps.
    ///     </para>
    ///     <para>
    ///         It must read in the voice of the cures it replaces, which are pinned as one line ending in a
    ///         full stop, opening upper-case, parting diagnosis from cure with a semicolon, and never in the
    ///         second person.
    ///     </para>
    /// </remarks>
    internal static string EmptySelectionHint(WorkspaceDiagnostics diagnostics)
    {
        bool load = diagnostics.FailedProjects.Count > 0;
        bool restore = diagnostics.RestoreFailedProjects.Count > 0;

        string cause = (load, restore) switch
        {
            (true, true) => $"{Failed(diagnostics)} failed to load and NuGet packages did not resolve for "
                            + $"{Unrestored(diagnostics)}",
            (true, false) => $"{Failed(diagnostics)} failed to load",
            _ => $"NuGet packages did not resolve for {Unrestored(diagnostics)}"
        };

        // A failed load needs the build as well as the restore; an unresolved package needs only the restore.
        string repair = load
            ? "restore and build the solution (dotnet restore, dotnet build)"
            : "restore the solution (dotnet restore)";

        return $"The model is incomplete: {cause}, so this selection may name types or rest on references "
               + $"that were never extracted; {repair}, then re-check before reading this as a spec defect.";
    }

    // The count, as a noun phrase every lede can take: "1 project" / "3 projects". A gate that knows exactly
    // which projects failed has no business saying "one or more".
    private static string Failed(WorkspaceDiagnostics diagnostics)
    {
        return Plurals.Counted(diagnostics.FailedProjects.Count, "project");
    }

    private static string Unrestored(WorkspaceDiagnostics diagnostics)
    {
        return Plurals.Counted(diagnostics.RestoreFailedProjects.Count, "project");
    }

    // A surface's whole refusal: one EvidenceBlock per cause it has, load failures first. Both ledes are
    // built by the caller and the unused one is dropped, so a single-cause message carries that cause's block
    // alone, with no branch in the caller and no conditional in any literal.
    private static string Message(
        WorkspaceDiagnostics diagnostics,
        bool withSelectionNote,
        string loadLede,
        string? loadTail,
        string restoreLede,
        string? restoreTail)
    {
        var blocks = new List<string>();

        if (diagnostics.FailedProjects.Count > 0)
            blocks.Add(EvidenceBlock.Compose(loadLede, diagnostics.FailedProjects, loadTail, withSelectionNote));

        if (diagnostics.RestoreFailedProjects.Count > 0)
            blocks.Add(
                EvidenceBlock.Compose(
                    restoreLede, diagnostics.RestoreFailedProjects, restoreTail, withSelectionNote));

        return string.Join("\n", blocks);
    }
}
