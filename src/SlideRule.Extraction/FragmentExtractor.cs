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
///     by running the model's passes over a <em>single</em> compilation.
/// </summary>
/// <remarks>
///     <para>
///         One rule holds across every edge family: sites dedupe by (file, line), the self-edge is dropped,
///         and each endpoint type decomposes to its definition-level FQN the way a type edge does (GRAMMAR
///         §4.1).
///     </para>
///     <para>
///         No fact recorded about another assembly's type reads syntax, on any path: a reference to another
///         project reaches a compilation as its source symbols or as a metadata image, and only the first
///         carries trees, so an external's facts are the same whichever form the reference took.
///     </para>
/// </remarks>
internal static class FragmentExtractor
{
    private const string GeneratedCodeAttributeFullName = "System.CodeDom.Compiler.GeneratedCodeAttribute";

    private static readonly SymbolDisplayFormat FullNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    /// <summary>
    ///     Extracts <paramref name="input" /> into one self-contained fragment, polling
    ///     <paramref name="cancellationToken" /> on entry, once per syntax tree the registration walk visits and
    ///     once per declared type each pass processes, and nowhere below that: a cancelled caller waits at worst
    ///     for one type's worth of work.
    /// </summary>
    /// <remarks>
    ///     The token is per-call state on the <see cref="ExtractState" />, never static, because extraction also
    ///     runs inside the compiler, where every analysis carries its own.
    /// </remarks>
    internal static CodebaseFragment Extract(CompilationInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, siblingProjects: null, cancellationToken)
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
    ///         solution and must never report what that check would not. A type this compilation cannot vouch
    ///         for (an error type, one in an assembly <paramref name="siblingProjects" /> attributes to several
    ///         projects, or a sibling with an error type in its hierarchy, which is what a truncated reference
    ///         closure looks like from here) is left out of the side fragments <em>and</em> off the far end of
    ///         every own edge, so no verb of either polarity judges it. A sibling's own facts and hierarchy are
    ///         all that is recorded: it carries no declaration sites and no members, because the member
    ///         inventory read from an image is not the one read from source, and sites in a file this
    ///         compilation does not hold cannot be pointed at.
    ///     </para>
    ///     <para>
    ///         Must agree with <see cref="FragmentMerger" /> on two points, or the merge mints a shadow node
    ///         and a note about types that are not split at all: a side fragment's assembly name is the one
    ///         its symbols were bound from, and a name this compilation declares itself is left out of every
    ///         side fragment's externals.
    ///     </para>
    ///     <para>
    ///         A null <paramref name="siblingProjects" /> builds no side fragment, and the own fragment is then
    ///         the ordinary extraction's without its registrations and project references.
    ///         <paramref name="cancellationToken" /> is polled at the grains
    ///         <see cref="Extract(CompilationInput,CancellationToken)" /> names, and once per sibling type.
    ///     </para>
    /// </remarks>
    internal static IReadOnlyList<CodebaseFragment> ExtractOneProject(
        CompilationInput input, SiblingProjects? siblingProjects, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, siblingProjects, cancellationToken)
            .RunOneProject(input);
    }

    /// <summary>
    ///     One document of a compilation extracted as the same kind of view <see cref="ExtractOneProject" />
    ///     takes of a whole one: the types <paramref name="tree" /> holds a declaring part of, each carried
    ///     <em>whole</em> (every part's declaration sites and edges, wherever those parts sit) as the own
    ///     fragment, then the side fragments, ordered as there.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The union over a compilation's documents of what this returns is what
    ///         <see cref="ExtractOneProject" /> returns of the compilation: a rule answered per document must say
    ///         what the same rule answers over the project, so a type is extracted whole from any one of its
    ///         files rather than a file's slice of it.
    ///     </para>
    ///     <para>
    ///         The own project is a sibling of itself here, the one structural difference from the
    ///         whole-compilation view: a type of this compilation that <em>another</em> document declares
    ///         arrives as a side fragment under the own project name and the own assembly name, with facts and
    ///         hierarchy but no members and no declaration sites, exactly as another project's type does.
    ///     </para>
    ///     <para>
    ///         A tree declaring no type yields one own fragment with nothing declared in it.
    ///         <paramref name="semanticModel" /> is the model the caller already holds for
    ///         <paramref name="tree" />, or <see langword="null" /> to mint one. A null
    ///         <paramref name="siblingProjects" /> builds no side fragment at all, the own project's included.
    ///         <paramref name="cancellationToken" /> is polled as <see cref="ExtractOneProject" /> polls it.
    ///     </para>
    /// </remarks>
    internal static IReadOnlyList<CodebaseFragment> ExtractDocument(
        CompilationInput input, SyntaxTree tree, SemanticModel? semanticModel, DeclaringTrees declaringTrees,
        SiblingProjects? siblingProjects, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return new ExtractState(input.Compilation, input.GeneratedTrees, siblingProjects, cancellationToken)
            .RunDocument(input, tree, semanticModel, declaringTrees);
    }

    /// <summary>
    ///     Every input extracted, in <em>input</em> order — the ordinal order the merge's first-declarer-wins
    ///     rule reads, so the model is byte-identical however the work is scheduled.
    ///     <paramref name="artifactFacts" /> pairs with <paramref name="inputs" /> by index, and it, or an entry of
    ///     it, is null wherever nothing evaluated the project.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Parallel because extraction dominates a cold run: each input gets its own
    ///         <see cref="ExtractState" />, the statics this class shares are pure, and Roslyn's symbol and
    ///         semantic-model APIs are thread-safe. Each fragment is written to its own slot, because a
    ///         completion-ordered list would silently move which fragment declares a shared FQN.
    ///     </para>
    ///     <para>
    ///         The first inner exception is rethrown with its stack intact, so an extraction failure reaches
    ///         a caller as the exception it threw rather than wrapped in an
    ///         <see cref="AggregateException" />, which the error-rendering path does not unwrap. A cancelled
    ///         run therefore reaches the caller as the <see cref="OperationCanceledException" /> an input's
    ///         extraction threw.
    ///     </para>
    /// </remarks>
    public static CodebaseFragment[] ExtractAll(
        IReadOnlyList<CompilationInput> inputs, IReadOnlyList<ProjectArtifactFacts?>? artifactFacts,
        CancellationToken cancellationToken)
    {
        var fragments = new CodebaseFragment[inputs.Count];

        try
        {
            Parallel.For(0, inputs.Count, i => fragments[i] = WithArtifactFacts(Extract(inputs[i], cancellationToken), ArtifactFactsAt(artifactFacts, i)));
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

    // The declared project references are left out: they are paths and the fragment states names, and the loader
    // has already turned them into the input's reference names, which is where the fragment's list comes from.
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

    // The member inventory filter (GRAMMAR §4.6). IsImplicitlyDeclared drops the backing fields and the record
    // equality, clone and deconstruct surface; the Ordinary screen drops accessors, constructors (static ones
    // included), operators, conversions, finalizers and explicit interface method implementations, and the two
    // ExplicitInterfaceImplementations screens drop the property and event ones. An explicit implementation is
    // interface plumbing (private, its name fixed by the interface), never authored surface.
    private static bool IsInventoried(ISymbol member)
    {
        if (member.IsImplicitlyDeclared) return false;

        // A member source cannot name is not authored surface. Of the members the other screens keep, this drops
        // only the synthesized top-level-statements entry point `<Main>$`, which a project-noun subject would
        // reach; the enclosing Program stays inventoried.
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

    // A partial method declares twice and a field or field-like event declarator has its own site; almost every
    // member declares once, which needs neither the ordering set nor the list it would be copied into.
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

    // A signature type's definition-level endpoints, decomposed as a type edge's are (GRAMMAR §4.1): a
    // constructed generic yields its definition and its type arguments, an array its element type, and type
    // parameters, pointers and dynamic yield nothing.
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
                if (definition.IsModelledType()) yield return definition;
                foreach (ITypeSymbol argument in named.TypeArguments)
                foreach (INamedTypeSymbol endpoint in DecomposeType(argument))
                    yield return endpoint;
                break;
        }
    }

    // The types in one inventoried member's signature positions (GRAMMAR §4.9), a void return skipped. Only
    // inventoried members reach here, which is what keeps constructors, accessors, operators and a delegate's
    // Invoke out without a screen of their own.
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

    // Effective visibility (GRAMMAR §4.9): a public member of an internal type is not surface, so it mints
    // nothing.
    private static bool IsEffectivelyPublicMember(ISymbol member)
    {
        if (member.DeclaredAccessibility != RoslynAccessibility.Public) return false;
        for (INamedTypeSymbol? type = member.ContainingType; type is not null; type = type.ContainingType)
            if (type.DeclaredAccessibility != RoslynAccessibility.Public)
                return false;
        return true;
    }

    // The parameter's name, for a primary constructor as for an ordinary one; null only for a parameter with no
    // source location, which a source-declared constructor does not produce.
    private static FragmentSite? ParameterSite(IParameterSymbol parameter)
    {
        foreach (Location location in parameter.Locations)
            if (location.SourceTree is { } tree)
                return FragmentSite.Of(tree, location.SourceSpan);

        return null;
    }

    // The invoked method-name identifier, so a fluent chain records each .AddX() on its own line rather than on
    // the shared receiver's.
    private static FragmentSite RegistrationSite(InvocationExpressionSyntax invocation)
    {
        SyntaxNode anchor = invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? memberAccess.Name
            : invocation.Expression;
        return FragmentSite.Of(anchor);
    }

    // One compilation's extraction state. Its memos hold symbols and trees, so they live here and die with the
    // compilation, never in a static.
    private sealed class ExtractState
    {
        // What a bare `catch` catches (GRAMMAR §4.8), resolved once per compilation; null where System.Exception
        // does not resolve, and a bare catch then mints nothing.
        private readonly INamedTypeSymbol? _bareCatchType;

        private readonly CancellationToken _cancellationToken;
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSites = new();

        // The swallowing subset of _catchEdgeUnfilteredSites (GRAMMAR §4.8): unfiltered and not ending in a throw,
        // recorded directly for the unfiltered subset's reason.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeSwallowingSites = new();

        // The unfiltered subset of _catchEdgeSites, keyed identically (GRAMMAR §4.8) and recorded directly rather
        // than derived by complement: sites dedupe by (file, line), so a filtered and an unfiltered catch of one
        // type on one line collapse to one site, and a complement would hide the unfiltered clause.
        private readonly Dictionary<(string Src, string Caught), SortedSet<FragmentSite>> _catchEdgeUnfilteredSites = new();
        private readonly Compilation _compilation;

        // RS1024 is suppressed at both symbol-keyed memos: each is built with SymbolEqualityComparer, as the rule
        // asks, and the analyzer that ships with the compiler-API version this project floors on reports them
        // anyway, however the dictionary type is spelled (measured). Each suppression stays two lines wide so a
        // symbol-keyed table built without the comparer still reds.
#pragma warning disable RS1024 // false positive: the comparer below is exactly what the rule asks for
        private readonly Dictionary<ISymbol, string> _constructedNames = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024
        private readonly Dictionary<(string Src, string Ctor), SortedSet<FragmentSite>> _constructorEdgeSites = new();
        private readonly Dictionary<string, DeclaredBuilder> _declared = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Tgt), SortedSet<FragmentSite>> _edgeSites = new();
        private readonly Dictionary<(string Src, string Exposed), SortedSet<FragmentSite>> _exposureEdgeSites = new();
        private readonly Dictionary<string, FragmentExternal> _externals = new(StringComparer.Ordinal);

        // The memo behind this state's FullNameOf, keyed on OriginalDefinition so every construction of one generic
        // shares an entry: the display walk is the most expensive per-reference operation in extraction, and a
        // handful of symbols recur across thousands of sites.
#pragma warning disable RS1024 // false positive, as above
        private readonly Dictionary<ISymbol, string> _fullNames = new(SymbolEqualityComparer.Default);
#pragma warning restore RS1024

        // [System.CodeDom.Compiler.GeneratedCode] as this compilation resolves it, or null where nothing
        // references the type, so nothing can wear it. Resolved once so the generated screen is a symbol compare
        // per attribute rather than a full display walk.
        private readonly INamedTypeSymbol? _generatedCodeAttribute;

        // The trees the workspace reported as generator output, and a memo of each tree's whole verdict
        // (provenance or banner): a tree typically declares many types, so the memo turns a per-type root walk
        // into a per-file one.
        private readonly IReadOnlyCollection<SyntaxTree>? _generatorOutputTrees;
        private readonly Dictionary<SyntaxTree, bool> _generatedTreeVerdicts = new();
        private readonly Dictionary<(string Src, string Injected), SortedSet<FragmentSite>> _injectionEdgeSites = new();
        private readonly Dictionary<(string Src, string MemberSymbolId), MemberEdgeBuilder> _memberEdges = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();

        // Compilation.GetSemanticModel mints a new model on every call (no provider is attached), and the edge
        // pass asks once per declaring syntax reference, so ModelFor keeps one model per tree.
        private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = new();

        // Null on the ordinary extraction, which keeps the sibling apparatus (the recording, the sibling pass, the
        // unvouched set) unreachable from it: that fragment is compared byte for byte against the goldens and
        // against the persisted cache.
        private readonly SiblingProjects? _siblingProjects;

        // Set on a document run alone. Null keeps the own-project arm of SortOneProjectName unreachable from every
        // other view, where an own-assembly type falls through to the sibling map.
        private string? _ownProjectName;

        // Keyed by full name rather than by symbol, the identity the merge unifies on: a side fragment states one
        // type per name, whichever construction was mentioned first.
        private readonly Dictionary<string, (INamedTypeSymbol Definition, string ProjectName, string AssemblyName, bool IsOwnAssembly)>
            _siblingSymbols = new(StringComparer.Ordinal);

        // The names this view cannot vouch for, which are dropped from it rather than reported on. Empty
        // wherever there is no sibling map, so the ordinary extraction materializes the lists it built.
        private readonly HashSet<string> _unvouched = new(StringComparer.Ordinal);
        private readonly Dictionary<(string Src, string Thrown), SortedSet<FragmentSite>> _throwEdgeSites = new();

        public ExtractState(
            Compilation compilation, IReadOnlyCollection<SyntaxTree>? generatorOutputTrees,
            SiblingProjects? siblingProjects, CancellationToken cancellationToken)
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

            WalkRegistrations();

            return Materialize(input, input.ProjectReferences);
        }

        public IReadOnlyList<CodebaseFragment> RunOneProject(CompilationInput input)
        {
            // The registration walk is skipped: every rule that reads a service registration is outside the set
            // a one-project view can decide, and the walk costs a pass over every syntax tree in every build. The
            // fragment's ServiceRegistrations is therefore empty here.
            return RunView(input, DeclaredTypes(_compilation));
        }

        public IReadOnlyList<CodebaseFragment> RunDocument(
            CompilationInput input, SyntaxTree tree, SemanticModel? semanticModel, DeclaringTrees declaringTrees)
        {
            if (semanticModel is not null) _semanticModels[tree] = semanticModel;

            _ownProjectName = input.ProjectName;

            // The registration walk is skipped for RunOneProject's reason and one more: it is a whole-compilation
            // walk, so running it per document would read every tree once per document and record the same facts
            // that many times.
            return RunView(input, declaringTrees.In(tree));
        }

        // The sibling pass runs after the own passes, because its subjects are the names they minted as
        // externals, and before materialization, because what it cannot vouch for decides which own edges survive.
        private IReadOnlyList<CodebaseFragment> RunView(CompilationInput input, IEnumerable<INamedTypeSymbol> ownTypes)
        {
            WalkOwnTypes(ownTypes);

            List<SideFragment> sides = WalkSiblings();

            var fragments = new List<CodebaseFragment> { Materialize(input, SiblingProjectReferences()) };
            foreach (SideFragment side in sides) fragments.Add(side.ToFragment());

            return fragments;
        }

        // The declaring pass ranges over what the caller hands it (every type of the compilation, or the types one
        // document holds a part of), and every later pass ranges over what it declared, every declaring part
        // included.
        private void WalkOwnTypes(IEnumerable<INamedTypeSymbol> types)
        {
            foreach (INamedTypeSymbol symbol in types)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                Declare(symbol);
            }

            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                PopulateHierarchy(builder, ResolveName);
            }

            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkEdges(builder.Symbol);
            }

            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkInjectionEdges(builder.Symbol);
            }

            // Exposure edges are never folded into the declaring pass that builds the inventory they screen: an
            // in-solution endpoint must resolve against the fully populated _declared table, not be minted as an
            // external mid-inventory.
            foreach (DeclaredBuilder builder in _declared.Values)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                WalkExposureEdges(builder);
            }
        }

        private void Declare(INamedTypeSymbol symbol)
        {
            if (!symbol.IsModelledType()) return;

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

            // One walk of the attribute list serves both attribute facts, which differ in what they record and in
            // their order, never in what they range over.
            List<INamedTypeSymbol> attributeClasses = AttributeClassesOf(symbol).ToList();

            builder.Attributes = attributeClasses
                .Select(nameOf)
                .ToList();

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

            // Drops the self-edge before resolving, so a self-edge mints no external. Returns the endpoint's FQN, or
            // null for an empty or self-dropped channel: the decision the catch subsets gate on.
            string? Record(INamedTypeSymbol? endpoint, Dictionary<(string, string), SortedSet<FragmentSite>> map, FragmentSite site)
            {
                if (endpoint is null) return null;

                string fqn = FullNameOf(endpoint);
                if (fqn == srcFqn) return null;

                ResolveName(endpoint);
                map.SitesFor((srcFqn, fqn)).Add(site);
                return fqn;
            }

            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                SyntaxNode root = reference.GetSyntax();
                SemanticModel model = ModelFor(root.SyntaxTree);
                foreach ((WalkChannels channels, FragmentSite site) in ReferenceWalker.Walk(root, model, _bareCatchType))
                {
                    // A member use rides the type channel: its containing type is the channel's target, which Record
                    // has already resolved, so its edge reuses that name.
                    if (Record(channels.Target, _edgeSites, site) is { } tgtFqn && channels.Member is { } member)
                        RecordMemberEdge(srcFqn, tgtFqn, member, site);

                    // Every channel below records independently of the type channel: an explicit `new Foo()` and a
                    // catch clause arrive with no target (a typed catch's type name mints the reference edge on its
                    // own visit), so a shared self-edge `continue` would drop them.
                    Record(channels.Constructed, _constructorEdgeSites, site);

                    // Both subsets are recorded inside Record's decision, so each stays a subset of the one before.
                    if (Record(channels.Caught, _catchEdgeSites, site) is { } caughtFqn)
                    {
                        if (!channels.CaughtHasFilter) _catchEdgeUnfilteredSites.SitesFor((srcFqn, caughtFqn)).Add(site);
                        if (channels is { CaughtHasFilter: false, CaughtEndsInThrow: false })
                            _catchEdgeSwallowingSites.SitesFor((srcFqn, caughtFqn)).Add(site);
                    }

                    // A `throw new X()` mints both the construction edge and the throw edge, at the same site.
                    Record(channels.Thrown, _throwEdgeSites, site);
                }
            }
        }

        // The unresolved: fallback, for a member with no documentation ID (an error or unnamed symbol), mirrors the
        // type facts' own.
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

        // Injection edges (GRAMMAR §4.7) from the declared instance constructors, primary ones included:
        // IsImplicitlyDeclared drops the record copy constructor and the parameterless default, and
        // InstanceConstructors holds no static constructor.
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
                        if (injectedFqn == srcFqn) continue;
                        _injectionEdgeSites.SitesFor((srcFqn, injectedFqn)).Add(site);
                    }
                }
            }
        }

        // Exposure edges (GRAMMAR §4.9) from the signature positions of each effectively public member, sited at the
        // member's own declarations.
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
                    if (exposedFqn == srcFqn) continue;
                    SortedSet<FragmentSite> sites = _exposureEdgeSites.SitesFor((srcFqn, exposedFqn));
                    foreach (FragmentSite site in member.DeclarationSites) sites.Add(site);
                }
            }
        }

        // Registration facts (GRAMMAR §4.7), walked over every tree because a registration needs no per-type
        // attribution (its most common composition root is a top-level-statements Program), and recorded as
        // definition-level names, never resolved to nodes, because registration is many-to-many.
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
            _registrationSites.SitesFor((registration.Lifetime, serviceFqn, implFqn)).Add(site);
        }

        /// <summary>
        ///     The memoized <see cref="FragmentExtractor.FullNameOf" />. Declared here, it hides the outer static
        ///     throughout <see cref="ExtractState" />, so every name this state reads comes back through the memo.
        /// </summary>
        /// <remarks>
        ///     A helper that names types therefore lives on this state: on the outer class, its calls would bind to
        ///     the outer static and walk the display again at every site.
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

        private SemanticModel ModelFor(SyntaxTree tree)
        {
            if (_semanticModels.TryGetValue(tree, out SemanticModel? model)) return model;

            model = _compilation.GetSemanticModel(tree);
            _semanticModels[tree] = model;
            return model;
        }

        // Shape facts follow C# declaration semantics: metadata encodes a static class as abstract and sealed (and
        // source may too), so the `&& !isStatic` mask reports neither. The three facts a symbol can fail to answer
        // are total, because the external mint reaches symbols the compiler never resolved (a partially loaded
        // workspace yields error symbols through base types, interfaces and attribute classes): the kind falls back
        // to Class, the baseline key (GRAMMAR §4.3) to unresolved:{fqn} for a symbol with no documentation ID at all
        // (an unnamed type; an error symbol has Roslyn's "!:" form), and accessibility to Public, which is
        // informational for an unresolved external (not a rule subject, §4.1) and true of a type reached across an
        // assembly boundary. Declared types always map, so the fallbacks never apply to them.
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

        // The generated fact `.Authored()` filters on (GRAMMAR §5.2). A type is generated when
        // [System.CodeDom.Compiler.GeneratedCode] sits on it or on a type containing it, so the nested types a
        // generator emits inside an attributed container ride along, or when every file declaring it is generator
        // output (GeneratedSourceSignals), the only signal a generator that emits no attribute leaves (a compiled
        // Razor view, measured).
        //
        // The file signals are read for this compilation's own types only: every external on every path is minted
        // through here, and another assembly's type is judged by the attribute alone, which both forms of a
        // reference carry.
        //
        // Every declaring file, never any: the generator that completes an author's partial class writes one of its
        // files ([GeneratedRegex] emits the author's own class beside wholly generated ones in a single
        // banner-carrying tree), and any-part would hand the author's class to the generated side.
        //
        // The Length check matters because All() over no files is true, and an unresolved name declares none:
        // Roslyn places it in this compilation's own global namespace, so it passes the own-assembly screen with
        // nothing to test and would otherwise report generated.
        private bool IsGeneratedType(INamedTypeSymbol definition)
        {
            if (HasGeneratedCodeAttribute(definition)) return true;
            if (!IsInOwnAssembly(definition)) return false;

            ImmutableArray<SyntaxReference> declarations = definition.DeclaringSyntaxReferences;
            return declarations.Length > 0 && declarations.All(d => IsGeneratedTree(d.SyntaxTree));
        }

        private bool IsInOwnAssembly(INamedTypeSymbol definition)
        {
            return SymbolEqualityComparer.Default.Equals(definition.ContainingAssembly, _compilation.Assembly);
        }

        // Reading the merged symbol is what decides the partial case for this arm: [GeneratedRegex] puts its
        // attribute on the generated METHOD, so the author's own partial class stays authored.
        private bool HasGeneratedCodeAttribute(INamedTypeSymbol definition)
        {
            if (_generatedCodeAttribute is null) return false;

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

        // The member inventory of one type (GRAMMAR §4.6), ordinal by documentation ID so the fragment and the
        // merged node are deterministic. An enum's fields are its values (reading one stays a member use, §4.5)
        // and a delegate's Invoke and BeginInvoke are runtime plumbing, so neither kind inventories anything.
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
                member.IsAbstract, // C# declaration semantics: an abstract member and a bodiless interface member report true
                member.IsVirtual, // false for an override or abstract member — an override is not itself "virtual"
                member.IsOverride, // the `override` keyword, sealed or not
                member.IsSealed, // a `sealed override` only; `sealed` makes an interface member non-virtual instead
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

        // The declared parameters (GRAMMAR §4.6, §5.6), each type at definition level as the return type is. Read
        // off the declared symbol, so an extension method's `this` and a default-valued parameter count; ref/in/out
        // leave the recorded type alone, a `params T[]` records the array type, and a `T?` records
        // System.Nullable<T>'s definition form.
        private IReadOnlyList<ParameterFacts> ParametersOf(ISymbol member)
        {
            if (member is not IMethodSymbol method) return [];
            if (method.Parameters.IsEmpty) return [];

            return method.Parameters
                .Select(parameter => new ParameterFacts(parameter.Name, DefinitionName(parameter.Type)))
                .ToList();
        }

        // The attributes the member symbol itself carries (GRAMMAR §4.6), as the definition and constructed name pair
        // the type side records, so a generic attribute's definition and construction are both readable. An
        // accessor's attributes and a method's [return:] ones hang off other symbols, which no member subject
        // ranges over, and stay outside the fact. Unlike the type side this mints nothing: member facts stay
        // by-name strings, so there is no ResolveName and no external node for an attribute only a member wears.
        // Ordinal by constructed name, so a persisted fragment is byte-stable however Roslyn ordered the list.
        private IReadOnlyList<FragmentConstruction> AttributeConstructionsOf(ISymbol member)
        {
            if (member.GetAttributes().IsEmpty) return []; // the majority of members, short of the projection

            return AttributeClassesOf(member)
                .Select(c => new FragmentConstruction(DefinitionName(c), ConstructedName(c)))
                .OrderBy(c => c.ConstructedName, StringComparer.Ordinal)
                .ToList();
        }

        // Definition-level, as edge targets are (GRAMMAR §4.1), so `Task<int>` matches a
        // `.Returning(typeof(Task<>))` anchor (§4.6); a void method records System.Void.
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
        ///     The FQN of a referenced type's definition, minting an external from this compilation's metadata
        ///     view on first mention where this input does not declare it; unification is the merge's.
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

        // The one-project view's half of the mint above, on first mention only. The external is minted either way:
        // the merge discards it wherever a side fragment declares the same name, because externals are recorded
        // after declarations.
        //
        // An error type is screened by its kind, not by a missing assembly: a type whose metadata never loaded
        // still has a containing assembly with a real name.
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

            if (!definition.IsModelledType()) return;

            // The own-project arm, reachable on a document run alone, for a type of this compilation that another
            // document declares: the sibling map answers the own project as absent, so without this arm such a type
            // would leave the view and every edge to it would point at an external with no layer.
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

        // The sibling pass (GRAMMAR §4.1). Closure is first-order: a sibling is declared here only because this
        // compilation named it, and its own hierarchy names are homed as the side fragment's externals rather than
        // promoted to siblings, which would grow the set differently on the two forms a reference can take.
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

                // A half-resolved hierarchy is the truncated-closure case: the chain stops at the error type and
                // AllInterfaces thins, while the whole-solution check reads the sibling from its own compilation
                // and sees all of it, so the type is dropped rather than judged on missing pieces. An own-assembly
                // type skips the screen: an unresolved base there is the author's own broken code, which the
                // document declaring the type records whole, and dropping it here would have two documents of one
                // project disagree about whether their own type exists.
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

        // One sibling's type, filled from symbols alone. Its facts are the mint's, taken unchanged: the mint reads
        // another project's generated fact from the attribute alone and an own type's from its files as well,
        // which is the verdict the document declaring that own type reaches.
        private FragmentType SiblingType(SideFragment side, INamedTypeSymbol definition, string fqn)
        {
            var builder = new DeclaredBuilder(_externals[fqn].Facts, definition, []);
            PopulateHierarchy(builder, symbol => SiblingName(side, symbol));

            return builder.ToFragmentType();
        }

        // ResolveName's non-promoting twin, homing the name in the side fragment's own externals. Every hierarchy
        // name of a side-fragment type needs a home, because the merge indexes external facts unguarded for a name
        // nothing declares; a name this compilation declares gets none, or a file linked into both projects would
        // put the sibling's assembly beside the own declaration, which the merge reads as one name meaning two types.
        private string SiblingName(SideFragment side, INamedTypeSymbol symbol)
        {
            INamedTypeSymbol definition = symbol.OriginalDefinition;
            string fqn = FullNameOf(definition);

            if (_declared.ContainsKey(fqn) || side.Externals.ContainsKey(fqn)) return fqn;

            // Homed under the own assembly wherever it declares the name, whatever assembly the symbol was bound
            // from, for the same reason: on a document run a sibling's hierarchy can name a type another document
            // of this compilation declares (a linked file's copy, bound to the sibling). On a whole-compilation run
            // the early return above has already taken every such name.
            string assemblyName = OwnAssemblyDeclares(definition)
                ? _compilation.Assembly.Name
                : definition.ContainingAssembly?.Name ?? "";

            side.Externals[fqn] = new FragmentExternal(ExtractFacts(definition, fqn), assemblyName);

            return fqn;
        }

        // Whether the own assembly declares a type of the same namespace, name and arity, found by walking down the
        // definition's own containing chain rather than the assembly's types, so the cost is one name's depth.
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

        // Null where the own assembly declares no namespace of that name, the common answer and the cheap one: a
        // foreign namespace stops the walk at its outermost segment.
        private INamespaceSymbol? OwnNamespace(INamespaceSymbol? namespaceSymbol)
        {
            if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace) return _compilation.Assembly.GlobalNamespace;

            INamespaceSymbol? parent = OwnNamespace(namespaceSymbol.ContainingNamespace);

            return parent?.GetMembers(namespaceSymbol.Name)
                .OfType<INamespaceSymbol>()
                .FirstOrDefault();
        }

        // The sibling projects among the assemblies the compilation was given, ordinal and distinct: a compiler
        // hands an analysis assemblies, so the input's own list cannot answer. That is the referenced closure (the
        // SDK passes the compiler every project output in it), not the declared list a whole-solution check
        // reports. Nothing this view feeds reads it, because every verb and predicate that could is held out of
        // the one-project set (OneProjectDecidability); making one decidable means handing this view the declared
        // list first.
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
        // of this compilation's own declarations, so only the target is tested.
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
        ///     The fragment the passes built, with <paramref name="projectReferences" /> from the caller: which
        ///     projects it references is the one fact the compilation cannot state.
        /// </summary>
        private CodebaseFragment Materialize(CompilationInput input, IReadOnlyList<string> projectReferences)
        {
            List<FragmentType> declaredTypes = _declared.Values
                .Select(b => b.ToFragmentType())
                .OrderBy(t => t.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            List<FragmentExternal> externals = _externals.Values
                .OrderBy(e => e.Facts.FullName, StringComparer.Ordinal)
                .ToList();

            List<FragmentEdge> edges = Vouched(
                _edgeSites.OrderedPairs((src, tgt, sites) => new FragmentEdge(src, tgt, sites.ToList())),
                edge => edge.TargetFullName);

            List<FragmentMemberEdge> memberEdges = Vouched(
                _memberEdges.OrderedPairs((src, symbolId, builder) => new FragmentMemberEdge(
                    src, builder.ContainingFullName, builder.MemberName, symbolId, builder.Kind, builder.Sites.ToList())),
                edge => edge.TargetContainingTypeFullName);

            List<FragmentConstructorEdge> constructorEdges = Vouched(
                _constructorEdgeSites.OrderedPairs((src, ctor, sites) => new FragmentConstructorEdge(src, ctor, sites.ToList())),
                edge => edge.ConstructedFullName);

            List<FragmentInjectionEdge> injectionEdges = Vouched(
                _injectionEdgeSites.OrderedPairs((src, injected, sites) => new FragmentInjectionEdge(src, injected, sites.ToList())),
                edge => edge.InjectedFullName);

            // An edge with no unfiltered (or no swallowing) site has no entry in that table, and materializes the
            // empty list there.
            List<FragmentCatchEdge> catchEdges = Vouched(
                _catchEdgeSites.OrderedPairs((src, caught, sites) => new FragmentCatchEdge(
                    src, caught, sites.ToList(),
                    _catchEdgeUnfilteredSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? unfiltered) ? unfiltered.ToList() : [],
                    _catchEdgeSwallowingSites.TryGetValue((src, caught), out SortedSet<FragmentSite>? swallowing) ? swallowing.ToList() : [])),
                edge => edge.CaughtFullName);

            List<FragmentThrowEdge> throwEdges = Vouched(
                _throwEdgeSites.OrderedPairs((src, thrown, sites) => new FragmentThrowEdge(src, thrown, sites.ToList())),
                edge => edge.ThrownFullName);

            List<FragmentExposureEdge> exposureEdges = Vouched(
                _exposureEdgeSites.OrderedPairs((src, exposed, sites) => new FragmentExposureEdge(src, exposed, sites.ToList())),
                edge => edge.ExposedFullName);

            List<FragmentServiceRegistration> serviceRegistrations =
                _registrationSites.OrderedRegistrations((lifetime, service, impl, sites) =>
                    new FragmentServiceRegistration(lifetime, service, impl, sites.ToList()));

            // The assembly name is read off the IAssemblySymbol rather than the compilation's own option string,
            // because the merge compares it against a FragmentExternal's, which ResolveName reads off
            // ContainingAssembly.Name: both operands must come from that one surface.
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
            ///     Every other slot is empty or unknown: this fragment states another project's types, not its
            ///     behaviour, and nothing here read the project file or the solution file. A guessed target
            ///     framework would have the merge note a framework collapse this view never observed.
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

    private sealed class DeclaredBuilder(
        TypeFacts facts,
        INamedTypeSymbol symbol,
        IReadOnlyList<(ISymbol Symbol, FragmentMember Member)> members)
    {
        private TypeFacts Facts { get; } = facts;

        public INamedTypeSymbol Symbol { get; } = symbol;

        /// <summary>
        ///     The §4.6 inventory, each member paired with the symbol it was read from, so the exposure pass
        ///     screens the members and their declaration sites rather than deriving them a second time.
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
    ///     Mutable accumulator for one member-use edge. Its facts are functions of the member symbol, captured on
    ///     first mention; every later site of the same member unions in.
    /// </summary>
    private sealed class MemberEdgeBuilder(string containingFullName, string memberName, MemberKind kind)
    {
        public string ContainingFullName { get; } = containingFullName;
        public string MemberName { get; } = memberName;
        public MemberKind Kind { get; } = kind;
        public SortedSet<FragmentSite> Sites { get; } = [];
    }
}
