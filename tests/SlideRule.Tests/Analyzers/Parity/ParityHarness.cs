using System.Globalization;
using Microsoft.CodeAnalysis;
using SlideRule.Analyzers;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Cli.Pipeline;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Rendering;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Baselines;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.ModelFile;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>Which form the compilations of one leg see a sibling project in.</summary>
internal enum ParityLeg
{
    /// <summary>The sibling arrives as the referenced compilation's own source symbols.</summary>
    CompilationReference,

    /// <summary>The sibling arrives as a metadata-only image, which is what a command-line build hands over.</summary>
    MetadataImage
}

/// <summary>Which of the two ways a host runs the analyzer a report was taken through.</summary>
/// <remarks>
///     The same analyzer either way, and the same findings: what moves is which of its actions does the
///     work, so a report taken one way and a report taken the other are the two halves of one claim.
/// </remarks>
internal enum ParityDriver
{
    /// <summary>The whole compilation asked at once, which is what a command-line build does.</summary>
    Batch,

    /// <summary>One document at a time, which is what an editor does as a file is edited.</summary>
    PerDocument
}

/// <summary>
///     One reported finding keyed by what it says rather than by where a host printed it: the rule, the
///     kind of violation, the two ends of its baseline identity, and the site.
/// </summary>
/// <remarks>
///     The near end is the edge's source or, for a violation about a subject, the subject — one slot,
///     because a key is compared against a key and the two never carry both. Read off the diagnostic's
///     properties on the build side and off the violation on the check side, never off a printed location:
///     a Razor site prints at its mapped <c>.cshtml</c> line while the check names the generated file.
/// </remarks>
internal sealed record ParityKey(string Rule, string Kind, string? Near, string? Target, string File, int Line)
{
    /// <summary>The one line a failure message prints for this key.</summary>
    internal string Describe()
    {
        string ends = Target is null ? Near ?? "(no identity)" : $"{Near} -> {Target}";
        var line = Line.ToString(CultureInfo.InvariantCulture);

        return $"{Rule} [{Kind}] {ends} at {File}:{line}";
    }
}

/// <summary>One compilation the build tier is run over, under the name a failure prints it by.</summary>
internal sealed record ParityCompilation(string Label, Compilation Compilation);

/// <summary>
///     A difference the design accepts on both legs, named and keyed on the reference that is missing —
///     asserted rather than shrugged at, so an unexplained missing key stays a red.
/// </summary>
/// <param name="Name">What the difference is, in the words of the missing reference.</param>
/// <param name="Covers">Whether a key the build did not report is this difference.</param>
internal sealed record ParityGap(string Name, Func<ParityKey, bool> Covers);

/// <summary>
///     What the whole-solution check says of one solution under one spec: the findings the build is held
///     to, and the two sets of rules that are outside that hold.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Unmeasured" /> is the rules the whole-solution check never measured — an operand
///         that matched nothing, a cell listed twice, a predicate that threw — whose report carries an
///         <c>EmptySubject</c> or <c>RuleError</c> violation and no verdict about code. A one-project view
///         drops the empty part and can report what the whole-solution run masks, so those rules are held
///         out of both sides rather than compared.
///     </para>
///     <para>
///         <see cref="Uncaptured" /> is the ratcheted rules with no captured section, which the build names
///         once rather than reporting: <see cref="UncheckedRules" /> is what it is entitled to name.
///     </para>
/// </remarks>
internal sealed class ParityExpectation
{
    internal ParityExpectation(
        IReadOnlyList<string> checkedRules, IReadOnlyList<ParityKey> keys, IReadOnlyList<string> unmeasured,
        IReadOnlyList<string> uncaptured, IReadOnlyDictionary<string, IReadOnlyList<string>> uncapturedFiles)
    {
        Checked = checkedRules;
        Keys = keys;
        Unmeasured = unmeasured;
        Uncaptured = uncaptured;
        UncapturedFiles = uncapturedFiles;
    }

    /// <summary>The rule IDs the pin holds the build to, ordinally sorted.</summary>
    internal IReadOnlyList<string> Checked { get; }

    /// <summary>Every finding the whole-solution check produces for those rules.</summary>
    internal IReadOnlyList<ParityKey> Keys { get; }

    /// <summary>The rule IDs the whole-solution check never measured, ordinally sorted.</summary>
    internal IReadOnlyList<string> Unmeasured { get; }

    /// <summary>The ratcheted rule IDs with no captured section, ordinally sorted.</summary>
    internal IReadOnlyList<string> Uncaptured { get; }

    /// <summary>Where each uncaptured rule would have reported, evaluated against an empty baseline.</summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<string>> UncapturedFiles { get; }

    /// <summary>
    ///     The (compilation, rule) pairs the build is entitled to name as unchecked: an uncaptured rule with
    ///     at least one site in a file that compilation holds.
    /// </summary>
    internal IReadOnlyList<(string Compilation, string Rule)> UncheckedRules(
        IReadOnlyList<ParityCompilation> compilations)
    {
        var pairs = new List<(string Compilation, string Rule)>();
        foreach (ParityCompilation compilation in compilations)
        {
            HashSet<string> owned = compilation.Compilation.SyntaxTrees
                .Select(tree => tree.FilePath)
                .ToHashSet(StringComparer.Ordinal);
            IEnumerable<string> naming = Uncaptured
                .Where(rule => UncapturedFiles.TryGetValue(rule, out IReadOnlyList<string>? files)
                               && files.Any(owned.Contains));
            pairs.AddRange(naming.Select(rule => (compilation.Label, rule)));
        }

        return pairs;
    }
}

/// <summary>What the build tier reported over every compilation of one leg.</summary>
internal sealed class ParityReport
{
    internal ParityReport(
        IReadOnlyList<(string Compilation, ParityKey Key, Diagnostic Diagnostic)> found,
        IReadOnlyList<(string Compilation, string Rule)> uncheckedRules, IReadOnlyList<string> strangers,
        IReadOnlyList<(string Compilation, string TreePath, ParityKey Key, Diagnostic Diagnostic)> placed)
    {
        Found = found;
        Unchecked = uncheckedRules;
        Strangers = strangers;
        Placed = placed;
        Keys = found
            .Select(finding => finding.Key)
            .ToList();
    }

    /// <summary>Every <c>SR2001</c> and <c>SR2002</c>, read off the diagnostic's properties.</summary>
    internal IReadOnlyList<ParityKey> Keys { get; }

    /// <summary>
    ///     The same findings, each under the compilation that reported it and the warning it printed — what a
    ///     claim about a duplicate or about the wording needs and a bare key cannot carry.
    /// </summary>
    internal IReadOnlyList<(string Compilation, ParityKey Key, Diagnostic Diagnostic)> Found { get; }

    /// <summary>Every <c>SR2092</c>, as the compilation that reported it and the rule it named.</summary>
    internal IReadOnlyList<(string Compilation, string Rule)> Unchecked { get; }

    /// <summary>Anything else the build tier said — a fault, a refusal, a missing file, a compiler AD0001.</summary>
    internal IReadOnlyList<string> Strangers { get; }

    /// <summary>
    ///     Where each finding was placed: the document it was reported in beside the key it reported. Empty on
    ///     <see cref="ParityDriver.Batch" />, which asks the whole compilation at once and so has no document
    ///     to name.
    /// </summary>
    internal IReadOnlyList<(string Compilation, string TreePath, ParityKey Key, Diagnostic Diagnostic)> Placed { get; }

    /// <summary>
    ///     The keys reported in the document at <paramref name="treePath" />, in report order.
    /// </summary>
    /// <remarks>
    ///     The path a tree carries, not a site a key names: those two agreeing is the claim
    ///     <c>ShouldPlaceEveryKeyInItsOwnTree</c> makes, so a lookup that read the key's own file would
    ///     answer it by construction.
    /// </remarks>
    /// <param name="treePath">The document to read, as the compilation's tree carries its path.</param>
    internal IReadOnlyList<ParityKey> KeysIn(string treePath)
    {
        return Placed
            .Where(placed => string.Equals(placed.TreePath, treePath, StringComparison.Ordinal))
            .Select(placed => placed.Key)
            .ToList();
    }
}

/// <summary>
///     One solution under one spec, prepared for the pin: the model file the build would be handed, the
///     baselines it names, the whole-solution expectation, and the compilations to run the analyzer over.
/// </summary>
internal sealed class ParitySubject
{
    private IReadOnlyList<ParityCompilation>? _imaged;

    internal ParitySubject(
        string name, CodebaseModel codebase, ModelDocument document, string modelText,
        IReadOnlyList<AdditionalText> files, BaselineIndex baselines, ParityExpectation expectation,
        IReadOnlyList<ParityCompilation> compilations)
    {
        Name = name;
        Codebase = codebase;
        Document = document;
        ModelText = modelText;
        Files = files;
        Baselines = baselines;
        Expectation = expectation;
        Compilations = compilations;
    }

    /// <summary>What a failure calls this subject.</summary>
    internal string Name { get; }

    /// <summary>The merged model the whole-solution check reads.</summary>
    internal CodebaseModel Codebase { get; }

    /// <summary>The document read back out of the model file, which is what the build tier classifies.</summary>
    internal ModelDocument Document { get; }

    /// <summary>The model file's text, byte for byte as the build would be handed it.</summary>
    internal string ModelText { get; }

    /// <summary>The model file and every baseline file it names, as the build hands them over.</summary>
    internal IReadOnlyList<AdditionalText> Files { get; }

    /// <summary>The baselines the whole-solution check loads.</summary>
    internal BaselineIndex Baselines { get; }

    /// <summary>What the whole-solution check says the build may report.</summary>
    internal ParityExpectation Expectation { get; }

    /// <summary>One compilation per project of the solution, in extraction order.</summary>
    internal IReadOnlyList<ParityCompilation> Compilations { get; }

    /// <summary>The same compilations with every sibling project substituted by a metadata-only image.</summary>
    internal IReadOnlyList<ParityCompilation> Imaged => _imaged ??= ParityHarness.AsImages(Compilations);

    /// <summary>The compilations of <paramref name="leg" />.</summary>
    internal IReadOnlyList<ParityCompilation> On(ParityLeg leg)
    {
        return leg == ParityLeg.MetadataImage ? Imaged : Compilations;
    }
}

/// <summary>
///     The parity pin's machinery: what a whole-solution check says the build may report, what the build
///     tier does report over each project, and the two forms a compilation sees a sibling in.
/// </summary>
/// <remarks>
///     <para>
///         The model text is composed the way the render verb composes it and then read back, so the
///         expectation is computed from exactly the rules the build tier is handed rather than from the
///         live model — a rule the composer could not write is not the build's to report.
///     </para>
///     <para>
///         Both sides are keyed on <see cref="BuildFindings" />, which is the projection the analyzer
///         itself reports through: the pin is then about which findings each view reaches, never about how
///         two projections happen to word the same finding.
///     </para>
/// </remarks>
internal static class ParityHarness
{
    /// <summary>
    ///     One solution under the spec <paramref name="specDll" /> carries, with the baselines that sit
    ///     beside the solution.
    /// </summary>
    /// <param name="name">What a failure calls this subject.</param>
    /// <param name="specDll">The spec assembly to load the model from.</param>
    /// <param name="solutionPath">The solution the model governs.</param>
    /// <param name="cancellationToken">The token the workspace and extraction run under.</param>
    internal static Task<ParitySubject> ForSolutionAsync(
        string name, string specDll, string solutionPath, CancellationToken cancellationToken)
    {
        ArchitectureModel model = ModelPipeline.LoadModel(specDll);

        return ForSolutionAsync(name, model, solutionPath, cancellationToken);
    }

    /// <summary>The same, for a model a caller built inline over a bed that carries no spec project.</summary>
    /// <param name="name">What a failure calls this subject.</param>
    /// <param name="model">The spec, already built.</param>
    /// <param name="solutionPath">The solution the model governs.</param>
    /// <param name="cancellationToken">The token the workspace and extraction run under.</param>
    internal static async Task<ParitySubject> ForSolutionAsync(
        string name, ArchitectureModel model, string solutionPath, CancellationToken cancellationToken)
    {
        CodebaseModel codebase = await FixtureCodebase.Of(solutionPath);
        string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
        BaselineIndex baselines = BaselineStore.LoadForModel(model, solutionDirectory);
        IReadOnlyList<ParityCompilation> compilations = await CompilationsOfAsync(solutionPath, cancellationToken);

        return Prepare(name, model, codebase, baselines, solutionDirectory, TextOnDisk, compilations);
    }

    /// <summary>
    ///     The same subject over compilations a test built by hand, with the baselines and the directory the
    ///     model file is taken to sit in stated rather than read off a disk.
    /// </summary>
    /// <param name="name">What a failure calls this subject.</param>
    /// <param name="model">The spec, already built.</param>
    /// <param name="codebase">The merged model the whole-solution check reads.</param>
    /// <param name="baselines">The baselines the whole-solution check loads.</param>
    /// <param name="modelDirectory">Where the build says the model file sits.</param>
    /// <param name="baselineTexts">The text of each baseline file, by the path the model names.</param>
    /// <param name="compilations">One compilation per project, in extraction order.</param>
    internal static ParitySubject ForCompilations(
        string name, ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines,
        string modelDirectory, IReadOnlyDictionary<string, string> baselineTexts,
        IReadOnlyList<ParityCompilation> compilations)
    {
        Dictionary<string, string> byResolvedPath = baselineTexts.ToDictionary(
            entry => BaselinePathResolver.Resolve(entry.Key, modelDirectory), entry => entry.Value,
            StringComparer.Ordinal);

        return Prepare(
            name, model, codebase, baselines, modelDirectory,
            path => byResolvedPath.GetValueOrDefault(path), compilations);
    }

    /// <summary>
    ///     What the whole-solution check over <paramref name="codebase" /> says a build of its projects may
    ///     report, given the rules <paramref name="document" /> carries.
    /// </summary>
    /// <param name="document">The model file's rules, which are the only ones the build tier ever sees.</param>
    /// <param name="codebase">The merged model.</param>
    /// <param name="baselines">The baselines as the tool loads them.</param>
    internal static ParityExpectation ExpectedFor(
        ModelDocument document, CodebaseModel codebase, BaselineIndex baselines)
    {
        List<ArchRule> inRules = document.Rules
            .Where(rule => OneProjectDecidability.Classify(rule).Decidable)
            .ToList();
        CheckReport report = ArchChecker.Check(inRules, codebase, baselines, null);

        List<string> unmeasured = report.Results
            .Where(result => result.Violations.Any(violation =>
                violation.Kind is ViolationKind.EmptySubject or ViolationKind.RuleError))
            .Select(result => result.Rule.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var outsideThePin = new HashSet<string>(unmeasured, StringComparer.Ordinal);

        List<string> uncaptured = inRules
            .Where(rule => rule.IsRatcheted && !baselines.TryGet(rule.Id, out _))
            .Select(rule => rule.Id)
            .Where(id => !outsideThePin.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        var withheld = new HashSet<string>(uncaptured, StringComparer.Ordinal);

        // One check answers both halves: a rule with no captured section was already evaluated against
        // nothing, so its findings here are the ones an empty baseline would give.
        IReadOnlyList<BuildFinding> findings = BuildFindings.From(report, _ => true);
        List<ParityKey> keys = findings
            .Where(finding => !outsideThePin.Contains(finding.RuleId) && !withheld.Contains(finding.RuleId))
            .Select(KeyOf)
            .ToList();
        Dictionary<string, IReadOnlyList<string>> uncapturedFiles = findings
            .Where(finding => withheld.Contains(finding.RuleId))
            .GroupBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(finding => finding.FilePath)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                StringComparer.Ordinal);

        List<string> checkedRules = inRules
            .Select(rule => rule.Id)
            .Where(id => !outsideThePin.Contains(id) && !withheld.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new ParityExpectation(checkedRules, keys, unmeasured, uncaptured, uncapturedFiles);
    }

    /// <summary>
    ///     The build tier's own answer over every compilation of one leg, taken through
    ///     <paramref name="driver" />, with the rules the whole-solution check over <paramref name="subject" />
    ///     never measured dropped from both the findings and the unchecked list.
    /// </summary>
    /// <remarks>
    ///     The two drivers differ in what the tool's own state does, and in nothing else. A batch run is
    ///     handed <c>SR2092</c> from the compilation's end action and records it; a per-document run cannot
    ///     be handed it at all — a diagnostic with no location is local to no document — so one that arrived
    ///     there would be a stranger, and <c>SR2093</c> with it.
    /// </remarks>
    /// <param name="subject">
    ///     The solution and spec under test: the model file and baseline files the build hands over, and the
    ///     rules its whole-solution check never measured.
    /// </param>
    /// <param name="compilations">The compilations to analyse, each under the label a failure prints.</param>
    /// <param name="driver">Which of the two ways a host runs the analyzer to take the answer through.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    /// <param name="tier">
    ///     The tier the build declares, or null to declare none, which the analyzer reads as the live tier.
    /// </param>
    internal static async Task<ParityReport> ReportAsync(
        this ParitySubject subject, IReadOnlyList<ParityCompilation> compilations, ParityDriver driver,
        CancellationToken cancellationToken, string? tier = null)
    {
        var excluded = new HashSet<string>(subject.Expectation.Unmeasured, StringComparer.Ordinal);
        var found = new List<(string Compilation, ParityKey Key, Diagnostic Diagnostic)>();
        var placed = new List<(string Compilation, string TreePath, ParityKey Key, Diagnostic Diagnostic)>();
        var uncheckedRules = new List<(string Compilation, string Rule)>();
        var strangers = new List<string>();

        foreach (ParityCompilation compilation in compilations)
        {
            if (driver == ParityDriver.Batch)
            {
                IReadOnlyList<Diagnostic> reported = await BuildTier.DiagnoseAsync(
                    compilation.Compilation, subject.Files, tier, cancellationToken: cancellationToken);

                foreach (Diagnostic diagnostic in reported)
                    switch (diagnostic.Id)
                    {
                        case "SR2001" or "SR2002":
                            ParityKey key = KeyOf(diagnostic);
                            if (!excluded.Contains(key.Rule)) found.Add((compilation.Label, key, diagnostic));
                            break;
                        case "SR2092":
                            string rule = diagnostic.Property("rule") ?? "(none)";
                            if (!excluded.Contains(rule)) uncheckedRules.Add((compilation.Label, rule));
                            break;
                        default:
                            strangers.Add($"{compilation.Label}: {diagnostic.Id} {Message(diagnostic)}");
                            break;
                    }

                continue;
            }

            IReadOnlyList<(SyntaxTree Tree, IReadOnlyList<Diagnostic> Reported)> documents =
                await BuildTier.DiagnoseEachDocumentAsync(
                    compilation.Compilation, subject.Files, tier, cancellationToken: cancellationToken);

            foreach ((SyntaxTree tree, IReadOnlyList<Diagnostic> reported) in documents)
            foreach (Diagnostic diagnostic in reported)
            {
                if (diagnostic.Id is not ("SR2001" or "SR2002"))
                {
                    strangers.Add($"{compilation.Label} ({tree.FilePath}): {diagnostic.Id} {Message(diagnostic)}");
                    continue;
                }

                ParityKey reportedKey = KeyOf(diagnostic);
                if (excluded.Contains(reportedKey.Rule)) continue;

                found.Add((compilation.Label, reportedKey, diagnostic));
                placed.Add((compilation.Label, tree.FilePath, reportedKey, diagnostic));
            }
        }

        return new ParityReport(found, uncheckedRules, strangers, placed);
    }

    /// <summary>
    ///     <paramref name="compilations" /> with every sibling compilation they reference substituted by a
    ///     metadata-only image of it — the form a command-line build of the same solution hands the compiler.
    /// </summary>
    /// <remarks>
    ///     Depth-first and memoized on the compilation itself, so a sibling deeper than one hop is imaged
    ///     against images too and a diamond is emitted once.
    /// </remarks>
    internal static IReadOnlyList<ParityCompilation> AsImages(IReadOnlyList<ParityCompilation> compilations)
    {
        var images = new Dictionary<Compilation, MetadataReference>();

        return compilations
            .Select(compilation => compilation with { Compilation = Imaged(compilation.Compilation, images) })
            .ToList();
    }

    /// <summary>
    ///     One compilation per C# project of the solution at <paramref name="solutionPath" />, ordered as
    ///     the whole-solution extraction orders them.
    /// </summary>
    /// <param name="solutionPath">The solution to open, through the shared warm pool.</param>
    /// <param name="cancellationToken">The token the load and the binding run under.</param>
    internal static async Task<IReadOnlyList<ParityCompilation>> CompilationsOfAsync(
        string solutionPath, CancellationToken cancellationToken)
    {
        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(solutionPath, cancellationToken);
        List<Project> projects = snapshot.Solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .OrderBy(project => project.Name, StringComparer.Ordinal)
            .ThenBy(project => snapshot.TargetFrameworks.GetValueOrDefault(project.Id) ?? "", StringComparer.Ordinal)
            .ToList();

        var compilations = new List<ParityCompilation>();
        foreach (Project project in projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null) continue;

            // The framework rides in the label because a multi-targeted project's compilations share a
            // name, and the unchecked-rule pin is one line per compilation.
            string? framework = snapshot.TargetFrameworks.GetValueOrDefault(project.Id);
            string label = framework is null ? project.Name : $"{project.Name} ({framework})";
            compilations.Add(new ParityCompilation(label, compilation));
        }

        return compilations;
    }

    /// <summary>
    ///     What the one-project pipeline says about <paramref name="ruleId" /> over every compilation of
    ///     <paramref name="subject" />, with the decidability table not consulted at all — the way to put a
    ///     rule the classifier holds out in front of the pin without flipping its row.
    /// </summary>
    /// <remarks>
    ///     Everything the analyzer does around the check is repeated here and nothing else: the own project
    ///     read off the assembly, the sibling table read off the model file, the one-project extraction, the
    ///     merge, and the narrowed check. It is deliberately not the analyzer itself, which would answer
    ///     nothing for a rule its own classifier calls undecidable.
    /// </remarks>
    /// <param name="subject">The solution and spec whose model file names the projects.</param>
    /// <param name="ruleId">The rule to check, which need not be one the table decides.</param>
    /// <param name="cancellationToken">The token extraction and checking run under.</param>
    internal static IReadOnlyList<ParityKey> ForcedKeys(
        ParitySubject subject, string ruleId, CancellationToken cancellationToken)
    {
        return ForcedKeys(subject, ruleId, subject.Compilations, cancellationToken);
    }

    /// <summary>The same, over the compilations of one chosen leg.</summary>
    /// <param name="subject">The solution and spec whose model file names the projects.</param>
    /// <param name="ruleId">The rule to check, which need not be one the table decides.</param>
    /// <param name="compilations">The compilations to read, which are one leg's.</param>
    /// <param name="cancellationToken">The token extraction and checking run under.</param>
    internal static IReadOnlyList<ParityKey> ForcedKeys(
        ParitySubject subject, string ruleId, IReadOnlyList<ParityCompilation> compilations,
        CancellationToken cancellationToken)
    {
        ArchRule rule = subject.Document.Rules.Single(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));

        var keys = new List<ParityKey>();
        foreach (ParityCompilation compilation in compilations)
        {
            if (BuildTierAnalyzer.OwnProjectName(subject.Document, compilation.Compilation) is not { } owner) continue;

            var input = new CompilationInput(compilation.Compilation, owner, Array.Empty<string>());
            var siblings = new SiblingProjects(BuildTierAnalyzer.ProjectTable(subject.Document), owner);
            IReadOnlyList<CodebaseFragment> fragments =
                FragmentExtractor.ExtractOneProject(input, siblings, cancellationToken);
            CheckReport report =
                BuildTierAnalyzer.CheckOneProject(fragments, [rule], subject.Baselines, cancellationToken);

            HashSet<string> owned = compilation.Compilation.SyntaxTrees
                .Select(tree => tree.FilePath)
                .ToHashSet(StringComparer.Ordinal);
            keys.AddRange(BuildFindings.From(report, owned.Contains)
                .Select(KeyOf));
        }

        return keys;
    }

    /// <summary>
    ///     What the whole-solution check says about <paramref name="ruleId" /> alone, keyed the same way —
    ///     the other half of a comparison the decidability table is not asked about.
    /// </summary>
    /// <param name="subject">The solution and spec under test.</param>
    /// <param name="ruleId">The rule to check.</param>
    internal static IReadOnlyList<ParityKey> SolutionKeys(ParitySubject subject, string ruleId)
    {
        ArchRule rule = subject.Document.Rules.Single(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        CheckReport report = ArchChecker.Check([rule], subject.Codebase, subject.Baselines, null);

        return BuildFindings.From(report, _ => true)
            .Select(KeyOf)
            .ToList();
    }

    /// <summary>The one line a failure prints for each of <paramref name="keys" />, ordinally sorted.</summary>
    internal static IReadOnlyList<string> Describe(IEnumerable<ParityKey> keys)
    {
        return keys.Select(key => key.Describe())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    ///     Each finding of <paramref name="report" /> as its key line and the warning's own message, ordinally
    ///     sorted — the form two runs' wording is compared in, so a difference names the finding it is about.
    /// </summary>
    /// <param name="report">What the build tier said over one leg.</param>
    internal static IReadOnlyList<string> DescribeWithMessages(ParityReport report)
    {
        return report.Found
            .Select(finding => $"{finding.Key.Describe()} :: {Message(finding.Diagnostic)}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
    }

    private static ParitySubject Prepare(
        string name, ArchitectureModel model, CodebaseModel codebase, BaselineIndex baselines,
        string modelDirectory, Func<string, string?> baselineTextFor, IReadOnlyList<ParityCompilation> compilations)
    {
        IReadOnlyList<ModelDocumentProject> projects = ModelDocumentProject.ListOf(codebase);
        string text = ModelDocumentComposer.Compose(ModelDocument.From(model, name, projects));
        ModelDocument document = text.ShouldReadBack(name);

        string modelPath = BaselinePathResolver.Resolve(ModelDocument.FileName, modelDirectory);
        var files = new List<AdditionalText> { BuildTier.File(modelPath, text) };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (ArchRule rule in model.Rules)
        {
            if (rule.BaselinePath is not { } declared || !seen.Add(declared)) continue;

            string resolved = BaselinePathResolver.Resolve(declared, modelDirectory);
            if (baselineTextFor(resolved) is not { } baselineText) continue;

            files.Add(BuildTier.File(resolved, baselineText));
        }

        ParityExpectation expectation = ExpectedFor(document, codebase, baselines);

        return new ParitySubject(name, codebase, document, text, files, baselines, expectation, compilations);
    }

    // The file the tool would have read, at the path the tool resolves it to. A declared path with nothing
    // behind it is a rule with no captured baseline, which is a state the pin has an answer for.
    private static string? TextOnDisk(string resolvedPath)
    {
        return File.Exists(resolvedPath) ? File.ReadAllText(resolvedPath) : null;
    }

    private static Compilation Imaged(Compilation compilation, Dictionary<Compilation, MetadataReference> images)
    {
        Compilation substituted = compilation;
        List<CompilationReference> references = compilation.References.OfType<CompilationReference>()
            .ToList();
        foreach (CompilationReference reference in references)
            substituted = substituted.ReplaceReference(reference, ImageOf(reference, images));

        return substituted;
    }

    private static MetadataReference ImageOf(
        CompilationReference reference, Dictionary<Compilation, MetadataReference> images)
    {
        if (images.TryGetValue(reference.Compilation, out MetadataReference? cached)) return cached;

        // The reference's own properties ride over: an alias or an embedded-interop reference that lost
        // them would bind differently rather than merely arrive in another form.
        Compilation sibling = Imaged(reference.Compilation, images);
        MetadataReference image = CompilationFactory.ImageOf(sibling, reference.Properties);
        images.Add(reference.Compilation, image);

        return image;
    }

    private static ParityKey KeyOf(BuildFinding finding)
    {
        return new ParityKey(
            finding.RuleId, finding.ViolationKind.ToString(), finding.Source ?? finding.Subject, finding.Target,
            finding.FilePath, finding.Line);
    }

    private static ParityKey KeyOf(Diagnostic diagnostic)
    {
        string line = diagnostic.Property("line") ?? "0";

        return new ParityKey(
            diagnostic.Property("rule") ?? "(none)", diagnostic.Property("kind") ?? "(none)",
            diagnostic.Property("source") ?? diagnostic.Property("subject"), diagnostic.Property("target"),
            diagnostic.Property("file") ?? "(none)", int.Parse(line, CultureInfo.InvariantCulture));
    }

    private static string Message(Diagnostic diagnostic)
    {
        return diagnostic.GetMessage(CultureInfo.InvariantCulture);
    }
}
