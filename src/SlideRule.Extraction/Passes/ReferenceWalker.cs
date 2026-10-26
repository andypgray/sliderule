using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SlideRule.Extraction.Fragments;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Walks a single type-declaration part and yields, per node, what it contributes to each of the five
///     channels <see cref="WalkChannels" /> carries: the type a name binds to (GRAMMAR §4.1), the member it
///     uses (§4.5), the type an object creation constructs (§4.5), the type a <c>catch</c> clause catches with
///     its two syntactic bits (§4.8), and the type a <c>throw</c> throws (§4.8).
/// </summary>
/// <remarks>
///     The <c>nameof</c> asymmetry is pinned: a <c>nameof</c> operand still mints its type edge but never
///     <em>uses</em> the member it names. Attribute applications, <c>base(…)</c>/<c>this(…)</c> initializers,
///     <c>with</c> expressions and array creation carry no object-creation expression, so none of them reaches
///     the construct channel. Type targets and member symbols are both normalized to definition level, so a
///     yielded member's <c>ContainingType</c> agrees with the type channel's target for that node.
/// </remarks>
internal static class ReferenceWalker
{
    /// <summary>
    ///     Yields every node of <paramref name="root" /> that contributes to a channel, with its site; a repeated
    ///     bind on one line yields duplicates, which the caller dedupes by (file, line).
    ///     <paramref name="bareCatchType" /> is <see cref="BareCatchTypeOf" />'s answer, resolved once per
    ///     compilation by the caller.
    /// </summary>
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

        TextLineCollection lines = name.SyntaxTree.GetText().Lines;
        int nameLine = lines.GetLineFromPosition(name.SpanStart).LineNumber;
        int widestLine = lines.GetLineFromPosition(widest.SpanStart).LineNumber;

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
    ///     <paramref name="compilation" /> resolves it, or <see langword="null" />, which mints nothing.
    /// </summary>
    /// <remarks>
    ///     Resolved once per compilation by the caller: the compilation keeps no special-type entry for
    ///     <c>System.Exception</c>, so each lookup is a metadata name resolution across the assembly and its
    ///     references.
    /// </remarks>
    internal static INamedTypeSymbol? BareCatchTypeOf(Compilation compilation)
    {
        return compilation.GetTypeByMetadataName("System.Exception");
    }

    /// <summary>
    ///     A referenced type's original definition, or <see langword="null" /> when the symbol is absent or is
    ///     not a type the model records (<see cref="TypeKindMapper.IsModelledType" />).
    /// </summary>
    private static INamedTypeSymbol? ReferencedDefinition(INamedTypeSymbol? symbol)
    {
        if (symbol is null) return null;

        INamedTypeSymbol definition = symbol.OriginalDefinition;
        if (!definition.IsModelledType()) return null;

        return definition;
    }

    /// <summary>
    ///     Resolves a syntax node to its channels. The type is <see langword="null" /> when the node is not a
    ///     reference at all (namespaces, type parameters, locals/params, discards, dynamic, arrays/pointers,
    ///     predefined-type keywords, and unresolved names all fall through).
    /// </summary>
    private static WalkChannels Resolve(SyntaxNode node, SemanticModel model, INamedTypeSymbol? bareCatchType)
    {
        switch (node)
        {
            // Covers type references and member references (invocations, property/field/event access,
            // static receivers, attribute names, method groups, extension methods, nameof operands).
            case SimpleNameSyntax name when name is not IdentifierNameSyntax { IsVar: true }:
                ISymbol? symbol = SymbolOf(name, model);
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
    ///     What <paramref name="name" /> binds to, as <see cref="SymbolInfoExtensions.BestCandidate(SymbolInfo)" />
    ///     reads it: asked of the call or creation that owns the name where that one agrees with the name, and bound
    ///     on its own as a type or namespace where it can only be one.
    /// </summary>
    /// <remarks>
    ///     The binder keeps no bound node of its own for an invoked method's name, a created type's name, or most
    ///     names written in a type inside an expression, so a query on one rebinds its whole binding root. In a
    ///     method body that root is the enclosing statement, which the member model hands back from its cache; a
    ///     field or property initializer has no statement and is rebound in full on every such query, which makes
    ///     the walk of a large initializer quadratic in its size. The owning call or creation does have a bound
    ///     node. Each owner arm takes its answer only for the symbol kinds where the two are the same symbol, and
    ///     asks the name itself otherwise: a delegate or function-pointer call resolves to the delegate's
    ///     <c>Invoke</c> while the name is the variable, and a creation of a COM interface resolves to its
    ///     coclass's constructor while the name is the interface. A speculative bind of a type name needs no bound
    ///     tree at all.
    /// </remarks>
    private static ISymbol? SymbolOf(SimpleNameSyntax name, SemanticModel model)
    {
        switch (OwnerOf(name))
        {
            case InvocationExpressionSyntax invocation:
                ISymbol? called = model.GetSymbolInfo(invocation).BestCandidate();
                if (called is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ReducedExtension or MethodKind.LocalFunction })
                    return called;

                // A `nameof` operator binds no symbol to its keyword.
                if (called is null && model.GetOperation(invocation) is INameOfOperation) return null;
                break;

            case ObjectCreationExpressionSyntax creation:
                if (model.GetSymbolInfo(creation).BestCandidate() is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                    && SymbolEqualityComparer.Default.Equals(constructor.ContainingType, model.GetTypeInfo(creation).Type))
                    return constructor.ContainingType;
                break;
        }

        if (StandaloneTypeOf(name) is { } type)
            return model.GetSpeculativeSymbolInfo(type.SpanStart, type, SpeculativeBindingOption.BindAsTypeOrNamespace).BestCandidate();

        return model.GetSymbolInfo(name).BestCandidate();
    }

    // The call a name is the invoked expression of, directly or as the member a member access or a conditional
    // access names, or the creation whose type it is; null for any other name.
    private static ExpressionSyntax? OwnerOf(SimpleNameSyntax name)
    {
        switch (name.Parent)
        {
            case InvocationExpressionSyntax invocation when invocation.Expression == name:
                return invocation;

            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name
                                                                && memberAccess.Parent is InvocationExpressionSyntax invocation
                                                                && invocation.Expression == memberAccess:
                return invocation;

            case MemberBindingExpressionSyntax memberBinding when memberBinding.Name == name
                                                                  && memberBinding.Parent is InvocationExpressionSyntax invocation
                                                                  && invocation.Expression == memberBinding:
                return invocation;

            case ObjectCreationExpressionSyntax creation when creation.Type == name:
                return creation;

            default:
                return null;
        }
    }

    // For a name in a type written inside an expression, which the compiler binds as a namespace or type and nothing
    // else, the syntax whose binding is the name's: the name, or the qualified name it is the right part of. Null for
    // any other name, and where the name's own answer is not a namespace or type: an alias before `::`, an attribute
    // name (which answers the attribute's constructor), and the type of an `is` (which can bind as a constant).
    private static TypeSyntax? StandaloneTypeOf(SimpleNameSyntax name)
    {
        if (!SyntaxFacts.IsInNamespaceOrTypeContext(name)) return null;

        TypeSyntax? standalone = name.Parent switch
        {
            QualifiedNameSyntax qualified when qualified.Right == name => qualified,
            AliasQualifiedNameSyntax aliasQualified when aliasQualified.Name == name => aliasQualified,
            AliasQualifiedNameSyntax => null,
            _ => name
        };
        if (standalone is null) return null;

        SyntaxNode outermost = standalone;
        while (outermost.Parent is QualifiedNameSyntax) outermost = outermost.Parent;

        if (outermost.Parent is AttributeSyntax || outermost.Parent.IsKind(SyntaxKind.IsExpression)) return null;
        if (!name.Ancestors().Any(ancestor => ancestor is ExpressionSyntax and not TypeSyntax)) return null;

        return standalone;
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
    ///     <c>catch (T e)</c> / <c>catch (T)</c>, or <paramref name="bareCatchType" /> for a bare <c>catch</c>,
    ///     whose syntax names no type at all.
    /// </summary>
    private static INamedTypeSymbol? CaughtTypeOf(
        CatchClauseSyntax catchClause, SemanticModel model, INamedTypeSymbol? bareCatchType)
    {
        if (catchClause.Declaration is { } declaration)
            return model.GetTypeInfo(declaration.Type).Type.BestNamedCandidate();

        return bareCatchType;
    }

    /// <summary>
    ///     The named type a <c>throw</c> expression throws (GRAMMAR §4.8): the thrown expression's NATURAL
    ///     static type — <see cref="TypeInfo.Type" />, never <see cref="TypeInfo.ConvertedType" />, because
    ///     the throw conversion would collapse every thrown expression to <c>System.Exception</c> and erase
    ///     the real type. A <c>throw ex</c> yields the variable's static type; <c>throw null</c> and a
    ///     type-parameter throw yield null (no named type), minting nothing.
    /// </summary>
    private static INamedTypeSymbol? ThrownTypeOf(ExpressionSyntax expression, SemanticModel model)
    {
        return model.GetTypeInfo(expression).Type.BestNamedCandidate();
    }

    /// <summary>
    ///     The named type an object-creation expression creates: the resolved constructor's containing type, or
    ///     the expression's type where no constructor binds, as for a type the compiler could only guess at.
    /// </summary>
    private static INamedTypeSymbol? CreatedTypeOf(BaseObjectCreationExpressionSyntax creation, SemanticModel model)
    {
        ISymbol? ctor = model.GetSymbolInfo(creation).BestCandidate();
        if (ctor is IMethodSymbol constructor) return constructor.ContainingType;

        return model.GetTypeInfo(creation).Type.BestNamedCandidate();
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
    ///     The normalized member a name uses (GRAMMAR §4.5), or <see langword="null" /> when the name is not a
    ///     member use. A <c>nameof</c> operand is excluded here, the member channel only, so its type edge
    ///     still rides. Constructors, operators, conversions, local functions and accessors are not uses; a
    ///     reduced extension call is a use of the declaring static class's method, and an accessor already
    ///     resolves to its property or event.
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
internal readonly record struct WalkChannels(
    INamedTypeSymbol? Target = null,
    ISymbol? Member = null,
    INamedTypeSymbol? Constructed = null,
    INamedTypeSymbol? Caught = null,
    bool CaughtHasFilter = false,
    bool CaughtEndsInThrow = false,
    INamedTypeSymbol? Thrown = null)
{
    internal static WalkChannels None => default;
}
