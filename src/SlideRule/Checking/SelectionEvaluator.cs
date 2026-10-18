using SlideRule.Codebase;
using SlideRule.Fluent;
using SlideRule.Internal;
using SlideRule.Model;

namespace SlideRule.Checking;

/// <summary>
///     Resolves a <see cref="Selection" /> to the set of <see cref="TypeNode" />s it names, given its
///     <see cref="SelectionPosition" /> (GRAMMAR §4.1, §5.1–§5.2): nouns pick the candidate set,
///     adjectives narrow it, <c>Except</c> subtracts.
/// </summary>
/// <remarks>
///     The hierarchy adjectives read the construction lists, comparing names as <see cref="AnchorKey" />
///     decides. External nodes carry empty construction lists, so hierarchy adjectives never match them (a
///     documented boundary, GRAMMAR §5.2).
/// </remarks>
internal sealed class SelectionEvaluator
{
    // The one noun MustBeRegistered resolves through, shared by every rule that uses the verb: the Scanned
    // memo is keyed on the noun by reference, so a per-call instance would rescan the universe per rule.
    private static readonly RegisteredNoun AnyLifetimeRegistered = new(null);

    private readonly ILookup<string, TypeNode> _byFullName;
    private readonly Dictionary<(SelectionNoun, SelectionPosition), IReadOnlyList<TypeNode>> _byNoun = new();
    private readonly Dictionary<(SelectionNoun, SelectionPosition), HashSet<TypeNode>> _admittedByNoun = new();
    private readonly Dictionary<SelectionAdjective, Func<TypeNode, bool>> _keepers = new();
    private readonly CodebaseModel _model;

    // Each declaring file's segments below the root, worked out once per path: a path glob tests every file of
    // every candidate type, and the files a partial type spans are tested again by every rule that asks.
    private readonly Dictionary<string, IReadOnlyList<string>?> _relativeByPath = new(StringComparer.Ordinal);
    private readonly SolutionRoot? _root;
    private readonly List<TypeNode> _solutionDeclared;
    private ILookup<string, TypeNode>? _byProjectName;

    /// <summary>
    ///     An evaluator over <paramref name="model" />, with <paramref name="root" /> the directory path globs are
    ///     relative to: the solution directory, or in the build analyzer the model file's. A run given no root
    ///     cannot answer a path glob, and says so where one is asked.
    /// </summary>
    internal SelectionEvaluator(CodebaseModel model, SolutionRoot? root = null)
    {
        _model = model;
        _root = root;

        // One pass over the type universe for two answers: the subject universe, and whether any type in
        // the model is declared by more than one project. The second is what lets SelectionAdmission skip
        // per-node attribution outright on a codebase where no source file compiles into two projects.
        var solutionDeclared = new List<TypeNode>();
        var multiplyDeclared = false;
        foreach (TypeNode type in model.Types)
        {
            if (!type.IsExternal) solutionDeclared.Add(type);
            if (type.AlsoDeclaredBy.Count > 0) multiplyDeclared = true;
        }

        _solutionDeclared = solutionDeclared;
        AnyMultiplyDeclared = multiplyDeclared;

        // The FQN noun index, built once per evaluator and immutable afterwards: a typeof operand is
        // otherwise a full linear scan of the type universe — the largest list in the model — per operand
        // per rule. It preserves Types order within a key, so the sets it feeds are populated in exactly
        // the order the equivalent Where scan would populate them.
        //
        // A lookup rather than a dictionary because a full name is not unique: CodebaseModel.ShadowedNames
        // is the model's own roster of the names that are not (GRAMMAR §4.1). An indexer here would silently
        // keep whichever came last in Types order and make the other invisible to every typeof operand — a
        // ban that reaches only one of two types it names is worse than a slow scan. The lookup is built
        // unconditionally rather than gated on that roster: it is one O(n) pass either way, and a name maps
        // to its one node through the same call whether or not anything shadows it.
        _byFullName = model.Types.ToLookup(type => type.FullName, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Whether any type in the model is declared by more than one project (GRAMMAR §4.1) — one source
    ///     file compiled into several projects. False on an ordinary codebase, where it collapses every
    ///     attribution question a target-position test would otherwise have to ask.
    /// </summary>
    internal bool AnyMultiplyDeclared { get; }

    // The declarer index, built on first use in the same shape ConstraintEvaluator's edge indexes take, so
    // a spec with no project noun never pays the grouping pass. Keyed on EVERY project that declares a
    // type rather than only the one whose facts the node carries: one source file compiled into several
    // projects is one node (GRAMMAR §4.1), and a project selection over any of those projects names it. A
    // type is listed once per declarer and the declarers are distinct, so the lookup still preserves Types
    // order within a key — a grouping keeps first-seen order, and the outer sequence is still Types.
    private ILookup<string, TypeNode> ByProjectName =>
        _byProjectName ??= _model.Types
            .SelectMany(type => type.Declarers, (type, declarer) => (Declarer: declarer, Type: type))
            .ToLookup(pair => pair.Declarer, pair => pair.Type, StringComparer.Ordinal);

    /// <summary>Whether the operand is a pattern/glob selection (anything but a bare <c>typeof</c>) — the inert-warning gate.</summary>
    internal static bool IsPatternSelection(Selection selection)
    {
        return selection.Adjectives.Count > 0 || SelectionWalk.NounOf(selection) is not TypeNoun;
    }

    /// <summary>
    ///     The parts a compound selection resolves through, or null where it resolves by its own noun — a
    ///     union's operands, a family's cells, a layer's definition (GRAMMAR §5.1, §4.1).
    /// </summary>
    /// <remarks>
    ///     The one answer to which selections are compound, because two walks ask it: membership here, and
    ///     provenance in <see cref="SelectionAdmission.Collect" />. Answered in one place so a fourth
    ///     compound shape reaches both by being added once — a shape the provenance walk did not know about
    ///     would fall through to the leaf and silently lose its parts' heads rather than fail.
    ///     <para>
    ///         The union arm comes first because a union has no single noun: reading
    ///         <see cref="Selection.Noun" /> on one throws by design.
    ///     </para>
    ///     <para>
    ///         Unmemoized on purpose, all three: a layer cell may carry a <c>Where</c> predicate through its
    ///         definition, and a project family's cells are decided by a project selection that may carry
    ///         one directly.
    ///     </para>
    /// </remarks>
    internal IReadOnlyList<Selection>? Composite(Selection selection)
    {
        if (selection is UnionSelection union) return union.Parts;

        return selection.Noun switch
        {
            EachNoun => Cells(selection),
            // A layer is transparent to its definition: it names what the definition names, in the caller's
            // position, and the layer's own adjectives narrow that afterwards — which is the compound fold
            // over a single part.
            LayerNoun { Definition: { } definition } => [definition],
            _ => null
        };
    }

    /// <summary>
    ///     <paramref name="filePath" />'s segments below the run's root, the file name last, or null where the file
    ///     is outside it. Throws <see cref="RuleEvaluationException" /> where the run was given no root: a path
    ///     glob is relative to a directory, and answering without one would select nothing and fail closed only
    ///     by accident.
    /// </summary>
    internal IReadOnlyList<string>? RelativeSegments(string filePath)
    {
        if (_root is null) throw NoRoot();

        if (_relativeByPath.TryGetValue(filePath, out IReadOnlyList<string>? segments)) return segments;

        segments = _root.Relative(filePath);
        _relativeByPath.Add(filePath, segments);
        return segments;
    }

    /// <summary>
    ///     The test of whether a type is declared under any of <paramref name="globs" />: any of its declaring files
    ///     below the run's root that a glob matches, each glob parsed once, here. Throws
    ///     <see cref="RuleEvaluationException" /> where the run was given no root.
    /// </summary>
    internal Func<TypeNode, bool> DeclaredUnder(IReadOnlyList<string> globs)
    {
        if (_root is null) throw NoRoot();

        List<PathGlob> parsed = globs.Select(glob => new PathGlob(glob)).ToList();
        return type =>
        {
            IReadOnlyList<string> paths = type.FilePaths;
            for (var i = 0; i < paths.Count; i++)
                if (RelativeSegments(paths[i]) is { } segments && MatchesAnyGlob(parsed, segments))
                    return true;

            return false;
        };
    }

    private static RuleEvaluationException NoRoot()
    {
        return new RuleEvaluationException(
            "A path glob is relative to the solution directory, and this run was given none.");
    }

    internal HashSet<TypeNode> Evaluate(Selection selection, SelectionPosition position)
    {
        // A union's operands, a family's cells and a layer's definition differ in what they mean and not in
        // how they combine (GRAMMAR §5.1): each part resolves in this same position, the memberships unite,
        // and the enclosing selection's own adjectives narrow the union.
        if (Composite(selection) is { } parts)
        {
            var members = new HashSet<TypeNode>();
            foreach (Selection part in parts) members.UnionWith(Evaluate(part, position));

            return Narrow(selection, members);
        }

        return Narrow(selection, ByNoun(selection.Noun, position));
    }

    /// <summary>
    ///     A selection's own adjectives applied to a candidate set already resolved in the same position —
    ///     the tail of <see cref="Evaluate" /> without its noun resolution.
    /// </summary>
    /// <remarks>
    ///     Exposed so a caller that already holds what the selection's noun resolves to can finish the
    ///     selection without resolving it a second time.
    /// </remarks>
    // May hand back the very set it was given, so a caller must pass one it owns and must not go on
    // mutating it afterwards. That is safe because a HashSet only ever reaches here from a compound fold —
    // Evaluate's parts loop and SelectionAdmission.Folded, each of which allocates one, fills it and lets
    // go. No ByNoun arm yields a HashSet: the universes are lists, the memo holds a list, and the rest are
    // lazy Wheres. An adjective-free union or family is the common case, and copying its membership a
    // second time would buy nothing.
    internal HashSet<TypeNode> Narrow(Selection selection, IEnumerable<TypeNode> candidates)
    {
        if (selection.Adjectives.Count == 0 && candidates is HashSet<TypeNode> owned) return owned;

        IEnumerable<TypeNode> current = candidates;
        foreach (SelectionAdjective adjective in selection.Adjectives) current = current.Where(Keeper(adjective));

        // The constructor is the point: adjectives can yield the same node twice, and naming HashSet here
        // says the deduplication is deliberate where a spread would leave it to the return type.
        // ReSharper disable once UseCollectionExpression
        return new HashSet<TypeNode>(current);
    }

    /// <summary>
    ///     Whether <paramref name="noun" /> names <paramref name="type" /> in <paramref name="position" /> — the
    ///     noun half of <see cref="Evaluate" />, asked of one type.
    /// </summary>
    /// <remarks>
    ///     The claim trace asks this rather than restating a noun's membership, so a trace and a check cannot
    ///     disagree about which types a noun names. A compound selection has no noun of its own to ask about;
    ///     its parts are asked instead (<see cref="Composite" />).
    /// </remarks>
    internal bool NounAdmits(SelectionNoun noun, SelectionPosition position, TypeNode type)
    {
        return Admitted(noun, position).Contains(type);
    }

    // The nodes a noun names in one position, as a set. Memoized for the reason ByNoun memoizes its scans: a noun
    // carries no predicate, so one noun in one position always names the same types, and a trace asks the same noun of
    // type after type.
    private HashSet<TypeNode> Admitted(SelectionNoun noun, SelectionPosition position)
    {
        (SelectionNoun noun, SelectionPosition position) key = (noun, position);
        if (_admittedByNoun.TryGetValue(key, out HashSet<TypeNode>? admitted)) return admitted;

        // The constructor says the memo's list becomes a set here, where a spread would leave it to the
        // declared type.
        // ReSharper disable once UseCollectionExpression
        admitted = new HashSet<TypeNode>(ByNoun(noun, position));
        _admittedByNoun[key] = admitted;
        return admitted;
    }

    /// <summary>
    ///     Whether <paramref name="adjective" /> keeps <paramref name="type" /> — the adjective half of
    ///     <see cref="Narrow" />, asked of one type.
    /// </summary>
    /// <remarks>
    ///     Sound one type at a time because a narrowing filters through the same per-type test
    ///     (<see cref="Keeper" />). An <c>Except</c> resolves its payload in target position here exactly as it
    ///     does inside a narrowing.
    /// </remarks>
    internal bool AdjectiveKeeps(SelectionAdjective adjective, TypeNode type)
    {
        // One keeper per adjective rather than one per type asked about, so an arm's setup runs once however many
        // types a trace asks it of. For an Except that setup resolves a whole selection.
        if (!_keepers.TryGetValue(adjective, out Func<TypeNode, bool>? keeps))
        {
            keeps = Keeper(adjective);
            _keepers[adjective] = keeps;
        }

        return keeps(type);
    }

    // Instance (not static) because the RegisteredNoun arm reads model-side registration facts
    // (CodebaseModel.ServiceRegistrations) — membership is many-to-many and never denormalized onto a
    // TypeNode (GRAMMAR §4.7). The other nouns select purely off the position-correct universe. Never an
    // iterator: TypeNounFullName must refuse a closed-generic noun eagerly, before any node is tested.
    private IEnumerable<TypeNode> ByNoun(SelectionNoun noun, SelectionPosition position)
    {
        // The memo's own probe, ahead of the switch: a memoized noun costs a lookup rather than a closure, a
        // delegate and a re-entered switch. Only the arms that go through Scanned ever store a key, so this
        // can hit for nothing else — the rest are computed on every call, as they must be.
        if (_byNoun.TryGetValue((noun, position), out IReadOnlyList<TypeNode>? hit)) return hit;

        bool subject = position == SelectionPosition.Subject;
        IEnumerable<TypeNode> universe = subject ? _solutionDeclared : _model.Types;
        switch (noun)
        {
            case TypesNoun:
                // arch.Types is solution-declared by definition (§5.1), so in subject position the universe
                // already IS the answer; only the target universe (which holds externals) needs the filter.
                return subject ? _solutionDeclared : Scanned(noun, position, () => universe.Where(t => !t.IsExternal));
            case LayerNoun layer:
                return Scanned(noun, position, () =>
                {
                    List<NamespacePattern> globs = layer.Globs.Select(g => new NamespacePattern(g)).ToList();
                    return universe.Where(t => MatchesAnyGlob(globs, t.Namespace));
                });
            case NamespaceNoun ns:
                return Scanned(noun, position, () =>
                {
                    var pattern = new NamespacePattern(ns.Glob);
                    return universe.Where(t => pattern.Matches(t.Namespace));
                });
            case ProjectNoun project:
                return ProjectMembers(project.Name, subject);
            case TypeNoun typeNoun:
                // The scan is a lookup wearing a Where: every node carrying the name, position-filtered.
                // Usually that is one — one source file compiled into several projects is conflated at merge,
                // and a name nothing declares is a single external. It is two where a project declares a name
                // a referenced assembly also supplies (GRAMMAR §4.1), and naming BOTH is what the position
                // filter below then makes right: in subject position the source declaration alone survives,
                // and in target position a ban reaches either binding rather than whichever node sorted last.
                string fullName = TypeNounFullName(typeNoun.Anchor);
                return _byFullName[fullName].Where(named => !(subject && named.IsExternal));
            case RegisteredNoun registered:
                // Membership = service ∪ implementation FQNs of the recognized registrations at this lifetime
                // (null = any lifetime, §4.7). Filtering the position-correct universe (subject = solution-
                // declared, target = all types incl. externals) means an external registered type matches in
                // target position but never enters a subject — exactly the §4.1 universe discipline.
                return Scanned(noun, position, () =>
                {
                    HashSet<string> registeredNames = RegisteredFullNames(registered.Lifetime);
                    return universe.Where(t => registeredNames.Contains(t.FullName));
                });
            default:
                // Fail closed: the closed noun hierarchy makes this arm unreachable for any v1 noun. An
                // unknown noun means a new noun without a switch arm; throw rather than select nothing (which
                // would vacuously pass every shape verb over an empty subject). ArchChecker contains it per-rule.
                throw new InvalidOperationException($"Unhandled selection noun '{noun.GetType().Name}'.");
        }
    }

    /// <summary>
    ///     The cells of a family subject, as selections, in the order the family declares or resolves
    ///     them (GRAMMAR §5.1) — the layers themselves, or one bare project noun per project the family's
    ///     project selection names.
    /// </summary>
    /// <remarks>
    ///     The one place cells are minted, so the four readers of the partition — the self-allowance, the
    ///     leaf verbs, the emptiness gate and the overlap check — all range over the same list, resolved
    ///     once per rule. A project cell is minted as the selection <c>arch.Project(name)</c> would be,
    ///     stamped with the family's own owner, which is what makes <see cref="SelectionAdmission.Collect" />
    ///     stage its project head and §4.1 attribution judge each instance at its declarer.
    /// </remarks>
    internal IReadOnlyList<Selection> Cells(Selection family)
    {
        var noun = (EachNoun)family.Noun;
        if (noun.Layers is { } layers) return layers;

        IReadOnlyList<ProjectNode> projects = ProjectSelectionEvaluator.Resolve(noun.Projects!, _model.Projects);
        return projects
            .Select(project => (Selection)new RefinedSelection(
                family.Owner, new ProjectNoun(project.Name), Array.Empty<SelectionAdjective>()))
            .ToList();
    }

    // The ordinal declarer index, then the position filter — the same nodes in the same order a
    // universe scan would yield, because a lookup grouping keeps Types order.
    private IEnumerable<TypeNode> ProjectMembers(string name, bool subject)
    {
        IEnumerable<TypeNode> declaring = ByProjectName[name];
        return subject ? declaring.Where(t => !t.IsExternal) : declaring;
    }

    // The scanning nouns' memo, keyed on (noun, position): every one of them walks the whole position-
    // correct universe, and a spec names the same noun in rule after rule. Caching at the NOUN level is
    // sound because a noun carries no user predicate — a Where or Must lambda arrives as an adjective, so
    // one noun in one position always scans to the same list. The materialized list is read-only to every
    // consumer (Evaluate copies it into a HashSet, Narrow wraps it in a Where), so sharing it is safe.
    private IReadOnlyList<TypeNode> Scanned(
        SelectionNoun noun, SelectionPosition position, Func<IEnumerable<TypeNode>> scan)
    {
        (SelectionNoun noun, SelectionPosition position) key = (noun, position);
        if (_byNoun.TryGetValue(key, out IReadOnlyList<TypeNode>? cached)) return cached;

        List<TypeNode> scanned = scan().ToList();
        _byNoun[key] = scanned;
        return scanned;
    }

    // A layer's glob scan, and an Under list's per declaring file: an index walk rather than a LINQ Any, because it
    // runs once per type in the universe over a list that is usually a single glob.
    private static bool MatchesAnyGlob(IReadOnlyList<NamespacePattern> globs, string ns)
    {
        for (var i = 0; i < globs.Count; i++)
            if (globs[i].Matches(ns))
                return true;

        return false;
    }

    private static bool MatchesAnyGlob(IReadOnlyList<PathGlob> globs, IReadOnlyList<string> relativeSegments)
    {
        for (var i = 0; i < globs.Count; i++)
            if (globs[i].Matches(relativeSegments))
                return true;

        return false;
    }

    /// <summary>
    ///     The nodes <c>arch.Registered()</c> names at any lifetime, in the given position (GRAMMAR §4.7) —
    ///     the set <c>MustBeRegistered</c> tests its subjects against.
    /// </summary>
    /// <remarks>
    ///     Resolved through the noun's own arm rather than off the registration facts directly, so the verb's
    ///     membership IS the noun's and the two cannot drift.
    /// </remarks>
    internal HashSet<TypeNode> RegisteredMembers(SelectionPosition position)
    {
        // The memoized set, shared rather than copied: the verb only tests membership against it, and nothing may
        // mutate it.
        return Admitted(AnyLifetimeRegistered, position);
    }

    // The FQN membership set of arch.Registered(lifetime) (GRAMMAR §4.7): the union of the service and
    // implementation full names of every recognized registration at the requested lifetime (null = all
    // lifetimes), skipping the null implementation of a factory/instance registration. The FQNs are already in
    // TypeNode.FullName form (definition-level, declared type-parameter names), so a node compares directly.
    private HashSet<string> RegisteredFullNames(Lifetime? lifetime)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ServiceRegistration registration in _model.ServiceRegistrations)
        {
            if (lifetime is { } wanted && registration.Lifetime != wanted) continue;
            names.Add(registration.ServiceFullName);
            if (registration.ImplementationFullName is { } implementation) names.Add(implementation);
        }

        return names;
    }

    private static string TypeNounFullName(TypeAnchor anchor)
    {
        return DefinitionFullName(
            anchor,
            "v1 reference edges are type-level. Ban the open definition and/or the argument type separately.");
    }

    /// <summary>
    ///     The extraction-format FQN of a definition-level anchor — the one helper every definition-level
    ///     anchor position resolves through (GRAMMAR §5.2, §4.6). A closed generic construction (which
    ///     has no definition-level node, GRAMMAR §4.1/§4.5) and a type with no source-level form are refused
    ///     as a <see cref="RuleEvaluationException" /> — a <see cref="ViolationKind.RuleError" /> the checker
    ///     surfaces rather than crashing. The <paramref name="closedGenericGuidance" /> tail is spliced
    ///     after "is a closed generic construction; " so each caller states its own actionable next step;
    ///     an open definition resolves normally.
    /// </summary>
    /// <remarks>
    ///     A string anchor already <em>is</em> the definition FQN, so it passes through verbatim and never
    ///     refuses, which is the escape hatch's whole point.
    /// </remarks>
    internal static string DefinitionFullName(TypeAnchor anchor, string closedGenericGuidance)
    {
        if (anchor.TryDefinitionKey(out string key, out AnchorRefusal refusal)) return key;

        throw Refused(anchor, refusal, closedGenericGuidance);
    }

    // The RuleError an anchor's refusal becomes, composed in the one place both key helpers reach, so what
    // a refusal reads like cannot drift between a definition-level and a construction-level position. The
    // closed-generic tail is the position's own next step; a position that ADMITS a closed generic has none
    // to splice and can only be refused for a type with no source-level form, whose message the anchor holds.
    private static RuleEvaluationException Refused(
        TypeAnchor anchor, AnchorRefusal refusal, string closedGenericGuidance)
    {
        if (refusal == AnchorRefusal.ClosedGeneric)
            return new RuleEvaluationException(
                $"`{anchor.SimpleDisplay}` is a closed generic construction; {closedGenericGuidance}");

        return new RuleEvaluationException(anchor.Unrepresentable!);
    }

    // An adjective as a test of one type. An arm hands back a predicate, which has no set to look across, so every
    // adjective filters one type at a time by construction. Its setup (an Except's payload, an anchor's matcher, a
    // pattern) still runs here, at the call, so a refusal fires before any type is tested. DeadNames reads a
    // Named list against a narrowing on that ground, and AdjectiveKeeps caches the keeper.
    private Func<TypeNode, bool> Keeper(SelectionAdjective adjective)
    {
        switch (adjective)
        {
            case InNamespaceAdjective inNamespace:
                var pattern = new NamespacePattern(inNamespace.Glob);
                return t => pattern.Matches(t.Namespace);
            case OfKindAdjective ofKind:
                return t => t.Kind == ofKind.Kind;
            case WithSuffixAdjective suffix:
                return t => t.Name.EndsWith(suffix.Suffix, StringComparison.Ordinal);
            case WithPrefixAdjective prefix:
                return t => t.Name.StartsWith(prefix.Prefix, StringComparison.Ordinal);
            case WithNameMatchingAdjective matching:
                var namePattern = new TypeNamePattern(matching.Glob);
                return t => namePattern.Matches(t.Name);
            case NamedAdjective named:
                // The same simple-name universe the glob form matches: no generic arity backtick, and a
                // nested type by its leaf — so a name reaches every type carrying it, in every namespace.
                var names = new HashSet<string>(named.Names, StringComparer.Ordinal);
                return t => names.Contains(t.Name);
            case ImplementingAdjective implementing:
                return InterfaceMatcher(implementing.Anchor);
            case DerivedFromAdjective derivedFrom:
                return BaseTypeMatcher(derivedFrom.Anchor);
            case AttributedWithAdjective attributedWith:
                return AttributeMatcher(attributedWith.Anchor);
            case ExceptAdjective except:
                HashSet<TypeNode> excluded = Evaluate(except.Payload, SelectionPosition.Target);
                return t => !excluded.Contains(t);
            case WhereAdjective where:
                return t => InvokePredicate(where.Predicate, t, "Where");
            case AuthoredAdjective:
                return t => !t.IsGenerated;
            case AccessibilityAdjective accessibility:
                // The accessibility the declaration writes, never an effective accessibility computed
                // through containers — the same fact ITypeInfo reports and MustBePublic tests, so the
                // adjective and the verb cannot disagree about one type.
                return t => t.Accessibility == accessibility.Accessibility;
            case UnderAdjective under:
                // Any declaring file under any glob (GRAMMAR §5.2): a partial type is where any of its files
                // is, and an external type, declared in no file of the solution, is under nothing. The root is
                // asked for here, at the call, so a run given none refuses before any type is tested.
                return DeclaredUnder(under.Globs);
            default:
                // Fail closed: an unknown adjective would silently widen the selection — and in a
                // MustOnly* target position a silently-widened allow-set is fail-open enforcement. A missing
                // arm is a bug; throw (ArchChecker contains it per-rule) rather than pass the widened set through.
                throw new InvalidOperationException($"Unhandled selection adjective '{adjective.GetType().Name}'.");
        }
    }

    // The three hierarchy matchers share one shape over one construction list each — interfaces, bases,
    // attributes. Which name each anchor form compares on, and why the key is read eagerly, is stated
    // once on AnchorKey (GRAMMAR §5.2).
    internal static Func<TypeNode, bool> InterfaceMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.AllInterfaces);
    }

    internal static Func<TypeNode, bool> BaseTypeMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.BaseTypeChain);
    }

    internal static Func<TypeNode, bool> AttributeMatcher(TypeAnchor anchor)
    {
        return ConstructionMatcher(anchor, t => t.AttributeConstructions);
    }

    /// <summary>
    ///     The name an anchor compares on, and whether it compares on the <em>definition</em> name — the
    ///     three-arm GRAMMAR §5.2 decision, stated once for every matcher that anchors on a
    ///     <see cref="TypeAnchor" />.
    /// </summary>
    /// <remarks>
    ///     A string anchor names a definition, so it matches every construction of that definition and a
    ///     constructed spelling matches nothing; an open-generic <c>typeof</c> matches on the definition
    ///     name ("any construction"); anything else on the constructed name ("that construction exactly").
    ///     The key is read here, so an unrepresentable <c>typeof</c> refuses while the matcher is being
    ///     built — before any subject is tested — and a string anchor, which carries its own name, refuses
    ///     nothing, which is the whole point of the hatch.
    /// </remarks>
    internal static (string Key, bool OnDefinition) AnchorKey(TypeAnchor anchor)
    {
        if (anchor.TryConstructionKey(out string key, out bool onDefinition, out AnchorRefusal refusal))
            return (key, onDefinition);

        // This position admits a closed generic, so the refusal is always an unrepresentable type and there
        // is no guidance tail for the message to splice.
        throw Refused(anchor, refusal, string.Empty);
    }

    // The one comparison the three matchers share, parameterized by which construction list to read.
    private static Func<TypeNode, bool> ConstructionMatcher(
        TypeAnchor anchor, Func<TypeNode, IReadOnlyList<TypeConstruction>> constructions)
    {
        (string key, bool onDefinition) = AnchorKey(anchor);
        Func<TypeConstruction, string> nameOf = onDefinition ? c => c.Definition.FullName : c => c.FullName;
        return t =>
        {
            IReadOnlyList<TypeConstruction>? candidates = constructions(t);
            for (var i = 0; i < candidates.Count; i++)
                if (nameOf(candidates[i]) == key)
                    return true;

            return false;
        };
    }

    internal static bool InvokePredicate(Func<ITypeInfo, bool> predicate, TypeNode type, string hatch)
    {
        return Invoke<TypeNode>(predicate, type, hatch, subject => subject.FullName);
    }

    // The member-flavored guarded invoke (GRAMMAR §5.6): the member `.Where`/`.Must` escape hatches run
    // here so a throwing predicate becomes a RuleError naming the member, not an aborted run.
    internal static bool InvokePredicate(Func<IMemberInfo, bool> predicate, IMemberInfo member, string hatch)
    {
        return Invoke(predicate, member, hatch, MemberIdentity);
    }

    // The project-flavored guarded invoke (GRAMMAR §5.6, §4.10). A project's own name is its whole
    // identity, so there is no qualified form to compose.
    internal static bool InvokePredicate(Func<IProjectInfo, bool> predicate, IProjectInfo project, string hatch)
    {
        return Invoke(predicate, project, hatch, subject => subject.Name);
    }

    // The guard the three strata share (GRAMMAR §5.6): a predicate that throws becomes a RuleEvaluationException
    // the checker turns into a RuleError, so one bad lambda reds its own rule instead of aborting the run.
    // Only the identity in the message differs per stratum, which is what `identityOf` supplies — and it is
    // read in the catch, so a subject whose identity is expensive costs nothing on the passing path.
    // Cancellation is the one throw that travels on unchanged: a predicate is the only user code a check
    // runs, so it is where a cancelled caller's token surfaces, and translating that into a rule error would
    // report a build the user interrupted as a defect in the spec. Must agree with the blanket arms of
    // ArchChecker.Verdict and ArchChecker.AttachExemplar, which let the same exception past for the same reason.
    private static bool Invoke<T>(Func<T, bool> predicate, T subject, string hatch, Func<T, string> identityOf)
    {
        try
        {
            return predicate(subject);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RuleEvaluationException(
                $"the `{hatch}` predicate threw {ex.GetType().Name} on `{identityOf(subject)}`: {ex.Message}");
        }
    }

    // The declaring-type-dot-member identity for a member escape-hatch error; the declaring type is always
    // a TypeNode (IMemberInfo.DeclaringType), so its FullName is available for a fully-qualified name.
    private static string MemberIdentity(IMemberInfo member)
    {
        return member.DeclaringType is TypeNode type ? $"{type.FullName}.{member.Name}" : member.Name;
    }
}
