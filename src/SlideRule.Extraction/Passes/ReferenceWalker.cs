using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Walks a single type-declaration part and yields, per node, five channels at once:
///     <list type="bullet">
///         <item>
///             the <b>type</b> it binds a name to (base lists, signatures, generic args, <c>typeof</c>,
///             casts, creations, static receivers, attribute names, <c>nameof</c> operands, and the
///             containing type of any member access) — GRAMMAR §4.1's "a source-level type reference";
///         </item>
///         <item>
///             the <b>member</b> it uses, when the name resolved through one — a method invocation or
///             method-group reference, a property/field/event access (including <c>?.</c>, compound
///             assignment, and <c>+=</c>/<c>-=</c> subscription), a reduced extension call, or a
///             <c>using static</c> bare name — GRAMMAR §4.5's "a source-level member access";
///         </item>
///         <item>
///             the <b>constructed</b> type an object-creation expression creates — explicit <c>new Foo()</c>
///             and target-typed <c>new()</c> — GRAMMAR §4.5's "a source-level object creation". Delegate
///             creation (<c>new Action(M)</c> and its target-typed form) is excluded, keyed on the created
///             symbol's <see cref="Microsoft.CodeAnalysis.TypeKind.Delegate" /> so both spellings skip;
///         </item>
///         <item>
///             the <b>caught</b> type a <c>catch</c> clause catches — the declared type of a typed
///             <c>catch (IOException e)</c>, or synthesized <c>System.Exception</c> for a bare <c>catch</c>
///             — GRAMMAR §4.8's "a source-level <c>catch</c> clause", carrying beside it two syntactic bits:
///             the <b>caught-has-filter</b> bit (whether that clause spells a <c>when</c> filter) and the
///             <b>caught-ends-in-throw</b> bit (whether its block's last statement is a <c>throw</c>, bare or
///             expression form). Both are purely syntactic — a <c>when (true)</c> is a filter, and
///             <c>catch { if (…) return; throw; }</c> ends in a throw — so neither a filter's contents nor a
///             handler's flow is ever judged; and
///         </item>
///         <item>
///             the <b>thrown</b> type a <c>throw</c> statement or throw expression throws — the thrown
///             expression's <em>static</em> (natural, never converted) type — GRAMMAR §4.8's "a source-level
///             <c>throw</c>". A bare rethrow (<c>throw;</c>) throws nothing.
///         </item>
///     </list>
/// </summary>
/// <remarks>
///     <para>
///         The <c>nameof</c> asymmetry is pinned: a <c>nameof</c> operand still mints its type edge but never
///         <em>uses</em> the member it names. Attribute applications, <c>base(…)</c>/<c>this(…)</c>
///         initializers, <c>with</c> expressions and array creation carry no object-creation expression, so
///         none of them reaches the construct channel.
///     </para>
///     <para>
///         Nested type declarations are a hard boundary: <c>descendIntoChildren</c> refuses to descend
///         into a <see cref="BaseTypeDeclarationSyntax" />/<see cref="DelegateDeclarationSyntax" /> other
///         than the root, so a nested type's references belong to the nested node (walked independently)
///         and file-level <c>using</c> directives and assembly-level attribute lists, which sit outside any
///         type, are never attributed to a type. A file of top-level statements is no exception: the
///         synthesized <c>Program</c> is declared by the whole compilation unit, so the descent refuses those
///         nodes as well, and <c>Program</c> keeps only its statements' references. Type targets are
///         normalized to their <c>OriginalDefinition</c> and gated by
///         <see cref="TypeKindMapper" />; member symbols are normalized to definition level too (a reduced
///         extension through <c>ReducedFrom</c>, then every member through <c>OriginalDefinition</c>), so a
///         yielded member's <c>ContainingType</c> agrees with the type channel's target for that node. A
///         repeated bind on one line yields duplicate tuples that the caller dedupes by (file, line).
///     </para>
/// </remarks>
internal static class ReferenceWalker
{
    /// <param name="root">The type-declaration part to walk.</param>
    /// <param name="model">The semantic model for <paramref name="root" />'s tree.</param>
    /// <param name="bareCatchType">
    ///     What a bare <c>catch</c> catches here — <see cref="BareCatchTypeOf" />, resolved once per
    ///     compilation by the caller rather than per clause.
    /// </param>
    public static IEnumerable<(WalkChannels Channels, FragmentSite Site)> Walk(
        SyntaxNode root, SemanticModel model, INamedTypeSymbol? bareCatchType)
    {
        foreach (SyntaxNode node in root.DescendantNodes(n => ReferenceEquals(n, root) || Descends(n)))
        {
            WalkChannels resolved = Resolve(node, model, bareCatchType);

            // Each channel is normalized to its OriginalDefinition and gated independently, so an explicit
            // `new Foo()` (which rides on Target: null, Constructed: Foo) — and a bare `catch` (Target: null,
            // Caught: Exception) — is never dropped by a type-channel guard. The coexistence tests are what
            // catch a regression here.
            INamedTypeSymbol? targetDefinition = ReferencedDefinition(resolved.Target);
            INamedTypeSymbol? constructedDefinition = ReferencedDefinition(resolved.Constructed);
            INamedTypeSymbol? caughtDefinition = ReferencedDefinition(resolved.Caught);
            INamedTypeSymbol? thrownDefinition = ReferencedDefinition(resolved.Thrown);
            if (targetDefinition is null && constructedDefinition is null && caughtDefinition is null && thrownDefinition is null) continue;

            // A member use rides the type channel; if that channel is gated out, its member goes with it.
            ISymbol? memberUse = targetDefinition is not null ? resolved.Member : null;

            // The two catch bits are syntactic, so they ride through the gate untouched — `with` is what
            // says so, rather than a seventh argument that could be transposed.
            WalkChannels gated = resolved with
            {
                Target = targetDefinition,
                Member = memberUse,
                Constructed = constructedDefinition,
                Caught = caughtDefinition,
                Thrown = thrownDefinition
            };

            yield return (gated, FragmentSite.Of(node.SyntaxTree, SpanOf(node)));
        }
    }

    // Nested type declarations are walked as roots of their own. The directives and global attribute lists at
    // the head of a file belong to no type, and a top-level-statements file is the one root that contains
    // them — the synthesized Program is declared by the whole compilation unit — so they are refused here
    // rather than by the choice of root. The directive node itself is still yielded and resolves to nothing.
    private static bool Descends(SyntaxNode node)
    {
        return node is not (BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or UsingDirectiveSyntax
            or AttributeListSyntax { Parent: CompilationUnitSyntax });
    }

    /// <summary>
    ///     What a yielded node's site covers: the reference itself rather than the node as the parser
    ///     bracketed it, narrowed or widened per kind. Every arm keeps the start on the node's own first
    ///     line, because the line the span starts on <em>is</em> the site.
    /// </summary>
    private static TextSpan SpanOf(SyntaxNode node)
    {
        switch (node)
        {
            // A qualified type name is the reference, so `Data.Store` is covered rather than `Store` alone.
            // Only a QualifiedNameSyntax widens: the name of a member access is a member use, and a member
            // use is the member's own name.
            case SimpleNameSyntax name:
                return WidenedName(name);

            // Through the argument list where the creation spells one, else through the type — so an
            // object initializer, which can run over many lines, is left outside.
            case ObjectCreationExpressionSyntax creation:
                int createdEnd = creation.ArgumentList is { } arguments ? arguments.Span.End : creation.Type.Span.End;
                return TextSpan.FromBounds(creation.NewKeyword.SpanStart, createdEnd);

            // Target-typed `new()` always has an argument list, which is the whole of what names the type.
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                return TextSpan.FromBounds(implicitCreation.NewKeyword.SpanStart, implicitCreation.ArgumentList.Span.End);

            // The clause's head and never its handler block.
            case CatchClauseSyntax catchClause:
                return TextSpan.FromBounds(catchClause.CatchKeyword.SpanStart, CatchHeadEnd(catchClause));

            case ThrowStatementSyntax throwStatement:
                return ThrowSpan(throwStatement, throwStatement.ThrowKeyword);

            case ThrowExpressionSyntax throwExpression:
                return ThrowSpan(throwExpression, throwExpression.ThrowKeyword);

            default:
                return node.Span;
        }
    }

    // The outermost qualified name this name is the right end of: `Store` in `Web.Data.Store` widens to all
    // of it, while `Data` — the right end of `Web.Data` alone — widens only that far. A qualification broken
    // across lines keeps the name's own span instead, so widening can never move the site's line.
    private static TextSpan WidenedName(SimpleNameSyntax name)
    {
        SyntaxNode widest = name;
        while (widest.Parent is QualifiedNameSyntax qualified && ReferenceEquals(qualified.Right, widest))
            widest = qualified;

        if (ReferenceEquals(widest, name)) return name.Span;

        SyntaxTree tree = name.SyntaxTree;
        int nameLine = tree.GetLineSpan(name.Span).StartLinePosition.Line;
        int widestLine = tree.GetLineSpan(widest.Span).StartLinePosition.Line;

        return widestLine == nameLine ? widest.Span : name.Span;
    }

    // A `when` filter is part of what the clause catches, so it is covered where there is one; failing that
    // the declaration names the type, and a bare `catch` has only its keyword.
    private static int CatchHeadEnd(CatchClauseSyntax catchClause)
    {
        if (catchClause.Filter is { } filter) return filter.Span.End;
        if (catchClause.Declaration is { } declaration) return declaration.Span.End;

        return catchClause.CatchKeyword.Span.End;
    }

    // To the end of the throw or the end of the keyword's line, whichever comes first: a thrown expression
    // wrapped over several lines is covered as far as the line the site is recorded at and no further.
    private static TextSpan ThrowSpan(SyntaxNode node, SyntaxToken keyword)
    {
        int start = keyword.SpanStart;
        TextLine line = node.SyntaxTree.GetText()
            .Lines.GetLineFromPosition(start);
        int end = Math.Min(node.Span.End, line.End);

        return TextSpan.FromBounds(start, end);
    }

    /// <summary>
    ///     What a bare <c>catch</c> catches (GRAMMAR §4.8): <c>System.Exception</c> as
    ///     <paramref name="compilation" /> resolves it, or <see langword="null" /> where it cannot be
    ///     resolved at all — nothing in a bare catch's syntax names the type, so this lookup is the only
    ///     source of that channel and a null answer mints nothing.
    /// </summary>
    /// <remarks>
    ///     Exposed for the caller to resolve <em>once per compilation</em> and hand to <see cref="Walk" />.
    ///     The compilation keeps no special-type entry for <c>System.Exception</c>, so each lookup is a real
    ///     metadata name resolution across the assembly and its references — a cost a file with a dozen bare
    ///     catches should pay once, not a dozen times.
    /// </remarks>
    internal static INamedTypeSymbol? BareCatchTypeOf(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("System.Exception");
    }

    /// <summary>
    ///     A referenced type's original definition, or <see langword="null" /> when the symbol is absent or
    ///     gated out — implicitly declared, un-nameable, or a <see cref="TypeKindMapper" /> non-match. Applied
    ///     independently to each type-valued channel so each stands or falls on its own.
    /// </summary>
    private static INamedTypeSymbol? ReferencedDefinition(INamedTypeSymbol? symbol)
    {
        if (symbol is null) return null;

        INamedTypeSymbol definition = symbol.OriginalDefinition;
        if (!definition.IsModelledType()) return null;

        return definition;
    }

    /// <summary>
    ///     Resolves a syntax node to its channels; each arm below names only the channels it sets. The type
    ///     is <see langword="null" /> when the node is not a reference at all (namespaces, type parameters,
    ///     locals/params, discards, dynamic, arrays/pointers, predefined-type keywords, and unresolved names
    ///     all fall through).
    /// </summary>
    private static WalkChannels Resolve(SyntaxNode node, SemanticModel model, INamedTypeSymbol? bareCatchType)
    {
        switch (node)
        {
            // IdentifierNameSyntax (excluding the `var` contextual keyword) and GenericNameSyntax.
            // Covers type references and member references (invocations, property/field/event access,
            // static receivers, attribute names, method groups, extension methods, nameof operands).
            case SimpleNameSyntax name when name is not IdentifierNameSyntax { IsVar: true }:
                ISymbol? symbol = model.GetSymbolInfo(name).BestCandidate();
                return new WalkChannels(ContainingTypeOf(symbol), MemberUseOf(symbol, name));

            // Explicit `new Foo()`: the inner `Foo` name syntax already mints the type edge on its own visit,
            // so this node contributes the construct channel ONLY — re-minting the type edge here would
            // double-count the §4.1 reference.
            case ObjectCreationExpressionSyntax creation:
                return new WalkChannels(Constructed: ConstructedChannelOf(CreatedTypeOf(creation, model)));

            // Target-typed `new()`: no inner type-name syntax exists, so this node is the sole source of BOTH
            // the type edge to the created type AND the construct edge (the type edge rides even for a
            // delegate `new()`, only the construct channel is delegate-gated).
            case ImplicitObjectCreationExpressionSyntax creation:
                INamedTypeSymbol? created = CreatedTypeOf(creation, model);
                return new WalkChannels(created, Constructed: ConstructedChannelOf(created));

            // A `catch` clause (§4.8): the ONE arm for catches — it reads `.Declaration` for the typed form
            // and synthesizes System.Exception for a bare `catch`, so there is no separate CatchDeclaration
            // arm (a typed catch's type-name syntax is a SimpleName visited on its own, minting the reference
            // edge; this arm carries the caught channel ONLY — no double-mint). A `when` filter is an ordinary
            // sub-expression walked on its own visits, so it never suppresses this edge.
            case CatchClauseSyntax catchClause:
                return new WalkChannels(
                    Caught: CaughtTypeOf(catchClause, model, bareCatchType),
                    CaughtHasFilter: catchClause.Filter is not null,
                    CaughtEndsInThrow: EndsInThrow(catchClause));

            // A `throw` statement with a non-null expression (§4.8): a bare rethrow `throw;` has a null
            // expression, so it never matches this arm and falls through — it throws nothing.
            case ThrowStatementSyntax { Expression: { } thrownExpression }:
                return new WalkChannels(Thrown: ThrownTypeOf(thrownExpression, model));

            // A throw expression (§4.8): `?? throw …`, a conditional/switch-expression arm, and the
            // expression-bodied `=> throw new X()` — its operand is always present.
            case ThrowExpressionSyntax throwExpression:
                return new WalkChannels(Thrown: ThrownTypeOf(throwExpression.Expression, model));

            default:
                return WalkChannels.None;
        }
    }

    /// <summary>
    ///     Whether a <c>catch</c> clause's block ends in a <c>throw</c> statement (GRAMMAR §4.8) — the bare
    ///     rethrow <c>throw;</c> and the expression form <c>throw new X(…)</c> alike, both being a
    ///     <see cref="ThrowStatementSyntax" />. Purely syntactic and purely terminal: it reads the block's
    ///     <em>last statement</em> and never analyses whether every path reaches it, so
    ///     <c>catch { if (…) return; throw; }</c> ends in a throw. An empty block does not.
    /// </summary>
    private static bool EndsInThrow(CatchClauseSyntax catchClause)
    {
        return catchClause.Block.Statements.LastOrDefault() is ThrowStatementSyntax;
    }

    /// <summary>
    ///     The named type a <c>catch</c> clause catches (GRAMMAR §4.8): the declared type of a typed
    ///     <c>catch (T e)</c> / <c>catch (T)</c> read off <see cref="CatchClauseSyntax.Declaration" />, or —
    ///     for a bare <c>catch</c>, whose syntax names no type at all — the
    ///     <paramref name="bareCatchType" /> the caller resolved for the whole compilation. A null there (a
    ///     compilation lacking <c>System.Exception</c>) yields null, minting nothing. A declared type the
    ///     compiler could only guess at, such as a name two imported namespaces both declare, takes its first
    ///     candidate, as the name's own reference edge does.
    /// </summary>
    private static INamedTypeSymbol? CaughtTypeOf(
        CatchClauseSyntax catchClause, SemanticModel model, INamedTypeSymbol? bareCatchType)
    {
        if (catchClause.Declaration is { } declaration)
            return model.GetTypeInfo(declaration.Type).Type?.BestCandidate() as INamedTypeSymbol;

        return bareCatchType;
    }

    /// <summary>
    ///     The named type a <c>throw</c> expression throws (GRAMMAR §4.8): the thrown expression's NATURAL
    ///     static type — <see cref="TypeInfo.Type" />, never <see cref="TypeInfo.ConvertedType" />, because
    ///     the throw conversion would collapse every thrown expression to <c>System.Exception</c> and erase
    ///     the real type. A <c>throw ex</c> yields the variable's static type; <c>throw null</c> and a
    ///     type-parameter throw yield null (no named type), minting nothing. A type the compiler could only guess
    ///     at takes its first candidate, as the name's own reference edge does.
    /// </summary>
    private static INamedTypeSymbol? ThrownTypeOf(ExpressionSyntax expression, SemanticModel model)
    {
        return model.GetTypeInfo(expression).Type?.BestCandidate() as INamedTypeSymbol;
    }

    /// <summary>
    ///     The named type an object-creation expression creates: the resolved constructor's containing type,
    ///     or (fallback) the expression's type. A type the compiler could only guess at, which binds no
    ///     constructor, takes its first candidate, as the name's own reference edge does.
    /// </summary>
    private static INamedTypeSymbol? CreatedTypeOf(BaseObjectCreationExpressionSyntax creation, SemanticModel model)
    {
        ISymbol? ctor = model.GetSymbolInfo(creation).BestCandidate();
        if (ctor is IMethodSymbol constructor) return constructor.ContainingType;

        ISymbol? created = model.GetTypeInfo(creation).Type?.BestCandidate();
        return created as INamedTypeSymbol;
    }

    /// <summary>
    ///     The construct channel for a created type: the type itself, unless it is a delegate. Delegate
    ///     creation (<c>new Action(M)</c> and its target-typed form) is not a construction edge (GRAMMAR
    ///     §4.5), keyed on the created symbol's <see cref="Microsoft.CodeAnalysis.TypeKind.Delegate" /> so
    ///     both spellings skip regardless of syntax.
    /// </summary>
    private static INamedTypeSymbol? ConstructedChannelOf(INamedTypeSymbol? created)
    {
        return created is { TypeKind: Microsoft.CodeAnalysis.TypeKind.Delegate } ? null : created;
    }

    private static INamedTypeSymbol? ContainingTypeOf(ISymbol? symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol type => type,
            IMethodSymbol method => method.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IEventSymbol @event => @event.ContainingType,
            _ => null
        };
    }

    /// <summary>
    ///     The normalized member a name uses (GRAMMAR §4.5), or <see langword="null" /> when the name is
    ///     not a member use. A <c>nameof</c> operand is excluded here (member channel only) so its type
    ///     edge still rides through unchanged. Methods are filtered to <see cref="MethodKind.Ordinary" />
    ///     and <see cref="MethodKind.ReducedExtension" /> — constructors, operators, conversions, local
    ///     functions, and accessors are not uses — then normalized <c>ReducedFrom</c>-first (a reduced
    ///     extension call is a use of the declaring static class's method) and <c>OriginalDefinition</c>-last;
    ///     properties/fields/events go straight to <c>OriginalDefinition</c> (an accessor already resolves
    ///     to its property/event).
    /// </summary>
    private static ISymbol? MemberUseOf(ISymbol? symbol, SyntaxNode node)
    {
        if (symbol is null || IsNameofOperand(node)) return null;

        switch (symbol)
        {
            case IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension } method:
                IMethodSymbol normalized = method.ReducedFrom ?? method;
                return normalized.OriginalDefinition;

            case IPropertySymbol property:
                return property.OriginalDefinition;

            case IFieldSymbol field:
                return field.OriginalDefinition;

            case IEventSymbol @event:
                return @event.OriginalDefinition;

            default:
                return null;
        }
    }

    /// <summary>
    ///     True when <paramref name="node" /> sits inside a <c>nameof(...)</c> operand — a compile-time
    ///     symbol reference, not a runtime use. The walk up the parent chain is bounded by the enclosing
    ///     statement/member (a <c>nameof</c>, if present, is reached first), so it stays local and cheap.
    /// </summary>
    private static bool IsNameofOperand(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } })
                return true;

            if (current is StatementSyntax or MemberDeclarationSyntax) break;
        }

        return false;
    }
}

/// <summary>
///     What one syntax node contributed to each of <see cref="ReferenceWalker" />'s five channels, plus the
///     two syntactic bits that ride beside the caught one. Every member defaults to "contributed nothing", so
///     a <c>Resolve</c> arm names only the channels it actually sets and the reader of an arm sees its whole
///     effect in one line.
/// </summary>
/// <remarks>
///     The channels are deliberately independent: a node can carry a construction with or without a type
///     reference, or a catch and no type reference, and nothing here couples them.
///     The record shape is what keeps a channel from being transposed with its neighbour at a call site —
///     the two <see cref="bool" />s are the pair the compiler could not otherwise tell apart.
/// </remarks>
/// <param name="Target">The type a name binds to (GRAMMAR §4.1).</param>
/// <param name="Member">The member a name uses (§4.5); rides the <paramref name="Target" /> channel.</param>
/// <param name="Constructed">The type an object creation creates (§4.5).</param>
/// <param name="Caught">The type a <c>catch</c> clause catches (§4.8).</param>
/// <param name="CaughtHasFilter">Whether that clause spells a <c>when</c> filter.</param>
/// <param name="CaughtEndsInThrow">Whether that clause's block ends in a <c>throw</c>.</param>
/// <param name="Thrown">The type a <c>throw</c> throws (§4.8).</param>
internal readonly record struct WalkChannels(
    INamedTypeSymbol? Target = null,
    ISymbol? Member = null,
    INamedTypeSymbol? Constructed = null,
    INamedTypeSymbol? Caught = null,
    bool CaughtHasFilter = false,
    bool CaughtEndsInThrow = false,
    INamedTypeSymbol? Thrown = null)
{
    /// <summary>The all-empty result: what a node that binds nothing resolves to.</summary>
    internal static WalkChannels None => default;
}
