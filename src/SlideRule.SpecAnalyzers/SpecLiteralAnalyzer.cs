using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SlideRule.Validation;

namespace SlideRule.SpecAnalyzers;

/// <summary>
///     Reads the string literals a spec writes into the authoring surface and reports, at the literal
///     itself, the verdict the contract would give when the spec is loaded.
/// </summary>
/// <remarks>
///     <para>
///         It must never warn about a literal that loading the spec would accept, and it may stay silent
///         wherever it is unsure, because loading the spec reports every one of these anyway. So every step
///         here refuses rather than guesses: an argument whose value is not a compile-time string constant,
///         a parameter carrying no marker, a marker naming a slot the contract does not know, a code with no
///         descriptor. The verdict itself is never computed here: it comes from the same types the loader
///         decides with.
///     </para>
///     <para>
///         It sees only a literal written at a marked parameter, and only within the method body it sits in.
///         A rule pack, a second spec class, a helper method and a second file are each out of reach of the
///         several-literal checks, which is why those three warnings say so in their own descriptions. A call
///         the spec never makes (one under a condition that is never taken) is read as a declaration here and
///         not by the loader, which is one of the five places this view can be wider than the loader's rather
///         than narrower; <c>SpecLiteralParityTests</c> names all five.
///     </para>
///     <para>
///         The only state is the test seams. Nothing is read from disk, the environment or a clock, and no
///         per-compilation value is held in a field: everything a run needs lives in the closures the
///         compilation-start and block-start actions make.
///     </para>
///     <para>
///         The compiler binds this analyzer to whichever copy of the contract it loaded, which can be another
///         version's, and a member that moved between the two throws when the JIT compiles the method naming
///         it, at that method's call site in its caller. So a method that names a type or member of the
///         contract is a boundary: marked never to be inlined, which keeps its compilation its own, and called
///         only from another boundary or from inside a try of the calling method that catches everything but
///         cancellation. No method the compiler calls is a boundary, the fault path names no contract type,
///         and no field's type makes loading one of these types bind the contract, which is why the verdicts
///         carry a code as a number and a name. <c>AnalyzerCoreFirewallTests</c> holds all of it over the
///         built assembly.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class SpecLiteralAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    ///     The marker's full name, which is how this assembly finds a marker in a compilation it holds no
    ///     reference into. It must agree with the name the contract declares the attribute under; the
    ///     attribute's own remarks record that agreement on the other side.
    /// </summary>
    internal const string MarkerMetadataName = "SlideRule.Validation.SpecSlotAttribute";

    /// <summary>
    ///     The kind number a collection expression carries. The member naming it was added to the compiler
    ///     API after the version this assembly is built against, so the kind cannot be written here and is
    ///     matched by its number instead. A compiler old enough not to name it is also old enough never to
    ///     produce one, so the number reaches nothing on those; <c>SpecLiteralAnalyzerTests</c> holds it to
    ///     the member a newer compiler names.
    /// </summary>
    internal const int CollectionExpressionKind = 0x7F;

    private readonly Action? _beforeDecide;
    private readonly Func<(string? Analyzer, string? Core)>? _versions;

    /// <summary>The constructor the compiler calls, which arms no seam.</summary>
    public SpecLiteralAnalyzer()
        : this(null)
    {
    }

    /// <summary>
    ///     The same analyzer with <paramref name="beforeDecide" /> invoked at the top of every call into
    ///     the contract, and <paramref name="versions" /> in place of the two versions the version check reads.
    /// </summary>
    /// <param name="beforeDecide">
    ///     Runs immediately inside the guarded region, so a fault thrown from it takes the path a fault in
    ///     the contract itself would take; null in production.
    /// </param>
    /// <param name="versions">
    ///     Supplies the two informational versions the version check compares, the analyzer's own first, in
    ///     place of reading them off the two assemblies; it runs inside the check, and is null in production.
    /// </param>
    internal SpecLiteralAnalyzer(Action? beforeDecide, Func<(string? Analyzer, string? Core)>? versions = null)
    {
        _beforeDecide = beforeDecide;
        _versions = versions;
    }

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SpecLiteralDiagnostics.All;

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Generated code is left alone: a literal in a file the author does not edit is a warning nobody
        // can act on, and the spec that carries it is validated in full when it is loaded anyway.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(OnCompilationStart);
    }

    /// <summary>
    ///     The version <paramref name="assembly" /> was built as: its informational version, or null where it
    ///     carries none.
    /// </summary>
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

    private void OnCompilationStart(CompilationStartAnalysisContext context)
    {
        // A project that does not reference the contract, or references one too old to carry the marker,
        // has nothing here to read. Registering nothing is what keeps this analyzer free for every
        // project in an estate that is not a spec project.
        INamedTypeSymbol? marker = context.Compilation.GetTypeByMetadataName(MarkerMetadataName);
        if (marker is null) return;

        // The version check comes before anything else that touches the contract. A difference is the one
        // warning and no literal is read; a fault means the contract cannot load at all, and is the one
        // warning too. Either is a fact about the compilation rather than about a call, so both are reported
        // from its end.
        try
        {
            if (!VersionsAgree(out string analyzerVersion, out string contractVersion))
            {
                context.RegisterCompilationEndAction(end =>
                    end.ReportDiagnostic(SpecLiteralDiagnostics.VersionsMismatched(analyzerVersion, contractVersion)));
                return;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            context.RegisterCompilationEndAction(end =>
                end.ReportDiagnostic(SpecLiteralDiagnostics.AnalyzerFailed(exception, Location.None)));
            return;
        }

        var faultReported = 0;

        // True for the first fault in this compilation and false after it. A fault is usually a fault in
        // every call site alike, and a build log carrying the same one a hundred times would bury
        // whatever else the build had to say.
        bool ClaimTheFault()
        {
            return Interlocked.CompareExchange(ref faultReported, 1, 0) == 0;
        }

        context.RegisterOperationBlockStartAction(block => OnOperationBlockStart(block, marker, ClaimTheFault));
    }

    // A block is one symbol's body, which is the scope the several-literal checks are made in. A lambda
    // or a local function inside a Define does not split it, so a rule declared inside one is compared
    // against the rules declared around it.
    private void OnOperationBlockStart(
        OperationBlockStartAnalysisContext context, INamedTypeSymbol marker, Func<bool> claimTheFault)
    {
        var ids = new ConcurrentBag<Declared>();
        var layerNames = new ConcurrentBag<Declared>();

        context.RegisterOperationAction(
            operation => OnInvocation(operation, marker, ids, layerNames, claimTheFault),
            OperationKind.Invocation);
        context.RegisterOperationBlockEndAction(end => OnBlockEnd(end, ids, layerNames, claimTheFault));
    }

    // The guarded region every literal is read in. A fault has to reach the developer as a warning they can
    // exclude by ID rather than as the compiler's own AD0001, which a warnings-as-errors build fails on;
    // cancellation is not a fault and passes through.
    private void OnInvocation(
        OperationAnalysisContext context, INamedTypeSymbol marker, ConcurrentBag<Declared> ids,
        ConcurrentBag<Declared> layerNames, Func<bool> claimTheFault)
    {
        try
        {
            Walk(context, marker, ids, layerNames);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!claimTheFault()) return;

            context.ReportDiagnostic(
                SpecLiteralDiagnostics.AnalyzerFailed(exception, context.Operation.Syntax.GetLocation()));
        }
    }

    // A boundary, as Report below it is, though neither names the contract. Report calls Decide with no guard
    // of its own, which is sound because the guard in OnInvocation encloses the whole chain; boundaries all the
    // way from that guard to Decide is the shape AnalyzerCoreFirewallTests holds that to.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Walk(
        OperationAnalysisContext context, INamedTypeSymbol marker, ConcurrentBag<Declared> ids,
        ConcurrentBag<Declared> layerNames)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol target = invocation.TargetMethod;
        IMethodSymbol declaration = target.OriginalDefinition;

        // The declaration is where a marker lives: a constructed generic's parameters are substitutions
        // and carry no attribute blob of their own, so an argument's own parameter symbol may have none.
        // In C# the target is never reduced and the arguments include an extension call's receiver, so an
        // ordinal means the same thing on both lists — and where it would not, this says nothing rather
        // than reading the parameter next door.
        if (declaration.Parameters.Length != target.Parameters.Length) return;

        foreach (IArgumentOperation argument in invocation.Arguments)
        {
            // An argument the caller did not write arrives as a DefaultValue, which is how every
            // caller-info parameter on the authoring surface arrives: there is no literal to report at.
            bool written = argument.ArgumentKind == ArgumentKind.Explicit
                           || argument.ArgumentKind == ArgumentKind.ParamArray;
            if (!written) continue;

            if (argument.Parameter is not { } parameter) continue;

            int ordinal = parameter.Ordinal;
            if (ordinal < 0 || ordinal >= declaration.Parameters.Length) continue;

            string? slot = SlotNameOn(declaration.Parameters[ordinal], marker);
            if (slot is null) continue;

            foreach (IOperation operand in Operands(argument))
            {
                var text = (string)operand.ConstantValue.Value!;
                Location location = operand.Syntax.GetLocation();

                Collect(slot, text, location, ids, layerNames);
                Report(context, slot, text, location);
            }
        }
    }

    // The literals the several-literal checks compare. A rule ID and a scope ID go in one list because
    // the contract compares them against each other as well as among themselves.
    private static void Collect(
        string slot, string text, Location location, ConcurrentBag<Declared> ids,
        ConcurrentBag<Declared> layerNames)
    {
        if (slot == nameof(SpecSlot.RuleId) || slot == nameof(SpecSlot.ScopeId))
            ids.Add(new Declared(slot, text, location));
        else if (slot == nameof(SpecSlot.LayerName))
            layerNames.Add(new Declared(slot, text, location));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Report(OperationAnalysisContext context, string slot, string text, Location location)
    {
        if (Decide(slot, text) is not { } verdict) return;

        Diagnostic? warning = SpecLiteralDiagnostics.AtTheLiteral(
            verdict.Code, verdict.CodeName, slot, verdict.Head, verdict.Tail, location);
        if (warning is not null) context.ReportDiagnostic(warning);
    }

    // The guarded region the several-literal checks are decided in, when the body they were collected from
    // ends.
    private void OnBlockEnd(
        OperationBlockAnalysisContext context, ConcurrentBag<Declared> ids, ConcurrentBag<Declared> layerNames,
        Func<bool> claimTheFault)
    {
        try
        {
            List<Declared> declaredIds = InSourceOrder(ids);
            List<Declared> declaredNames = InSourceOrder(layerNames);
            if (declaredIds.Count == 0 && declaredNames.Count == 0) return;

            foreach (Collision collision in DecideCollisions(declaredIds, declaredNames))
            {
                List<Declared> sites = collision.OverIds ? declaredIds : declaredNames;
                foreach (Diagnostic warning in Warnings(collision, sites)) context.ReportDiagnostic(warning);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!claimTheFault()) return;

            // Nothing here belongs to one call site, and a block-end diagnostic placed at an arbitrary one
            // of them would read as a claim about that call.
            context.ReportDiagnostic(SpecLiteralDiagnostics.AnalyzerFailed(exception, Location.None));
        }
    }

    // The callbacks that filled the bag may run at once and in any order, so source order is imposed
    // here. A tag is an index into the list this returns and the contract reports at tags, so an order
    // that moved between runs would move the warning.
    private static List<Declared> InSourceOrder(ConcurrentBag<Declared> collected)
    {
        return collected
            .OrderBy(entry => entry.Location.SourceTree?.FilePath ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Location.SourceSpan.Start)
            .ToList();
    }

    // Where a collision is reported and what it points at. A duplicate is one fact about every
    // declaration that took the ID, so each one after the first is warned about and points back at the
    // first. The other two name two declarations in the order the contract hands them over: the one to
    // fix, then the one it collided with.
    private static IEnumerable<Diagnostic> Warnings(Collision collision, IReadOnlyList<Declared> sites)
    {
        IReadOnlyList<int> tags = collision.Tags;
        if (tags.Count < 2) yield break;
        if (tags.Any(tag => tag < 0 || tag >= sites.Count)) yield break;

        // A constant of the contract's enum, which compiles to its number and leaves no reference behind.
        if (collision.Code == (int)SpecValidationErrorCode.DuplicateId)
        {
            Location[] first = [sites[tags[0]].Location];
            for (var index = 1; index < tags.Count; index++)
            {
                Declared repeat = sites[tags[index]];
                Diagnostic? warning = SpecLiteralDiagnostics.Collision(
                    collision.Code, collision.CodeName, repeat.Slot, collision.Sentence, repeat.Location, first);
                if (warning is not null) yield return warning;
            }

            yield break;
        }

        Declared reported = sites[tags[0]];
        Location[] related = [sites[tags[1]].Location];
        Diagnostic? diagnostic = SpecLiteralDiagnostics.Collision(
            collision.Code, collision.CodeName, reported.Slot, collision.Sentence, reported.Location, related);
        if (diagnostic is not null) yield return diagnostic;
    }

    // The marker as it sits in metadata: one attribute of the class this compilation resolved, carrying
    // one string. Compared by symbol rather than by rendered name, so a type of the same name from
    // somewhere else is not read as the marker.
    private static string? SlotNameOn(IParameterSymbol parameter, INamedTypeSymbol marker)
    {
        foreach (AttributeData attribute in parameter.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)) continue;
            if (attribute.ConstructorArguments.Length != 1) continue;
            if (attribute.ConstructorArguments[0].Value is not string name) continue;

            return name;
        }

        return null;
    }

    // Every string constant an argument actually hands to the slot, and nothing else. A plain walk over
    // the value's operations is not enough: an interpolated string with a hole has no constant of its
    // own while its literal parts have, so a walk would report the pieces of a value nobody wrote as a
    // literal. Accepting a constant only beneath the operations that carry a written value through is
    // what makes an expanded params call, an explicit array and a collection expression each yield their
    // elements, while a concatenation is one constant reported once and an interpolation with a hole is
    // nothing at all.
    private static IEnumerable<IOperation> Operands(IArgumentOperation argument)
    {
        IOperation value = argument.Value;

        return value.DescendantsAndSelf()
            .Where(operation => operation.ConstantValue is { HasValue: true, Value: string })
            .Where(operation => CarriedThrough(operation, value));
    }

    // Whether everything strictly above this operation, up to and including the argument's value, only
    // carries a written value through rather than computing one. Walked upwards through Parent rather
    // than downwards, because the child accessor was renamed in a later compiler and a call compiled
    // against this one would not bind against that.
    private static bool CarriedThrough(IOperation operand, IOperation value)
    {
        if (ReferenceEquals(operand, value)) return true;

        for (IOperation? ancestor = operand.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.ConstantValue.HasValue) return false;
            if (!CarriesValuesThrough(ancestor)) return false;

            if (ReferenceEquals(ancestor, value)) return true;
        }

        return false;
    }

    // The operations that hand on a value written inside them, named one by one. A test on the type
    // would be wider than that and wrong: a helper returning an array, a method called on a constant
    // receiver and a conditional choosing between two arrays are all array-typed, and each computes its
    // value rather than carrying a written one — so reading a literal out of any of them would report a
    // string the slot never receives. A collection expression cannot be named at the compiler API this
    // assembly is built against and is matched by its kind number; a spread inside one is deliberately
    // absent, because what it spreads is another collection's elements rather than literals written here.
    private static bool CarriesValuesThrough(IOperation operation)
    {
        if (operation is IArrayCreationOperation or IArrayInitializerOperation or IConversionOperation)
            return true;

        return (int)operation.Kind == CollectionExpressionKind;
    }

    /// <summary>
    ///     Whether the copy of the contract the compiler bound this analyzer to is its own version, with the
    ///     two versions it compared.
    /// </summary>
    /// <remarks>
    ///     Never inlined, and it names one contract type, which every version carries, so compiling it fails
    ///     only where the contract cannot load at all. It runs before any other call into the contract is
    ///     compiled, because compiling one of those is what a moved member breaks.
    /// </remarks>
    /// <param name="analyzerVersion">This analyzer's own version.</param>
    /// <param name="contractVersion">The version of the contract the compiler loaded for it.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool VersionsAgree(out string analyzerVersion, out string contractVersion)
    {
        (string? Analyzer, string? Core) informational = _versions?.Invoke()
                                                         ?? (InformationalVersionOf(typeof(SpecLiteralAnalyzer).Assembly),
                                                             InformationalVersionOf(typeof(IArchitectureSpec).Assembly));
        analyzerVersion = ReleasedVersion(informational.Analyzer);
        contractVersion = ReleasedVersion(informational.Core);

        return string.Equals(analyzerVersion, contractVersion, StringComparison.Ordinal);
    }

    /// <summary>
    ///     What the contract decides a literal written in this slot draws, or null when it draws nothing
    ///     and null when no slot carries the name.
    /// </summary>
    /// <remarks>
    ///     Never inlined, and the only place one literal reaches the contract. The compiler loads whatever
    ///     copy of the contract sits beside this assembly, and a copy whose shape has moved throws when the
    ///     method that would call into it is compiled rather than when the call runs — so keeping the call
    ///     in a method of its own is what puts that throw inside the caller's guard instead of at the
    ///     callback's own entry, where nothing could catch it. The code crosses back out as its number and
    ///     its name, converted here, so nothing outside a boundary holds the contract's enum.
    /// </remarks>
    /// <param name="slotName">The slot name the marker carried.</param>
    /// <param name="literal">The literal as it was written.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Verdict? Decide(string slotName, string literal)
    {
        _beforeDecide?.Invoke();

        SpecSlot? slot = SpecSlot.ByName(slotName);
        if (slot is null) return null;

        LiteralVerdict? verdict = slot.Decide(literal);

        return verdict is null
            ? null
            : new Verdict((int)verdict.Code, verdict.Code.ToString(), verdict.Head, verdict.Tail);
    }

    /// <summary>
    ///     What the contract decides the collected literals of one body draw between them, in the order it
    ///     reports them.
    /// </summary>
    /// <remarks>
    ///     Never inlined, for the reason <see cref="Decide" /> records. A tag is an index into the list it
    ///     was built from, which is what lets the verdicts come back carrying no locations and still be
    ///     placed.
    /// </remarks>
    /// <param name="ids">Every rule and scope ID the body declared, in source order.</param>
    /// <param name="layerNames">Every layer name the body declared, in source order.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private List<Collision> DecideCollisions(IReadOnlyList<Declared> ids, IReadOnlyList<Declared> layerNames)
    {
        _beforeDecide?.Invoke();

        var declarations = new List<IdDeclaration>(ids.Count);
        for (var tag = 0; tag < ids.Count; tag++)
        {
            IdRole role = ids[tag].Slot == nameof(SpecSlot.ScopeId) ? IdRole.Scope : IdRole.Rule;
            declarations.Add(new IdDeclaration(ids[tag].Text, role, tag));
        }

        var names = new List<string>(layerNames.Count);
        foreach (Declared name in layerNames) names.Add(name.Text);

        var collisions = new List<Collision>();
        foreach (CollisionVerdict verdict in SpecIdCollisions.Decide(declarations))
            collisions.Add(new Collision((int)verdict.Code, verdict.Code.ToString(), verdict.Sentence, verdict.Tags, true));

        foreach (CollisionVerdict verdict in SpecLayerNames.Decide(names))
            collisions.Add(new Collision((int)verdict.Code, verdict.Code.ToString(), verdict.Sentence, verdict.Tags, false));

        return collisions;
    }

    /// <summary>One literal this analysis accepted: the slot it fills, its text, and where it was written.</summary>
    private readonly struct Declared(string slot, string text, Location location)
    {
        /// <summary>The name of the slot the marker named.</summary>
        internal string Slot { get; } = slot;

        /// <summary>The literal as the constant folded to.</summary>
        internal string Text { get; } = text;

        /// <summary>The literal's own span, which is where a warning about it sits.</summary>
        internal Location Location { get; } = location;
    }

    /// <summary>What the contract decided about one literal, reduced to what a warning needs.</summary>
    /// <remarks>
    ///     The contract's own verdict type is not carried past <see cref="Decide" />, and neither is its code
    ///     enum: a struct with a field of a contract type binds the contract wherever it is loaded, which is
    ///     the method holding one, so a code that crossed out as the enum would throw at the guard's own
    ///     entry instead of inside it. Strings and a number are what crosses back out.
    /// </remarks>
    private readonly struct Verdict(int code, string codeName, string head, string tail)
    {
        /// <summary>Which mistake it is, as the contract's code numbers it.</summary>
        internal int Code { get; } = code;

        /// <summary>The code's name, which the <c>code</c> property carries.</summary>
        internal string CodeName { get; } = codeName;

        /// <summary>The sentence up to the subject clause, or the whole sentence where it names none.</summary>
        internal string Head { get; } = head;

        /// <summary>The sentence after the subject clause; empty where it names none.</summary>
        internal string Tail { get; } = tail;
    }

    /// <summary>What the contract decided about several literals, reduced to what the warnings need.</summary>
    /// <remarks>
    ///     <c>Tags</c> are indexes into the list the verdict was decided over, and <c>OverIds</c> says
    ///     which of the two lists that was. The code crosses out as a number and a name, for the reason
    ///     <see cref="Verdict" /> records.
    /// </remarks>
    private readonly struct Collision(
        int code,
        string codeName,
        string sentence,
        IReadOnlyList<int> tags,
        bool overIds)
    {
        /// <summary>Which mistake it is, as the contract's code numbers it.</summary>
        internal int Code { get; } = code;

        /// <summary>The code's name, which the <c>code</c> property carries.</summary>
        internal string CodeName { get; } = codeName;

        /// <summary>The whole message, with no subject clause to fill in.</summary>
        internal string Sentence { get; } = sentence;

        /// <summary>The declarations that took part, as indexes in the order the contract reports them.</summary>
        internal IReadOnlyList<int> Tags { get; } = tags;

        /// <summary>Whether the tags index the IDs rather than the layer names.</summary>
        internal bool OverIds { get; } = overIds;
    }
}
