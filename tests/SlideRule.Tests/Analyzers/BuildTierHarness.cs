using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Analyzers;
using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Analyzers;

/// <summary>An additional file the build hands over, held in memory; a null text is a file that would not read.</summary>
internal sealed class InMemoryAdditionalText(string path, SourceText? text) : AdditionalText
{
    internal InMemoryAdditionalText(string path, string? content)
        : this(path, content is null ? null : SourceText.From(content))
    {
    }

    public override string Path { get; } = path;

    public override SourceText? GetText(CancellationToken cancellationToken = default)
    {
        return text;
    }
}

/// <summary>The analyzer config keys a project declares, as the one global section.</summary>
internal sealed class InMemoryConfigOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
{
    public override IEnumerable<string> Keys => values.Keys;

    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        return values.TryGetValue(key, out value);
    }
}

/// <summary>
///     The options provider a compilation is analysed under. Only the global section carries anything: the
///     build tier reads one MSBuild property and nothing per file.
/// </summary>
internal sealed class InMemoryConfigOptionsProvider(IReadOnlyDictionary<string, string> global)
    : AnalyzerConfigOptionsProvider
{
    private static readonly AnalyzerConfigOptions None = new InMemoryConfigOptions(new Dictionary<string, string>());

    public override AnalyzerConfigOptions GlobalOptions { get; } = new InMemoryConfigOptions(global);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
    {
        return None;
    }

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
    {
        return None;
    }
}

/// <summary>
///     Runs the build-tier analyzer the two ways a host runs it — over a whole compilation as a build does,
///     and document by document as an editor does, both through <see cref="CompilationWithAnalyzers" /> —
///     and builds the two-project world the facts are written against.
/// </summary>
/// <remarks>
///     <para>
///         The own project's name and the assembly it produces are deliberately different words, because
///         the analyzer has to answer "whose code is this" from the assembly and report it under the
///         project: a view that conflated them would pass every fact where a project happens to be named
///         after its output.
///     </para>
///     <para>
///         Paths are written with forward slashes and no drive letter, so the path resolver folds them the
///         same way on every platform the suite runs on.
///     </para>
/// </remarks>
internal static class BuildTier
{
    /// <summary>The own project, whose compilation every fact analyses.</summary>
    internal const string OwnProject = "Web";

    /// <summary>The assembly the own project produces, which is not its name.</summary>
    internal const string OwnAssembly = "Web.Site";

    /// <summary>The sibling project, reached only through a reference.</summary>
    internal const string SiblingProject = "Data";

    /// <summary>The assembly the sibling produces, which is not its name either.</summary>
    internal const string SiblingAssembly = "Data.Store";

    /// <summary>Where the build says the model file sits; every baseline path resolves against its directory.</summary>
    internal const string ModelPath = "/repo/sliderule.model.json";

    /// <summary>The baseline path a ratcheted rule carries, as a spec author writes it.</summary>
    internal const string BaselinePath = "arch/baselines/migration.json";

    /// <summary>Where the build hands that baseline over.</summary>
    internal const string BaselineFilePath = "/repo/arch/baselines/migration.json";

    /// <summary>A second baseline, for the facts that need one rule's file to be unlike another's.</summary>
    internal const string OtherBaselinePath = "arch/baselines/other.json";

    /// <summary>Where the build hands that second baseline over.</summary>
    internal const string OtherBaselineFilePath = "/repo/arch/baselines/other.json";

    /// <summary>The ID of the second rule, for the facts that need two ratcheted rules at once.</summary>
    internal const string SecondRuleId = "layering/web-through-the-domain-too";

    /// <summary>The symbol ID of the type that breaks the rule.</summary>
    internal const string OwnTypeId = "T:Web.OrderController";

    /// <summary>The symbol ID of the sibling type two of its declarations reach.</summary>
    internal const string StoreId = "T:Data.Store";

    /// <summary>The symbol ID of the sibling type the third declaration reaches.</summary>
    internal const string LedgerId = "T:Data.Ledger";

    /// <summary>The sibling's source: the types the own project reaches.</summary>
    private const string SiblingSource =
        """
        namespace Data;

        public class Store {}

        public class Ledger {}

        public class Fault : System.Exception
        {
            public Fault() { }

            public Fault(string message) : base(message) { }
        }
        """;

    /// <summary>
    ///     The own project's source. Line 5 and line 6 reach <c>Data.Store</c> and line 7 reaches
    ///     <c>Data.Ledger</c>, and all three field declarations are indented, which is what the located span
    ///     is pinned against.
    /// </summary>
    internal const string OwnSource =
        """
        namespace Web;

        public class OrderController
        {
            public Data.Store First;
            public Data.Store Second;
            public Data.Ledger Book;
        }
        """;

    /// <summary>The same project with nothing in it that any rule here is broken by.</summary>
    internal const string CleanSource =
        """
        namespace Web;

        public class OrderController
        {
            public string Reference;
        }
        """;

    /// <summary>The rule ID every fact about a broken rule uses.</summary>
    internal const string RuleId = "layering/web-through-the-domain";

    /// <summary>The reason the rule carries, which its warning repeats.</summary>
    internal const string Because = "The web layer goes through the domain, so a controller that reaches the "
                                    + "store puts persistence in the delivery path.";

    /// <summary>The fix the rule carries, which its warning repeats.</summary>
    internal const string Fix = "Take the value from a domain service and let that service own the store.";

    /// <summary>What <c>SR2093</c> says for the fault <see cref="Boom" /> throws: its first line only.</summary>
    internal const string FaultMessage =
        "The SlideRule analyzer failed in this project, so some or all of its findings are missing: "
        + "InvalidOperationException: boom. This is a fault in the analyzer and says nothing about the code, "
        + "and 'sliderule check' is unaffected. Report it at https://github.com/andypgray/sliderule/issues.";

    /// <summary>The fault a seam throws to stand for any fault in the analyzer, with a second line to drop.</summary>
    internal static void Boom()
    {
        throw new InvalidOperationException("boom\nsecond line");
    }

    /// <summary>The whole message a rule warning says, for the own type reaching <paramref name="target" />.</summary>
    /// <remarks>
    ///     The law sentence and the exemplar's phrase are read off the built rule so the model stays their one
    ///     author; every other segment is typed literally, because the composed line is what these rows pin.
    /// </remarks>
    internal static string Composed(ArchitectureModel model, string ruleId, string target, string? grown = null)
    {
        string allowance = grown is null ? string.Empty : grown + " ";
        string? exemplar = model.Rule(ruleId).Exemplar;
        string pointer = exemplar is null ? string.Empty : $" Exemplar: {exemplar}.";

        return $"{ruleId}: Web.OrderController references {target}. {allowance}"
               + $"{model.Rule(ruleId).Sentence} Because: {Because} Fix: {Fix}{pointer}";
    }

    /// <summary>The rule every fact about a broken law uses: the own project must not reach the sibling.</summary>
    internal static void EnforceRule(Arch arch)
    {
        arch.Rule(RuleId)
            .Enforce(arch.Layer("Web", "Web.*").MustNotReference(arch.Layer("Data", "Data.*")))
            .Because(Because)
            .Fix(Fix);
    }

    /// <summary>The same law as scheduled debt, ratcheted against <see cref="BaselinePath" />.</summary>
    internal static void MigrateRule(Arch arch)
    {
        MigrateRule(arch, RuleId, BaselinePath);
    }

    /// <summary>The same debt under a chosen ID and baseline.</summary>
    /// <param name="arch">The spec being defined.</param>
    /// <param name="ruleId">The rule's ID.</param>
    /// <param name="baselinePath">The baseline path the rule carries, as a spec author writes it.</param>
    internal static void MigrateRule(Arch arch, string ruleId, string baselinePath)
    {
        MigrateRule(arch, arch.Layer("Web", "Web.*"), arch.Layer("Data", "Data.*"), ruleId, baselinePath);
    }

    /// <summary>
    ///     The same debt over layers the caller declared, for a spec that carries two ratcheted rules: a
    ///     layer name may be declared only once, so two rules over the same pair share them.
    /// </summary>
    /// <param name="arch">The spec being defined.</param>
    /// <param name="web">The own project's layer.</param>
    /// <param name="data">The sibling's layer.</param>
    /// <param name="ruleId">The rule's ID.</param>
    /// <param name="baselinePath">The baseline path the rule carries, as a spec author writes it.</param>
    internal static void MigrateRule(Arch arch, Layer web, Layer data, string ruleId, string baselinePath)
    {
        arch.Rule(ruleId)
            .Migrate(
                from: "Controllers hold the store directly.",
                to: web.MustNotReference(data))
            .Baseline(baselinePath)
            .Because(Because)
            .Fix(Fix);
    }

    /// <summary>
    ///     The sibling quarantined hermetically, whose containment rule ratchets like a Migrate rule and
    ///     yet states a law rather than scheduled debt.
    /// </summary>
    internal static void QuarantineScope(Arch arch)
    {
        arch.Scope("legacy/data")
            .Quarantine(arch.Namespace("Data.*"))
            .Baseline(BaselinePath)
            .Dragons("The store's connection handling is structural and is not to be tidied.")
            .Because(Because);
    }

    /// <summary>A file the build hands over; a null text is a file that would not read.</summary>
    /// <param name="path">The path the build reports for it.</param>
    /// <param name="text">Its content, or null for a text that will not come back.</param>
    internal static AdditionalText File(string path, string? text)
    {
        return new InMemoryAdditionalText(path, text);
    }

    /// <summary>The own project's compilation, referencing the sibling as source symbols or as an image.</summary>
    /// <param name="asImage">Whether the sibling arrives as a metadata image rather than a compilation reference.</param>
    /// <param name="source">The own project's source; defaults to <see cref="OwnSource" />.</param>
    internal static CSharpCompilation OwnCompilation(bool asImage = false, string source = OwnSource)
    {
        return OwnCompilationOf(asImage, ("Web.cs", source));
    }

    /// <summary>The same compilation over several files, for the facts about where a site sits.</summary>
    internal static CSharpCompilation OwnCompilationOf(bool asImage, params (string Path, string Source)[] files)
    {
        CSharpCompilation sibling = CompilationFactory.CreateCompilation(
            SiblingAssembly, [CompilationFactory.CoreLibrary], ("Data.cs", SiblingSource));
        MetadataReference reference = CompilationFactory.Reference(sibling, asImage);

        return CompilationFactory.CreateCompilation(
            OwnAssembly, [CompilationFactory.CoreLibrary, reference], files);
    }

    /// <summary>The model <paramref name="define" /> builds, as the text a model file carries.</summary>
    internal static string ModelText(Action<Arch> define)
    {
        return ModelTextFor(Checker.Model(define));
    }

    /// <summary>The same, for a model a caller already built and goes on to read rules off.</summary>
    internal static string ModelTextFor(ArchitectureModel model)
    {
        IReadOnlyList<ModelDocumentProject> projects =
        [
            new(OwnProject, [OwnAssembly]),
            new(SiblingProject, [SiblingAssembly])
        ];

        return ModelTextFor(model, projects);
    }

    /// <summary>The same, over a project table a fact states for itself.</summary>
    internal static string ModelTextFor(ArchitectureModel model, IReadOnlyList<ModelDocumentProject> projects)
    {
        return ModelDocumentComposer.Compose(ModelDocument.From(model, "Spec", projects));
    }

    /// <summary>The model file as the build hands it over.</summary>
    internal static AdditionalText ModelFile(string? text)
    {
        return new InMemoryAdditionalText(ModelPath, text);
    }

    /// <summary>
    ///     The ratcheted rule's model file beside a baseline file holding <paramref name="baseline" />, both fresh
    ///     texts on every call, as a build hands them to each compilation.
    /// </summary>
    internal static IReadOnlyList<AdditionalText> MigratedWith(string baseline)
    {
        return
        [
            ModelFile(ModelText(MigrateRule)),
            File(BaselineFilePath, baseline)
        ];
    }

    /// <summary>
    ///     A baseline for <see cref="RuleId" /> that grandfathers the two store sites and not the ledger, so the
    ///     ratcheted rule reports one new debt.
    /// </summary>
    internal static string BaselineHoldingTheStore()
    {
        return BaselineComposer.Compose(
            RuleId, BaselineEntry.ForEdge(OwnTypeId, StoreId).WithSiteCount(2));
    }

    /// <summary>The diagnostics the analyzer reports over <paramref name="compilation" />.</summary>
    /// <param name="compilation">The compilation being built.</param>
    /// <param name="additionalFiles">The files the build hands over.</param>
    /// <param name="tier">The tier the project declares, or null for a project that declares none.</param>
    /// <param name="beforeCheck">The fault seam, invoked inside the guard of whichever action checks.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    internal static Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        Compilation compilation,
        IReadOnlyList<AdditionalText> additionalFiles,
        string? tier = null,
        Action? beforeCheck = null,
        CancellationToken cancellationToken = default)
    {
        return DiagnoseAsync(
            new BuildTierAnalyzer(beforeCheck), compilation, additionalFiles, tier, cancellationToken);
    }

    /// <summary>The same run by an analyzer the caller built, with whichever seams it armed.</summary>
    /// <remarks>
    ///     An exception that gets past the analyzer's guards is in the set this returns, as the compiler's own
    ///     <c>AD0001</c>: the driver is handed no exception callback, so it files the failure as a diagnostic
    ///     of the analyzer's own, and a fact that claims one warning and nothing else reds on it.
    /// </remarks>
    /// <param name="analyzer">The analyzer, with its seams armed.</param>
    /// <param name="compilation">The compilation being built.</param>
    /// <param name="additionalFiles">The files the build hands over.</param>
    /// <param name="tier">The tier the project declares, or null for a project that declares none.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    internal static async Task<IReadOnlyList<Diagnostic>> DiagnoseAsync(
        BuildTierAnalyzer analyzer,
        Compilation compilation,
        IReadOnlyList<AdditionalText> additionalFiles,
        string? tier = null,
        CancellationToken cancellationToken = default)
    {
        CompilationWithAnalyzers withAnalyzers = Armed(compilation, additionalFiles, tier, analyzer);

        ImmutableArray<Diagnostic> reported = await withAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);

        return Sorted(reported);
    }

    /// <summary>
    ///     The diagnostics the analyzer reports for each document of <paramref name="compilation" /> on its
    ///     own, keyed by the path of the tree each set was asked for — exactly the call an IDE makes as a
    ///     file is edited, which runs the analyzer's semantic actions over that one document and returns the
    ///     diagnostics local to it.
    /// </summary>
    /// <remarks>
    ///     A diagnostic with no location is not local to any document and so appears in none of these
    ///     sets, however the analyzer reported it — which is why the tool's own state is asked of
    ///     <see cref="DiagnoseAsync(Compilation, IReadOnlyList{AdditionalText}, string, Action, CancellationToken)" />
    ///     instead.
    /// </remarks>
    /// <param name="compilation">The compilation being built.</param>
    /// <param name="additionalFiles">The files the build hands over.</param>
    /// <param name="tier">The tier the project declares, or null for a project that declares none.</param>
    /// <param name="beforeCheck">The fault seam, invoked inside the guard of whichever action checks.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<Diagnostic>>> DiagnosePerDocumentAsync(
        Compilation compilation,
        IReadOnlyList<AdditionalText> additionalFiles,
        string? tier = null,
        Action? beforeCheck = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(SyntaxTree Tree, IReadOnlyList<Diagnostic> Reported)> documents =
            await DiagnoseEachDocumentAsync(compilation, additionalFiles, tier, beforeCheck, cancellationToken);

        var byPath = new Dictionary<string, IReadOnlyList<Diagnostic>>(StringComparer.Ordinal);
        foreach ((SyntaxTree tree, IReadOnlyList<Diagnostic> reported) in documents)
            byPath.Add(tree.FilePath, reported);

        return byPath;
    }

    /// <summary>
    ///     The same run with each document kept apart: one entry per syntax tree, in the order the
    ///     compilation carries them, so two trees under one path are two entries rather than one.
    /// </summary>
    /// <remarks>
    ///     The form to ask for, rather than <see cref="DiagnosePerDocumentAsync" />, wherever the union over
    ///     the documents has to be the whole of what they said: keying by path throws wherever a second tree
    ///     carries the same path, which a compilation built by hand can do and a build cannot.
    /// </remarks>
    /// <param name="compilation">The compilation being built.</param>
    /// <param name="additionalFiles">The files the build hands over.</param>
    /// <param name="tier">The tier the project declares, or null for a project that declares none.</param>
    /// <param name="beforeCheck">The fault seam, invoked inside the guard of whichever action checks.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    internal static async Task<IReadOnlyList<(SyntaxTree Tree, IReadOnlyList<Diagnostic> Reported)>>
        DiagnoseEachDocumentAsync(
            Compilation compilation,
            IReadOnlyList<AdditionalText> additionalFiles,
            string? tier = null,
            Action? beforeCheck = null,
            CancellationToken cancellationToken = default)
    {
        CompilationWithAnalyzers withAnalyzers =
            Armed(compilation, additionalFiles, tier, new BuildTierAnalyzer(beforeCheck));

        var documents = new List<(SyntaxTree Tree, IReadOnlyList<Diagnostic> Reported)>();
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            // The model has to come off the very compilation the analyzers were armed over: a model minted
            // from another instance is a model of another compilation as far as the driver is concerned.
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            ImmutableArray<Diagnostic> reported =
                await withAnalyzers.GetAnalyzerSemanticDiagnosticsAsync(semanticModel, null, cancellationToken);
            documents.Add((tree, Sorted(reported)));
        }

        return documents;
    }

    // The one global analyzer-config key the analyzer reads, which is how a build declares its tier.
    private const string TierProperty = "build_property.SlideRuleTier";

    private static CompilationWithAnalyzers Armed(
        Compilation compilation, IReadOnlyList<AdditionalText> additionalFiles, string? tier,
        BuildTierAnalyzer analyzer)
    {
        // What a project declaring the tier states, or nothing at all: a build that declares no tier is the
        // common case and the analyzer reads it as live, so a null here is a project with no global section
        // rather than one carrying an empty key.
        Dictionary<string, string> global = tier is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(StringComparer.Ordinal) { [TierProperty] = tier };
        var analyzerOptions = new AnalyzerOptions(
            [.. additionalFiles],
            new InMemoryConfigOptionsProvider(global));

        return compilation.WithAnalyzers([analyzer], analyzerOptions);
    }

    // One order for both calls, so a fact written against the batch result reads the same way round when it
    // is asked of one document.
    private static IReadOnlyList<Diagnostic> Sorted(ImmutableArray<Diagnostic> reported)
    {
        return reported
            .OrderBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
            .ToList();
    }
}
