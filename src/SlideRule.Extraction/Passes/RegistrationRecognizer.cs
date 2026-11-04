using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SlideRule.Extraction.Fragments;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace SlideRule.Extraction.Passes;

/// <summary>
///     Recognizes container-registration calls (GRAMMAR §4.7) inside one compilation's syntax and reports
///     each as a <see cref="RecognizedRegistration" />, and recognizes calls to registration wrappers as
///     <see cref="RecognizedWrapperCall" />. The recognition gate is <b>symbol-first</b> — never name-only: a
///     registration call must resolve (its reduced form for an extension call) to a method whose containing
///     namespace is <c>Microsoft.Extensions.DependencyInjection</c> (or its <c>.Extensions</c> sub-namespace, the
///     <c>TryAdd*</c> family's home), whose name is one of the armed calls, and whose first parameter is
///     <c>IServiceCollection</c>. A <c>ServiceDescriptor</c> counts where it is handed to such a collection, as the
///     argument or through a local assigned once, and so does a subclass declared in this compilation whose
///     constructor passes its own parameters to the base constructor.
/// </summary>
/// <remarks>
///     A wrapper is a method the solution declares, in any namespace, whose first parameter is
///     <c>IServiceCollection</c>, or a builder exposing one as a readable instance property, and which has type
///     parameters or a <c>ServiceLifetime</c> parameter (<see cref="IsWrapper" />). Inside one, passed as
///     <c>wrapper</c>, a type parameter or a lifetime parameter of that wrapper is readable and comes back as
///     itself, for the template the merge expands at each call. Every other registration form is the §4.7
///     honesty boundary and yields nothing: <c>Configure</c>/<c>AddOptions</c>, keyed-service overloads,
///     assembly-scanning registrars, and framework defaults. An unreadable registration is absent from the model
///     rather than guessed at, which is why a non-literal lifetime argument declines the call instead of assuming
///     one.
/// </remarks>
internal sealed class RegistrationRecognizer
{
    private const string DiNamespace = "Microsoft.Extensions.DependencyInjection";
    private const string DiExtensionsNamespace = "Microsoft.Extensions.DependencyInjection.Extensions";
    private const string HostedServiceCall = "AddHostedService";
    private const string DbContextCall = "AddDbContext";
    private const string DbContextPoolCall = "AddDbContextPool";
    private const string HttpClientCall = "AddHttpClient";
    private const string DescribeCall = "Describe";

    // The Add/TryAdd trio, dispatched as one arm. Declared before ArmedCalls, which is seeded from it.
    private static readonly HashSet<string> AddFamilyCalls = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient"
    };

    // Every armed name, and the ONE list of them: the syntactic pre-filter screens against this set and
    // Recognize dispatches on the very members it was built from, so the cheap gate and the real gate cannot
    // come to disagree about what is armed. A name the pre-filter drops could never have reached an arm.
    private static readonly HashSet<string> ArmedCalls =
        new(AddFamilyCalls, StringComparer.Ordinal) { HostedServiceCall, DbContextCall, DbContextPoolCall, HttpClientCall };

    // The ServiceDescriptor factories, screened and dispatched like ArmedCalls. The keyed factories carry other
    // names, so none of them reaches the arm.
    private static readonly HashSet<string> DescriptorCalls = new(StringComparer.Ordinal)
    {
        DescribeCall, "Singleton", "Scoped", "Transient"
    };

    // The collection calls a descriptor counts under: ICollection's Add, IList's Insert, and the MEDI extensions.
    private static readonly HashSet<string> DescriptorSinks = new(StringComparer.Ordinal)
    {
        "Add", "Insert", "TryAdd", "TryAddEnumerable", "Replace"
    };

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _hostedService;

    // The names the compilation's using aliases give ServiceLifetime, which a lifetime argument may be spelled
    // through; the call-side pre-filter reads them as it reads the type's own name.
    private readonly HashSet<string> _lifetimeAliases;
    private readonly Func<SyntaxTree, SemanticModel> _modelFor;
    private readonly INamedTypeSymbol? _serviceCollection;
    private readonly INamedTypeSymbol? _serviceDescriptor;
    private readonly INamedTypeSymbol? _serviceLifetime;
    private readonly IReadOnlyCollection<string> _solutionAssemblies;
    private readonly INamedTypeSymbol? _systemType;

    /// <summary>
    ///     A recognizer for <paramref name="compilation" />. <paramref name="modelFor" /> gives the semantic model of
    ///     one of its trees, for a descriptor subclass's constructor declared in a file other than the creation's.
    ///     <paramref name="solutionAssemblies" /> names the assemblies the solution's projects produce.
    /// </summary>
    public RegistrationRecognizer(
        Compilation compilation, Func<SyntaxTree, SemanticModel> modelFor, IReadOnlyCollection<string>? solutionAssemblies)
    {
        _compilation = compilation;
        _modelFor = modelFor;
        _solutionAssemblies = solutionAssemblies ?? [];
        _serviceCollection = compilation.GetTypeByMetadataName($"{DiNamespace}.IServiceCollection");
        _serviceDescriptor = compilation.GetTypeByMetadataName($"{DiNamespace}.ServiceDescriptor");
        _serviceLifetime = compilation.GetTypeByMetadataName($"{DiNamespace}.ServiceLifetime");
        _hostedService = compilation.GetTypeByMetadataName("Microsoft.Extensions.Hosting.IHostedService");
        _systemType = compilation.GetTypeByMetadataName("System.Type");
        _lifetimeAliases = _serviceCollection is null ? [] : LifetimeAliases(compilation);
    }

    // Read from the using directives alone, which sit at the top of a file or a namespace and never inside a member. A
    // newer compiler reports no Name for an alias of a type that is not a name (`using Pair = (int, int);`), though
    // the property is declared non-null in the API this project builds against.
    private static HashSet<string> LifetimeAliases(Compilation compilation)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<UsingDirectiveSyntax> directives = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot()
                .DescendantNodes(node => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
            .OfType<UsingDirectiveSyntax>();
        foreach (UsingDirectiveSyntax directive in directives)
            if (directive is { Alias.Name: var alias, Name: { } target } && target.GetLastToken().ValueText == "ServiceLifetime")
                aliases.Add(alias.Identifier.ValueText);

        return aliases;
    }

    /// <summary>
    ///     False when <c>Microsoft.Extensions.DependencyInjection.IServiceCollection</c> is not referenced by
    ///     the compilation, so the gate can never pass.
    /// </summary>
    public bool IsActive => _serviceCollection is not null;

    /// <summary>
    ///     The key a wrapper's template and every call to it share: its definition's documentation ID and its
    ///     assembly's name, which read the same from source, from a compilation reference and from metadata.
    /// </summary>
    public static string? WrapperKey(IMethodSymbol wrapper)
    {
        IMethodSymbol definition = wrapper.OriginalDefinition;
        return definition.GetDocumentationCommentId() is { } id ? $"{definition.ContainingAssembly?.Name}:{id}" : null;
    }

    /// <summary>
    ///     Whether <paramref name="method" />, unreduced, is a registration wrapper: the one predicate both the
    ///     declaring side's template and the calling side's call record apply, so the two cannot disagree.
    /// </summary>
    public bool IsWrapper(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary || method.Parameters.Length == 0) return false;

        // Ahead of ReachesServices, which reads every member of the first parameter's type and its bases: the
        // declaring side asks this of every method the solution declares.
        if (method.TypeParameters.Length == 0 && !method.Parameters.Any(parameter => IsServiceLifetime(parameter.Type))) return false;
        if (!ReachesServices(method.Parameters[0].Type)) return false;

        // The framework's own methods in these namespaces (Configure<T>, AddKeyedSingleton) have the shape too, so
        // only one the solution declares is a wrapper there.
        return !IsDiNamespace(method) || IsSolutionDeclared(method);
    }

    // Source symbols come only from the solution's compilations. One read from metadata is the solution's when a
    // project of the solution produces its assembly, as when a project references a sibling's built output by path.
    private bool IsSolutionDeclared(IMethodSymbol method)
    {
        if (!method.OriginalDefinition.DeclaringSyntaxReferences.IsEmpty) return true;

        return method.ContainingAssembly is { Name: var assembly } && _solutionAssemblies.Contains(assembly);
    }

    /// <summary>Whether <paramref name="type" /> derives from <c>ServiceDescriptor</c>, directly or not.</summary>
    public bool IsDescriptorSubclass(INamedTypeSymbol type)
    {
        return BaseTypesAndSelf(type)
            .Skip(1)
            .Any(baseType => SymbolEqualityComparer.Default.Equals(baseType.OriginalDefinition, _serviceDescriptor));
    }

    // The collection itself, or a builder exposing it as a readable instance property, declared on the type, a base
    // class or an interface it implements.
    private bool ReachesServices(ITypeSymbol type)
    {
        if (IsServiceCollection(type)) return true;

        IEnumerable<ITypeSymbol> owners = BaseTypesAndSelf(type).Concat(type.AllInterfaces);
        return owners
            .SelectMany(owner => owner.GetMembers())
            .OfType<IPropertySymbol>()
            .Any(property => property is { IsStatic: false, GetMethod: not null } && IsServiceCollection(property.Type));
    }

    private static IEnumerable<ITypeSymbol> BaseTypesAndSelf(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    private bool IsServiceCollection(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _serviceCollection);
    }

    /// <summary>
    ///     Reports the registration an invocation makes, or null when it is not a recognized call or its types or
    ///     lifetime cannot be read.
    /// </summary>
    public RecognizedRegistration? Recognize(InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol? wrapper)
    {
        // Syntactic pre-filter, ahead of the symbol gate: GetSymbolInfo binds the enclosing member body, and
        // the registration walk offers it every invocation in the solution to find the few dozen in a
        // composition root. C# gives an invoked method the same identifier text in every spelling (extension,
        // static, generic, conditional access), so a readable name that is not armed can never reach an arm —
        // while a shape whose name cannot be read (an invoked delegate-valued expression, say) falls through to
        // the gate below, which decides it.
        if (InvokedName(invocation) is { } invoked && !ArmedCalls.Contains(invoked) && !DescriptorCalls.Contains(invoked))
            return null;

        if (model.GetSymbolInfo(invocation).BestCandidate() is not IMethodSymbol method) return null;

        if (IsDescriptorFactory(method))
            return AddedToServices(invocation, model) ? RecognizeDescriptorFactory(method, invocation, model, wrapper) : null;

        // The unreduced signature carries the IServiceCollection `this` parameter and the true namespace/name
        // (an extension call resolves to the reduced method, whose first parameter is the first value arg).
        IMethodSymbol signature = method.ReducedFrom ?? method;
        if (!PassesGate(signature)) return null;

        if (AddFamilyCalls.Contains(signature.Name))
        {
            IReadOnlyList<ExpressionSyntax?> arguments = ArgumentsByParameter(invocation.ArgumentList.Arguments, method.Parameters);
            return RecognizeAddFamily(Literal(LifetimeFromName(signature.Name)), method, arguments, model, wrapper);
        }

        return signature.Name switch
        {
            HostedServiceCall => RecognizeHostedService(method, wrapper),
            DbContextCall or DbContextPoolCall => RecognizeDbContext(method, invocation, model, wrapper),
            HttpClientCall => RecognizeHttpClient(method, wrapper),
            _ => null
        };
    }

    /// <summary>
    ///     Reports the registration a <c>new ServiceDescriptor(…)</c> handed to a service collection makes, or null
    ///     for any other creation and for a keyed descriptor. <paramref name="descriptorSubclasses" /> names the
    ///     classes this compilation declares that derive from <c>ServiceDescriptor</c>.
    /// </summary>
    public RecognizedRegistration? Recognize(
        BaseObjectCreationExpressionSyntax creation, SemanticModel model, IMethodSymbol? wrapper, IReadOnlyCollection<string> descriptorSubclasses)
    {
        // The pre-filter: a creation that spells a type, or a target-typed one held in a local that declares it,
        // names a descriptor, and a target-typed one with neither can only reach a descriptor sink as one of its
        // arguments.
        if (SpelledType(creation) is { } type && !NamesDescriptor(type, descriptorSubclasses)) return null;
        if (creation.ArgumentList is not { } argumentList || !AddedToServices(creation, model)) return null;

        if (model.GetSymbolInfo(creation).BestCandidate() is not IMethodSymbol constructor) return null;
        if (!SymbolEqualityComparer.Default.Equals(constructor.ContainingType, _serviceDescriptor)
            && !IsDescriptorSubclass(constructor.ContainingType))
            return null;

        IReadOnlyList<ExpressionSyntax?> arguments = ArgumentsByParameter(argumentList.Arguments, constructor.Parameters);
        if (ForwardedToDescriptor(constructor, arguments) is not ({ } descriptorConstructor, { } descriptorArguments)) return null;
        if (descriptorConstructor.Parameters.Any(parameter => parameter.Name == "serviceKey")) return null;

        // The constructor with no lifetime parameter is the instance form, which the type registers as a singleton.
        FragmentRegistrationLifetime? lifetime = LifetimeArgument(descriptorConstructor, descriptorArguments, model, wrapper, Lifetime.Singleton);
        return lifetime is { } readable ? RecognizeTypeofAddFamily(readable, descriptorConstructor, descriptorArguments, model, wrapper) : null;
    }

    /// <summary>
    ///     Reports a call to a registration wrapper, or null for anything else. <paramref name="localWrappers" />
    ///     names the wrappers this compilation declares, which a call may reach without a generic name.
    /// </summary>
    public RecognizedWrapperCall? RecognizeWrapperCall(
        InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol? wrapper, IReadOnlyCollection<string> localWrappers)
    {
        // The pre-filter: a wrapper declared in another project is reached by a generic name or a lifetime
        // argument, since its name is unknown here; one declared here is reached by its name.
        if (!MayCallWrapper(invocation, wrapper, localWrappers)) return null;

        if (model.GetSymbolInfo(invocation).BestCandidate() is not IMethodSymbol method) return null;

        IMethodSymbol signature = method.ReducedFrom ?? method;
        if (!IsWrapper(signature) || WrapperKey(signature) is not { } key) return null;

        // A receiver of a type that is not generic over the wrapper's own type parameters infers nothing during
        // reduction, so the reduced call keeps every type argument the definition declares. A count that differs
        // is a receiver that inferred some, such as a builder passed as a type parameter, and records nothing.
        if (method.TypeArguments.Length != signature.TypeParameters.Length) return null;

        ImmutableArray<ITypeSymbol?> typeArguments = method.TypeArguments
            .Select(argument => Readable(argument, wrapper))
            .ToImmutableArray();

        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        ImmutableArray<(string, FragmentRegistrationLifetime?)>.Builder lifetimes = ImmutableArray.CreateBuilder<(string, FragmentRegistrationLifetime?)>();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!IsServiceLifetime(parameters[i].Type)) continue;

            ExpressionSyntax? argument = ArgumentForParameter(invocation.ArgumentList.Arguments, parameters, i);
            if (argument is not null) lifetimes.Add((parameters[i].Name, LifetimeOf(argument, model, wrapper)));
        }

        return new RecognizedWrapperCall(key, typeArguments, lifetimes.ToImmutable());
    }

    /// <summary>
    ///     The declared default of each of <paramref name="wrapper" />'s <c>ServiceLifetime</c> parameters that has
    ///     one, read from the defining declaration of a partial method, which is where C# takes defaults from.
    /// </summary>
    public IEnumerable<(string Parameter, Lifetime Lifetime)> LifetimeDefaults(IMethodSymbol wrapper)
    {
        IMethodSymbol declaring = wrapper.PartialDefinitionPart ?? wrapper;
        foreach (IParameterSymbol parameter in declaring.Parameters)
            if (IsServiceLifetime(parameter.Type) && parameter is { HasExplicitDefaultValue: true, ExplicitDefaultValue: int value })
                yield return (parameter.Name, LifetimeFromValue(value));
    }

    // The invoked method's identifier text, for the shapes an invocation can spell one in: `x.M(…)`,
    // `x?.M(…)`, and a bare `M(…)` (including their generic forms, whose GenericNameSyntax carries the same
    // identifier). Null for anything else — an invoked expression, a parenthesized receiver chain — which is
    // the signal to decide semantically rather than guess.
    private static string? InvokedName(InvocationExpressionSyntax invocation)
    {
        return InvokedSimpleName(invocation)?.Identifier.ValueText;
    }

    private static SimpleNameSyntax? InvokedSimpleName(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            SimpleNameSyntax simple => simple,
            _ => null
        };
    }

    private bool MayCallWrapper(InvocationExpressionSyntax invocation, IMethodSymbol? wrapper, IReadOnlyCollection<string> localWrappers)
    {
        if (InvokedSimpleName(invocation) is not { } name) return false;
        if (name is GenericNameSyntax || localWrappers.Contains(name.Identifier.ValueText)) return true;

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            switch (argument.Expression)
            {
                case MemberAccessExpressionSyntax { Expression: var owner } when NamesLifetime(owner.GetLastToken().ValueText):
                    return true;
                case IdentifierNameSyntax identifier when wrapper is not null && IsLifetimeParameterOf(wrapper, identifier.Identifier.ValueText):
                    return true;
            }

        return false;
    }

    private bool NamesLifetime(string owner)
    {
        return owner.EndsWith("ServiceLifetime", StringComparison.Ordinal) || _lifetimeAliases.Contains(owner);
    }

    private bool IsLifetimeParameterOf(IMethodSymbol wrapper, string name)
    {
        return wrapper.Parameters.Any(parameter => parameter.Name == name && IsServiceLifetime(parameter.Type));
    }

    // The name is checked by the caller's switch, so a MEDI-namespace method with the wrong name (Configure,
    // AddKeyedSingleton, TryAddEnumerable) passes here yet falls through to the no-arm default — the §4.7
    // honesty boundary.
    private bool PassesGate(IMethodSymbol signature)
    {
        if (signature.Parameters.Length == 0) return false;
        if (!IsDiNamespace(signature)) return false;

        return IsServiceCollection(signature.Parameters[0].Type);
    }

    private static bool IsDiNamespace(IMethodSymbol method)
    {
        string ns = method.ContainingNamespace.ToDisplayString();
        return ns is DiNamespace or DiExtensionsNamespace;
    }

    private bool IsDescriptorFactory(IMethodSymbol method)
    {
        return method.IsStatic
               && DescriptorCalls.Contains(method.Name)
               && SymbolEqualityComparer.Default.Equals(method.ContainingType, _serviceDescriptor);
    }

    // A descriptor registers only once a collection takes it: as a sink's argument, or held in a local that is never
    // written again and that a sink takes. One built and handed to anything else (removed, compared, stored)
    // records nothing, however many times it is read.
    private bool AddedToServices(ExpressionSyntax descriptor, SemanticModel model)
    {
        if (descriptor.Parent is ArgumentSyntax argument) return IsSinkArgument(argument, model);

        return HeldIn(descriptor) is ({ } statement, { } declarator) && HeldLocalAddedToServices(statement, declarator, model);
    }

    private bool IsSinkArgument(ArgumentSyntax argument, SemanticModel model)
    {
        if (argument.Parent is not ArgumentListSyntax { Parent: InvocationExpressionSyntax sink }) return false;
        if (sink.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name } memberAccess) return false;
        if (!DescriptorSinks.Contains(name)) return false;

        ITypeSymbol? receiver = model.GetTypeInfo(memberAccess.Expression).Type;
        return receiver is not null
               && (IsServiceCollection(receiver)
                   || receiver.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract, _serviceCollection)));
    }

    // The plain local declaration whose initializer is value: not a using declaration and not a ref local.
    private static (LocalDeclarationStatementSyntax Statement, VariableDeclaratorSyntax Declarator)? HeldIn(ExpressionSyntax value)
    {
        if (value.Parent is not EqualsValueClauseSyntax
            {
                Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax statement } declaration } declarator
            })
            return null;

        return statement.UsingKeyword.IsKind(SyntaxKind.None) && declaration.Type is not RefTypeSyntax ? (statement, declarator) : null;
    }

    // Every use of the local is bound, within the block, switch or top-level statements its scope covers, so a
    // like-named local or field elsewhere neither adds it nor counts as a write.
    private bool HeldLocalAddedToServices(LocalDeclarationStatementSyntax statement, VariableDeclaratorSyntax declarator, SemanticModel model)
    {
        if (model.GetDeclaredSymbol(declarator) is not ILocalSymbol local) return false;
        if (statement.Ancestors().FirstOrDefault(node => node is BlockSyntax or SwitchStatementSyntax or CompilationUnitSyntax) is not { } scope)
            return false;

        var added = false;
        foreach (IdentifierNameSyntax use in scope.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (use.Identifier.ValueText != local.Name) continue;
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(use).Symbol, local)) continue;
            if (IsWrite(use)) return false;

            added |= use.Parent is ArgumentSyntax argument && IsSinkArgument(argument, model);
        }

        return added;
    }

    // Assigned (directly or as a deconstruction target), passed by ref or out, aliased by a ref, or incremented.
    private static bool IsWrite(IdentifierNameSyntax use)
    {
        ExpressionSyntax target = use;
        SyntaxNode? parent = use.Parent;
        while (parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple })
        {
            target = tuple;
            parent = tuple.Parent;
        }

        return parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == target,
            ArgumentSyntax argument => argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword),
            RefExpressionSyntax => true,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
            PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
            _ => false
        };
    }

    // The type a creation spells, or for a target-typed one the type its local declares; null when neither is written.
    private static TypeSyntax? SpelledType(BaseObjectCreationExpressionSyntax creation)
    {
        return creation is ObjectCreationExpressionSyntax { Type: var type } ? type : HeldIn(creation)?.Statement.Declaration.Type;
    }

    private static bool NamesDescriptor(TypeSyntax type, IReadOnlyCollection<string> descriptorSubclasses)
    {
        string? name = RightmostName(type);
        return name is null
            ? type.ToString().EndsWith("ServiceDescriptor", StringComparison.Ordinal)
            : name.EndsWith("ServiceDescriptor", StringComparison.Ordinal) || descriptorSubclasses.Contains(name);
    }

    private static string? RightmostName(TypeSyntax type)
    {
        return type switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            NullableTypeSyntax nullable => RightmostName(nullable.ElementType),
            _ => null
        };
    }

    // A subclass's constructor read as the ServiceDescriptor constructor it forwards to, each base argument bound to
    // the creation's argument for the parameter it names. The constructor is bound with its own tree's model, since
    // it may be declared in another file. Null for a constructor outside this compilation's syntax, one without a
    // base initializer, one that passes a base argument other than a bare parameter of its own, and a creation that
    // leaves a forwarded parameter to its default, whose value lives in the constructor's tree and is not read.
    private (IMethodSymbol Constructor, IReadOnlyList<ExpressionSyntax?> Arguments)? ForwardedToDescriptor(
        IMethodSymbol constructor, IReadOnlyList<ExpressionSyntax?> arguments)
    {
        while (!SymbolEqualityComparer.Default.Equals(constructor.ContainingType.OriginalDefinition, _serviceDescriptor))
        {
            IMethodSymbol definition = constructor.OriginalDefinition;
            if (definition.DeclaringSyntaxReferences.Length != 1) return null;

            SyntaxReference reference = definition.DeclaringSyntaxReferences[0];
            if (!_compilation.ContainsSyntaxTree(reference.SyntaxTree)) return null;
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax { Initializer: { } initializer }
                || !initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.BaseKeyword))
                return null;

            SemanticModel declarationModel = _modelFor(reference.SyntaxTree);
            if (declarationModel.GetSymbolInfo(initializer).BestCandidate() is not IMethodSymbol baseConstructor) return null;

            IReadOnlyList<ExpressionSyntax?> forwarded = ArgumentsByParameter(initializer.ArgumentList.Arguments, baseConstructor.Parameters);
            var mapped = new ExpressionSyntax?[forwarded.Count];
            for (var i = 0; i < forwarded.Count; i++)
            {
                if (forwarded[i] is null) continue;
                if (forwarded[i] is not IdentifierNameSyntax name
                    || declarationModel.GetSymbolInfo(name).Symbol is not IParameterSymbol parameter
                    || !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, definition)
                    || arguments[parameter.Ordinal] is not { } argument)
                    return null;

                mapped[i] = argument;
            }

            constructor = baseConstructor;
            arguments = mapped;
        }

        return (constructor, arguments);
    }

    /// <summary>
    ///     The (service, implementation) pair a generic registration call's type arguments name, or
    ///     <see langword="null" /> when they name none this recognizer can use.
    /// </summary>
    /// <remarks>
    ///     The generic arms decode the same shape and must keep deciding it identically, so the split lives here
    ///     once, with <paramref name="selfWhenSingle" /> the axis they differ on. An unusable second type argument
    ///     records "no distinct implementation"; an unusable first one (a type parameter of anything but the
    ///     wrapper, an array, <c>dynamic</c>, a name the compiler cannot find at all) makes the whole call
    ///     unreadable, which is the §4.7 honesty boundary: absent from the model rather than guessed at.
    /// </remarks>
    private static (ITypeSymbol Service, ITypeSymbol? Implementation)? ServiceAndImplementation(
        ImmutableArray<ITypeSymbol> typeArguments, bool selfWhenSingle, IMethodSymbol? wrapper)
    {
        if (typeArguments.Length is not (1 or 2)) return null;
        if (Readable(typeArguments[0], wrapper) is not { } service) return null;

        if (typeArguments.Length == 2) return (service, Readable(typeArguments[1], wrapper));

        return selfWhenSingle ? (service, service) : (service, null);
    }

    // A named type through the extraction's candidate policy, or a type parameter of the wrapper being read.
    private static ITypeSymbol? Readable(ITypeSymbol type, IMethodSymbol? wrapper)
    {
        return (ITypeSymbol?)type.BestNamedCandidate() ?? WrapperTypeParameter(type, wrapper);
    }

    private static ITypeParameterSymbol? WrapperTypeParameter(ISymbol? symbol, IMethodSymbol? wrapper)
    {
        return symbol is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter
               && wrapper is not null
               && SymbolEqualityComparer.Default.Equals(parameter.DeclaringMethod?.OriginalDefinition, wrapper.OriginalDefinition)
            ? parameter
            : null;
    }

    private RecognizedRegistration? RecognizeAddFamily(
        FragmentRegistrationLifetime lifetime, IMethodSymbol method, IReadOnlyList<ExpressionSyntax?> arguments, SemanticModel model,
        IMethodSymbol? wrapper)
    {
        // AddSingleton<IFoo, Foo>() → (service, impl); AddSingleton<Foo>() → self only when the call is
        // receiver-only, since a factory/instance overload names no implementation type.
        bool receiverOnly = method.Parameters.Length == 0;
        if (ServiceAndImplementation(method.TypeArguments, receiverOnly, wrapper) is { } decoded)
            return new RecognizedRegistration(lifetime, decoded.Service, decoded.Implementation);

        // No usable type-args → the typeof overloads, mirroring the split by Type-parameter count.
        return RecognizeTypeofAddFamily(lifetime, method, arguments, model, wrapper);
    }

    // The arguments are the method's, aligned with its parameters.
    private RecognizedRegistration? RecognizeTypeofAddFamily(
        FragmentRegistrationLifetime lifetime, IMethodSymbol method, IReadOnlyList<ExpressionSyntax?> arguments, SemanticModel model,
        IMethodSymbol? wrapper)
    {
        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        if (parameters.Length == 0 || !IsSystemType(parameters[0].Type)) return null;

        if (TypeFromTypeofArgument(arguments[0], model, wrapper) is not { } service) return null;

        // Add(typeof(IFoo), typeof(Foo)) → (service, impl); the impl slot may be a non-typeof Type (skipped).
        if (parameters.Length >= 2 && IsSystemType(parameters[1].Type))
        {
            ITypeSymbol? impl = TypeFromTypeofArgument(arguments[1], model, wrapper);
            return new RecognizedRegistration(lifetime, service, impl);
        }

        // Add(typeof(Foo)) → (Foo, Foo); Add(typeof(Foo), factory/instance) → (Foo, null).
        return parameters.Length == 1
            ? new RecognizedRegistration(lifetime, service, service)
            : new RecognizedRegistration(lifetime, service, null);
    }

    // ServiceDescriptor.Singleton/Scoped/Transient mirror the Add family's overloads without the collection, so
    // they decode through the same arm; Describe takes its lifetime as an argument, which must be readable.
    private RecognizedRegistration? RecognizeDescriptorFactory(
        IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol? wrapper)
    {
        IReadOnlyList<ExpressionSyntax?> arguments = ArgumentsByParameter(invocation.ArgumentList.Arguments, method.Parameters);
        FragmentRegistrationLifetime? lifetime = method.Name == DescribeCall
            ? LifetimeArgument(method, arguments, model, wrapper, absent: null)
            : Literal(LifetimeFromName(method.Name));

        return lifetime is { } readable ? RecognizeAddFamily(readable, method, arguments, model, wrapper) : null;
    }

    private RecognizedRegistration? RecognizeHostedService(IMethodSymbol method, IMethodSymbol? wrapper)
    {
        if (method.TypeArguments.Length != 1) return null;
        if (Readable(method.TypeArguments[0], wrapper) is not { } impl) return null;

        // Service is the synthesized IHostedService (no syntactic mention); implementation-only fallback (T
        // in the service slot, impl null) when the framework type is not resolvable in this compilation.
        return _hostedService is not null
            ? new RecognizedRegistration(Literal(Lifetime.Singleton), _hostedService, impl)
            : new RecognizedRegistration(Literal(Lifetime.Singleton), impl, null);
    }

    private static RecognizedRegistration? RecognizeHttpClient(IMethodSymbol method, IMethodSymbol? wrapper)
    {
        // AddHttpClient<TClient, TImpl>() → (TClient, TImpl); AddHttpClient<TClient>() → (TClient, TClient);
        // the named-only string form (no type-args) registers no user type → nothing.
        if (ServiceAndImplementation(method.TypeArguments, selfWhenSingle: true, wrapper) is not { } decoded) return null;

        return new RecognizedRegistration(Literal(Lifetime.Transient), decoded.Service, decoded.Implementation);
    }

    private RecognizedRegistration? RecognizeDbContext(
        IMethodSymbol method, InvocationExpressionSyntax invocation, SemanticModel model, IMethodSymbol? wrapper)
    {
        // AddDbContext<TContext>() → (TContext, TContext); AddDbContext<TContext, TImpl>() → (TContext, TImpl).
        if (ServiceAndImplementation(method.TypeArguments, selfWhenSingle: true, wrapper) is not { } decoded) return null;

        IReadOnlyList<ExpressionSyntax?> arguments = ArgumentsByParameter(invocation.ArgumentList.Arguments, method.Parameters);
        if (LifetimeArgument(method, arguments, model, wrapper, Lifetime.Scoped) is not { } lifetime) return null;

        return new RecognizedRegistration(lifetime, decoded.Service, decoded.Implementation);
    }

    // The lifetime bound to the first ServiceLifetime parameter: absent (its default used, or no such parameter)
    // → absent; a literal ServiceLifetime.X → honored; the wrapper's own lifetime parameter → that parameter;
    // anything else → null, present but unreadable, so the caller never guesses and skips the call.
    private FragmentRegistrationLifetime? LifetimeArgument(
        IMethodSymbol method, IReadOnlyList<ExpressionSyntax?> arguments, SemanticModel model, IMethodSymbol? wrapper,
        Lifetime? absent)
    {
        FragmentRegistrationLifetime? whenAbsent = absent is { } fallback ? Literal(fallback) : null;
        if (_serviceLifetime is null) return whenAbsent;

        ImmutableArray<IParameterSymbol> parameters = method.Parameters;
        int lifetimeParameter = -1;
        for (var i = 0; i < parameters.Length; i++)
            if (IsServiceLifetime(parameters[i].Type))
            {
                lifetimeParameter = i;
                break;
            }

        if (lifetimeParameter < 0) return whenAbsent;

        ExpressionSyntax? argument = arguments[lifetimeParameter];
        return argument is null ? whenAbsent : LifetimeOf(argument, model, wrapper);
    }

    private FragmentRegistrationLifetime? LifetimeOf(ExpressionSyntax argument, SemanticModel model, IMethodSymbol? wrapper)
    {
        ISymbol? symbol = model.GetSymbolInfo(argument).Symbol;
        if (symbol is IFieldSymbol { ContainingType.TypeKind: RoslynTypeKind.Enum } field
            && SymbolEqualityComparer.Default.Equals(field.ContainingType.OriginalDefinition, _serviceLifetime))
            return field.ConstantValue is int value ? Literal(LifetimeFromValue(value)) : null;

        if (symbol is IParameterSymbol parameter
            && wrapper is not null
            && IsServiceLifetime(parameter.Type)
            && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol.OriginalDefinition, wrapper.OriginalDefinition))
            return new FragmentRegistrationLifetime(null, parameter.Name);

        return null;
    }

    // Each parameter's argument expression, null where the call passes none. Every expression is a node of the
    // call's own tree, so the call's semantic model reads it.
    private static IReadOnlyList<ExpressionSyntax?> ArgumentsByParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments, ImmutableArray<IParameterSymbol> parameters)
    {
        var bound = new ExpressionSyntax?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) bound[i] = ArgumentForParameter(arguments, parameters, i);

        return bound;
    }

    // The argument expression bound to the method's parameter at parameterIndex: matched by name-colon first, else
    // the argument in that position when it is not named. A named argument may stand in its own position ahead of
    // positional ones, so a position counts every argument before it, named or not.
    private static ExpressionSyntax? ArgumentForParameter(
        SeparatedSyntaxList<ArgumentSyntax> arguments, ImmutableArray<IParameterSymbol> parameters, int parameterIndex)
    {
        string parameterName = parameters[parameterIndex].Name;
        foreach (ArgumentSyntax argument in arguments)
            if (argument.NameColon?.Name.Identifier.Text == parameterName)
                return argument.Expression;

        return parameterIndex < arguments.Count && arguments[parameterIndex].NameColon is null ? arguments[parameterIndex].Expression : null;
    }

    private static FragmentRegistrationLifetime Literal(Lifetime lifetime)
    {
        return new FragmentRegistrationLifetime(lifetime);
    }

    private static Lifetime LifetimeFromName(string name)
    {
        if (name.EndsWith("Singleton", StringComparison.Ordinal)) return Lifetime.Singleton;
        if (name.EndsWith("Scoped", StringComparison.Ordinal)) return Lifetime.Scoped;
        return Lifetime.Transient; // the only remaining recognized suffix
    }

    // ServiceLifetime's underlying values: Singleton 0, Scoped 1, Transient 2.
    private static Lifetime LifetimeFromValue(int value)
    {
        return value switch
        {
            0 => Lifetime.Singleton,
            2 => Lifetime.Transient,
            _ => Lifetime.Scoped
        };
    }

    private bool IsServiceLifetime(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _serviceLifetime);
    }

    private bool IsSystemType(ITypeSymbol type)
    {
        return SymbolEqualityComparer.Default.Equals(type, _systemType);
    }

    // The operand binds through BestCandidate like every other extraction bind, the invoked method's included:
    // a typeof naming a type the compilation can only guess at (an internal type whose InternalsVisibleTo the
    // load did not see, a name two imported namespaces both declare) still records its registration. A name the
    // compiler cannot find at all has no candidate, so it still records nothing.
    private static ITypeSymbol? TypeFromTypeofArgument(ExpressionSyntax? argument, SemanticModel model, IMethodSymbol? wrapper)
    {
        if (argument is not TypeOfExpressionSyntax typeOf) return null;

        SymbolInfo info = model.GetSymbolInfo(typeOf.Type);
        return (ITypeSymbol?)info.BestNamedCandidate() ?? WrapperTypeParameter(info.BestCandidate(), wrapper);
    }
}

/// <summary>
///     One recognized registration reported by <see cref="RegistrationRecognizer" />. Each type is a named type
///     or a type parameter of the wrapper being read, and the lifetime a literal or that wrapper's parameter. The
///     implementation is null when the registration names no distinct one: a factory/instance form, or the
///     <c>AddHostedService</c> unresolvable-service fallback, where <see cref="Service" /> holds the
///     implementation.
/// </summary>
internal readonly record struct RecognizedRegistration(
    FragmentRegistrationLifetime Lifetime,
    ITypeSymbol Service,
    ITypeSymbol? Implementation);

/// <summary>
///     A call to a registration wrapper: its key, its type arguments by ordinal (null where unreadable), and the
///     <c>ServiceLifetime</c> arguments it passes by parameter name (null where not a literal or the enclosing
///     wrapper's own parameter).
/// </summary>
internal readonly record struct RecognizedWrapperCall(
    string Wrapper,
    ImmutableArray<ITypeSymbol?> TypeArguments,
    ImmutableArray<(string Parameter, FragmentRegistrationLifetime? Lifetime)> LifetimeArguments);
