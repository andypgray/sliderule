using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlideRule.Extraction.Fragments;
using SlideRule.Extraction.Passes;
using CoreAccessibility = SlideRule.Accessibility;
using CoreTypeKind = SlideRule.TypeKind;
using RoslynAccessibility = Microsoft.CodeAnalysis.Accessibility;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace SlideRule.Extraction;

/// <summary>
///     Extracts one <see cref="CompilationInput" /> into a self-contained <see cref="CodebaseFragment" />
///     by running the model's passes over a <em>single</em> compilation: declare the types, fill their
///     hierarchy, then mint each edge family. Each <c>Walk*</c> method below states what its own family
///     keys on.
/// </summary>
/// <remarks>
///     <para>
///         One rule holds across every edge family: sites dedupe by (file, line), the self-edge is dropped,
///         and each endpoint type decomposes to its definition-level FQN the way a type edge does (GRAMMAR
///         §4.1).
///     </para>
///     <para>
///         Hierarchy fills <em>by FQN</em>, recording every referenced-but-not-declared FQN in
///         <see cref="CodebaseFragment.Externals" /> with the facts from this compilation's metadata view.
///         That is what makes a fragment self-contained, and it is why a type another project declares
///         still lands in <c>Externals</c> here: this is the per-input half of the split builder, and the
///         merge — not this pass — decides unification (declared-beats-external). See
///         <see cref="FragmentMerger" />.
///     </para>
///     <para>
///         Those facts never read another assembly's syntax, on any path. A reference to another project
///         reaches a compilation either as that project's source symbols or as a metadata image, and only
///         the first carries trees, so another assembly's type takes its generated fact from the attribute
///         alone. An external's facts are then the same whichever form the reference took.
///     </para>
/// </remarks>
internal static class FragmentExtractor
{
    private const string GeneratedCodeAttributeFullName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    private static readonly SymbolDisplayFormat FullNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    public static CodebaseFragment Extract(CompilationInput input)
    {
        return Extract(input, CancellationToken.None);
    }

    /// <summary>
    ///     The same extraction, abandonable: <paramref name="cancellationToken" /> is observed on entry,
    ///     once per syntax tree the registration walk visits, and once per declared type each pass
    ///     processes, so a cancelled caller waits at worst for one type's worth of work.
    /// </summary>
    /// <remarks>
    ///     Those three grains are the whole of the polling: one type's worth of work is short enough to
    ///     finish after a cancel, so the per-node walker and the Roslyn calls under it take no token. The
    ///     token is per-call state, held on the <see cref="ExtractState" /> that dies with the compilation —
    ///     never static, because both halves of extraction are loaded inside the compiler and every analysis
    ///     carries its own.
    /// </remarks>
    internal static CodebaseFragment Extract(CompilationInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, cancellationToken, siblingProjects: null)
            .Run(input);
    }

    /// <summary>
    ///     One project's compilation extracted as a view a rule can be checked over on its own: the
    ///     compilation's own fragment first, then one facts-only fragment per sibling project-and-assembly
    ///     whose types it touched, ordered ordinally by project name and then assembly name. Merging the
    ///     list yields a model in which a sibling's type is solution-declared under its own project rather
    ///     than an external with no layer, which is what lets a rule about the reference between them be
    ///     answered at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The direction is the contract: this view may report <em>less</em> than a check over the whole
    ///         solution and must never report what that check would not. Every drop below serves it. A type
    ///         this compilation cannot vouch for — an error type, one in an assembly
    ///         <paramref name="siblingProjects" /> attributes to several projects, or a sibling with an
    ///         error type in its hierarchy, which is what a truncated reference closure looks like from here
    ///         — is left out of the side fragments <em>and</em> off the far end of every own edge, so no
    ///         verb of either polarity judges it. A sibling's own facts and hierarchy are all that is
    ///         recorded: it carries no declaration sites and no members, because the member inventory read
    ///         from an image is not the one read from source, and sites in a file this compilation does not
    ///         hold cannot be pointed at.
    ///     </para>
    ///     <para>
    ///         Must agree with <see cref="FragmentMerger" /> on two points, or the merge mints a shadow node
    ///         and a note about types that are not split at all: a side fragment's assembly name is the one
    ///         its symbols were bound from, and a name this compilation declares itself is left out of every
    ///         side fragment's externals.
    ///     </para>
    /// </remarks>
    /// <param name="input">
    ///     The compilation to extract, whose <see cref="CompilationInput.ProjectName" /> names the project
    ///     that produced it — never the assembly name, which is what a project selection would then miss.
    /// </param>
    /// <param name="siblingProjects">
    ///     Which project of the solution produces each assembly, or null where nothing said: with no map
    ///     the own fragment is the one <see cref="Extract(CompilationInput)" /> produces and no side
    ///     fragment is built.
    /// </param>
    /// <param name="cancellationToken">
    ///     Observed at the grains <see cref="Extract(CompilationInput,CancellationToken)" /> names, plus
    ///     once per sibling type.
    /// </param>
    internal static IReadOnlyList<CodebaseFragment> ExtractOneProject(
        CompilationInput input, SiblingProjects? siblingProjects, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, cancellationToken, siblingProjects)
            .RunOneProject(input);
    }

    /// <summary>
    ///     One document of a compilation extracted as the same kind of view <see cref="ExtractOneProject" />
    ///     takes of a whole one: the types <paramref name="tree" /> holds a declaring part of, each carried
    ///     <em>whole</em> — every part's declaration sites and every part's edges, wherever those parts sit
    ///     — as the own fragment, then one facts-only fragment per project-and-assembly whose types it
    ///     touched, ordered ordinally by project name and then assembly name. Nothing here merges or checks
    ///     anything.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The union over a compilation's documents of what this returns is what
    ///         <see cref="ExtractOneProject" /> returns of the compilation, and that is the whole contract:
    ///         a rule answered per document must say what the same rule answers over the project, so a type
    ///         is extracted whole from any one of its files rather than a file's slice of it.
    ///     </para>
    ///     <para>
    ///         The own project is a sibling of itself here, which is the one structural difference from the
    ///         whole-compilation view: a type of this compilation that <em>another</em> document declares is
    ///         not in this document's own fragment, so it arrives as a side fragment under the own project
    ///         name and the own assembly name — facts and hierarchy, no members and no declaration sites,
    ///         exactly as another project's type does. Two things follow from it being this compilation's
    ///         own type rather than a sibling's. Its generated verdict reads the file signals as well as the
    ///         attribute, because these are trees this compilation holds and the document that declares it
    ///         reads them too. And a half-resolved hierarchy does not drop it, because the truncated
    ///         reference closure that screen exists for is not something that can happen to a type compiled
    ///         here.
    ///     </para>
    /// </remarks>
    /// <param name="input">
    ///     The compilation the document belongs to, whose <see cref="CompilationInput.ProjectName" /> names
    ///     the project that produced it — the name the own project's own side fragment is recorded under.
    /// </param>
    /// <param name="tree">
    ///     The document to extract, one of <paramref name="input" />'s syntax trees. A tree declaring no
    ///     type is not an error: it yields one own fragment with nothing declared in it.
    /// </param>
    /// <param name="semanticModel">
    ///     The model the caller already holds for <paramref name="tree" />, or <see langword="null" /> to
    ///     mint one. Every other tree this extraction reads mints its own on first ask either way, which is
    ///     what a partial type declared across several files needs.
    /// </param>
    /// <param name="declaringTrees">
    ///     Which types each of the compilation's trees holds a declaring part of, built once for the
    ///     compilation and handed to every document's extraction.
    /// </param>
    /// <param name="siblingProjects">
    ///     Which project of the solution produces each assembly, or null where nothing said: with no map no
    ///     side fragment is built at all, the own project's included.
    /// </param>
    /// <param name="cancellationToken">
    ///     Observed at the grains <see cref="Extract(CompilationInput,CancellationToken)" /> names, plus
    ///     once per sibling type.
    /// </param>
    internal static IReadOnlyList<CodebaseFragment> ExtractDocument(
        CompilationInput input, SyntaxTree tree, SemanticModel? semanticModel, DeclaringTrees declaringTrees,
        SiblingProjects? siblingProjects, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, cancellationToken, siblingProjects)
            .RunDocument(input, tree, semanticModel, declaringTrees);
    }

    /// <summary>
    ///     Every input extracted, in <em>input</em> order — the ordinal order the merge's first-declarer-wins
    ///     rule reads, so the model is byte-identical however the work is scheduled.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Extraction is the dominant phase of a cold run and the one that is embarrassingly parallel:
    ///         each input gets its own <see cref="ExtractState" />, the statics this class shares are pure,
    ///         and Roslyn's symbol and semantic-model APIs are thread-safe. Order is kept by writing each
    ///         fragment to its own slot rather than by the order the work finishes in — a
    ///         completion-ordered list would silently move which fragment declares a shared FQN.
    ///     </para>
    ///     <para>
    ///         The first inner exception is rethrown with its stack intact, so an extraction failure reaches
    ///         a caller as the exception it threw rather than wrapped in an
    ///         <see cref="AggregateException" /> the error-rendering path has never had to know about.
    ///     </para>
    /// </remarks>
    /// <param name="inputs">The compilations to extract, in extraction order.</param>
    /// <param name="artifactFacts">
    ///     What MSBuild said about each input's project, at the same index — null, or a null entry, wherever
    ///     nothing evaluated it, which is every path that hands compilations over directly.
    /// </param>
    public static CodebaseFragment[] ExtractAll(
        IReadOnlyList<CompilationInput> inputs, IReadOnlyList<ProjectArtifactFacts?>? artifactFacts = null)
    {
        var fragments = new CodebaseFragment[inputs.Count];

        try
        {
            Parallel.For(0, inputs.Count, i => fragments[i] = WithArtifactFacts(Extract(inputs[i]), ArtifactFactsAt(artifactFacts, i)));
        }
        catch (AggregateException aggregate) when (aggregate.InnerExceptions.Count > 0)
        {
            ExceptionDispatchInfo.Capture(aggregate.InnerExceptions[0]).Throw();
        }

        return fragments;
    }

    private static ProjectArtifactFacts? ArtifactFactsAt(IReadOnlyList<ProjectArtifactFacts?>? artifactFacts, int index)
    {
        return artifactFacts is not null && index < artifactFacts.Count ? artifactFacts[index] : null;
    }

    // Stamped onto the finished fragment rather than threaded through the extraction, because none of it
    // comes from the compilation: an evaluated fact and a bound symbol are two different reads of the same
    // project, and only one of them has anything to say about a hand-built input. The declared project
    // references are left out on purpose. They are paths and the fragment states names, and the loader has
    // already turned them into the input's reference names, which is where the fragment's list comes from.
    private static CodebaseFragment WithArtifactFacts(CodebaseFragment fragment, ProjectArtifactFacts? facts)
    {
        if (facts is null) return fragment;

        return fragment with
        {
            DeclaredTargetFrameworks = facts.TargetFrameworks,
            TargetFrameworksSite = facts.TargetFrameworksSite,
            PackageReferences = facts.PackageReferences,
            IsPackable = facts.IsPackable,
            IsPackableSite = facts.IsPackableSite,
            LocksPackages = facts.LocksPackages,
            LocksPackagesSite = facts.LocksPackagesSite,
            Properties = facts.Properties
        };
    }

    /// <summary>
    ///     Every type <paramref name="compilation" />'s own assembly declares, nested types included — a
    ///     namespace walk rather than a syntax walk, which is what admits a top-level-statements
    ///     <c>Program</c>, declared by no type-declaration syntax at all.
    /// </summary>
    /// <remarks>
    ///     <see cref="DeclaringTrees" /> indexes this same walk by tree, so a document run declares exactly
    ///     the types one file holds a part of.
    /// </remarks>
    internal static IEnumerable<INamedTypeSymbol> DeclaredTypes(Compilation compilation)
    {
        return TypesInNamespace(compilation.Assembly.GlobalNamespace);
    }

    private static IEnumerable<INamedTypeSymbol> TypesInNamespace(INamespaceSymbol ns)
    {
        foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
        foreach (INamedTypeSymbol type in TypesInNamespace(child))
            yield return type;

        foreach (INamedTypeSymbol type in ns.GetTypeMembers())
        foreach (INamedTypeSymbol nested in TypeAndNested(type))
            yield return nested;
    }

    private static IEnumerable<INamedTypeSymbol> TypeAndNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (INamedTypeSymbol nested in type.GetTypeMembers())
        foreach (INamedTypeSymbol descendant in TypeAndNested(nested))
            yield return descendant;
    }

    private static string FullNameOf(INamedTypeSymbol symbol)
    {
        return symbol.OriginalDefinition.ToDisplayString(FullNameFormat);
    }

    private static string NamespaceOf(INamedTypeSymbol symbol)
    {
        return symbol.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "";
    }

    private static SyntaxToken? IdentifierOf(SyntaxNode node)
    {
        return node switch
        {
            BaseTypeDeclarationSyntax type => type.Identifier,
            DelegateDeclarationSyntax del => del.Identifier,
            _ => null
        };
    }

    // The ratified exclusion filter (GRAMMAR §4.6): drop every compiler-generated/implicitly-declared member
    // (auto-property and field-like-event backing fields, the record equality/clone/deconstruct surface),
    // every member with no source-writable name, every non-Ordinary method (which is exactly accessors,
    // constructors incl. static, operators, conversions, finalizers, and explicit interface METHOD
    // implementations), indexers, and anything that is not a method/property/field/event. Explicit interface
    // implementations are excluded uniformly across all three member kinds: a METHOD impl falls out via the
    // non-Ordinary MethodKind screen, a PROPERTY or EVENT impl via its non-empty
    // ExplicitInterfaceImplementations. An explicit impl is interface plumbing (Private, its name fixed by
    // the interface), never authored surface — so no member subject ever sees one.
    private static bool IsInventoried(ISymbol member)
    {
        if (member.IsImplicitlyDeclared) return false;

        // The member-side twin of the type-side screen in Declare: a member source cannot name is not
        // authored surface, so no rule can be about it. Subtracts exactly the synthesized top-level-statements
        // entry point `<Main>$` — every other unwritable member is already gone on the screens below — which a
        // project-noun subject reaches (the enclosing Program type IS authored surface and stays inventoried).
        if (!member.CanBeReferencedByName) return false;

        return member switch
        {
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary,
            IPropertySymbol { IsIndexer: false, ExplicitInterfaceImplementations.IsEmpty: true } => true,
            IFieldSymbol => true,
            IEventSymbol { ExplicitInterfaceImplementations.IsEmpty: true } => true,
            _ => false
        };
    }

    // The attribute classes one symbol wears, nulls dropped. The one spelling of the projection both the
    // type side and the member side read attributes through.
    private static IEnumerable<INamedTypeSymbol> AttributeClassesOf(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Select(a => a.AttributeClass)
            .OfType<INamedTypeSymbol>();
    }

    private static MemberKind MemberKindOf(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol => MemberKind.Method,
            IPropertySymbol => MemberKind.Property,
            IFieldSymbol => MemberKind.Field,
            IEventSymbol => MemberKind.Event,
            _ => throw new ArgumentOutOfRangeException(nameof(member), member.Kind, "not an inventoried member symbol")
        };
    }

    // Declaration sites for a member: the identifier line of each declaring syntax — a partial method
    // has two, a field/field-like-event declarator has its own — deduped and ordered by (file, line), the
    // same discipline the type declaration-site pass uses. Almost every member declares once, and that case
    // needs neither the ordering set nor the list it would be copied into.
    private static IReadOnlyList<FragmentSite> MemberDeclarationSites(ISymbol member)
    {
        ImmutableArray<SyntaxReference> references = member.DeclaringSyntaxReferences;
        if (references.Length == 1) return [DeclarationSiteOf(references[0])];

        var sites = new SortedSet<FragmentSite>();
        foreach (SyntaxReference reference in references)
            sites.Add(DeclarationSiteOf(reference));

        return sites.ToList();
    }

    /// <summary>
    ///     The site one declaring reference of a member contributes: its identifier token where the syntax
    ///     has one, else the declaring node itself.
    /// </summary>
    private static FragmentSite DeclarationSiteOf(SyntaxReference reference)
    {
        SyntaxNode node = reference.GetSyntax();
        return MemberIdentifierOf(node) is { } identifier
            ? FragmentSite.Of(node.SyntaxTree, identifier.Span)
            : FragmentSite.Of(node);
    }

    private static SyntaxToken? MemberIdentifierOf(SyntaxNode node)
    {
        return node switch
        {
            MethodDeclarationSyntax method => method.Identifier,
            PropertyDeclarationSyntax property => property.Identifier,
            EventDeclarationSyntax @event => @event.Identifier,
            VariableDeclaratorSyntax declarator => declarator.Identifier, // a field or a field-like event
            ParameterSyntax parameter => parameter.Identifier, // a positional record property
            _ => null
        };
    }

    // The definition-level endpoints one constructor-parameter type contributes as injection edges (GRAMMAR
    // §4.7), decomposed exactly like a type edge (§4.1): a constructed generic yields its definition and every
    // type argument (recursively), an array yields its element type (recursively), a plain named type yields
    // itself. Type parameters, pointers, and dynamic contribute nothing. Endpoints are gated the same way a
    // type-edge target is (implicitly declared / un-nameable / TypeKindMapper non-match are skipped).
    private static IEnumerable<INamedTypeSymbol> DecomposeType(ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                foreach (INamedTypeSymbol endpoint in DecomposeType(array.ElementType))
                    yield return endpoint;
                break;

            case INamedTypeSymbol named:
                INamedTypeSymbol definition = named.OriginalDefinition;
                if (TypeKindMapper.IsModelledType(definition)) yield return definition;
                foreach (ITypeSymbol argument in named.TypeArguments)
                foreach (INamedTypeSymbol endpoint in DecomposeType(argument))
                    yield return endpoint;
                break;
        }
    }

    // The signature-position types one inventoried member contributes as exposure edges (GRAMMAR §4.9): a
    // method's return type (System.Void skipped — a void return names nothing) and each parameter type; a
    // property/field/event's own type. Only members IsInventoried admits reach here (Ordinary methods,
    // non-indexer properties, fields, events — never an explicit interface impl of any kind), so constructors,
    // accessors, operators, and the delegate Invoke never contribute. Each yielded type is decomposed
    // definition-level by DecomposeType, exactly like a
    // constructor-parameter type on the injection axis.
    private static IEnumerable<ITypeSymbol> SignatureTypesOf(ISymbol member)
    {
        switch (member)
        {
            case IMethodSymbol method:
                if (!method.ReturnsVoid) yield return method.ReturnType;
                foreach (IParameterSymbol parameter in method.Parameters)
                    yield return parameter.Type;
                break;
            case IPropertySymbol property:
                yield return property.Type;
                break;
            case IFieldSymbol field:
                yield return field.Type;
                break;
            case IEventSymbol @event:
                yield return @event.Type;
                break;
        }
    }

    // Effective visibility (GRAMMAR §4.9): the member is public AND every containing type up the chain is
    // public. A public member nested in an internal type is not surface, so it mints nothing — the honesty
    // boundary (an internal member is not part of the type's external contract). No explicit interface
    // implementation ever reaches this gate — IsInventoried (§4.6) drops all three kinds before the
    // exposure pass runs, so the inventory filter, not this gate, is what keeps explicit impls (Private
    // plumbing) out of the exposure graph.
    private static bool IsEffectivelyPublicMember(ISymbol member)
    {
        if (member.DeclaredAccessibility != RoslynAccessibility.Public) return false;
        for (INamedTypeSymbol? type = member.ContainingType; type is not null; type = type.ContainingType)
            if (type.DeclaredAccessibility != RoslynAccessibility.Public)
                return false;
        return true;
    }

    // The declaration site of a constructor parameter: its in-source identifier location (a primary-constructor
    // parameter, an ordinary-constructor parameter — both point at the parameter name), file path raw from the
    // syntax tree and 1-based line, the same discipline every other site pass uses. Null for a parameter with
    // no source location (should not arise for a source-declared constructor).
    private static FragmentSite? ParameterSite(IParameterSymbol parameter)
    {
        foreach (Location location in parameter.Locations)
            if (location.SourceTree is { } tree)
                return FragmentSite.Of(tree, location.SourceSpan);

        return null;
    }

    // The site of a registration call: the invoked method-name identifier (so a fluent chain records each
    // .AddX() on its own line, not the shared receiver line), file path raw from the syntax tree, 1-based line.
    private static FragmentSite RegistrationSite(InvocationExpressionSyntax invocation)
    {
        SyntaxNode anchor = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name
            : invocation.Expression;
        return FragmentSite.Of(anchor);
    }

    private sealed class ExtractState
    {
        // What a bare `catch` catches here (GRAMMAR §4.8), resolved once for the compilation rather than
        // once per clause — the walk is handed the answer. Null where System.Exception cannot be resolved,
        // which mints nothing.
        private readonly INamedTypeSymbol? _bareCatchType;

        // Observed at the three grains Extract names and nowhere else: a cancelled caller waits at worst for
        // one type's worth of work, so the per-node walker never polls.
        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();

        // The swallowing subset of _catchEdgeUnfilteredSites, keyed identically (GRAMMAR §4.8): unfiltered AND
        // not ending in a throw. Recorded as its own parallel fact for the same polarity reason as the
        // unfiltered subset — a ban must read the sites it forbids directly, never a complement, because sites
        // dedupe by (file, line) and a complement would hide the swallowing clause of a same-line collision.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSwallowingSites = new();

        // The unfiltered subset of _catchEdgeSites, keyed identically (GRAMMAR §4.8). Recorded as its own
        // parallel fact rather than derived by complement: sites dedupe by (file, line), so a filtered and an
        // unfiltered catch of one type on one physical line collapse to a single site, and a complement would
        // then hide the unfiltered clause. Recording the unfiltered sites is truthful for a ban either way.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeUnfilteredSites = new();
        private readonly Compilation _compilation;

        // The memo behind ConstructedName, the sibling of _fullNames for the one fact that must NOT erase a
        // construction: it keys on the constructed symbol rather than its definition, so IEnumerable<int> and
        // IEnumerable<string> get an entry each and neither can be served the other's name.
        //
        // Both symbol-keyed memos here are suppressed for RS1024. The rule wants a symbol-keyed dictionary
        // built with SymbolEqualityComparer, which is what both are; the analyzer that ships with the
        // compiler-API version this project floors on reports them anyway, and spelling the dictionary type
        // out instead of inferring it makes no difference. Measured, not assumed. The suppression is two
        // lines wide at each memo so a future symbol-keyed table built WITHOUT the comparer still reds.
#pragma warning disable RS1024 // false positive: the comparer below is exactly what the rule asks for
        private readonly Dictionary<ISymbol, string> _constructedNames = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();
        private readonly Dictionary<string, DeclaredBuilder> _declared = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(string Src, string Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externals = new(StringComparer.Ordinal);

        // The per-compilation memo behind this state's FullNameOf. The display walk is the most expensive
        // per-reference operation in extraction and the same handful of symbols recur across thousands of
        // sites, so the memo is keyed on OriginalDefinition — what FullNameOf reads — and every construction of
        // one generic shares its entry. It holds symbols, so it is scoped to the state that dies with the
        // compilation.
#pragma warning disable RS1024 // false positive, as above
        private readonly Dictionary<ISymbol, string> _fullNames = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024

        // [System.CodeDom.Compiler.GeneratedCode] as this compilation resolves it, or null where the type is
        // not referenced at all — in which case nothing can wear it and every type is authored. Resolved once
        // so the generated screen is a symbol compare per attribute rather than a full display walk.
        private readonly INamedTypeSymbol? _generatedCodeAttribute;

        // Which of this compilation's trees the workspace reported as generator output, and the memo over
        // the whole per-tree verdict (provenance or banner). The verdict is asked only of this compilation's
        // own trees: once per declaring part of every declared type, and on a document run again for each own
        // type another document declares, while a tree typically declares many types — so the memo turns a
        // per-type root walk into a per-file one. Both hold trees, so both are scoped to the state that dies
        // with the compilation.
        private readonly IReadOnlyCollection<SyntaxTree>? _generatorOutputTrees;
        private readonly Dictionary<SyntaxTree, bool> _generatedTreeVerdicts = new();
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), MemberEdgeBuilder> _memberEdges = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();

        // The per-tree semantic-model memo. Compilation.GetSemanticModel mints a NEW model every call — no
        // provider is attached — and the edge pass asks once per declaring syntax reference, so one model
        // per tree is minted on first ask and dies with this state.
        private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = new();

        // Which project produces each referenced assembly, supplied only through the one-project and document
        // entries. Null is what keeps their whole apparatus — the recording below, the sibling pass, the
        // unvouched set — unreachable from the ordinary extraction, whose fragment is compared byte for byte
        // against the goldens and against the persisted cache.
        private readonly SiblingProjects? _siblingProjects;

        // The own project's name on a document run, null on every other path. Null is what keeps the
        // own-project arm of SortOneProjectName unreachable from the ordinary extraction and from the
        // whole-compilation one-project view, so neither view's fragment depends on it; on those paths an
        // own-assembly type falls through to the sibling map.
        private string? _ownProjectName;

        // Full name → the symbol this compilation bound it from, where it lives, and whether that is this
        // compilation's own assembly. Keyed by name rather than by symbol because that is the identity the
        // merge unifies on, and because the side fragment states one type per name whichever construction
        // was mentioned first. The own-assembly flag records the answer SortOneProjectName reached, for the
        // one reader that needs it: the sibling pass's truncated-closure screen, which an own type skips.
        private readonly Dictionary<string, (INamedTypeSymbol Definition, string ProjectName, string AssemblyName, bool IsOwnAssembly)>
            _siblingSymbols = new(StringComparer.Ordinal);

        // The names this view cannot vouch for, which are dropped from it rather than reported on. Empty
        // wherever there is no sibling map, so the ordinary extraction materializes the lists it built.
        private readonly HashSet<string> _unvouched = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public ExtractState(
            Compilation compilation, IReadOnlyCollection<SyntaxTree>? generatorOutputTrees,
            CancellationToken cancellationToken, SiblingProjects? siblingProjects)
        {
            _compilation = compilation;
            _bareCatchType = ReferenceWalker.BareCatchTypeOf(compilation);
            _generatedCodeAttribute = compilation.GetTypeByMetadataName(GeneratedCodeAttributeFullName);
            _generatorOutputTrees = generatorOutputTrees;
            _cancellationToken = cancellationToken;
            _siblingProjects = siblingProjects;
        }

        public CodebaseFragment Run(CompilationInput input)
        {
            WalkOwnTypes(DeclaredTypes(_compilation));

            // Pass R — registration facts, over every syntax tree rather than per declared type.
            WalkRegistrations();

            return Materialize(input, input.ProjectReferences);
        }

        /// <summary>
        ///     The one-project run: this compilation's own passes, then the sibling pass, then the own
        ///     fragment ahead of the side fragments it found.
        /// </summary>
        public IReadOnlyList<CodebaseFragment> RunOneProject(CompilationInput input)
        {
            // Pass R is skipped: every rule that reads a service registration is outside the set a
            // one-project view can decide, and the walk costs a pass over every syntax tree in every build.
            // The fragment's ServiceRegistrations is therefore empty here.
            return RunView(input, DeclaredTypes(_compilation));
        }

        /// <summary>
        ///     The document run: the own passes over the types one syntax tree holds a declaring part of,
        ///     then the sibling pass, then the own fragment ahead of the side fragments it found.
        /// </summary>
        /// <remarks>
        ///     <see cref="RunOneProject" /> but for two things: what pass 1 ranges over, and the own project
        ///     joining the sibling set so that a type another document declares still reaches the merge as a
        ///     solution type. Passes 2 onwards read every declaring part of every type pass 1 declared, so
        ///     the two runs differ in <em>which</em> types are extracted and never in how much of one.
        /// </remarks>
        public IReadOnlyList<CodebaseFragment> RunDocument(
            CompilationInput input, SyntaxTree tree, SemanticModel? semanticModel, DeclaringTrees declaringTrees)
        {
            // The compiler's own model for the document where the caller held one. Every other tree this
            // run reads — the other parts of a partial type — still mints its own on first ask.
            if (semanticModel is not null) _semanticModels[tree] = semanticModel;

            _ownProjectName = input.ProjectName;

            // Pass R is skipped for RunOneProject's reason and for one more: a registration fact is a
            // whole-compilation walk, so running it per document would read every tree of the compilation
            // once per document and record the same facts that many times.
            return RunView(input, declaringTrees.In(tree));
        }

        /// <summary>
        ///     The run both views share: the own passes over <paramref name="ownTypes" />, then the sibling
        ///     pass, then the own fragment ahead of the side fragments it found.
        /// </summary>
        /// <remarks>
        ///     The sibling pass runs last because its subjects are exactly the names the own passes minted
        ///     as externals, and it runs before materialization because what it cannot vouch for decides
        ///     which of the own edges survive.
        /// </remarks>
        private IReadOnlyList<CodebaseFragment> RunView(CompilationInput input, IEnumerable<INamedTypeSymbol> ownTypes)
        {
            WalkOwnTypes(ownTypes);

            List<SideFragment> sides = WalkSiblings();

            var fragments = new List<CodebaseFragment> { Materialize(input, SiblingProjectReferences()) };
            foreach (SideFragment side in sides) fragments.Add(side.ToFragment());

            return fragments;
        }

        // Pass 1 ranges over what the caller hands it — every type of the compilation on a whole-compilation
        // run, the types one document holds a part of on a document run — and every pass after it ranges
        // over what pass 1 declared.
        private void WalkOwnTypes(IEnumerable<INamedTypeSymbol> types)
        {
            // Pass 1 — declare every solution-declared type this run is about.
            foreach (INamedTypeSymbol symbol in types)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Declare(symbol);
            }

            // Pass 2 — hierarchy (once per declared type, in first-declaration order).
            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                PopulateHierarchy(builder, ResolveName);
            }

            // Pass 3 — edges (every declaring part; sites dedupe).
            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkEdges(builder.Symbol);
            }

            // Pass 3b — injection edges (declared instance constructors; primary ctors included).
            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkInjectionEdges(builder.Symbol);
            }

            // Pass 3c — exposure edges (signature positions of effectively-public members). Its own
            // declaration-side pass, never folded into the Pass-1 inventory loop: an in-solution endpoint must
            // resolve against the fully-populated _declared table, not be minted as an external mid-inventory.
            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkExposureEdges(builder);
            }
        }

        private void Declare(INamedTypeSymbol symbol)
        {
            if (!TypeKindMapper.IsModelledType(symbol)) return;

            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (!_declared.TryGetValue(fqn, out DeclaredBuilder? builder))
            {
                TypeFacts facts = ExtractFacts(definition, fqn);
                builder = new DeclaredBuilder(facts, definition, BuildMembers(definition, facts.Kind));
                _declared[fqn] = builder;
            }

            AccumulateDeclarationSites(builder.Sites, definition);
        }

        private void PopulateHierarchy(DeclaredBuilder builder, Func<INamedTypeSymbol, string> nameOf)
        {
            FragmentConstruction Construction(INamedTypeSymbol type)
            {
                return new FragmentConstruction(nameOf(type), ConstructedName(type));
            }

            INamedTypeSymbol symbol = builder.Symbol;

            builder.BaseTypeFullName = symbol.BaseType is { } baseType ? nameOf(baseType) : null;

            builder.Interfaces = symbol.Interfaces
                .Select(nameOf)
                .ToList();

            // The two attribute facts read one walk of the attribute list, not one each — they differ in what
            // they record (a resolved FQN vs a definition/construction pair) and in their order, never in what
            // they range over.
            List<INamedTypeSymbol> attributeClasses = AttributeClassesOf(symbol).ToList();

            builder.Attributes = attributeClasses
                .Select(nameOf)
                .ToList();

            // Construction-preserving facts: the Definition side resolves to the OriginalDefinition FQN
            // (via nameOf), while the constructed name displays the CONSTRUCTED symbol, so a closed
            // generic keeps its substituted arguments.
            builder.AllInterfaces = symbol.AllInterfaces
                .Select(Construction)
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();

            var baseChain = new List<FragmentConstruction>();
            for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
                baseChain.Add(Construction(current)); // nearest-first; derivation order is meaningful, so not sorted
            builder.BaseTypeChain = baseChain;

            builder.AttributeConstructions = attributeClasses
                .Select(Construction)
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();
        }

        private void WalkEdges(INamedTypeSymbol symbol)
        {
            string srcFqn = FullNameOf(symbol);

            // The recipe every channel below runs, held once — the extractor-side twin of
            // FragmentMerger.MergeSimpleEdge. The order inside is the essential part: name the endpoint,
            // drop the self-edge, and only THEN resolve, so a self-edge mints no external. Returns the
            // endpoint's FQN, or null when the channel was empty or self-dropped — which is what lets the
            // catch channel gate its two parallel subsets on the same decision rather than restating it.
            string? Record(INamedTypeSymbol? endpoint, Dictionary<(string, string), SortedSet<FragmentSite>> map, FragmentSite site)
            {
                if (endpoint is null) return null;

                string fqn = FullNameOf(endpoint);
                if (fqn == srcFqn) return null;

                ResolveName(endpoint);
                FragmentSiteSets.For(map, (srcFqn, fqn)).Add(site);
                return fqn;
            }

            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = reference.GetSyntax();
                SemanticModel model = ModelFor(root.SyntaxTree);
                foreach ((WalkChannels channels, FragmentSite site) in ReferenceWalker.Walk(root, model, _bareCatchType))
                {
                    // The type channel: mint the reference edge (and any member use riding it), self-edges dropped.
                    // The member's containing type is the type-channel target (they agree by construction), so
                    // Record already resolved it; the member edge reuses that FQN and records the member.
                    if (Record(channels.Target, _edgeSites, site) is { } tgtFqn && channels.Member is { } member)
                        RecordMemberEdge(srcFqn, tgtFqn, member, site);

                    // The construct channel: mint the construction edge, self-construction dropped to mirror the
                    // type-edge self-drop (§4.1). Rides independently of the type channel — an explicit `new Foo()`
                    // arrives here with target=null, so a shared self-edge `continue` would silently drop it.
                    Record(channels.Constructed, _constructorEdgeSites, site);

                    // The catch channel (§4.8): mint the catch edge, self-catch dropped like the type-edge self-drop.
                    // Rides independently — a typed catch arrives here with target=null (its type-name syntax minted
                    // the reference edge on its own visit) and a bare catch names no type at all. The same site also
                    // joins the edge's unfiltered subset when the clause spells no `when` filter, so the subset holds
                    // by construction; a bare `catch` is unfiltered, a bare `catch when (…)` is not. It joins the
                    // swallowing subset in turn when it is unfiltered AND its block does not end in a throw, so the
                    // subset-of-a-subset holds by construction too.
                    if (Record(channels.Caught, _catchEdgeSites, site) is { } caughtFqn)
                    {
                        if (!channels.CaughtHasFilter) FragmentSiteSets.For(_catchEdgeUnfilteredSites, (srcFqn, caughtFqn)).Add(site);
                        if (channels is { CaughtHasFilter: false, CaughtEndsInThrow: false })
                            FragmentSiteSets.For(_catchEdgeSwallowingSites, (srcFqn, caughtFqn)).Add(site);
                    }

                    // The throw channel (§4.8): mint the throw edge, self-throw dropped like the type-edge self-drop.
                    // Rides independently of the construct channel — a `throw new X()` mints BOTH the construction
                    // edge (above) and the throw edge here at the same site.
                    Record(channels.Thrown, _throwEdgeSites, site);
                }
            }
        }

        // One member edge per (source, member DocumentationCommentId); sites union like the type-edge pass. The
        // SymbolId fallback mirrors TypeFacts's (unresolved:{fqn}) for a member with no DocID (error/unnamed).
        private void RecordMemberEdge(string srcFqn, string containingFqn, ISymbol member, FragmentSite site)
        {
            string memberSymbolId = member.GetDocumentationCommentId() ?? "unresolved:" + containingFqn + "." + member.Name;

            if (!_memberEdges.TryGetValue((srcFqn, memberSymbolId), out MemberEdgeBuilder? builder))
            {
                builder = new MemberEdgeBuilder(containingFqn, member.Name, MemberKindOf(member));
                _memberEdges[(srcFqn, memberSymbolId)] = builder;
            }

            builder.Sites.Add(site);
        }

        // Injection edges (GRAMMAR §4.7): the declared instance constructors of one type. Primary constructors
        // are included; the !IsImplicitlyDeclared filter drops the record copy constructor and the parameterless
        // default, and InstanceConstructors already excludes the static constructor. Each parameter's type
        // decomposes definition-level like a type edge (§4.1); self-injection is dropped like the type-edge
        // self-drop; ResolveName runs on every decomposed endpoint so external injected types get nodes.
        private void WalkInjectionEdges(INamedTypeSymbol symbol)
        {
            string srcFqn = FullNameOf(symbol);
            foreach (IMethodSymbol ctor in symbol.InstanceConstructors)
            {
                if (ctor.IsImplicitlyDeclared) continue;

                foreach (IParameterSymbol parameter in ctor.Parameters)
                {
                    if (ParameterSite(parameter) is not { } site) continue;

                    foreach (INamedTypeSymbol endpoint in DecomposeType(parameter.Type))
                    {
                        string injectedFqn = ResolveName(endpoint);
                        if (injectedFqn == srcFqn) continue; // self-injection dropped
                        FragmentSiteSets.For(_injectionEdgeSites, (srcFqn, injectedFqn)).Add(site);
                    }
                }
            }
        }

        // Exposure edges (GRAMMAR §4.9): a declaration-side pass over the members of one type. An edge is minted
        // from every public signature position (SignatureTypesOf: a method's return + parameter types, a
        // property/field/event's type) of each effectively-public member — an inventoried member (§4.6) that is
        // itself public AND nested only in public types. Each signature type decomposes definition-level like a
        // type edge (§4.1); self-exposure is dropped like the type-edge self-drop; ResolveName runs on every
        // decomposed endpoint so external exposed types get nodes. The member's declaration sites (a partial
        // member's several parts union) are the edge's sites. The pass screens pass 1's inventory, so the §4.6
        // filter and the sites are read, not recomputed, and an enum or a delegate, which inventories nothing,
        // contributes nothing here.
        private void WalkExposureEdges(DeclaredBuilder builder)
        {
            string srcFqn = FullNameOf(builder.Symbol);
            foreach ((ISymbol symbol, FragmentMember member) in builder.Members)
            {
                if (!IsEffectivelyPublicMember(symbol)) continue;

                foreach (ITypeSymbol signatureType in SignatureTypesOf(symbol))
                foreach (INamedTypeSymbol endpoint in DecomposeType(signatureType))
                {
                    string exposedFqn = ResolveName(endpoint);
                    if (exposedFqn == srcFqn) continue; // self-exposure dropped
                    SortedSet<FragmentSite> sites = FragmentSiteSets.For(_exposureEdgeSites, (srcFqn, exposedFqn));
                    foreach (FragmentSite site in member.DeclarationSites) sites.Add(site);
                }
            }
        }

        // Registration facts (GRAMMAR §4.7): a whole-compilation walk over every syntax tree. Registration is a
        // string-side fact needing no per-type attribution — its most common composition root is a
        // top-level-statements Program — so one pass over every tree is the natural formulation. Each
        // recognized call (RegistrationRecognizer) yields a (lifetime, service, implementation?) fact recorded
        // string-side (definition-level FQNs, never resolved to nodes — registration is many-to-many).
        private void WalkRegistrations()
        {
            var recognizer = new RegistrationRecognizer(_compilation);
            if (!recognizer.IsActive) return; // MEDI not referenced — nothing can be recognized

            foreach (SyntaxTree tree in _compilation.SyntaxTrees)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                SemanticModel model = ModelFor(tree);
                foreach (InvocationExpressionSyntax invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
                foreach (RecognizedRegistration registration in recognizer.Recognize(invocation, model))
                    RecordRegistration(registration, RegistrationSite(invocation));
            }
        }

        private void RecordRegistration(RecognizedRegistration registration, FragmentSite site)
        {
            string serviceFqn = FullNameOf(registration.Service);
            string? implFqn = registration.Implementation is { } impl ? FullNameOf(impl) : null;
            FragmentSiteSets.For(_registrationSites, (registration.Lifetime, serviceFqn, implFqn)).Add(site);
        }

        /// <summary>
        ///     The memoized <see cref="FragmentExtractor.FullNameOf" />. Declaring it here hides the outer
        ///     static one throughout <see cref="ExtractState" />, so every name this state reads — the walk's
        ///     four channels, each pass's source name, <see cref="ResolveName" />'s own lookup — comes back
        ///     through the memo, and the same symbol is displayed once per compilation rather than once per
        ///     site.
        /// </summary>
        /// <remarks>
        ///     That claim is why the member-facts helpers (<see cref="BuildMember" />, <see cref="ParametersOf" />,
        ///     <see cref="AttributeConstructionsOf" /> and <see cref="MemberTypeNames" />) live on this state
        ///     rather than on the outer class: a static helper's call would bind to the outer
        ///     <see cref="FragmentExtractor.FullNameOf" /> and walk the display again for every member,
        ///     parameter, and member attribute in the compilation.
        /// </remarks>
        // ReSharper disable once MemberHidesStaticFromOuterClass
        private string FullNameOf(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            if (_fullNames.TryGetValue(definition, out string? fqn)) return fqn;

            fqn = FragmentExtractor.FullNameOf(definition);
            _fullNames[definition] = fqn;
            return fqn;
        }

        /// <summary>
        ///     The definition-level FQN of any type in extraction format — <see cref="FullNameOf" /> widened
        ///     to the types a signature position can hold (arrays, pointers, type parameters), sharing its
        ///     memo because both key on the same <c>OriginalDefinition</c> and read the same display format.
        /// </summary>
        private string DefinitionName(ITypeSymbol type)
        {
            ITypeSymbol definition = type.OriginalDefinition;
            if (_fullNames.TryGetValue(definition, out string? name)) return name;

            name = definition.ToDisplayString(FullNameFormat);
            _fullNames[definition] = name;
            return name;
        }

        /// <summary>
        ///     The CONSTRUCTED display name of a type — the half of a <see cref="FragmentConstruction" /> that
        ///     must keep its substituted type arguments, so it is memoized against the constructed symbol
        ///     rather than the definition <see cref="DefinitionName" /> keys on.
        /// </summary>
        private string ConstructedName(INamedTypeSymbol symbol)
        {
            if (_constructedNames.TryGetValue(symbol, out string? name)) return name;

            name = symbol.ToDisplayString(FullNameFormat);
            _constructedNames[symbol] = name;
            return name;
        }

        /// <summary>The one semantic model for <paramref name="tree" />, minted on first ask.</summary>
        private SemanticModel ModelFor(SyntaxTree tree)
        {
            if (_semanticModels.TryGetValue(tree, out SemanticModel? model)) return model;

            model = _compilation.GetSemanticModel(tree);
            _semanticModels[tree] = model;
            return model;
        }

        // Scalar shape + identity facts read once from the original definition, whose FQN the caller has
        // already resolved. Shape facts are normalized to C# declaration semantics: a static class is encoded
        // abstract+sealed in metadata (and may be so from source), and the `&& !isStatic` mask converges both
        // paths so a static class reports neither. All three facts a symbol can fail to answer are total here,
        // because the external-mint path reaches symbols the compiler never resolved (a partially-loaded
        // workspace yields error symbols through base types, interfaces, and attribute classes): TypeKind falls
        // back to Class, the baseline key (GRAMMAR §4.3) to an unresolved:{fqn} form when the symbol has no
        // DocumentationCommentId at all (an error symbol does have one — Roslyn's "!:" form — so this covers
        // unnamed types), and accessibility to Public — an unresolved external is not a rule subject (§4.1),
        // its accessibility is informational, and a type reached across an assembly boundary is
        // visibly-public surface. Declared types always map, so all three fallbacks are no-ops for them.
        private TypeFacts ExtractFacts(INamedTypeSymbol definition, string fqn)
        {
            CoreTypeKind kind = TypeKindMapper.TryMap(definition, out CoreTypeKind mapped) ? mapped : CoreTypeKind.Class;
            CoreAccessibility accessibility =
                AccessibilityMapper.TryMap(definition, out CoreAccessibility declared) ? declared : CoreAccessibility.Public;
            bool isStatic = definition.IsStatic;
            return new TypeFacts(
                fqn,
                definition.GetDocumentationCommentId() ?? "unresolved:" + fqn,
                definition.Name,
                NamespaceOf(definition),
                kind,
                accessibility,
                definition.IsSealed && !isStatic,
                isStatic,
                definition.IsAbstract && !isStatic,
                definition.IsRecord,
                IsGeneratedType(definition));
        }

        // The generated fact `.Authored()` filters on (GRAMMAR §5.2), from two independent signals. A type is
        // generated when [System.CodeDom.Compiler.GeneratedCode] sits on it or on any type containing it — so the
        // nested types a generator emits inside an attributed container ride along without carrying their own
        // attribute — OR when every file declaring it is generator output (GeneratedSourceSignals). The second
        // signal is what reaches the generators that emit no attribute at all — a compiled Razor view, measured.
        //
        // The file signals are read for this compilation's own types only. Every external on every path is
        // minted through here, and another assembly's type declares files only when the reference arrived as
        // that project's source: held as an image, the same type declares none. Reading them would make its
        // verdict depend on the form the reference took, and would parse another project's trees to do it, so
        // another assembly's type is judged by the attribute alone, which both forms carry.
        //
        // ALL declaring files, never any: the attribute is a claim about a type, the file signals are claims
        // about a file, and a file fact lifts to a type only when it holds of every file declaring it. The
        // generator that completes an author's partial class writes ONE of its files — [GeneratedRegex] emits
        // the author's own class beside wholly generated ones in a single banner-carrying tree — and any-part
        // would hand the author's class to the generated side.
        //
        // The Length check guards the own types that declare no file at all, and All() over an empty sequence
        // is true. An unresolved name is one: Roslyn places it in this compilation's own global namespace, so
        // it passes the screen above with nothing to test, and would otherwise report generated.
        private bool IsGeneratedType(INamedTypeSymbol definition)
        {
            if (HasGeneratedCodeAttribute(definition)) return true;
            if (!IsInOwnAssembly(definition)) return false;

            ImmutableArray<SyntaxReference> declarations = definition.DeclaringSyntaxReferences;
            return declarations.Length > 0 && declarations.All(d => IsGeneratedTree(d.SyntaxTree));
        }

        // Whether a type belongs to this compilation's own assembly rather than to one it references.
        private bool IsInOwnAssembly(INamedTypeSymbol definition)
        {
            return SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, _compilation.Assembly);
        }

        // Reading the merged symbol is what decides the partial case for this arm: [GeneratedRegex] puts its
        // attribute on the generated METHOD, so the author's own partial class stays authored.
        private bool HasGeneratedCodeAttribute(INamedTypeSymbol definition)
        {
            if (_generatedCodeAttribute is null) return false; // unreferenced — nothing in this compilation wears it

            for (INamedTypeSymbol? current = definition; current is not null; current = current.ContainingType)
                if (current.GetAttributes().Any(IsGeneratedCodeAttribute))
                    return true;

            return false;
        }

        private bool IsGeneratedTree(SyntaxTree tree)
        {
            if (_generatedTreeVerdicts.TryGetValue(tree, out bool memoized)) return memoized;

            bool verdict = GeneratedSourceSignals.IsGeneratedTree(tree, _generatorOutputTrees);
            _generatedTreeVerdicts[tree] = verdict;
            return verdict;
        }

        private bool IsGeneratedCodeAttribute(AttributeData attribute)
        {
            return SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _generatedCodeAttribute);
        }

        // The declared-member inventory of one type (GRAMMAR §4.6): its Ordinary methods, non-indexer
        // properties, fields, and events. Enum and delegate types contribute nothing — an enum's fields are its
        // values (an enum-value read stays a recorded member USE, §4.5) and a delegate's Invoke/BeginInvoke are
        // runtime plumbing. Ordered ordinal by the member's DocumentationCommentId, so the fragment (and the
        // merged node built from it) is deterministic.
        // Each survivor is kept paired with the symbol it was built from: the exposure pass (§4.9) screens this
        // same inventory rather than re-walking GetMembers and recomputing every member's declaration sites.
        private IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> BuildMembers(INamedTypeSymbol type, CoreTypeKind kind)
        {
            if (kind is CoreTypeKind.Enum or CoreTypeKind.Delegate) return [];

            var members = new List<(ISymbol Symbol, FragmentMember Member)>();
            foreach (ISymbol member in type.GetMembers())
                if (IsInventoried(member))
                    members.Add((member, BuildMember(member)));

            members.Sort((left, right) => string.CompareOrdinal(left.Member.Facts.SymbolId, right.Member.Facts.SymbolId));
            return members;
        }

        private FragmentMember BuildMember(ISymbol member)
        {
            string symbolId = member.GetDocumentationCommentId()
                              ?? "unresolved:" + FullNameOf(member.ContainingType) + "." + member.Name;
            (string? returnTypeFullName, string? memberTypeFullName) = MemberTypeNames(member);

            var facts = new MemberFacts(
                symbolId,
                member.Name,
                MemberKindOf(member),
                AccessibilityMapper.Map(member),
                member.IsStatic,
                member.IsAbstract, // C# declaration semantics: an abstract member and every interface member report true
                member.IsVirtual, // false for an override or abstract member — an override is not itself "virtual"
                member is IMethodSymbol { IsAsync: true },
                returnTypeFullName,
                memberTypeFullName,
                ParametersOf(member),
                AttributeConstructionsOf(member),
                member is IPropertySymbol { SetMethod: not null }, // any setter, `private set` and `init` included
                member is IPropertySymbol { SetMethod.IsInitOnly: true }, // the init-only refinement of the above
                member is IFieldSymbol { IsReadOnly: true }, // C# `readonly`; false for a const (IsConst below)
                member is IFieldSymbol { IsConst: true }); // const is also IsStatic — already pinned

            return new FragmentMember(facts, MemberDeclarationSites(member));
        }

        // The declared parameters of a member (GRAMMAR §4.6, §5.6): a method's own parameter list in declaration
        // order, each type normalized by the same DefinitionName helper the return type uses (a constructed generic
        // erases to its definition). The list is read off the DECLARED symbol, so an extension method's `this`
        // parameter is included and a default-valued parameter counts; ref/in/out leave the recorded type alone,
        // a `params T[]` records the array type, and a `T?` records System.Nullable<T>'s definition form. A
        // property/field/event carries no parameters, so it yields the empty list — as does the parameterless
        // method that is the common case, without building a projection to discover it.
        private IReadOnlyList<ParameterFacts> ParametersOf(ISymbol member)
        {
            if (member is not IMethodSymbol method) return [];
            if (method.Parameters.IsEmpty) return [];

            return method.Parameters
                .Select(parameter => new ParameterFacts(parameter.Name, DefinitionName(parameter.Type)))
                .ToList();
        }

        // The declared attributes of a member (GRAMMAR §4.6): what the member symbol itself carries, each as the
        // same definition/constructed name pair the type-side attribute-construction fact records — so a C# 11
        // generic attribute's definition (N.MarkAttribute<T>) and its construction (N.MarkAttribute<System.Int32>)
        // are both readable, and for the ordinary non-generic case the two names coincide. DECLARED-ONLY is the
        // boundary: attributes are read off this one symbol, so a property's accessor attributes and a method's
        // [return:] attributes are deliberately outside the fact — they hang off different symbols (the accessor
        // method, the return-value pseudo-symbol) that no member subject ever ranges over. Unlike the type side
        // this mints nothing: the member model keeps its by-FQN-string discipline, so there is no ResolveName
        // here and no external node is created for an attribute only a member wears. Sorted ordinal by constructed
        // name so a persisted fragment is byte-stable however Roslyn happened to order the attribute list.
        private IReadOnlyList<FragmentConstruction> AttributeConstructionsOf(ISymbol member)
        {
            if (member.GetAttributes().IsEmpty) return []; // the majority of members, short of the projection

            return AttributeClassesOf(member)
                .Select(c => new FragmentConstruction(DefinitionName(c), ConstructedName(c)))
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();
        }

        // Exactly one of the two names is non-null: a method carries its return type (System.Void for void), a
        // property/field/event its member type. Both are the definition-level FQN in extraction format — the
        // return type read off OriginalDefinition so `Task<int>` matches a `.Returning(typeof(Task<>))` anchor
        // (§4.6), the same construction-erasing normalization ResolveName uses for edge targets (§4.1).
        private (string? ReturnTypeFullName, string? MemberTypeFullName) MemberTypeNames(ISymbol member)
        {
            return member switch
            {
                IMethodSymbol method => (DefinitionName(method.ReturnType), null),
                IPropertySymbol property => (null, DefinitionName(property.Type)),
                IFieldSymbol field => (null, DefinitionName(field.Type)),
                IEventSymbol @event => (null, DefinitionName(@event.Type)),
                _ => (null, null)
            };
        }

        /// <summary>
        ///     The FQN of a referenced type's original definition. When that FQN is not declared by this
        ///     input, it is a referenced-but-not-declared type, so an external record (first mention wins)
        ///     is minted from this compilation's metadata view, deferring cross-input unification to the merge.
        /// </summary>
        private string ResolveName(INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (!_declared.ContainsKey(fqn) && !_externals.ContainsKey(fqn))
            {
                _externals[fqn] = new FragmentExternal(ExtractFacts(definition, fqn), definition.ContainingAssembly?.Name ?? "");
                if (_siblingProjects is not null) SortOneProjectName(definition, fqn);
            }

            return fqn;
        }

        // The one-project view's half of the mint above, entered only when a sibling map was supplied and
        // only on first mention, which is where the mint's own first-wins rule already puts the work. The
        // external is minted either way: the merge discards it wherever a side fragment declares the same
        // name, because externals are recorded after declarations.
        //
        // What keeps an error type out of the sibling set is the screen Declare applies to a type of this
        // compilation, not the absence of an assembly — a type whose metadata never loaded still has a
        // containing assembly with a real name.
        private void SortOneProjectName(INamedTypeSymbol definition, string fqn)
        {
            if (definition.TypeKind == RoslynTypeKind.Error)
            {
                _unvouched.Add(fqn);
                return;
            }

            if (definition.ContainingAssembly?.Name is not { Length: > 0 } assemblyName) return;

            if (_siblingProjects!.IsAmbiguous(assemblyName))
            {
                _unvouched.Add(fqn);
                return;
            }

            if (!TypeKindMapper.IsModelledType(definition)) return;

            // The own-project arm, reachable on a document run alone: a type of this compilation that some
            // OTHER document declares. The sibling map answers the own project as absent — it is nobody's
            // sibling — so without this arm such a type would leave the view entirely and every edge to it
            // would point at an external with no layer, which is the opposite of what this view is for.
            if (_ownProjectName is { } ownProjectName
                && IsInOwnAssembly(definition)
                && !_declared.ContainsKey(fqn))
            {
                _siblingSymbols[fqn] = (definition, ownProjectName, _compilation.Assembly.Name, IsOwnAssembly: true);
                return;
            }

            if (!_siblingProjects.TryGetProject(assemblyName, out string? projectName)) return;

            _siblingSymbols[fqn] = (definition, projectName!, assemblyName, IsOwnAssembly: false);
        }

        // The sibling pass (GRAMMAR §4.1 reads the same facts of a type that is not the subject): one
        // FragmentType per recorded sibling, carrying its scalar facts and its hierarchy and nothing else —
        // no declaration sites, no members, no edges. Closure is first-order by construction: a sibling is
        // declared here only because this compilation named it, and its own hierarchy names are homed as the
        // side fragment's externals rather than promoted to siblings of their own, which would grow the set
        // differently on the two forms a reference can take.
        private List<SideFragment> WalkSiblings()
        {
            var sides = new Dictionary<(string Project, string Assembly), SideFragment>();

            foreach (string fqn in _siblingSymbols.Keys.OrderBy(name => name, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();

                (INamedTypeSymbol definition, string projectName, string assemblyName, bool isOwnAssembly) =
                    _siblingSymbols[fqn];

                List<INamedTypeSymbol> attributeClasses = AttributeClassesOf(definition).ToList();
                var baseChain = new List<INamedTypeSymbol>();
                for (INamedTypeSymbol? current = definition.BaseType; current is not null; current = current.BaseType)
                    baseChain.Add(current); // nearest-first, as the declared-type pass reads it

                // A hierarchy this compilation only half resolved is the truncated-closure case: the chain
                // stops at the error type and AllInterfaces thins, while the whole-solution check reads the
                // sibling from its own compilation and sees all of it. Neither is repairable from here, so
                // the type is dropped rather than judged against a hierarchy that is missing pieces.
                //
                // A type of this compilation's OWN assembly skips the screen, because a truncated closure is
                // not what an error in its hierarchy means: the compilation holds all of it, an unresolved
                // base is the author's own broken code, and the document that declares the type records it
                // whole — error base and all — so dropping it here would have two documents of one project
                // disagree about whether their own type exists.
                if (!isOwnAssembly
                    && (HasErrorType(baseChain) || HasErrorType(definition.Interfaces)
                                                || HasErrorType(definition.AllInterfaces) || HasErrorType(attributeClasses)))
                {
                    _unvouched.Add(fqn);
                    continue;
                }

                SideFragment side = SideFor(sides, projectName, assemblyName);
                side.Declared[fqn] = SiblingType(side, definition, fqn);
            }

            return sides.Values
                .OrderBy(side => side.ProjectName, StringComparer.Ordinal)
                .ThenBy(side => side.AssemblyName, StringComparer.Ordinal)
                .ToList();
        }

        private static bool HasErrorType(IEnumerable<INamedTypeSymbol> symbols)
        {
            return symbols.Any(symbol => symbol.TypeKind == RoslynTypeKind.Error);
        }

        private static SideFragment SideFor(
            Dictionary<(string Project, string Assembly), SideFragment> sides, string projectName, string assemblyName)
        {
            if (sides.TryGetValue((projectName, assemblyName), out SideFragment? side)) return side;

            side = new SideFragment(projectName, assemblyName);
            sides[(projectName, assemblyName)] = side;
            return side;
        }

        // One sibling's type, filled by PopulateHierarchy the way a declared one is — symbols only, never
        // syntax — but with every name homed through SiblingName, and through a DeclaredBuilder given the
        // empty member list this type deliberately has. Its facts are the ones the mint recorded when this
        // compilation first named it, taken unchanged: the mint reads another project's generated fact from
        // the attribute alone and an own type's from its files as well, which is the verdict the document
        // declaring that own type reaches.
        private FragmentType SiblingType(SideFragment side, INamedTypeSymbol definition, string fqn)
        {
            var builder = new DeclaredBuilder(_externals[fqn].Facts, definition, []);
            PopulateHierarchy(builder, symbol => SiblingName(side, symbol));

            return builder.ToFragmentType();
        }

        // ResolveName's non-promoting twin: the same definition-level FQN, homed in the SIDE fragment's own
        // externals. Every hierarchy name of a side-fragment type must have a home — the merge indexes its
        // external facts unguarded for a name nothing declares — and a name THIS compilation declares is
        // deliberately left without one: a file linked into both projects would otherwise put the sibling's
        // assembly beside the own declaration, which the merge reads as one name meaning two types.
        private string SiblingName(SideFragment side, INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (_declared.ContainsKey(fqn) || side.Externals.ContainsKey(fqn)) return fqn;

            // Homed under the own assembly wherever the own assembly declares the name, whatever assembly
            // this symbol was bound from. On a document run a sibling's hierarchy can name a type this
            // compilation declares in ANOTHER document — a file linked into both projects, whose copy
            // arrives here bound to the sibling — and homing that under the sibling's assembly has the merge
            // read one name as two types, minting a shadow node and a note about a split that is not one.
            // On a whole-compilation run every such name is already declared, so the early return above has
            // taken it and this cannot fire.
            string assemblyName = OwnAssemblyDeclares(definition)
                ? _compilation.Assembly.Name
                : definition.ContainingAssembly?.Name ?? "";

            side.Externals[fqn] = new FragmentExternal(ExtractFacts(definition, fqn), assemblyName);

            return fqn;
        }

        // Whether this compilation's own assembly declares a type of the same namespace, name and arity as
        // one it bound from somewhere else. Answered by walking the assembly's namespace tree down the
        // definition's own containing chain and asking for that one name, never by walking the assembly's
        // types: the cost is the depth of a single name, asked once per hierarchy name of a side fragment.
        private bool OwnAssemblyDeclares(INamedTypeSymbol definition)
        {
            INamespaceSymbol? containing = OwnNamespace(definition.ContainingNamespace);
            if (containing is null) return false;

            // Outermost first, so a nested name is looked up through the containers that qualify it.
            var chain = new List<INamedTypeSymbol>();
            for (INamedTypeSymbol? current = definition; current is not null; current = current.ContainingType)
                chain.Add(current);
            chain.Reverse();

            INamedTypeSymbol? found = containing.GetTypeMembers(chain[0].Name, chain[0].Arity)
                .FirstOrDefault();
            for (var i = 1; i < chain.Count && found is not null; i++)
                found = found.GetTypeMembers(chain[i].Name, chain[i].Arity)
                    .FirstOrDefault();

            return found is not null;
        }

        // The own assembly's namespace of the same name as the one given, or null where it declares none —
        // which is the common answer and the cheap one, since a foreign namespace stops the walk at its
        // outermost segment.
        private INamespaceSymbol? OwnNamespace(INamespaceSymbol? namespaceSymbol)
        {
            if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace) return _compilation.Assembly.GlobalNamespace;

            INamespaceSymbol? parent = OwnNamespace(namespaceSymbol.ContainingNamespace);

            return parent?.GetMembers(namespaceSymbol.Name)
                .OfType<INamespaceSymbol>()
                .FirstOrDefault();
        }

        // The own fragment's project references in this view: the sibling PROJECTS among the assemblies the
        // compilation was given, ordinal and distinct. The input's own list cannot answer it, because what a
        // compiler hands an analysis is assemblies and what the model states is project names.
        //
        // That makes this the compiler's referenced closure, not the declared list a whole-solution check
        // reports: the SDK passes the compiler every project output in the closure, and a compilation carries
        // no project file to read the declarations from. Nothing this view feeds reads the list, because every
        // verb and predicate that could is held out of the one-project set (OneProjectDecidability); making
        // one decidable means handing this view the declared list first.
        private IReadOnlyList<string> SiblingProjectReferences()
        {
            if (_siblingProjects is null) return [];

            var projects = new SortedSet<string>(StringComparer.Ordinal);
            foreach (AssemblyIdentity identity in _compilation.ReferencedAssemblyNames)
                if (_siblingProjects.TryGetProject(identity.Name, out string? projectName))
                    projects.Add(projectName!);

            return projects.ToList();
        }

        // Every edge whose far end this view cannot vouch for, gone from the family. Leaving such a type
        // external is not enough: the throw allow-list has no external exemption, and an Except payload
        // reads an external's empty hierarchy as "not exempt", so both would report against a type the
        // whole-solution check judges on facts this compilation never had. A source endpoint is always one
        // of this compilation's own declarations, so only the target is tested. The ordinary extraction
        // vouches for everything, and hands back the list it built.
        private List<TEdge> Vouched<TEdge>(List<TEdge> edges, Func<TEdge, string> targetOf)
        {
            if (_unvouched.Count == 0) return edges;

            return edges.Where(edge => !_unvouched.Contains(targetOf(edge)))
                .ToList();
        }

        private static void AccumulateDeclarationSites(SortedSet<FragmentSite> sites, INamedTypeSymbol definition)
        {
            foreach (SyntaxReference reference in definition.DeclaringSyntaxReferences)
            {
                SyntaxNode node = reference.GetSyntax();
                if (IdentifierOf(node) is { } identifier)
                    sites.Add(FragmentSite.Of(node.SyntaxTree, identifier.Span));
            }
        }

        /// <summary>
        ///     The fragment this compilation's passes built, with <paramref name="projectReferences" /> for
        ///     the one fact the passes cannot state: which projects it references, which the caller knows
        ///     and the compilation does not.
        /// </summary>
        private CodebaseFragment Materialize(CompilationInput input, IReadOnlyList<string> projectReferences)
        {
            // Canonical ordering makes the serialized fragment stable; the merge re-derives global order,
            // so a fragment's internal order never affects the model.
            List<FragmentType> declaredTypes = _declared.Values
                .Select(b => b.ToFragmentType())
                .OrderBy(t => t.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            List<FragmentExternal> externals = _externals.Values
                .OrderBy(e => e.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            List<FragmentEdge> edges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _edgeSites, (src, tgt, sites) => new FragmentEdge(src, tgt, sites.ToList())),
                edge => edge.TargetFullName);

            List<FragmentMemberEdge> memberEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _memberEdges,
                    (src, symbolId, builder) => new FragmentMemberEdge(
                        src, builder.ContainingFullName, builder.MemberName, symbolId, builder.Kind, builder.Sites.ToList())),
                edge => edge.TargetContainingTypeFullName);

            List<FragmentConstructorEdge> constructorEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _constructorEdgeSites, (src, ctor, sites) => new FragmentConstructorEdge(src, ctor, sites.ToList())),
                edge => edge.ConstructedFullName);

            List<FragmentInjectionEdge> injectionEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _injectionEdgeSites, (src, injected, sites) => new FragmentInjectionEdge(src, injected, sites.ToList())),
                edge => edge.InjectedFullName);

            // An edge whose every site is filtered has no entry in either parallel table, which materializes as
            // the empty list — the honest reading, since nothing about that edge is unfiltered. An edge whose
            // every unfiltered site ends in a throw materializes the third list empty for the same reason.
            List<FragmentCatchEdge> catchEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _catchEdgeSites,
                    (src, caught, sites) => new FragmentCatchEdge(
                        src, caught, sites.ToList(),
                        _catchEdgeUnfilteredSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? unfiltered) ? unfiltered.ToList() : [],
                        _catchEdgeSwallowingSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? swallowing) ? swallowing.ToList() : [])),
                edge => edge.CaughtFullName);

            List<FragmentThrowEdge> throwEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _throwEdgeSites, (src, thrown, sites) => new FragmentThrowEdge(src, thrown, sites.ToList())),
                edge => edge.ThrownFullName);

            List<FragmentExposureEdge> exposureEdges = Vouched(
                FragmentSiteSets.OrderedPairs(
                    _exposureEdgeSites, (src, exposed, sites) => new FragmentExposureEdge(src, exposed, sites.ToList())),
                edge => edge.ExposedFullName);

            List<FragmentServiceRegistration> serviceRegistrations = FragmentSiteSets.OrderedRegistrations(
                _registrationSites,
                (lifetime, service, impl, sites) => new FragmentServiceRegistration(lifetime, service, impl, sites.ToList()));

            // The assembly name is read off the IAssemblySymbol rather than the compilation's own option
            // string, because the merge compares it against a FragmentExternal's — which ResolveName
            // reads off ContainingAssembly.Name, the same surface. Taking both operands from Roslyn's one
            // notion of an assembly's name is what makes that comparison meaningful by construction.
            return new CodebaseFragment(
                input.ProjectName, input.TargetFramework, projectReferences, declaredTypes, externals, edges,
                memberEdges, constructorEdges, injectionEdges, catchEdges, throwEdges, exposureEdges,
                serviceRegistrations, input.SolutionMember, input.Compilation.Assembly.Name,
                UndeclaredProjectReferences: input.UndeclaredProjectReferences);
        }

        /// <summary>
        ///     Accumulator for one sibling project-and-assembly's side fragment: the types this compilation
        ///     named, and the homes their hierarchy needs.
        /// </summary>
        /// <remarks>
        ///     Per (project, assembly) pair rather than per project, because a fragment states one assembly
        ///     name and the merge's shadow test compares it: two assemblies of one project folded together
        ///     would attribute half of them to the wrong one and split the name.
        /// </remarks>
        private sealed class SideFragment(string projectName, string assemblyName)
        {
            internal string ProjectName { get; } = projectName;

            internal string AssemblyName { get; } = assemblyName;

            internal Dictionary<string, FragmentType> Declared { get; } = new(StringComparer.Ordinal);

            internal Dictionary<string, FragmentExternal> Externals { get; } = new(StringComparer.Ordinal);

            /// <summary>
            ///     The fragment itself, its two collections in the same ordinal-by-FQN order a compilation's
            ///     own fragment materializes them in.
            /// </summary>
            /// <remarks>
            ///     Every other slot is empty or unknown, and each deliberately. No edge, registration or
            ///     project reference is recorded because none was read — this fragment states another
            ///     project's types, not its behaviour. The target framework and solution membership are
            ///     <see langword="null" /> for the same reason: nothing here read the project file or the
            ///     solution file, and unknown is the answer every project verb passes on, where a guess
            ///     would have the merge note a framework collapse this view never observed.
            /// </remarks>
            internal CodebaseFragment ToFragment()
            {
                List<FragmentType> declaredTypes = Declared.Values
                    .OrderBy(t => t.Facts.FullName, StringComparer.Ordinal)
                    .ToList();
                List<FragmentExternal> externals = Externals.Values
                    .OrderBy(e => e.Facts.FullName, StringComparer.Ordinal)
                    .ToList();

                return new CodebaseFragment(
                    ProjectName, null, [], declaredTypes, externals, [], [], [], [], [], [], [], [],
                    SolutionMember: null, AssemblyName);
            }
        }
    }

    /// <summary>
    ///     Mutable accumulator for one declared type: fixed facts + representative symbol, unioned declaration sites, and
    ///     hierarchy filled in pass 2.
    /// </summary>
    private sealed class DeclaredBuilder(
        TypeFacts facts,
        INamedTypeSymbol symbol,
        IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> members)
    {
        // Private where its siblings are not, because the distinction is real: the owner reads Symbol and
        // Members as it walks, while Facts is only ever handed straight back out by ToFragmentType below.
        private TypeFacts Facts { get; } = facts;

        public INamedTypeSymbol Symbol { get; } = symbol;

        /// <summary>
        ///     The §4.6 inventory, each member paired with the symbol it was read from — pass 1's work, held so
        ///     the exposure pass reads the members and their declaration sites rather than deriving them twice.
        /// </summary>
        public IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> Members { get; } = members;

        public SortedSet<FragmentSite> Sites { get; } = [];
        public string? BaseTypeFullName { get; set; }
        public IReadOnlyList<string> Interfaces { get; set; } = [];
        public IReadOnlyList<string> Attributes { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> AllInterfaces { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> BaseTypeChain { get; set; } = [];
        public IReadOnlyList<FragmentConstruction> AttributeConstructions { get; set; } = [];

        public FragmentType ToFragmentType()
        {
            return new FragmentType(
                Facts,
                Sites.ToList(),
                BaseTypeFullName,
                Interfaces,
                Attributes,
                AllInterfaces,
                BaseTypeChain,
                AttributeConstructions,
                Members.Select(m => m.Member).ToList());
        }
    }

    /// <summary>
    ///     Mutable accumulator for one member-use edge, keyed by (source FQN, member DocumentationCommentId): the
    ///     member's declaring-type FQN, simple name, and kind (all functions of the member symbol) plus its unioned
    ///     use sites. The facts are captured once on first mention; every later site of the same member unions in.
    /// </summary>
    private sealed class MemberEdgeBuilder(string containingFullName, string memberName, MemberKind kind)
    {
        public string ContainingFullName { get; } = containingFullName;
        public string MemberName { get; } = memberName;
        public MemberKind Kind { get; } = kind;
        public SortedSet<FragmentSite> Sites { get; } = [];
    }
}
