using Microsoft.CodeAnalysis;
using SlideRule.Internal;
using SlideRule.Roslyn.Caching;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Replay;

/// <summary>The states <see cref="BinlogCaptureStore.Validate" /> can report.</summary>
internal enum CaptureState
{
    /// <summary>No capture exists for this solution; the run takes the cold path with no notice.</summary>
    Absent,

    /// <summary>The capture still reflects the tree's structure; replay the recorded binlog copy.</summary>
    Usable,

    /// <summary>A capture existed but no longer holds; carry the notice and fall back to a design-time build.</summary>
    Invalid
}

/// <summary>
///     The result of <see cref="BinlogCaptureStore.Validate" />. <see cref="BinlogCopyPath" /> is set only on
///     <see cref="CaptureState.Usable" /> (the copy to replay); <see cref="Notice" /> only on
///     <see cref="CaptureState.Invalid" /> — a complete user-facing line, carrying no <c>warning: </c> prefix
///     of its own.
/// </summary>
internal sealed record CaptureValidation(CaptureState State, string? BinlogCopyPath, string? Notice)
{
    internal static CaptureValidation Absent()
    {
        return new CaptureValidation(CaptureState.Absent, null, null);
    }

    internal static CaptureValidation Usable(string binlogCopyPath)
    {
        return new CaptureValidation(CaptureState.Usable, binlogCopyPath, null);
    }

    internal static CaptureValidation Invalid(string notice)
    {
        return new CaptureValidation(CaptureState.Invalid, null, notice);
    }
}

/// <summary>
///     The ingest/validate boundary over one solution's persisted build capture: a copied binlog plus a
///     structure-only-keyed manifest that lets a later run replay the build with no design-time build, as long
///     as the tree's structure has not moved. It is the sibling of the <see cref="ExtractionCacheStore" /> and
///     mirrors its disciplines.
/// </summary>
/// <remarks>
///     <para>
///         The fragment cache keys on structure and per-document content; the capture keys on structure alone,
///         because replay reads source text from current disk, so content edits are invisible to it and must
///         leave it valid. What invalidates a capture is anything that changes the meaning of the captured csc
///         command lines.
///     </para>
///     <para>
///         A binlog older than the tree's structural files, or one that does not cover exactly the solution's
///         csproj set, is a loud <see cref="UserErrorException" /> refusal at ingest: the capture contract is
///         "from a build of the current tree", and a subset binlog would silently shrink the model forever. A
///         capture invalidated later is not loud: <see cref="Validate" /> reports an
///         <see cref="CaptureState.Invalid" /> notice and the run falls back to a design-time build, because a
///         hard error would break CI on any csproj change until re-capture.
///     </para>
///     <para>
///         Persistence is best-effort: the caller already holds its replayed solution, so an I/O failure while
///         persisting must not fail the run.
///     </para>
/// </remarks>
internal sealed class BinlogCaptureStore
{
    // The manifest schema this store reads and writes. A manifest written under any other version degrades
    // to one UnreadableNotice and re-captures — acceptable for disposable derived data, never a wrong answer.
    private const int CurrentSchemaVersion = 3;

    /// <summary>The <see cref="CaptureState.Invalid" /> notice for a garbled/torn/missing-copy/schema case.</summary>
    internal const string UnreadableNotice =
        "build capture is unreadable; running a design-time build instead. Re-capture: rebuild with -bl and "
        + "re-run with --binlog.";

    /// <summary>
    ///     The <see cref="CaptureState.Invalid" /> notice when the capture is from another tool version: replay
    ///     fidelity is guaranteed only against the version that captured it.
    /// </summary>
    internal const string VersionMismatchNotice =
        "build capture was written by a different SlideRule version; running a design-time build instead. "
        + "Re-capture: rebuild with -bl and re-run with --binlog.";

    private readonly string cacheDirectory;
    private readonly string captureBinlogPath;
    private readonly string captureManifestPath;
    private readonly string solutionPath;

    /// <summary>
    ///     Creates a store for <paramref name="solutionPath" />'s capture. The files live under
    ///     <paramref name="cacheRootOverride" /> when given, else the default cache root — either way in the
    ///     per-solution subdirectory <see cref="CacheLocations" /> derives. Never reads an environment
    ///     variable itself: <c>SLIDERULE_CACHE_DIR</c> arrives here already resolved through the caller's
    ///     <c>IEnvironment</c> seam.
    /// </summary>
    public BinlogCaptureStore(string solutionPath, string? cacheRootOverride = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        this.solutionPath = solutionPath;

        // Derived once: the directory is a symlink resolve, a case fold and a SHA-256, and this store wants
        // all three of it — the manifest, the binlog copy, and the directory itself to create.
        cacheDirectory = CacheLocations.CacheDirectory(solutionPath, cacheRootOverride);
        captureManifestPath = CacheLocations.CaptureManifestPathIn(cacheDirectory);
        captureBinlogPath = CacheLocations.CaptureBinlogPathIn(cacheDirectory);
    }

    /// <summary>
    ///     The number of file-content reads (SHA-256 computations) <see cref="Validate" /> has performed: zero in
    ///     the steady state, where every structural input is trusted on stat alone. A test observable only.
    /// </summary>
    internal long ContentHashCount { get; private set; }

    // The message factories are internal rather than private so tests pin the dictated text without
    // duplicating the format.

    /// <summary>The <see cref="UserErrorException" /> text when a structural file is newer than the binlog.</summary>
    internal static string StaleAtIngestMessage(string binlogArgument, string newestStructuralFile)
    {
        return $"--binlog '{binlogArgument}' predates '{newestStructuralFile}' (the build no longer reflects "
               + "the current tree). Rebuild with -bl and pass the fresh binlog.";
    }

    /// <summary>The refusal text when the binlog is missing one or more of the solution's csproj members.</summary>
    internal static string MissingCoverageMessage(string binlogArgument, IEnumerable<string> missingCsprojs)
    {
        List<string> ordered = missingCsprojs
            .OrderBy(csproj => csproj, StringComparer.Ordinal)
            .ToList();
        return EvidenceBlock.Compose(
            $"--binlog '{binlogArgument}' does not cover the solution; missing from the binlog:",
            ordered,
            "Build the whole solution with -bl and pass that binlog.");
    }

    /// <summary>The refusal text when the capture target is a solution filter rather than a full solution.</summary>
    internal static string SolutionFilterNotSupportedMessage(string solutionFileName)
    {
        return $"'{solutionFileName}' is a solution filter (.slnf); build captures require the full solution. "
               + "Pass the .sln/.slnx instead.";
    }

    /// <summary>The refusal text when the binlog contains a project the solution does not list.</summary>
    internal static string ExtraCoverageMessage(
        string binlogArgument, string solutionFileName, IEnumerable<string> extraCsprojs)
    {
        List<string> ordered = extraCsprojs
            .OrderBy(csproj => csproj, StringComparer.Ordinal)
            .ToList();
        return EvidenceBlock.Compose(
            $"--binlog '{binlogArgument}' contains projects that are not in '{solutionFileName}':",
            ordered,
            "Pass a .binlog produced by building exactly this solution.");
    }

    /// <summary>The <see cref="CaptureState.Invalid" /> notice when a structural input no longer matches.</summary>
    internal static string StaleNotice(string offendingFile)
    {
        return $"build capture is stale ('{offendingFile}' no longer matches the capture); running a "
               + "design-time build instead. Re-capture: rebuild with -bl and re-run with --binlog.";
    }

    /// <summary>
    ///     Sanity-checks <paramref name="replayedSolution" />, just replayed from the absolute
    ///     <paramref name="binlogFullPath" />, and if it passes persists the capture, returning false when a
    ///     best-effort I/O step failed. Refusals quote <paramref name="binlogArgument" />, the user's as-typed
    ///     <c>--binlog</c> value, verbatim.
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     The solution is a solution filter, or the binlog is stale or does not cover exactly the solution.
    /// </exception>
    public bool Ingest(
        Solution replayedSolution, string binlogFullPath, string binlogArgument, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogFullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(binlogArgument);

        // A solution filter narrows a build to a subset; a capture keyed on it would silently shrink the
        // model, and a replay owns no workspace to announce the narrowing from. The cold .slnf path is
        // unaffected: this guard is only about persisting a build capture.
        if (SolutionProjectFileParser.IsFilterFormat(solutionPath))
            throw new UserErrorException(SolutionFilterNotSupportedMessage(Path.GetFileName(solutionPath)));

        IReadOnlyList<ProjectInputs> inputs = CollectProjects(replayedSolution);
        IReadOnlyList<string> structuralPaths = ProjectCone.SolutionStructuralPaths(solutionPath, inputs);
        List<CaptureProjectEntry> projects = inputs.Select(ToEntry).ToList();

        RefuseIfStale(structuralPaths, binlogFullPath, binlogArgument, ct);
        RefuseIfCoverageMismatch(projects, binlogArgument);

        return TryPersist(projects, structuralPaths, binlogFullPath, ct);
    }

    private static void RefuseIfStale(
        IReadOnlyList<string> structuralPaths, string binlogFullPath, string binlogArgument, CancellationToken ct)
    {
        DateTime binlogTime = File.GetLastWriteTimeUtc(binlogFullPath);

        string? newestOffender = null;
        DateTime newestOffenderTime = default;
        foreach (string path in structuralPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(path)) continue;

            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime <= binlogTime) continue; // equal timestamps tolerate coarse filesystem clocks

            bool newer = newestOffender is null
                         || writeTime > newestOffenderTime
                         || (writeTime == newestOffenderTime && string.CompareOrdinal(path, newestOffender) < 0);
            if (newer)
            {
                newestOffender = path;
                newestOffenderTime = writeTime;
            }
        }

        if (newestOffender is not null)
            throw new UserErrorException(StaleAtIngestMessage(binlogArgument, newestOffender));
    }

    private void RefuseIfCoverageMismatch(IReadOnlyList<CaptureProjectEntry> projects, string binlogArgument)
    {
        // Canonicalized once per side, not once per comparison: resolving a path walks its ancestors probing
        // for reparse points, and the two-way subtraction below asks about every member twice. The refusals
        // quote the original spellings, which is what the pairing keeps hold of.
        IReadOnlyList<(string Original, string Canonical)> solutionCsprojs = CanonicalPairs(SolutionProjectFileParser.ReadCsprojMembers(solutionPath));
        IReadOnlyList<(string Original, string Canonical)> replayCsprojs = CanonicalPairs(projects.Select(project => project.CsprojPath));

        var replayCanonical = new HashSet<string>(
            replayCsprojs.Select(csproj => csproj.Canonical), PathComparison.Comparer);
        List<string> missing = solutionCsprojs
            .Where(csproj => !replayCanonical.Contains(csproj.Canonical))
            .Select(csproj => csproj.Original)
            .ToList();
        if (missing.Count > 0)
            throw new UserErrorException(MissingCoverageMessage(binlogArgument, missing));

        var solutionCanonical = new HashSet<string>(
            solutionCsprojs.Select(csproj => csproj.Canonical), PathComparison.Comparer);
        List<string> extra = replayCsprojs
            .Where(csproj => !solutionCanonical.Contains(csproj.Canonical))
            .Select(csproj => csproj.Original)
            .ToList();
        if (extra.Count > 0)
            throw new UserErrorException(
                ExtraCoverageMessage(binlogArgument, Path.GetFileName(solutionPath), extra));
    }

    private bool TryPersist(
        IReadOnlyList<CaptureProjectEntry> projects, IReadOnlyList<string> structuralPaths,
        string binlogFullPath, CancellationToken ct)
    {
        List<FileStamp> structuralStamps;
        try
        {
            Directory.CreateDirectory(cacheDirectory);
            structuralStamps = structuralPaths.Select(FileStamping.StampOf).ToList();

            // Binlog copy BEFORE the manifest: a torn state is then a manifest-less orphan (validated Absent),
            // never a manifest pointing at a binlog that was never written.
            AtomicFile.Copy(binlogFullPath, captureBinlogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }

        ct.ThrowIfCancellationRequested();
        var manifest = new CaptureManifest(
            CurrentSchemaVersion, FileStamping.CurrentToolVersion, structuralStamps, projects, FileStamping.StampOf(captureBinlogPath));
        return TryWriteManifestAtomic(manifest);
    }

    /// <summary>Validates the persisted capture against disk with zero MSBuild.</summary>
    /// <remarks>
    ///     Existence flips and content changes on structural inputs invalidate; a bare mtime touch with unchanged
    ///     bytes does not (hash-verified on a stat mismatch, then the manifest is rewritten with promoted stamps so
    ///     the next run is pure-stat). Never throws (bar cancellation): an unexpected failure is the unreadable
    ///     variant, because a capture must never break a run.
    /// </remarks>
    public CaptureValidation Validate(CancellationToken ct = default)
    {
        try
        {
            return ValidateCore(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any unexpected failure (I/O mid-sweep, a malformed record STJ still bound) degrades to the
            // unreadable variant, since the capture is disposable; the filter lets a cancellation travel on.
            return CaptureValidation.Invalid(UnreadableNotice);
        }
    }

    private CaptureValidation ValidateCore(CancellationToken ct)
    {
        if (!File.Exists(captureManifestPath)) return CaptureValidation.Absent();

        CaptureManifest? manifest = TryReadManifest();
        if (manifest is null) return CaptureValidation.Invalid(UnreadableNotice);
        if (manifest.SchemaVersion != CurrentSchemaVersion) return CaptureValidation.Invalid(UnreadableNotice);
        if (!string.Equals(manifest.ToolVersion, FileStamping.CurrentToolVersion, StringComparison.Ordinal))
            return CaptureValidation.Invalid(VersionMismatchNotice);

        // The binlog we would replay must still be on disk (a torn write or a hand-deleted copy).
        if (!File.Exists(captureBinlogPath)) return CaptureValidation.Invalid(UnreadableNotice);

        // Structural sweep — an existence flip or content change is the stale variant, naming the file.
        if (FileStamping.SweepStructural(
                manifest.StructuralStamps, () => ContentHashCount++, ct, out FileStamp? changed)
            is not { } refreshedStructural)
            return CaptureValidation.Invalid(StaleNotice(changed!.Path));

        // Every recorded document (source and obj-generated) must still exist — a delete or clean is stale.
        foreach (CaptureProjectEntry project in manifest.Projects)
        foreach (string documentPath in project.DocumentPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(documentPath)) return CaptureValidation.Invalid(StaleNotice(documentPath));
        }

        // Cone scan for a *.cs added outside bin/obj that the capture did not record — a membership change.
        foreach (CaptureProjectEntry project in manifest.Projects)
        {
            ct.ThrowIfCancellationRequested();
            if (FirstConeAdd(project) is { } added) return CaptureValidation.Invalid(StaleNotice(added));
        }

        PromoteIfChanged(manifest, refreshedStructural);
        return CaptureValidation.Usable(captureBinlogPath);
    }

    // The SDK-glob add a stat sweep cannot see: a *.cs in neither ConeFiles nor DocumentPaths. ProjectCone.FirstAdd
    // stops at the first hit, because this asks whether the membership moved, not by how much.
    private static string? FirstConeAdd(CaptureProjectEntry project)
    {
        var known = new HashSet<string>(project.DocumentPaths, PathComparison.Comparer);
        known.UnionWith(project.ConeFiles);

        return ProjectCone.FirstAdd(project.ProjectDirectory, known);
    }

    private void PromoteIfChanged(CaptureManifest manifest, IReadOnlyList<FileStamp> refreshedStructural)
    {
        if (FileStamping.StampsEqual(manifest.StructuralStamps, refreshedStructural)) return; // already reflects disk

        CaptureManifest promoted = manifest with { StructuralStamps = refreshedStructural };
        TryWriteManifestAtomic(promoted); // best-effort; a later change is still caught by the next stat delta
    }

    // One entry per C# project, with the full document set, obj-generated sources included: they are csc inputs
    // the binlog's command line fixes and replay cannot regenerate, so a clean that deletes one must invalidate
    // the capture rather than let replay drift from the real build. That predicate is this store's only
    // departure from the shared collapse; everything else, the multi-target-framework collapse included, is
    // SolutionProjects' answer and the fragment cache's too.
    private static IReadOnlyList<ProjectInputs> CollectProjects(Solution solution)
    {
        return SolutionProjects.Collect(solution, static (_, _) => true);
    }

    private static CaptureProjectEntry ToEntry(ProjectInputs project)
    {
        List<string> coneFiles = ProjectCone.Enumerate(project.ProjectDirectory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        return new CaptureProjectEntry(
            project.ProjectName,
            project.CsprojPath,
            project.ProjectDirectory,
            project.DocumentPaths,
            coneFiles,
            project.EvaluatedOutputPath,
            project.IntermediateAssemblyPath);
    }

    // Absence is asked separately by ValidateCore, which, unlike the fragment cache, must tell "no capture"
    // (silent cold path) from "a capture that no longer reads" (a notice the operator has to act on).
    private CaptureManifest? TryReadManifest()
    {
        return ManifestJson.TryRead(captureManifestPath, ManifestJson.Context.CaptureManifest);
    }

    private bool TryWriteManifestAtomic(CaptureManifest manifest)
    {
        return ManifestJson.TryWriteAtomic(captureManifestPath, manifest, ManifestJson.Context.CaptureManifest);
    }

    private static IReadOnlyList<(string Original, string Canonical)> CanonicalPairs(IEnumerable<string> paths)
    {
        return paths
            .Select(path => (Original: path, Canonical: PathCanonicalizer.Resolve(path)))
            .ToList();
    }
}
