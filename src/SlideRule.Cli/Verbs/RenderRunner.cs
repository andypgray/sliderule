using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Pipeline;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.ModelFile;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Files;

namespace SlideRule.Cli.Verbs;

/// <summary>
///     The <c>render</c> pipeline: load the model → extract the codebase, but only when the model has
///     scopes or layers carrying anchored rules → hand both to
///     <see cref="ContextFileComposer" />, which composes every target file's managed block (the root
///     block in the solution directory, each layer's local-rules card and each scope's card in its own
///     directory, merged where they coincide). The composer is shared with the card-drift gate, so this
///     command and the test that proves the committed cards current cannot disagree about what a render
///     produces. Every target is spliced through
///     the byte-level <see cref="ManagedBlockFile" /> adapter and reported as <c>wrote</c>/<c>unchanged</c>
///     with a solution-relative path. <c>--diagram &lt;path&gt;</c> adds a second, independent target: the
///     codebase graph and the law as two fences, composed by <see cref="DiagramComposer" />, on the same
///     wrote/unchanged stream. <c>--model</c> adds a third, and the one whose whole file this command owns:
///     <see cref="ModelDocument" /> as JSON beside the root block, written through
///     <see cref="WholeFile" /> and reported on the same stream, with the rule split beneath a write.
///     <c>--check</c> composes every target the same way and writes none, reporting each as <c>current</c> or
///     <c>stale</c> against the same comparison a write makes before it writes.
/// </summary>
/// <remarks>
///     <para>
///         <b>What it refuses.</b> Render fails closed on an incomplete model like every verb that consumes
///         it — exit 2 after the diagnostics and before the first byte hits disk, opt-out
///         <c>--allow-workspace-diagnostics</c> — because its output is committed context: a card whose
///         project failed to load cannot be placed and would be dropped from the committed files, and
///         <c>--diagram</c> would draw the survey <c>graph</c> refuses. A solution filter that left declared
///         projects unchecked refuses in the same position and on the same reasoning
///         (<see cref="NarrowedUniverseNotice.RenderRefusal" />), with no opt-out: the fix is to run against
///         the solution the filter references. On a complete model it exits 0 on success; expected
///         failures surface as <see cref="UserErrorException" /> (exit 2). It exits 1 only under <c>--check</c>, when a
///         target is stale.
///     </para>
///     <para>
///         <b>The model file is never written from a partial model</b>, even where the flag opted the rest
///         of the render into one. Its projects are the projects the spec governs, so one written while a
///         project failed to load would quietly leave out exactly the project that failed, and a reader
///         with no spec assembly has nothing to notice that against. An explicit <c>--model</c> therefore
///         refuses the whole command, and an existing file is left alone with a warning. A failed restore
///         is not this cause: such a project is still in the codebase under its own name.
///     </para>
///     <para>
///         <b>It fronts the persisted extraction cache</b> (<c>--no-cache</c> opts out), which is what the
///         refusals above make safe to say: render reads <em>presence</em> — a card it can place — and its
///         output is idempotent, additive and reviewed as a diff, so a stale card is corrected by the next
///         run and never removes anything. The one conclusion it draws from absence, "this project's card
///         cannot be placed", it refuses on rather than writes, and it refuses on a diagnostic a cache hit
///         replays.
///     </para>
/// </remarks>
internal sealed class RenderRunner(
    TextWriter output,
    TextWriter error,
    ISolutionSource? source = null,
    IEnvironment? environment = null)
    : CacheWiredRunner(source, environment)
{
    public async Task<int> RunAsync(RenderRequest request, CancellationToken ct)
    {
        ValidateDiagramOptions(request);

        using var source = await CodebaseSource.CreateWithSpecAsync(
            SolutionSource, Environment, request.Solution, request.Spec, request.WorkingDirectory,
            request.NoCache, ct);

        // Composed like every other verb's, so the MSBuild-selection note accompanies the load failures.
        WorkspaceDiagnostics diagnostics = source.Diagnostics;
        WorkspaceDiagnosticsRenderer.Render(error, diagnostics);

        // Fail closed before the first byte hits disk: the files render writes are committed context, and a
        // partial model composes them wrong — a card whose project failed to load resolves no directory and
        // is dropped, and --diagram draws the very survey graph refuses to print.
        if (IncompleteModelNotices.Refused(
                error, diagnostics, request.AllowWorkspaceDiagnostics, IncompleteModelGate.RenderMessage))
            return ExitCodes.Error;

        // And the narrowing refusal in the same position, for the same reason one step over: a card from a
        // project the filter left unchecked places nowhere and would be dropped from files that outlive the
        // run, and --diagram would draw a survey missing whole projects.
        if (diagnostics.IsNarrowed)
        {
            NarrowingNotices.Refusal(error, source, NarrowedUniverseNotice.RenderRefusal);
            return ExitCodes.Error;
        }

        string specName = Path.GetFileNameWithoutExtension(source.Resolution.DllPath);
        string solutionDirectory = source.SolutionDirectory;

        // The flag is the opt-in once; the file's presence is the standing request after it. So a team turns
        // the model file on by asking for it, and then renders — nobody has to remember a flag to keep
        // a committed file current, and nobody gets one they never asked for.
        string modelPath = Path.Combine(solutionDirectory, ModelDocument.FileName);
        bool wantsModel = request.Model || File.Exists(modelPath);

        // Reachable only where --allow-workspace-diagnostics let the run past the gate above, and only on a
        // failed LOAD: a project whose restore failed is still in the codebase under its own name, so the
        // project list the file records is whole either way. Settled here, before any target is written,
        // because an explicit --model means the whole command is refused rather than partly honoured.
        if (wantsModel && diagnostics.FailedProjects.Count > 0)
        {
            if (request.Model)
            {
                LineBlocks.Write(error, IncompleteModelGate.RenderModelRefusal(diagnostics));
                return ExitCodes.Error;
            }

            await error.WriteLineAsync(IncompleteModelGate.RenderModelWarning(diagnostics));
            wantsModel = false;
        }

        // Extraction only earns its cost when there is something scoped to place — a scope of either
        // posture, or a layer carrying anchored rules; with nothing scoped the composer gets no codebase
        // and returns the root block alone. The model file needs it whatever the spec places, because the
        // projects it records are the projects the spec governs, and only an extraction names them.
        CodebaseModel? codebase = ContextFileComposer.HasAnythingToPlace(source.Model) || wantsModel
            ? await source.ExtractAsync(source.Resolution.ExcludeProjectNames, ct)
            : null;

        ContextComposition composition = ContextFileComposer.Compose(
            source.Model, codebase, solutionDirectory, specName);

        foreach (string warning in composition.Warnings) await error.WriteLineAsync($"warning: {warning}");

        // A root block beside an unimported CLAUDE file is one Claude Code's sessions never read. Said on every
        // run, like the warnings above, because the cure is one line that stays harmless once it is in.
        if (ClaudeInstructionFiles.Warning(solutionDirectory, Environment) is { } missed)
            await error.WriteLineAsync($"warning: {missed}");

        // Composed and read back before the first byte of any target hits disk. A file that does not read
        // back as it was written is a defect in this tool rather than in the spec, and the answer to one is
        // to render nothing at all — not to leave a fresh block and fresh cards standing beside a model file
        // nobody can trust. The codebase is in hand wherever the file is wanted: wanting it is one of the
        // two conditions the extraction above runs on.
        (ModelDocument Document, string Composed)? model = null;
        if (wantsModel)
        {
            ModelDocument document = ModelDocument.From(source.Model, specName, ModelDocumentProject.ListOf(codebase!));
            string composed = ModelDocumentComposer.Compose(document);
            if (ModelReadBackRefusal(composed) is { } refusal)
            {
                LineBlocks.Write(error, refusal);
                return ExitCodes.Error;
            }

            model = (document, composed);
        }

        // Every target once, in the order the report reads: the composition root-first, then the model file,
        // then the diagram.
        var placement = new Placement(output, solutionDirectory, request.Check);
        foreach (ContextFile file in composition.Files) placement.Block(file.Path, file.Body);

        // The model file is a whole file this tool owns rather than a managed block inside somebody else's, so
        // it goes down through the whole-file writer and reports on the same wrote/unchanged stream as every
        // other target. The split rides a write alone: an unchanged file did not change how the rules divide
        // either, and repeating the figures every run would teach the reader to skim the line that did change.
        if (model is { } wanted)
        {
            WriteOutcome? outcome = placement.Whole(modelPath, wanted.Composed);
            if (outcome == WriteOutcome.Wrote)
                foreach (string line in ModelSplitReport.Lines(wanted.Document))
                    await output.WriteLineAsync(line);
        }

        if (request.Diagram is { } diagramPath)
        {
            string diagram = await ComposeDiagramAsync(request, source, specName, ct);
            placement.Block(diagramPath, diagram);
        }

        // After the last extraction, not after the first: --diagram extracts a second time, and the
        // re-extraction set is only final once every ExtractAsync this run makes has run.
        RecordCacheOutcome(source);

        if (placement.Stale == 0) return ExitCodes.Clean;

        // A stale file is the rendered law disagreeing with the spec, which is check's exit 1 by another name:
        // something the committer has to fix, not something the tool failed at.
        await output.WriteLineAsync(
            $"render --check: {Plurals.Counted(placement.Stale, "file")} of {placement.Placed} would change; run "
            + "'sliderule render' and commit what it writes.");
        return ExitCodes.Violations;
    }

    /// <summary>
    ///     Reads <paramref name="composed" /> back and composes what was read, and reports the refusal to
    ///     print when either step disagrees with the text that went in — or null when the file is its own
    ///     fixed point, which is the only state a write is allowed from.
    /// </summary>
    /// <remarks>
    ///     A function of the text alone, so the one branch that is unreachable without a defect in the
    ///     writer or the reader can still be driven by a test.
    /// </remarks>
    internal static string? ModelReadBackRefusal(string composed)
    {
        ModelReadResult result = ModelDocumentReader.Read(composed);
        if (result.Refusal is { } refusal) return ReadBackRefusal(refusal.Describe());

        string recomposed = ModelDocumentComposer.Compose(result.Document!);
        if (string.Equals(recomposed, composed, StringComparison.Ordinal)) return null;

        return ReadBackRefusal("the file read back, and composing what was read gave different text.");
    }

    // The two ways the round trip can disagree read as one refusal with one piece of evidence: what a reader
    // does about either is the same, and the difference between them is a detail for whoever receives the
    // report.
    private static string ReadBackRefusal(string evidence)
    {
        return EvidenceBlock.Compose(
            $"error: {ModelDocument.FileName} was not written, and nothing else was rendered: the composed "
            + "file did not read back as it was written, which is a defect in sliderule and not in the "
            + "spec:",
            [evidence],
            "Please report it with the spec that produced it.");
    }

    // The scope options are meaningless without a target file, and silently ignoring them would let a
    // mistyped --diagram render the whole solution. Validated before the workspace cost, as baseline validates
    // its add companions.
    private static void ValidateDiagramOptions(RenderRequest request)
    {
        if (request.Diagram is null && (request.DiagramOnly is not null || request.DiagramExclude is not null))
            throw new UserErrorException("--diagram-only and --diagram-exclude apply only with --diagram <path>.");
    }

    // The diagram target. It asks for its own model, with no project exclusions, which is the same call
    // `graph` makes: the scoped-card extraction above passes the spec resolution's excluded projects (the
    // spec project plus the private plumbing only it references), and reusing that model would draw a
    // different universe than the survey does. The shared invariant is one summarizer and one summary
    // shape. Where the two differ is the view: the survey fence draws only declared solution members,
    // while `graph` reports every project it loaded and labels which is which — so a project missing from
    // the drawing is a question `graph` answers rather than the two disagreeing. The two exclusion sets cost
    // one walk between them, not two: CodebaseSource walks once per source and memoizes the merge per
    // exclusion, so what this second call pays is a merge over fragments already in hand. The law fence
    // beside it costs nothing extra — it is pure over the model already in hand.
    private static async Task<string> ComposeDiagramAsync(
        RenderRequest request, CodebaseSource source, string specName, CancellationToken ct)
    {
        CodebaseModel codebase = await source.ExtractAsync([], ct);
        GraphSummary summary = GraphSummarizer.Summarize(codebase);
        return DiagramComposer.Compose(
            summary, source.SolutionName, source.Model, specName, DiagramScopeFrom(request));
    }

    private static DiagramScope DiagramScopeFrom(RenderRequest request)
    {
        return new DiagramScope(GlobList.Parse(request.DiagramOnly), GlobList.Parse(request.DiagramExclude));
    }

    // Where each target goes: written and reported wrote/unchanged, or under --check compared with the file on
    // disk and reported current/stale, counting what would change.
    private sealed class Placement(TextWriter output, string solutionDirectory, bool check)
    {
        internal int Placed { get; private set; }

        internal int Stale { get; private set; }

        internal void Block(string path, string body)
        {
            Place(path, () => ManagedBlockFile.IsCurrent(path, body), () => ManagedBlockFile.Splice(path, body));
        }

        internal WriteOutcome? Whole(string path, string composed)
        {
            return Place(path, () => WholeFile.IsCurrent(path, composed), () => WholeFile.Write(path, composed));
        }

        // The write's outcome, or null under --check, where nothing is written.
        private WriteOutcome? Place(string path, Func<bool> isCurrent, Func<WriteOutcome> write)
        {
            Placed++;
            if (check)
            {
                bool current = isCurrent();
                output.WriteLine(WriteReport.CheckLine(current, solutionDirectory, path));
                if (!current) Stale++;
                return null;
            }

            WriteOutcome outcome = write();
            output.WriteLine(WriteReport.Line(outcome, solutionDirectory, path));
            return outcome;
        }
    }
}
