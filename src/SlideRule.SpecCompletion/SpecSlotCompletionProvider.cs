using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;

namespace SlideRule.SpecCompletion;

/// <summary>
///     Offers the namespaces and types a spec can anchor on, inside the string literal being written, in
///     the editor a spec is written in.
/// </summary>
/// <remarks>
///     <para>
///         An editor builds a project's completion providers by reflection and does it inside catch blocks
///         that report nothing, so an assembly that cannot be loaded, a type that cannot be constructed and a
///         member that no longer binds all produce the same thing: no list, and no message anywhere saying
///         why. That is why this assembly references no project in this repository, why every compiler-API
///         member it calls must exist both in the version it is built against and in the versions it runs
///         beside, and why it catches nothing itself. A fault thrown out of here reaches the host's own
///         extension manager, which does report it, while a fault this file swallowed would look exactly like
///         a list with nothing in it.
///     </para>
///     <para>
///         It costs a project that has no spec in it nothing. The trigger is a scan of one line; the request
///         that follows stops at the token kind for every completion in every file that is not inside a
///         string literal, and at the marker lookup for every project that does not reference the contract.
///     </para>
/// </remarks>
[ExportCompletionProvider(nameof(SpecSlotCompletionProvider), LanguageNames.CSharp)]
[Shared]
internal sealed class SpecSlotCompletionProvider : CompletionProvider
{
    private readonly NameUniverse _universe = new();

    /// <summary>
    ///     Whether the character just typed should open a list, which it should only inside a string
    ///     literal and only for the two characters that begin one and continue one.
    /// </summary>
    /// <remarks>
    ///     Letters never trigger, so typing a word inside ordinary prose opens nothing; asking for a list
    ///     explicitly and the refilter a language server performs on every keystroke both arrive as an
    ///     invocation rather than an insertion and never reach here at all, which is what makes the list
    ///     re-appear as an author keeps typing a name.
    /// </remarks>
    public override bool ShouldTriggerCompletion(
        SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
    {
        if (trigger.Kind != CompletionTriggerKind.Insertion) return false;
        if (trigger.Character != '"' && trigger.Character != '.') return false;

        return LiteralCaret.InsideLiteral(text, caretPosition);
    }

    /// <summary>The names for the slot the caret is in, or nothing at all.</summary>
    /// <remarks>
    ///     The order of the steps is the order of their cost: the syntax token first, then the segment,
    ///     then the marker lookup that decides whether this is a spec project, and only then the semantic
    ///     work of binding the call and walking the compilation's namespaces.
    /// </remarks>
    public override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        Document document = context.Document;
        CancellationToken cancellationToken = context.CancellationToken;

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return;

        // An interpolated, raw or u8 literal carries a different token kind, so each falls out here. None
        // of them can be a slot's value: the contract takes a compile-time constant, and the spec-literal
        // analyzer reads the same shapes this does.
        SyntaxToken token = root.FindToken(context.Position);
        if (!token.IsKind(SyntaxKind.StringLiteralToken)) return;
        if (token.Parent is not LiteralExpressionSyntax literal) return;
        if (LiteralCaret.SegmentAt(token, context.Position) is not { } segment) return;

        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (model is null) return;

        Compilation compilation = model.Compilation;
        INamedTypeSymbol? marker = compilation.GetTypeByMetadataName(SpecSlotNames.MarkerMetadataName);
        if (marker is null) return;

        SlotKind kind = SlotBinding.KindAt(literal, model, marker, cancellationToken);
        if (kind == SlotKind.None) return;

        IReadOnlyList<Offer> offers =
            _universe.Offers(document.Project, compilation, marker, kind, segment.Path, cancellationToken);
        if (offers.Count == 0) return;

        // The whole segment, so that a commit replaces it rather than appending to a half-typed one. Set before
        // the first item is added, because adding an item stamps that item's own span from here — the two ways
        // to set a span on an item afterwards are both errors at this compiler API.
        context.CompletionListSpan = segment.Span;

        foreach (Offer offer in offers) context.AddItem(SlotItems.For(offer, segment.Span));

        // Only once the list has something in it. The whole list is names for one slot, and a host that
        // mixed them with the keywords and snippets another provider offers inside a string literal would
        // be offering two answers to one question.
        context.IsExclusive = true;
    }

    /// <summary>The edit that commits <paramref name="item" />, which is never just its display text.</summary>
    public override Task<CompletionChange> GetChangeAsync(
        Document document, CompletionItem item, char? commitKey, CancellationToken cancellationToken)
    {
        return Task.FromResult(SlotItems.ChangeFor(item));
    }
}
