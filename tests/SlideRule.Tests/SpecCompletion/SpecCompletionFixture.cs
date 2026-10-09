using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Shouldly;
using SlideRule.SpecCompletion;
using SlideRule.Tests.SpecAnalyzers;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.SpecCompletion;

/// <summary>
///     A workspace shaped the way a spec author's is: a product project with namespaces and types to anchor
///     on, and a spec project that references it and the contract, with the completion provider composed
///     into the host the way an editor composes it.
/// </summary>
/// <remarks>
///     <para>
///         <b>The provider arrives as a built-in part, not through an analyzer reference.</b> Adding its
///         assembly to the host's default composition is what an editor ends up with once it has loaded the
///         analyzer reference the contract package ships, and it is the only way to reach the provider
///         without waiting on a background load with no completion signal. One test covers the other route
///         end to end, deliberately, because it is the route a reader actually gets.
///     </para>
///     <para>
///         The product project's own namespaces sit under <c>MyApp</c> and so does the spec's, which is the
///         arrangement worth testing: the shared root has to be offered because the product contributes to
///         it, while the spec's own leaf under it has to be absent.
///     </para>
/// </remarks>
public sealed class SpecCompletionFixture : IDisposable
{
    /// <summary>Where the caret is, written into the source and taken out before it is parsed.</summary>
    internal const string Caret = "$$";

    private const string ProductAssembly = "MyApp";

    private const string SpecAssembly = "MyApp.Architecture";

    private const string PlainAssembly = "MyApp.Reporting";

    private const string SpecPath = "Spec.cs";

    private const string ProductPath = "Product.cs";

    private const string PlainPath = "Report.cs";

    /// <summary>
    ///     The product a spec written here governs: four namespaces two deep, and one type of each shape a
    ///     slot filters on.
    /// </summary>
    internal const string ProductSource =
        """
        namespace MyApp
        {
            public interface IRegistry
            {
            }
        }

        namespace MyApp.Domain
        {
            public interface IHandler<T>
            {
            }

            public interface IMap<TKey, TValue>
            {
            }

            internal interface IInternalRepository
            {
            }

            public abstract class EntityBase
            {
            }

            public sealed class InvoiceEntity
            {
                public sealed class Line
                {
                }
            }

            public class Container<T>
            {
                public class Entry
                {
                }
            }

            public static class Registry
            {
            }

            public sealed class AuditedAttribute : System.Attribute
            {
            }

            public enum Standing
            {
                Open
            }

            public delegate void Notify();
        }

        namespace MyApp.Domain.Orders
        {
            public interface IOrderHandler
            {
            }

            public class Order
            {
            }
        }

        namespace MyApp.Web
        {
            public class HomeController
            {
            }
        }
        """;

    /// <summary>The contract, which is the one assembly this repository builds that a spec compiles against.</summary>
    private const string ContractFile = "SlideRule.dll";

    /// <summary>
    ///     Everything this repository builds except the contract, by file name.
    /// </summary>
    /// <remarks>
    ///     The test process resolves the whole product, and a spec compiled against all of it would see the
    ///     namespaces the contract's absence is supposed to prove something about — <c>SlideRule</c> would be
    ///     offered by way of the extraction assembly, and the fact that the contract is left out would
    ///     quietly stop meaning anything. Read off the solution rather than listed, so a project added to
    ///     <c>src/</c> cannot silently put its namespace back into every list.
    /// </remarks>
    private static readonly string[] BuiltHere = SolutionInventory.All
        .Select(project => project.AssemblyName + ".dll")
        .Append(SpecSnippet.Suite + ".dll")
        .Where(file => !string.Equals(file, ContractFile, StringComparison.OrdinalIgnoreCase))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>What a spec project compiles against: the test host's own set, plus the contract.</summary>
    internal static readonly ImmutableArray<MetadataReference> References = SpecSnippet.Less(BuiltHere);

    /// <summary>
    ///     The same set with the contract taken out, which is every project in an estate that has no spec in
    ///     it.
    /// </summary>
    private static readonly ImmutableArray<MetadataReference> WithoutTheContract =
        SpecSnippet.Less([.. BuiltHere, ContractFile]);

    private static readonly HostServices Host =
        MefHostServices.Create(MefHostServices.DefaultAssemblies.Add(typeof(SpecSlotCompletionProvider).Assembly));

    private readonly AdhocWorkspace _workspace;

    private readonly Project _spec;

    private readonly Project _plain;

    /// <summary>Builds the three projects and holds the two every fact is written into.</summary>
    public SpecCompletionFixture()
        : this(Host)
    {
    }

    /// <summary>
    ///     The same three projects over <paramref name="host" />, with <paramref name="specAnalyzers" /> on the
    ///     spec project, for the fact that reaches the provider through an analyzer reference instead.
    /// </summary>
    /// <param name="host">The host services the workspace is composed from.</param>
    /// <param name="specAnalyzers">The analyzer references the spec project carries.</param>
    internal SpecCompletionFixture(HostServices host, params AnalyzerReference[] specAnalyzers)
    {
        _workspace = new AdhocWorkspace(host);

        var productId = ProjectId.CreateNewId(ProductAssembly);
        var specId = ProjectId.CreateNewId(SpecAssembly);
        var plainId = ProjectId.CreateNewId(PlainAssembly);

        ProjectInfo product = Library(productId, ProductAssembly, References)
            .WithDocuments([Document(productId, ProductPath, ProductSource)]);
        ProjectInfo spec = Library(specId, SpecAssembly, References)
            .WithProjectReferences([new ProjectReference(productId)])
            .WithAnalyzerReferences(specAnalyzers);
        ProjectInfo plain = Library(plainId, PlainAssembly, WithoutTheContract)
            .WithProjectReferences([new ProjectReference(productId)]);

        Solution solution = AdhocSolution.Of(_workspace, product, spec, plain);
        _spec = solution.GetProject(specId)!;
        _plain = solution.GetProject(plainId)!;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _workspace.Dispose();
    }

    /// <summary>
    ///     What the provider offers at the caret in <paramref name="body" />, with nothing asserted about
    ///     whether the source compiles.
    /// </summary>
    /// <remarks>
    ///     The half-typed shapes an author is actually in the middle of — an unterminated literal above all
    ///     — do not compile, and they are the shapes worth the most. Every fact that is a claim about
    ///     silence goes through the asserting entry point instead.
    /// </remarks>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="cancellationToken">The running test's token.</param>
    internal Task<OfferedList> OfferedAsync(string body, CancellationToken cancellationToken)
    {
        return AskAsync(_spec, SpecPath, Compose(body), CompletionTrigger.Invoke, cancellationToken);
    }

    /// <summary>
    ///     What the provider offers at the caret in a project that never references the contract, which is
    ///     every project in an estate that has no spec in it.
    /// </summary>
    /// <param name="source">A whole file, with <c>$$</c> where the caret is.</param>
    /// <param name="cancellationToken">The running test's token.</param>
    internal Task<OfferedList> OfferedWithoutTheContractAsync(string source, CancellationToken cancellationToken)
    {
        return AskAsync(_plain, PlainPath, source, CompletionTrigger.Invoke, cancellationToken);
    }

    private async Task<OfferedList> AskAsync(
        Project project,
        string path,
        string composed,
        CompletionTrigger trigger,
        CancellationToken cancellationToken)
    {
        (Document document, int caret) = DocumentAt(project, path, composed);
        CompletionService service = CompletionService.GetService(document)
            .ShouldNotBeNull("No C# completion service was composed into the workspace. The test project has "
                             + "to reference Microsoft.CodeAnalysis.CSharp.Features for one to exist at all.");

        CompletionList list = await service.GetCompletionsAsync(
            document, caret, trigger, roles: null, options: null, cancellationToken);
        Compilation compilation = (await document.Project.GetCompilationAsync(cancellationToken))!;

        return new OfferedList(composed, document, service, compilation, list);
    }

    /// <summary>
    ///     A spec document with <paramref name="body" /> written into it, and where its caret is, for a fact
    ///     that asks the service itself.
    /// </summary>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    internal (Document Document, int Caret) SpecDocument(string body)
    {
        return DocumentAt(_spec, SpecPath, Compose(body));
    }

    // The composed source added to the project as a document, with its caret marker taken out.
    private static (Document Document, int Caret) DocumentAt(Project project, string path, string composed)
    {
        (string source, int caret) = WithoutCaret(composed);
        Document document = project.AddDocument(path, SourceText.From(source), filePath: path);

        return (document, caret);
    }

    /// <summary>The source with its caret marker taken out, and where the caret was.</summary>
    /// <param name="composed">A source with <c>$$</c> where the caret is.</param>
    internal static (string Source, int Caret) WithoutCaret(string composed)
    {
        int caret = composed.IndexOf(Caret, StringComparison.Ordinal);
        caret.ShouldBeGreaterThanOrEqualTo(0, $"The source carries no {Caret} caret marker:\n{composed}");

        string source = composed.Remove(caret, Caret.Length);

        return (source, caret);
    }

    /// <summary>
    ///     Whether typing <paramref name="typed" /> at the caret opens a list, asked of the provider itself.
    /// </summary>
    /// <remarks>
    ///     Asked of the provider and not of the service, because the service says yes when any provider
    ///     says yes and Roslyn's own symbol completion triggers on a dot everywhere — so a truth table over
    ///     the service would be a truth table about Roslyn. That the service reaches this gate at all is a
    ///     separate fact, driven through a real insertion trigger.
    /// </remarks>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="typed">The character the author typed, which sits immediately before the caret.</param>
    internal static bool Triggers(string body, char typed)
    {
        string composed = Compose(body);
        (string source, int caret) = WithoutCaret(composed);

        return new SpecSlotCompletionProvider().ShouldTriggerCompletion(
            SourceText.From(source), caret, CompletionTrigger.CreateInsertionTrigger(typed), options: null!);
    }

    /// <summary>
    ///     What the provider offers when the request arrives as an insertion rather than as an explicit
    ///     ask, which is the path a host takes while an author is typing.
    /// </summary>
    /// <param name="body">The statements of <c>Define</c>, with <c>$$</c> where the caret is.</param>
    /// <param name="typed">The character the author typed, which sits immediately before the caret.</param>
    /// <param name="cancellationToken">The running test's token.</param>
    internal Task<OfferedList> OfferedOnInsertionAsync(
        string body, char typed, CancellationToken cancellationToken)
    {
        return AskAsync(
            _spec, SpecPath, Compose(body), CompletionTrigger.CreateInsertionTrigger(typed), cancellationToken);
    }

    /// <summary>The spec source a body is written into, caret marker and all.</summary>
    /// <param name="body">The statements of <c>Define</c>.</param>
    internal static string Compose(string body)
    {
        return $$"""
                 using SlideRule;

                 namespace MyApp.Architecture;

                 public sealed class Spec : IArchitectureSpec
                 {
                     public void Define(Arch arch)
                     {
                         {{body}}
                     }
                 }
                 """;
    }

    private static ProjectInfo Library(ProjectId id, string name, ImmutableArray<MetadataReference> references)
    {
        return ProjectInfo.Create(id, VersionStamp.Default, name, name, LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithParseOptions(SpecSnippet.Language)
            .WithCompilationOptions(SpecSnippet.Options);
    }

    private static DocumentInfo Document(ProjectId project, string path, string source)
    {
        var text = TextAndVersion.Create(SourceText.From(source), VersionStamp.Default, path);

        return DocumentInfo.Create(
            DocumentId.CreateNewId(project), path, loader: TextLoader.From(text), filePath: path);
    }
}

/// <summary>
///     One completion request and everything a fact about it needs: the source it was made over, the
///     document and service that answered it, and the list that came back.
/// </summary>
/// <remarks>
///     The source is kept with its caret marker still in it, because that is the thing a failure has to
///     print: a list of names means nothing without the half-written literal it was offered for.
/// </remarks>
/// <param name="source">The spec source with the caret marker still in place.</param>
/// <param name="document">The document the request was made over.</param>
/// <param name="service">The service that answered, which is also what commits an item.</param>
/// <param name="compilation">The spec project's compilation, for the facts that read symbols directly.</param>
/// <param name="list">What came back.</param>
internal sealed class OfferedList(
    string source,
    Document document,
    CompletionService service,
    Compilation compilation,
    CompletionList list)
{
    /// <summary>The spec source with the caret marker still in place.</summary>
    internal string Source { get; } = source;

    /// <summary>The document the request was made over.</summary>
    internal Document Document { get; } = document;

    /// <summary>The service that answered, and the one that commits an item.</summary>
    internal CompletionService Service { get; } = service;

    /// <summary>The spec project's compilation.</summary>
    internal Compilation Compilation { get; } = compilation;

    /// <summary>
    ///     What tells this provider's rows apart from the host's own: every item it makes carries the text
    ///     a commit would write, and no other provider's does. The name a host stamps on an item is not a
    ///     public property, so this is the marker available from outside.
    /// </summary>
    private const string Marker = SlotItems.InsertProperty;

    /// <summary>The items this provider put in the list, in the order the list carries them.</summary>
    /// <remarks>
    ///     Narrowed by provider, because every claim made here is about this provider and not about the
    ///     list. Where the caret is outside a literal the host's own providers answer with keywords, and a
    ///     fact reading "nothing was offered" off the whole list would be a fact about Roslyn rather than
    ///     about the silence this provider owes.
    /// </remarks>
    internal IReadOnlyList<CompletionItem> Items { get; } = Mine(list).ToList();

    /// <summary>Every offered name as an author reads it, display text and type parameters together.</summary>
    internal IReadOnlyList<string> Texts { get; } = Mine(list)
        .Select(TextOf)
        .ToList();

    /// <summary>Everything else the host offered at the same caret, which a failure prints as context.</summary>
    internal IReadOnlyList<string> Others { get; } = list.ItemsList
        .Where(item => !item.Properties.ContainsKey(Marker))
        .Select(TextOf)
        .ToList();

    private static IEnumerable<CompletionItem> Mine(CompletionList list)
    {
        return list.ItemsList
            .Where(item => item.Properties.ContainsKey(Marker));
    }

    /// <summary>One item's whole text, which is what a row shows and not what a commit writes.</summary>
    internal static string TextOf(CompletionItem item)
    {
        return item.DisplayTextPrefix + item.DisplayText + item.DisplayTextSuffix;
    }

    /// <summary>The whole subject, which every failure here prints.</summary>
    /// <param name="header">What was being asked.</param>
    internal string Describe(string header)
    {
        string offered = Texts.Count == 0 ? "  (nothing)" : "  " + string.Join("\n  ", Texts);
        string others = Others.Count == 0
            ? ""
            : $"\n\nOffered by the host's own providers ({Others.Count}):\n  " + string.Join("\n  ", Others);

        return $"{header}.\n\nSource:\n{Source}\n\nOffered ({Texts.Count}):\n{offered}{others}";
    }
}
