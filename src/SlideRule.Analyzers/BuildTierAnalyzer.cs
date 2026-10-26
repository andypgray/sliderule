using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Extraction.Passes;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.ModelFile;
using SlideRule.Rendering;

namespace SlideRule.Analyzers;

/// <summary>
///     Checks the project being compiled against the architecture the build handed over as
///     <c>sliderule.model.json</c>, and reports a broken rule as a warning at the offending code, from
///     the document being analysed and at the end of the build.
/// </summary>
/// <remarks>
///     <para>
///         Three properties decide every question here. It may never report what a check over the whole
///         solution would not, so anything this view cannot settle — an assembly the file does not name, an
///         assembly two projects both claim, a rule whose verdict needs projects this compilation cannot
///         see — ends the run silently rather than in a guess. It may never surface as the compiler's own
///         <c>AD0001</c>, because an estate that builds with warnings as errors excludes the SR IDs by
///         name and would fail on it: every reader returns a refusal value, and the guarded regions below
///         turn anything else into a warning of its own. And it must be cheap when it has nothing to say,
///         so each step returns before the next one costs anything, no extraction happens until a rule is
///         known to be checkable here, and no document is extracted until it is known to declare a type.
///     </para>
///     <para>
///         The only static state is the two path-keyed reads, one of model files and one of baselines, which
///         keep one read per path and replace it when the text at that path changes, so a host that analyses
///         many projects in one process reads a given file once rather than once per project. The live tier's
///         per-compilation state lives in the closure the start action builds, never in a field, because the
///         compiler shares one analyzer instance across compilations. Nothing is read from disk, the
///         environment or a clock: the compilation and the additional files the build supplies are the whole
///         input.
///     </para>
///     <para>
///         The compiler binds this analyzer to whichever copy of SlideRule it loaded, which can be
///         another version's, and a member that moved between the two throws when the JIT compiles the
///         method naming it, at that method's call site in its caller. So a method that names a type or member
///         of SlideRule or SlideRule.Extraction is a boundary: marked never to be inlined,
///         which keeps its compilation its own, and called only from another boundary or from inside a try of
///         the calling method that catches everything but cancellation. No method the compiler calls is a
///         boundary, the fault path names neither assembly, and no field's type makes loading one of these
///         types bind them. <c>AnalyzerCoreFirewallTests</c> holds all of it over the built assembly.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class BuildTierAnalyzer : DiagnosticAnalyzer
{
    private const string TierProperty = "build_property.SlideRuleTier";

    private enum Tier
    {
        Off,
        Build,
        Live
    }

    // Each keeps a handle of this assembly's own rather than a Core type, so nothing in the analyzer's static
    // state names Core, and each read is a guard that holds a fault in the handle rather than throwing it, so
    // the start reports it as the compilation's one fault.
    private static readonly PathKeyedReads<LoadedModel> ModelReads = new(text => LoadedModel.Load(text, null));

    private static readonly PathKeyedReads<HandedBaseline> BaselineReads = new(text => HandedBaseline.Load(text, null));

    private readonly PathKeyedReads<LoadedModel> _modelReads;
    private readonly PathKeyedReads<HandedBaseline> _baselineReads;
    private readonly Action? _beforeStart;
    private readonly Action? _beforeModelRefusal;
    private readonly Action? _beforeBaselineRefusal;
    private readonly Action? _beforeCheck;
    private readonly Func<(string? Analyzer, string? Core)>? _versions;

    /// <summary>The constructor the compiler calls, which arms no seam.</summary>
    public BuildTierAnalyzer()
        : this(beforeCheck: null)
    {
    }

    /// <summary>
    ///     The same analyzer with a test seam armed at each point where a fault can arise, each invoked where a
    ///     fault in the real code would be thrown, so a fault thrown from it takes that fault's path; every seam
    ///     is null in production.
    /// </summary>
    /// <param name="beforeCheck">
    ///     Runs inside the guard of whichever action checks — once per compilation on the build tier, once
    ///     per document that declares a type on the live tier — and before any extraction.
    /// </param>
    /// <param name="beforeStart">
    ///     Runs first inside the boundary the compilation start calls past the version check.
    /// </param>
    /// <param name="beforeModelRead">
    ///     Runs inside the model read's guard, before the read. Arming it gives this instance reads of its own
    ///     over the same guarded read, so the reads every analyzer shares are never handed a fault and a read
    ///     another instance kept never stands in for one this seam would have seen.
    /// </param>
    /// <param name="beforeBaselineRead">The same, for a baseline file's read.</param>
    /// <param name="beforeModelRefusal">Runs inside the guard of the action that reports a refused model file.</param>
    /// <param name="beforeBaselineRefusal">
    ///     Runs inside the guard of the action that reports a refused baseline file, for every baseline file
    ///     that action is handed.
    /// </param>
    /// <param name="versions">
    ///     Supplies the two informational versions the version check compares, the analyzer's own first, in
    ///     place of reading them off the two assemblies; it runs inside the check.
    /// </param>
    internal BuildTierAnalyzer(
        Action? beforeCheck = null,
        Action? beforeStart = null,
        Action? beforeModelRead = null,
        Action? beforeBaselineRead = null,
        Action? beforeModelRefusal = null,
        Action? beforeBaselineRefusal = null,
        Func<(string? Analyzer, string? Core)>? versions = null)
    {
        _beforeCheck = beforeCheck;
        _beforeStart = beforeStart;
        _beforeModelRefusal = beforeModelRefusal;
        _beforeBaselineRefusal = beforeBaselineRefusal;
        _versions = versions;
        _modelReads = beforeModelRead is null
            ? ModelReads
            : new PathKeyedReads<LoadedModel>(text => LoadedModel.Load(text, beforeModelRead));
        _baselineReads = beforeBaselineRead is null
            ? BaselineReads
            : new PathKeyedReads<HandedBaseline>(text => HandedBaseline.Load(text, beforeBaselineRead));
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => BuildTierDiagnostics.All;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Generated code is analysed and reported on, because the whole-solution check reports a site in a
        // generated file and the two must not disagree about where a rule is broken.
        context.ConfigureGeneratedCodeAnalysis(
            GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    // This member and ReleasedVersion must agree with their copies in SpecLiteralAnalyzer and the adapter's
    // ContractVersion, since Core cannot hold code the version check runs; CopiedMemberParityTests holds them
    // identical.
    internal static string? InformationalVersionOf(Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }

    /// <summary>
    ///     <paramref name="informational" /> up to its first <c>+</c>, which drops the commit a build appends,
    ///     so one version compares equal however it was built and reads short; empty for a missing version,
    ///     which no real version equals.
    /// </summary>
    internal static string ReleasedVersion(string? informational)
    {
        if (informational is null) return string.Empty;

        int plus = informational.IndexOf('+');

        return plus < 0 ? informational : informational.Substring(0, plus);
    }

    // Only the tier is read out here, first and alone, so a project that switched the analyzer off touches
    // nothing, the version check included. Everything past it runs inside this one guard, and a fault anywhere
    // in it is the compilation's one fault: the start registers nothing before the last thing that can fail,
    // so the end action below is then the whole of what the compilation reports.
    private void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        Tier tier = TierOf(context.Options.AnalyzerConfigOptionsProvider.GlobalOptions);
        if (tier == Tier.Off) return;

        Exception? fault;
        try
        {
            if (!VersionsAgree(out string analyzerVersion, out string coreVersion))
            {
                context.RegisterCompilationEndAction(end =>
                    end.ReportDiagnostic(BuildTierDiagnostics.VersionsMismatched(analyzerVersion, coreVersion)));
                return;
            }

            fault = Start(context, tier);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            fault = exception;
        }

        if (fault is not null) context.RegisterCompilationEndAction(end => ReportFault(end, fault));
    }

    // Whether the copy of SlideRule the compiler bound this analyzer to is its own version. It names one
    // Core type, which every version carries, so compiling it fails only where Core cannot load at all; and it
    // runs before any other boundary is compiled, because compiling another is what a moved member breaks.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool VersionsAgree(out string analyzerVersion, out string coreVersion)
    {
        (string? Analyzer, string? Core) informational = _versions?.Invoke()
                                                         ?? (InformationalVersionOf(typeof(BuildTierAnalyzer).Assembly),
                                                             InformationalVersionOf(typeof(IArchitectureSpec).Assembly));
        analyzerVersion = ReleasedVersion(informational.Analyzer);
        coreVersion = ReleasedVersion(informational.Core);

        return string.Equals(analyzerVersion, coreVersion, StringComparison.Ordinal);
    }

    // The start's boundary, holding everything the compilation start does past the version check. It registers
    // only once nothing it calls can fail, so a fault leaves nothing registered behind it; a read that comes
    // back faulted is returned as the fault to report, and so the compilation checks nothing further.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Exception? Start(CompilationStartAnalysisContext context, Tier tier)
    {
        _beforeStart?.Invoke();

        AdditionalText? modelFile = ModelFileAmong(context.Options.AdditionalFiles);
        SourceText? modelText = modelFile?.GetText(context.CancellationToken);
        if (modelFile is null || modelText is null)
        {
            context.RegisterCompilationEndAction(end => end.ReportDiagnostic(BuildTierDiagnostics.MissingModelFile()));
            return null;
        }

        LoadedModel loaded = _modelReads.Read(modelFile.Path, modelText);
        if (loaded.Fault is { } unreadModel) return unreadModel;

        if (loaded.Result.Refusal is not null)
        {
            var refusalFaults = new FaultSlot();
            context.RegisterAdditionalFileAction(file => OnModelFile(file, modelFile, modelText, loaded, refusalFaults));
            context.RegisterCompilationEndAction(end => ReportHeldFault(end, refusalFaults));
            return null;
        }

        ModelDocument document = loaded.Result.Document!;
        if (OwnProjectName(document, context.Compilation) is not { } ownProjectName) return null;
        if (loaded.InRules.Count == 0) return null;

        string modelDirectory = BaselinePathResolver.DirectoryOf(modelFile.Path);
        Dictionary<string, AdditionalText> baselineFiles =
            BaselineFilesFor(loaded.InRules, modelDirectory, context.Options.AdditionalFiles);

        // Every baseline file the In rules name, read once here and shared by the action that reports a refusal
        // and the check that consumes the document, because reading in both would read the same file twice per
        // compilation. This compilation's own text rides beside each read, for placing a refusal.
        var handed = new Dictionary<AdditionalText, (HandedBaseline Baseline, SourceText Text)>();
        var handedByPath = new Dictionary<string, HandedBaseline>(PathComparison.Comparer);
        foreach (KeyValuePair<string, AdditionalText> baselineFile in baselineFiles)
        {
            AdditionalText file = baselineFile.Value;
            SourceText? baselineText = file.GetText(context.CancellationToken);
            if (baselineText is null) continue;

            HandedBaseline baseline = _baselineReads.Read(file.Path, baselineText);

            // The compilation's one fault rather than a rule reported unchecked: an SR2092 would blame a
            // baseline the build never handed over, when it was handed over and could not be read.
            if (baseline.Fault is { } unreadBaseline) return unreadBaseline;

            handed.Add(file, (baseline, baselineText));
            handedByPath.Add(baselineFile.Key, baseline);
        }

        var faults = new FaultSlot();
        if (handed.Count > 0) context.RegisterAdditionalFileAction(file => OnBaselineFile(file, handed, faults));

        if (tier == Tier.Build)
        {
            context.RegisterCompilationEndAction(end =>
                RunGuarded(end, loaded, ownProjectName, modelDirectory, handedByPath, faults));
            return null;
        }

        RegisterLive(context, loaded, ownProjectName, modelDirectory, handedByPath, faults);
        return null;
    }

    // The live tier answers per document, so everything a document needs is settled here, once, while one
    // thread is running: repeating the rule plan per document would both pay for it again and let two
    // documents disagree about which rules are in play. What comes out is read-only for the rest of the
    // compilation, bar the withheld set the document actions write and the fault slot they hold into.
    private void RegisterLive(
        CompilationStartAnalysisContext context, LoadedModel loaded, string ownProjectName, string modelDirectory,
        IReadOnlyDictionary<string, HandedBaseline> handedByPath, FaultSlot faults)
    {
        CheckState? state = null;
        try
        {
            state = Planned(context.Compilation, loaded, ownProjectName, modelDirectory, handedByPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing was registered for the plan, so the fault has no document action to be held by: it is
            // the end action's to name.
            faults.Hold(exception);
        }

        if (state is { } planned) context.RegisterSemanticModelAction(model => AnalyzeDocument(model, planned, faults));

        // Registered however the plan went, because the end action is where the compilation's one fault is
        // named, whichever action held it. No rule to check and nothing held is the cheap case, and it reports
        // exactly what the build tier's own early return reports: nothing at all.
        context.RegisterCompilationEndAction(end => EndLive(end, state, faults));
    }

    // Null where no rule is checkable here, the one case in which neither tier has anything to run at all.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CheckState? Planned(
        Compilation compilation, LoadedModel loaded, string ownProjectName, string modelDirectory,
        IReadOnlyDictionary<string, HandedBaseline> handedByPath)
    {
        RulePlan plan = RulePlan.For(loaded, modelDirectory, handedByPath);
        if (plan.ToCheck.Count == 0) return null;

        ModelDocument document = loaded.Result.Document!;
        var siblings = new SiblingProjects(ProjectTable(document), ownProjectName);
        var declaringTrees = new Lazy<DeclaringTrees>(() => DeclaringTrees.Of(compilation));

        return new CheckState(
            ownProjectName, modelDirectory, plan, siblings, TreesByPath(compilation), declaringTrees);
    }

    // One document's whole share of the work, from deciding whether it has any to reporting its findings.
    // The guard is the document's own: a fault here loses this document's findings and is named once at the
    // end, rather than stopping the documents that have not run yet.
    private void AnalyzeDocument(SemanticModelAnalysisContext context, CheckState state, FaultSlot faults)
    {
        try
        {
            SyntaxTree tree = context.SemanticModel.SyntaxTree;

            // A compilation built by hand can hold two trees under one path, and the findings of both would
            // be reported at one path twice over. The first declarer of a path is the document that answers
            // for it, which is the same tree the build tier's own lookup would have chosen.
            if (!state.FirstDeclarers.TryGetValue(tree.FilePath, out SyntaxTree? declarer)) return;
            if (!ReferenceEquals(declarer, tree)) return;

            // A document declaring no type has nothing any rule here could be broken by, and this return
            // sits above the seam: never reaching the seam is what says no extraction happened either.
            if (!DeclaresAType(state, tree)) return;

            _beforeCheck?.Invoke();
            ReportDocument(context, state, tree);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Hold(exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool DeclaresAType(CheckState state, SyntaxTree tree)
    {
        return state.DeclaringTrees.Value.In(tree).Count > 0;
    }

    // One document extracted, merged and checked on its own. The types this document declares are carried
    // whole — every part of a partial type, wherever the other parts sit — so the check answers here what it
    // would answer over the project, and the findings are then narrowed to the sites this document holds.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportDocument(SemanticModelAnalysisContext context, CheckState state, SyntaxTree tree)
    {
        SemanticModel semanticModel = context.SemanticModel;
        var input = new CompilationInput(semanticModel.Compilation, state.OwnProject, Array.Empty<string>());
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractDocument(
            input, tree, semanticModel, state.DeclaringTrees.Value, state.Siblings, context.CancellationToken);

        IEnumerable<BuildFinding> findings = state.Reportable(
            fragments, path => string.Equals(path, tree.FilePath, StringComparison.Ordinal), context.CancellationToken);

        foreach (BuildFinding finding in findings)
        {
            // Built on this document's own tree rather than on one looked up by path, which is the only
            // tree a semantic-model action may place a diagnostic in.
            Location site = SiteLocation(tree, finding, context.CancellationToken);
            context.ReportDiagnostic(BuildTierDiagnostics.Finding(finding, site));
        }
    }

    // The live tier's end action, and the whole of it: the findings were reported by the documents that found
    // them, and a diagnostic with no location cannot be reported from a document action at all — the driver
    // classifies it as non-local and no per-document result carries it. So the withheld rules are named
    // here, under a guard of their own, and then the compilation's one fault.
    private static void EndLive(CompilationAnalysisContext context, CheckState? state, FaultSlot faults)
    {
        if (state is not null)
            try
            {
                state.ReportWithheld(context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                faults.Hold(exception);
            }

        ReportHeldFault(context, faults);
    }

    // The one place a fault becomes a warning, on either tier; cancellation is not a fault and passes through
    // every guard.
    private static void ReportFault(CompilationAnalysisContext context, Exception fault)
    {
        context.ReportDiagnostic(BuildTierDiagnostics.AnalyzerFailed(fault));
    }

    private static void ReportHeldFault(CompilationAnalysisContext context, FaultSlot faults)
    {
        if (faults.Fault is { } fault) ReportFault(context, fault);
    }

    // The build tier's guarded region, which is the whole compilation's: one check, one place to fault, and
    // the compilation's one fault named once it has run.
    private void RunGuarded(
        CompilationAnalysisContext context, LoadedModel loaded, string ownProjectName, string modelDirectory,
        IReadOnlyDictionary<string, HandedBaseline> handedByPath, FaultSlot faults)
    {
        try
        {
            _beforeCheck?.Invoke();
            Run(context, loaded, ownProjectName, modelDirectory, handedByPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Hold(exception);
        }

        ReportHeldFault(context, faults);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(
        CompilationAnalysisContext context, LoadedModel loaded, string ownProjectName, string modelDirectory,
        IReadOnlyDictionary<string, HandedBaseline> handedByPath)
    {
        CheckState? state = Planned(context.Compilation, loaded, ownProjectName, modelDirectory, handedByPath);
        if (state is null) return;

        var input = new CompilationInput(context.Compilation, state.OwnProject, Array.Empty<string>());
        IReadOnlyList<CodebaseFragment> fragments =
            FragmentExtractor.ExtractOneProject(input, state.Siblings, context.CancellationToken);

        IEnumerable<BuildFinding> findings =
            state.Reportable(fragments, state.FirstDeclarers.ContainsKey, context.CancellationToken);

        foreach (BuildFinding finding in findings)
        {
            Location site = SiteLocation(state.FirstDeclarers[finding.FilePath], finding, context.CancellationToken);
            context.ReportDiagnostic(BuildTierDiagnostics.Finding(finding, site));
        }

        state.ReportWithheld(context);
    }

    // Every rule is checked except a ratcheted one whose baseline file would not read, which is dropped
    // entirely because its own refusal has already said why. A ratcheted rule with no captured baseline is
    // still checked, so that its findings can be counted and then withheld.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<ArchRule> RulesToCheck(
        IReadOnlyList<ArchRule> inRules, BaselineIndex baselines, Func<string, HandedBaseline?> handedFor,
        ICollection<string> uncaptured)
    {
        var toCheck = new List<ArchRule>(inRules.Count);
        foreach (ArchRule rule in inRules)
        {
            if (rule.BaselinePath is { } declared)
            {
                if (handedFor(declared)?.Result.Refusal is not null) continue;

                if (!baselines.TryGet(rule.Id, out _)) uncaptured.Add(rule.Id);
            }

            toCheck.Add(rule);
        }

        return toCheck;
    }

    // The model-refusal action's guard. A fault here is held for the end action, which names it once for the
    // compilation, because the fault warning is one an end action reports.
    private void OnModelFile(
        AdditionalFileAnalysisContext context, AdditionalText modelFile, SourceText modelText, LoadedModel loaded,
        FaultSlot faults)
    {
        if (!ReferenceEquals(context.AdditionalFile, modelFile)) return;

        try
        {
            _beforeModelRefusal?.Invoke();
            ReportModelRefusal(context, modelFile, modelText, loaded);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Hold(exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportModelRefusal(
        AdditionalFileAnalysisContext context, AdditionalText modelFile, SourceText modelText, LoadedModel loaded)
    {
        ModelRefusal refusal = loaded.Result.Refusal!;
        context.ReportDiagnostic(BuildTierDiagnostics.ModelRefused(refusal, modelFile.Path, modelText));
    }

    // The baseline-refusal action's guard, held the same way.
    private void OnBaselineFile(
        AdditionalFileAnalysisContext context,
        IReadOnlyDictionary<AdditionalText, (HandedBaseline Baseline, SourceText Text)> handed, FaultSlot faults)
    {
        if (!handed.TryGetValue(context.AdditionalFile, out (HandedBaseline Baseline, SourceText Text) read)) return;

        try
        {
            _beforeBaselineRefusal?.Invoke();
            ReportBaselineRefusal(context, read.Baseline, read.Text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            faults.Hold(exception);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReportBaselineRefusal(
        AdditionalFileAnalysisContext context, HandedBaseline baseline, SourceText text)
    {
        if (baseline.Result.Refusal is not { } refusal) return;

        context.ReportDiagnostic(BuildTierDiagnostics.BaselineRefused(refusal, context.AdditionalFile.Path, text));
    }

    // Absent, or any value but 'off' and 'build', is 'live': the switch fails towards checking, and an
    // MSBuild property arrives as it was written, which can be padded or capitalised.
    private static Tier TierOf(AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue(TierProperty, out string? declared)) return Tier.Live;

        string value = declared.Trim();
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)) return Tier.Off;
        if (string.Equals(value, "build", StringComparison.OrdinalIgnoreCase)) return Tier.Build;

        return Tier.Live;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AdditionalText? ModelFileAmong(ImmutableArray<AdditionalText> additionalFiles)
    {
        foreach (AdditionalText file in additionalFiles)
            if (string.Equals(PathFormat.Leaf(file.Path), ModelDocument.FileName, PathComparison.Comparison))
                return file;

        return null;
    }

    // The file lists assemblies, not compilations, so the project is found by the assembly this compilation
    // produces — and the answer is the project's own name, which a project selection reads and which is not
    // the assembly name. Two projects claiming one assembly name leaves this view unable to say whose code
    // it is reading, so it says nothing.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static string? OwnProjectName(ModelDocument document, Compilation compilation)
    {
        var producers = new AssemblyProducers();
        foreach (ModelDocumentProject project in document.Projects)
        foreach (string assembly in project.Assemblies)
            producers.Record(assembly, project.Name);

        return producers.TryGetProducer(compilation.Assembly.Name, out string? owner) ? owner : null;
    }

    // The own project rides in the table it is the own project of: the carrier answers it as absent and
    // still counts it towards ambiguity, which is what makes an assembly name shared with a sibling
    // undecidable rather than silently attributed here.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static IReadOnlyList<(string ProjectName, IReadOnlyList<string> AssemblyNames)> ProjectTable(
        ModelDocument document)
    {
        var table = new List<(string ProjectName, IReadOnlyList<string> AssemblyNames)>(document.Projects.Count);
        foreach (ModelDocumentProject project in document.Projects) table.Add((project.Name, project.Assemblies));

        return table;
    }

    /// <summary>
    ///     <paramref name="fragments" /> merged and <paramref name="rules" /> checked over them, narrowed to
    ///     what one project's view can decide.
    /// </summary>
    /// <remarks>
    ///     Path globs read against <paramref name="modelDirectory" />: the model file is rendered beside the
    ///     solution, so its directory is the solution directory the check's own hosts read them against. The
    ///     root is built in here rather than carried in, so no Core type enters the state that outlives this
    ///     boundary.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static CheckReport CheckOneProject(
        IReadOnlyList<CodebaseFragment> fragments, IReadOnlyList<ArchRule> rules, BaselineIndex baselines,
        string modelDirectory, CancellationToken cancellationToken)
    {
        CodebaseModel codebase = FragmentMerger.Merge(fragments);
        var root = new SolutionRoot(modelDirectory);

        return ArchChecker.Check(
            rules, codebase, baselines, null, NarrowedUniverse.OneProject, null, root, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<string, AdditionalText> BaselineFilesFor(
        IReadOnlyList<ArchRule> inRules, string modelDirectory, ImmutableArray<AdditionalText> additionalFiles)
    {
        var found = new Dictionary<string, AdditionalText>(PathComparison.Comparer);
        Dictionary<string, AdditionalText>? filesByPath = null;
        foreach (ArchRule rule in inRules)
        {
            if (rule.BaselinePath is not { } declared) continue;

            string resolved = BaselinePathResolver.Resolve(declared, modelDirectory);
            if (found.ContainsKey(resolved)) continue;

            filesByPath ??= FilesByPath(additionalFiles);
            if (!filesByPath.TryGetValue(resolved, out AdditionalText? file)) continue;

            found.Add(resolved, file);
        }

        return found;
    }

    // Both sides go through the same normalizer, so a spec's forward slashes and a build's own separators
    // name the same file. First file wins where two normalize alike, as in a walk in the order handed over.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Dictionary<string, AdditionalText> FilesByPath(ImmutableArray<AdditionalText> additionalFiles)
    {
        var files = new Dictionary<string, AdditionalText>(PathComparison.Comparer);
        foreach (AdditionalText file in additionalFiles)
        {
            string path = BaselinePathResolver.Normalize(file.Path);
            if (!files.ContainsKey(path)) files.Add(path, file);
        }

        return files;
    }

    // First declarer wins, and the key is the tree's path exactly as it carries it: a compilation built by
    // hand can hold two trees under one path and any number under none, which a ToDictionary would throw on.
    private static Dictionary<string, SyntaxTree> TreesByPath(Compilation compilation)
    {
        var trees = new Dictionary<string, SyntaxTree>(StringComparer.Ordinal);
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
            if (!trees.ContainsKey(tree.FilePath))
                trees.Add(tree.FilePath, tree);

        return trees;
    }

    // A site's span is the reference the extractor recorded, so the squiggle covers that and nothing else. A
    // site with no span (one built by hand, or one read back from the cache, which never stores a span) falls
    // back to the line from its first non-blank character to its end, and so does a span the text cannot hold
    // or one that starts on a line other than the site's, because the site's line is the identity every report
    // agrees on and a squiggle must not contradict it. Either way a pragma on the site's line suppresses the
    // warning. A line outside the text cannot happen, the site having come from this compilation, and falls
    // back to the first line rather than throwing inside the guard for a reason no reader could act on.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Location SiteLocation(SyntaxTree tree, BuildFinding finding, CancellationToken cancellationToken)
    {
        SourceText text = tree.GetText(cancellationToken);
        TextLineCollection lines = text.Lines;

        if (finding.Span is { } span && CoversTheSitesLine(text, lines, span, finding.Line))
            return Location.Create(tree, new TextSpan(span.Start, span.Length));

        int index = finding.Line - 1;
        if (index < 0 || index >= lines.Count) index = 0;

        TextLine found = lines[index];
        int start = found.Start;
        while (start < found.End && char.IsWhiteSpace(text[start])) start++;

        return Location.Create(tree, TextSpan.FromBounds(start, found.End));
    }

    // The two things a recorded span has to survive to be used: the text it was recorded against is the text
    // being analysed here, and its start really is on the line the finding names.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CoversTheSitesLine(SourceText text, TextLineCollection lines, SourceSpan span, int line)
    {
        if (span.Start < 0 || span.Length <= 0) return false;
        if (span.Start + span.Length > text.Length) return false;

        return lines.GetLinePosition(span.Start).Line + 1 == line;
    }

    // What both tiers settle before they check anything, computed in one place so neither can drift into
    // checking a rule the other drops or printing debt the other withholds.
    private sealed class RulePlan
    {
        private readonly ISet<string> _uncaptured;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private RulePlan(BaselineIndex baselines, IReadOnlyList<ArchRule> toCheck, ISet<string> uncaptured)
        {
            Baselines = baselines;
            ToCheck = toCheck;
            _uncaptured = uncaptured;
        }

        /// <summary>The captured baselines the checked rules are ratcheted against.</summary>
        internal BaselineIndex Baselines
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get;
        }

        /// <summary>The rules to check here, in model order.</summary>
        internal IReadOnlyList<ArchRule> ToCheck
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static RulePlan For(
            LoadedModel loaded, string modelDirectory, IReadOnlyDictionary<string, HandedBaseline> handedByPath)
        {
            HandedBaseline? HandedFor(string declared)
            {
                string resolved = BaselinePathResolver.Resolve(declared, modelDirectory);

                return handedByPath.TryGetValue(resolved, out HandedBaseline? handed) ? handed : null;
            }

            // Both delegates are consumed before this returns, by the index builder and the rule filter, so
            // what they name is compiled inside this boundary like everything else it calls.
            BaselineIndex baselines = BaselineIndexBuilder.ForRules(
                loaded.InRules, path => HandedFor(path)?.Result.Document);

            var uncaptured = new HashSet<string>(StringComparer.Ordinal);
            List<ArchRule> toCheck = RulesToCheck(loaded.InRules, baselines, HandedFor, uncaptured);

            return new RulePlan(baselines, toCheck, uncaptured);
        }

        /// <summary>
        ///     Whether a finding of <paramref name="ruleId" /> is to be counted rather than printed.
        /// </summary>
        /// <remarks>
        ///     An uncaptured rule was evaluated against an empty baseline only to answer whether it would
        ///     report here at all. Printing its findings would print an estate's whole existing debt on the
        ///     first build that ran this analyzer, so the rule is named instead and the finding is dropped.
        /// </remarks>
        internal bool Withholds(string ruleId)
        {
            return _uncaptured.Contains(ruleId);
        }
    }

    // Everything a compilation's checks share, built once: by the start action on the live tier, where every
    // document action reads it, and by the one check on the build tier. One member is written while those
    // document actions run concurrently and is the only mutable state here: the withheld set. It is not a
    // field of the analyzer, because the compiler shares one analyzer instance across every compilation it
    // drives.
    private sealed class CheckState
    {
        // A set rather than a counter: a rule is named once however many of its findings were withheld.
        private readonly ConcurrentDictionary<string, byte> _withheld =
            new(StringComparer.Ordinal);

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal CheckState(
            string ownProjectName, string modelDirectory, RulePlan plan, SiblingProjects siblings,
            IReadOnlyDictionary<string, SyntaxTree> treesByPath, Lazy<DeclaringTrees> declaringTrees)
        {
            OwnProject = ownProjectName;
            ModelDirectory = modelDirectory;
            Plan = plan;
            Siblings = siblings;
            FirstDeclarers = treesByPath;
            DeclaringTrees = declaringTrees;
        }

        /// <summary>The project this compilation's types are recorded under.</summary>
        internal string OwnProject { get; }

        /// <summary>
        ///     The model file's directory, which path globs read against; a string, so no Core type rides here.
        /// </summary>
        private string ModelDirectory { get; }

        private RulePlan Plan { get; }

        internal SiblingProjects Siblings
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get;
        }

        /// <summary>The first declarer of each path, which is the document that answers for it.</summary>
        internal IReadOnlyDictionary<string, SyntaxTree> FirstDeclarers { get; }

        /// <summary>Which types each tree holds a declaring part of, walked once for the compilation.</summary>
        /// <remarks>
        ///     Lazy under its default ExecutionAndPublication mode, so two documents arriving at once see
        ///     one lookup and one walk pays for it; the lookup is read-only from the moment it exists, and
        ///     the build tier, which never reads it, never pays for it at all.
        /// </remarks>
        internal Lazy<DeclaringTrees> DeclaringTrees
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get;
        }

        /// <summary>
        ///     The findings one check of <paramref name="fragments" /> makes in the files
        ///     <paramref name="ownsFile" /> accepts, less the withheld ones, each recorded as it is met.
        /// </summary>
        /// <remarks>
        ///     Enumerated only inside the boundary that asked for it, which is what keeps the state machine
        ///     behind it, and all it names, inside that boundary too.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal IEnumerable<BuildFinding> Reportable(
            IReadOnlyList<CodebaseFragment> fragments, Func<string, bool> ownsFile, CancellationToken cancellationToken)
        {
            CheckReport report = CheckOneProject(fragments, Plan.ToCheck, Plan.Baselines, ModelDirectory, cancellationToken);

            IReadOnlyList<BuildFinding> findings = BuildFindings.From(report, ownsFile);

            foreach (BuildFinding finding in findings)
            {
                if (Plan.Withholds(finding.RuleId))
                {
                    Withhold(finding.RuleId);
                    continue;
                }

                yield return finding;
            }
        }

        private void Withhold(string ruleId)
        {
            _withheld.TryAdd(ruleId, 0);
        }

        private bool IsWithheld(string ruleId)
        {
            return _withheld.ContainsKey(ruleId);
        }

        // The rules whose findings were counted rather than printed, walked in Plan.ToCheck's order, which is
        // model order, rather than in the order the findings arrived, so two builds of one project report them the
        // same way round.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void ReportWithheld(CompilationAnalysisContext context)
        {
            foreach (ArchRule rule in Plan.ToCheck)
                if (IsWithheld(rule.Id))
                    context.ReportDiagnostic(BuildTierDiagnostics.RuleNotChecked(rule.Id));
        }
    }

    // The compilation's one fault, held by whichever action met it and named by the one end action that
    // reports for the compilation. Written while document and additional-file actions run concurrently.
    private sealed class FaultSlot
    {
        private Exception? _fault;

        /// <summary>The first fault held, or null where none was.</summary>
        internal Exception? Fault => Volatile.Read(ref _fault);

        /// <summary>
        ///     Holds <paramref name="exception" />, if nothing was held before it, so a fault that stopped every
        ///     document is named once rather than once per document.
        /// </summary>
        internal void Hold(Exception exception)
        {
            Interlocked.CompareExchange(ref _fault, exception, null);
        }
    }
}
