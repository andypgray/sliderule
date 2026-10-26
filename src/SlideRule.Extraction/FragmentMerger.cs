using SlideRule.Codebase;
using SlideRule.Extraction.Fragments;
using SlideRule.Internal;
using SlideRule.Prose;

namespace SlideRule.Extraction;

/// <summary>
///     Merges a set of per-input <see cref="CodebaseFragment" />s into one <see cref="CodebaseModel" />,
///     unifying by fully-qualified name so the model carries global cross-input semantics rather than one
///     compilation's view. One code path serves cold runs, the fast test path, and cache hits, so the
///     cache cannot change results by construction.
/// </summary>
/// <remarks>
///     <para>
///         Two rules run across every axis. Per-type <b>facts</b> — a node's shape and <c>ProjectName</c>, its
///         hierarchy, its member inventory — are <em>winner-only</em>: fragments are visited in input order and
///         the first declarer wins, so a later fragment never overwrites what an earlier one established.
///         Per-edge <b>evidence</b> is <em>unioned</em>: each axis keys its site-set on the edge's endpoint
///         pair, drops the self-edge, and resolves both endpoints to the same <see cref="TypeNode" />
///         instances <see cref="CodebaseModel.Types" /> holds — so an external endpoint is one shared node
///         however many fragments referenced it.
///     </para>
///     <para>
///         Every node is declared before any hierarchy is populated, which is what makes a declaration beat
///         every fragment's external view of the name, except where the assembly a fragment bound the name
///         from is one no declaring fragment produces: there the name denotes two types, the declaration and a
///         shallow external attributed to the supplying assembly. Each fragment's endpoints resolve to whichever
///         its compilation bound, on every axis including the hierarchy, so every table pairing endpoints keys
///         on a <c>NodeKey</c> rather than on the name alone, and the split is disclosed as an advisory
///         <see cref="CodebaseModel.MergeNotes">merge note</see>.
///     </para>
///     <para>
///         A later declarer under a <em>different</em> project name is cross-project conflation: facts still
///         follow the first declarer, the later ones are recorded as <see cref="TypeNode.AlsoDeclaredBy" /> and
///         in a merge note, declaration sites union with each declarer's own share kept beside the union, and an
///         edge either end of which is such a name keeps its instances beside its sites. A later declarer under
///         the <em>same</em> project name is another target framework of one project file: its references union
///         like every edge, but a type two frameworks both declare carries the first-extracted framework's
///         members and hierarchy, which a second kind of merge note records per project.
///     </para>
///     <para>
///         Where one external FQN carries different facts across compilations (mixed assembly versions), the
///         winner is the first fragment in input order that <em>references</em> it, which makes the tie
///         deterministic.
///     </para>
/// </remarks>
internal static class FragmentMerger
{
    public static CodebaseModel Merge(IReadOnlyList<CodebaseFragment> fragments)
    {
        return new MergeState().Run(fragments);
    }

    /// <summary>
    ///     The merge inputs a caller keeps: <paramref name="fragments" /> minus
    ///     <paramref name="excludeProjectNames" />.
    /// </summary>
    /// <remarks>
    ///     Dropping a project here is byte-identical to never extracting it — a referenced-but-dropped
    ///     project survives as an external node — which is what lets one fragment set serve every caller
    ///     whatever each excludes.
    /// </remarks>
    internal static List<CodebaseFragment> Retain(
        IReadOnlyList<CodebaseFragment> fragments, IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return fragments.ToList();

        var excluded = new HashSet<string>(excludeProjectNames, StringComparer.Ordinal);
        return fragments.Where(f => !excluded.Contains(f.ProjectName)).ToList();
    }

    /// <summary>
    ///     <paramref name="excludeProjectNames" /> as one ordinal-stable string, for a caller memoizing the
    ///     models it merges from one fragment set. Two callers excluding the same projects in a different
    ///     order are the same merge, so the key sorts; <c>'\n'</c> cannot occur in a project name.
    /// </summary>
    /// <remarks>
    ///     Must agree with <see cref="Retain" />: the key identifies a merge exactly as far as <c>Retain</c>
    ///     distinguishes one, or a memo keyed on it strands a wrong model.
    /// </remarks>
    internal static string ExclusionKey(IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return "";

        return string.Join("\n", excludeProjectNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    private sealed class MergeState
    {
        // The supplier map every fragment shares when no name is shadowed or declared by several projects, the
        // only names NodeKeyFor and ReachedCopy consult a supplier map for. Never written to.
        private static readonly Dictionary<string, string> EmptySuppliers = new(StringComparer.Ordinal);

        private readonly EdgeTable<NodeKey> _catchEdges = new();

        // The two catch narrowings are edge tables of their own rather than plain site maps, because each carries
        // its own per-instance shares: which of a copy's `catch` clauses spell a filter, and which of those hold
        // the failure, are facts about that copy that the edge's union cannot derive.
        private readonly EdgeTable<NodeKey> _catchEdgeSwallowing = new();
        private readonly EdgeTable<NodeKey> _catchEdgeUnfiltered = new();
        private readonly EdgeTable<NodeKey> _constructorEdges = new();
        private readonly EdgeTable<NodeKey> _edges = new();
        private readonly EdgeTable<NodeKey> _exposureEdges = new();
        private readonly Dictionary<NodeKey, FragmentExternal> _externalFacts = new();

        private readonly EdgeTable<NodeKey> _injectionEdges = new();
        private readonly EdgeTable<string> _memberEdges = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);

        // Project name → every framework that won the facts of a type two or more of the project's frameworks
        // declared, ordinal-sorted. An entry exists only where such a collapse actually happened, which is the
        // note's gate: without it a note would claim a winner for a project whose frameworks share nothing.
        private readonly Dictionary<string, SortedSet<string>> _multiFrameworkWinners = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeKey, TypeNode> _nodes = new();

        // Assembly name → the project whose fragments produce it, read to tell which declarer's copy of a shared
        // name a compilation bound.
        private readonly AssemblyProducers _projectsByAssembly = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();

        // Every name a fragment declares has a roster, and nothing else does.
        private readonly Dictionary<string, DeclarerRoster> _rosters = new(StringComparer.Ordinal);

        // Shadowed FQN → every project whose own compilation bound that name from a shadowing assembly rather
        // than from the declaration, ordinal-sorted. Recorded by the supplier pass, never re-derived from the
        // edges, which miss a name no edge was minted for.
        private readonly Dictionary<string, SortedSet<string>> _shadowBinders = new(StringComparer.Ordinal);

        // Shadowed FQN → every assembly that supplied that name to a fragment which did not compile it and which
        // none of its declarers produce, ordinal-sorted, one entry per type so one note line can name them all.
        // This table is both the shadow node's gate and the merge note's content, so the model splits a name
        // exactly where the report says it does.
        private readonly Dictionary<string, SortedSet<string>> _shadowingAssemblies = new(StringComparer.Ordinal);
        private readonly EdgeTable<NodeKey> _throwEdges = new();

        // Whether any roster has a later declarer: the one fact the supplier pass's fast path needs of them all.
        private bool _anyShared;

        private IReadOnlyList<CodebaseFragment> _fragments = [];

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            _fragments = fragments;

            foreach (CodebaseFragment fragment in fragments)
                _projectsByAssembly.Record(fragment.AssemblyName, fragment.ProjectName);

            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                foreach (FragmentType declared in fragment.DeclaredTypes)
                    DeclareMerged(declared, fragment, index);
            }

            // After declaration, so RecordExternal can ask the rosters alone whether anything declares a name,
            // which is what splits an ordinary external from a shadowed name.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // Every shadow gets its node here rather than when an endpoint resolves to it: a name referenced only
            // where extraction keeps its facts as strings, a member's parameter or return type (GRAMMAR §4.6),
            // would otherwise be noted and then not be in Types.
            foreach (KeyValuePair<string, SortedSet<string>> entry in _shadowingAssemblies)
            {
                string fqn = entry.Key;
                SortedSet<string> shadowing = entry.Value;

                foreach (string assembly in shadowing)
                    ResolveNode(new NodeKey(fqn, assembly));
            }

            // Each fragment's answer to "which assembly did I bind this name from", built after the passes above
            // because it records only the names they found in dispute: shadowed, or declared by several projects.
            // A solution with neither, which is nearly every solution, builds no map at all.
            List<Dictionary<string, string>> suppliers = _shadowingAssemblies.Count == 0 && !_anyShared
                ? Enumerable.Repeat(EmptySuppliers, fragments.Count)
                    .ToList()
                : fragments.Select(SuppliersOf)
                    .ToList();

            // Hierarchy and members (GRAMMAR §4.6) from the winning fragment only, the hierarchy resolved through
            // that fragment's bindings: a declarer whose own compilation bound a shadowed name from an assembly
            // reaches the assembly's node, as its edges do. Externals keep their empty member list.
            foreach (DeclarerRoster roster in _rosters.Values)
            {
                PopulateHierarchy(roster.Node, roster.Winning, suppliers[roster.WinningFragment]);
                PopulateMembers(roster.Node, roster.Winning);
            }

            // One fragment at a time rather than one family at a time, so each family reads the supplier map and
            // the project of the fragment that observed it. Nothing here is order-sensitive: every site and
            // instance set is a SortedSet, every map keys on its endpoint pair, the external facts table was frozen
            // above so ResolveNode mints the same facts whenever it first runs, _memberFacts is first-wins over
            // facts that are a function of the SymbolId, and Materialize re-sorts the nodes.
            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                var binding = new FragmentBinding(suppliers[index], fragment.ProjectName);

                foreach (FragmentEdge edge in fragment.Edges)
                    MergeSimpleEdge(binding, edge.SourceFullName, edge.TargetFullName, _edges, edge.Sites);

                foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                    MergeMemberEdge(binding, memberEdge);

                foreach (FragmentConstructorEdge constructorEdge in fragment.ConstructorEdges)
                    MergeSimpleEdge(
                        binding, constructorEdge.SourceFullName, constructorEdge.ConstructedFullName, _constructorEdges,
                        constructorEdge.Sites);

                foreach (FragmentInjectionEdge injectionEdge in fragment.InjectionEdges)
                    MergeSimpleEdge(
                        binding, injectionEdge.SourceFullName, injectionEdge.InjectedFullName, _injectionEdges,
                        injectionEdge.Sites);

                // Registrations (GRAMMAR §4.7) resolve to no node: they are many-to-many and matched model-side at
                // evaluation, so a shadowed name needs nothing here, both its nodes wearing the name.
                foreach (FragmentServiceRegistration registration in fragment.ServiceRegistrations)
                    MergeRegistration(registration);

                foreach (FragmentCatchEdge catchEdge in fragment.CatchEdges)
                    MergeCatchEdge(binding, catchEdge);

                foreach (FragmentThrowEdge throwEdge in fragment.ThrowEdges)
                    MergeSimpleEdge(binding, throwEdge.SourceFullName, throwEdge.ThrownFullName, _throwEdges, throwEdge.Sites);

                foreach (FragmentExposureEdge exposureEdge in fragment.ExposureEdges)
                    MergeSimpleEdge(
                        binding, exposureEdge.SourceFullName, exposureEdge.ExposedFullName, _exposureEdges,
                        exposureEdge.Sites);
            }

            return Materialize();
        }

        private void DeclareMerged(FragmentType declared, CodebaseFragment fragment, int fragmentIndex)
        {
            string fqn = declared.Facts.FullName;
            if (!_rosters.TryGetValue(fqn, out DeclarerRoster? roster))
            {
                var node = declared.Facts.ToTypeNode(fragment.ProjectName, isExternal: false);
                _nodes[NodeKey.Unshadowed(fqn)] = node;
                _rosters[fqn] = roster = new DeclarerRoster(node, declared, fragmentIndex);
            }
            else
            {
                bool sameProject = string.Equals(roster.Winner, fragment.ProjectName, StringComparison.Ordinal);
                if (sameProject) NoteFrameworkCollapse(roster, fragment.TargetFramework);
                else NoteConflation(roster, fragment.ProjectName);

                roster.RecordSites(fragment.ProjectName, declared.DeclarationSites);
            }

            if (fragment.AssemblyName is { Length: > 0 } assemblyName) roster.Assemblies.Add(assemblyName);

            foreach (FragmentSite site in declared.DeclarationSites) roster.Sites.Add(site);
        }

        // The facts and ProjectName keep following the first declarer, while selections and edge attribution
        // reach every declarer through the roster.
        private void NoteConflation(DeclarerRoster roster, string laterProjectName)
        {
            roster.AddLoser(laterProjectName);
            _anyShared = true;
        }

        // One project file's several target frameworks both declaring this FQN, so the type collapses onto the
        // framework extracted first. Each case that records no note is a correctness matter, not an optimisation:
        // with a framework unknown on either side (a single-framework project, a hand-built input) nothing truthful
        // can be said about which won; a type only one framework declares (a #if-guarded class, a
        // framework-conditional <Compile>) keeps its own framework's facts and never reaches here; and two
        // declarations from one framework (two project files sharing a file name and a framework, or a hand-built
        // input listing one compilation twice) collapsed nothing, so a note would name a framework as winning over
        // itself. The winning framework is the winning fragment's, whose facts the type carries.
        private void NoteFrameworkCollapse(DeclarerRoster roster, string? laterFramework)
        {
            if (laterFramework is null) return;
            if (_fragments[roster.WinningFragment].TargetFramework is not { } winningFramework) return;
            if (string.Equals(winningFramework, laterFramework, StringComparison.Ordinal)) return;

            // Every winner is kept, not only the first collapse's: which collapse the merge meets first depends
            // on the order a fragment lists its types, and frameworks that share different sets of types have a
            // winner per set.
            _multiFrameworkWinners.OrdinalNamesFor(roster.Winner)
                .Add(winningFramework);
        }

        // Every type in the set has the same later declarers, so the first type's roster names them.
        private string ConflationNote(DeclarerSet set, IReadOnlyList<string> fqns)
        {
            SortedSet<string> losers = _rosters[fqns[0]].Losers!;
            string declarers = ProseFormat.JoinQuotedAnd([set.Winner, .. losers]);
            string types = ProseFormat.JoinQuotedAnd(fqns);
            string selections = ProseFormat.JoinReferencesAnd([.. losers.Select(loser => $"arch.Project('{loser}')")]);
            string possessive = fqns.Count == 1 ? "its" : "their";
            string pronoun = fqns.Count == 1 ? "it" : "them";

            return $"Projects {declarers} each declare {types}; {possessive} facts and project attribution "
                   + $"follow '{set.Winner}' (the first declarer), but {selections} selections include {pronoun} too, "
                   + "and each declarer's reference to its own compiled-in copy counts against that declarer alone.";
        }

        // One note per multi-framework project whose frameworks share a type. A single winner, which is every
        // project targeting two frameworks, keeps the plain sentence.
        private string FrameworkCollapseNote(string projectName, ProjectArtifacts artifacts)
        {
            SortedSet<string> frameworks = artifacts.CarriedFrameworks;
            SortedSet<string> winners = _multiFrameworkWinners[projectName];
            string targeted = ProseFormat.JoinQuotedAnd([.. frameworks]);

            if (winners.Count == 1)
                return $"Project '{projectName}' targets {targeted}; the types they share take their members and "
                       + $"hierarchy from '{winners.Min}' (the first extracted) but keep every framework's references, "
                       + "so a rule about their members or hierarchy is checked against that framework alone and a "
                       + "rule about what they reference sees every framework.";

            string eachWinner = ProseFormat.JoinQuoted([.. winners]);
            return $"Project '{projectName}' targets {targeted}; each type they share takes its members and "
                   + $"hierarchy from the first of them that declares it ({eachWinner}) but keeps every framework's "
                   + "references, so a rule about its members or hierarchy is checked against that framework "
                   + "alone and a rule about what it references sees every framework.";
        }

        // The conflated types regrouped under the declarer set they share, the winner and its losers, one note per
        // set.
        private Dictionary<DeclarerSet, SortedSet<string>> ConflatedTypesByDeclarerSet()
        {
            Dictionary<DeclarerSet, SortedSet<string>> bySet = new();
            if (!_anyShared) return bySet;

            foreach (KeyValuePair<string, DeclarerRoster> entry in _rosters)
            {
                string fqn = entry.Key;
                DeclarerRoster roster = entry.Value;
                if (roster.Losers is not { } losers) continue;

                var set = new DeclarerSet(roster.Winner, string.Join("\n", losers));
                bySet.OrdinalNamesFor(set)
                    .Add(fqn);
            }

            return bySet;
        }

        // The shadowed names regrouped under the project that declares each of them, with that project's
        // supplying assemblies unioned, one note per project.
        private Dictionary<string, (SortedSet<string> Types, SortedSet<string> Assemblies)> ShadowedNamesByProject()
        {
            Dictionary<string, (SortedSet<string> Types, SortedSet<string> Assemblies)> byProject = new(StringComparer.Ordinal);

            foreach (KeyValuePair<string, SortedSet<string>> shadowed in _shadowingAssemblies)
            {
                string fqn = shadowed.Key;
                SortedSet<string> assemblies = shadowed.Value;

                string declaringProject = _rosters[fqn].Winner;
                if (!byProject.TryGetValue(declaringProject, out (SortedSet<string> Types, SortedSet<string> Assemblies) entry))
                    byProject[declaringProject] = entry = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));

                entry.Types.Add(fqn);
                foreach (string assembly in assemblies) entry.Assemblies.Add(assembly);
            }

            return byProject;
        }

        private static string ShadowedNamesNote(string projectName, (SortedSet<string> Types, SortedSet<string> Assemblies) shadowed)
        {
            string types = ProseFormat.JoinQuotedAnd([.. shadowed.Types]);
            string assemblies = ProseFormat.JoinQuotedAnd([.. shadowed.Assemblies]);
            string noun = Plurals.Noun(shadowed.Assemblies.Count, "assembly");
            string verb = shadowed.Assemblies.Count == 1 ? "supplies" : "supply";

            return $"Project '{projectName}' declares {types}, which referenced {noun} {assemblies} also {verb}; "
                   + "every reference resolves to whichever of the two the referencing compilation bound, so "
                   + $"a selection over project '{projectName}' reaches the declared one alone.";
        }

        private void RecordExternal(FragmentExternal external)
        {
            string fqn = external.Facts.FullName;
            if (!_rosters.TryGetValue(fqn, out DeclarerRoster? roster))
            {
                NodeKey key = NodeKey.Unshadowed(fqn);
                if (!_externalFacts.ContainsKey(key)) _externalFacts[key] = external;
                return;
            }

            NoteShadowIfForeign(fqn, roster, external);
        }

        // The shadow test. Every guard fails open rather than optimising: an unknown supplying assembly (an error
        // symbol with no containing assembly reaches the fragment as ""), no known declaring assembly (a hand-built
        // fragment, or a cached one that lacks the fact), and a supplier that is one of the declaring assemblies,
        // which is every ordinary reference into a project of this solution. Only a known disagreement mints a
        // shadow.
        private void NoteShadowIfForeign(string fqn, DeclarerRoster roster, FragmentExternal external)
        {
            string supplying = external.AssemblyName;
            if (supplying.Length == 0) return;

            HashSet<string> declaring = roster.Assemblies;
            if (declaring.Count == 0 || declaring.Contains(supplying)) return;

            _shadowingAssemblies.OrdinalNamesFor(fqn)
                .Add(supplying);
            var shadowKey = new NodeKey(fqn, supplying);
            if (!_externalFacts.ContainsKey(shadowKey)) _externalFacts[shadowKey] = external;
        }

        // FQN → the assembly THIS fragment bound it from, for the names in dispute it referenced but did not
        // declare (shadowed, or declared by several projects); its externals carry exactly that answer, and no
        // other name is ever asked of the map. A fragment that declared the FQN itself has no entry, which is what
        // leaves a test project's reference to its OWN stand-in alone, and a declarer's reference to its own copy.
        // The binder roster is taken in the same walk, on the test NodeKeyFor makes.
        private Dictionary<string, string> SuppliersOf(CodebaseFragment fragment)
        {
            var suppliers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (FragmentExternal external in fragment.Externals)
            {
                string fqn = external.Facts.FullName;
                bool shadowed = _shadowingAssemblies.TryGetValue(fqn, out SortedSet<string>? shadowing);
                if (!shadowed && SharedRoster(fqn) is null) continue;

                suppliers[fqn] = external.AssemblyName;
                if (shadowed && shadowing!.Contains(external.AssemblyName)) RecordBinder(fqn, fragment.ProjectName);
            }

            return suppliers;
        }

        private void RecordBinder(string fqn, string projectName)
        {
            _shadowBinders.OrdinalNamesFor(fqn)
                .Add(projectName);
        }

        // Which node one fragment's mention of an FQN denotes: its own declared-or-first-declarer node,
        // unless this fragment bound the name from an assembly recorded as shadowing it.
        private NodeKey NodeKeyFor(IReadOnlyDictionary<string, string> suppliers, string fqn)
        {
            if (!_shadowingAssemblies.TryGetValue(fqn, out SortedSet<string>? shadowing)) return NodeKey.Unshadowed(fqn);
            if (!suppliers.TryGetValue(fqn, out string? supplier)) return NodeKey.Unshadowed(fqn);

            return shadowing.Contains(supplier) ? new NodeKey(fqn, supplier) : NodeKey.Unshadowed(fqn);
        }

        private void PopulateHierarchy(TypeNode node, FragmentType declared, IReadOnlyDictionary<string, string> suppliers)
        {
            if (declared.BaseTypeFullName is { } baseFqn) node.BaseType = ResolveNode(NodeKeyFor(suppliers, baseFqn));

            node.Interfaces = declared.Interfaces
                .Select(f => ResolveNode(NodeKeyFor(suppliers, f)))
                .ToList();

            node.Attributes = declared.Attributes
                .Select(f => ResolveNode(NodeKeyFor(suppliers, f)))
                .ToList();

            node.AllInterfaces = declared.AllInterfaces.Select(c => ToConstruction(suppliers, c))
                .ToList();
            node.BaseTypeChain = declared.BaseTypeChain.Select(c => ToConstruction(suppliers, c))
                .ToList();
            node.AttributeConstructions = declared.AttributeConstructions.Select(c => ToConstruction(suppliers, c))
                .ToList();
        }

        private TypeConstruction ToConstruction(IReadOnlyDictionary<string, string> suppliers, FragmentConstruction construction)
        {
            return new TypeConstruction(
                ResolveNode(NodeKeyFor(suppliers, construction.DefinitionFullName)), construction.ConstructedName);
        }

        private static void PopulateMembers(TypeNode node, FragmentType declared)
        {
            node.Members = declared.DeclaredMembers
                .Select(member => member.ToMemberNode(node))
                .ToList();
        }

        // The endpoint protocol every edge family shares: each endpoint mapped to the node THIS fragment's
        // compilation bound it to, ResolveNode on both, and the instance this compilation made of the edge; null
        // for a self-edge, a guard that is defensive because extraction already drops them. A source endpoint is
        // always a type this fragment declared, so NodeKeyFor hands it straight back; mapping it anyway is what
        // makes the guard compare nodes rather than names once one name can denote two.
        private EdgeMerge? ResolveEdge(FragmentBinding binding, string source, string target)
        {
            NodeKey sourceKey = NodeKeyFor(binding.Suppliers, source);
            NodeKey targetKey = NodeKeyFor(binding.Suppliers, target);
            if (sourceKey == targetKey) return null;

            ResolveNode(sourceKey);
            TypeNode targetNode = ResolveNode(targetKey);
            InstanceKey? instance = InstanceOf(binding, sourceKey, targetKey, targetNode);
            return new EdgeMerge((sourceKey, targetKey), instance);
        }

        // Reports the key it merged under and the instance key it kept, or null for the dropped self-edge, so an
        // axis carrying parallel narrowings (catch) gates them on the same guard and records them under the same
        // key rather than restating either.
        private EdgeMerge? MergeSimpleEdge(
            FragmentBinding binding, string source, string target, EdgeTable<NodeKey> table, IReadOnlyList<FragmentSite> sites)
        {
            if (ResolveEdge(binding, source, target) is not { } merged) return null;

            table.Merge(merged.Key, merged.Instance, sites);
            return merged;
        }

        // The instance this fragment's compilation made of one edge (GRAMMAR §4.1), kept only where an end of the
        // edge is a name several projects declare. Copies that drifted apart make different references, so the
        // source node, whose facts are the first declarer's, cannot say which declarers made it; and a project
        // that declares no copy of a shared target binds the copy of whichever declarer it references, which the
        // target node cannot say either. An edge between names one project each declares keeps no record, because
        // its two nodes already name its one instance. A target bound from an assembly shadowing the name is the
        // assembly's node, not a shared target, however many projects declare the name.
        private InstanceKey? InstanceOf(FragmentBinding binding, NodeKey sourceKey, NodeKey targetKey, TypeNode targetNode)
        {
            bool sharedSource = SharedRoster(sourceKey.FullName) is not null;
            DeclarerRoster? sharedTarget = targetKey.SupplyingAssembly is null ? SharedRoster(targetKey.FullName) : null;
            if (!sharedSource && sharedTarget is null) return null;

            // A shared target's node is the first declarer's, so the copy it reached comes from ReachedCopy.
            string reached = sharedTarget is not null
                ? ReachedCopy(binding, targetKey.FullName, sharedTarget)
                : targetNode.ProjectName;

            return new InstanceKey(binding.ProjectName, reached);
        }

        // The roster of a name several projects declare, or null. Until some name gains a later declarer no roster
        // is shared, so a solution that declares no name twice answers from one flag and hashes no name per edge.
        private DeclarerRoster? SharedRoster(string fqn)
        {
            if (!_anyShared) return null;

            return _rosters.TryGetValue(fqn, out DeclarerRoster? roster) && roster.IsShared ? roster : null;
        }

        // Whose copy of a name several projects declare one compilation bound, answered per compilation from its
        // supplier map: a compilation that declares the name records no external for it and binds its own copy,
        // since it binds its own source ahead of any referenced assembly's; one that records an external bound the
        // copy of the declarer producing that assembly. Asking the project instead would be wrong for a
        // multi-targeted project whose #if declares the name under one framework only. Every unknown falls back to
        // the first declarer, the copy whose facts the node carries.
        private string ReachedCopy(FragmentBinding binding, string fqn, DeclarerRoster roster)
        {
            if (!binding.Suppliers.TryGetValue(fqn, out string? assembly))
                return roster.Declares(binding.ProjectName) ? binding.ProjectName : roster.Winner;

            if (!_projectsByAssembly.TryGetProducer(assembly, out string? supplier)) return roster.Winner;

            return roster.Declares(supplier!) ? supplier! : roster.Winner;
        }

        // A use is attributed the way an edge into the member's declaring type is, through ResolveEdge, so a rule
        // owning one copy's instances reads that copy's uses.
        private void MergeMemberEdge(FragmentBinding binding, FragmentMemberEdge edge)
        {
            if (ResolveEdge(binding, edge.SourceFullName, edge.TargetContainingTypeFullName) is not { } merged) return;

            // Member facts are functions of the SymbolId, so the first mention wins and every later one agrees.
            // The containing type is stored as the NODE key BuildMemberEdges resolves with, or a member on a
            // shadowed name would always hang on the source declaration, whichever assembly the reference bound.
            if (!_memberFacts.ContainsKey(edge.MemberSymbolId))
                _memberFacts[edge.MemberSymbolId] =
                    new MemberEdgeFacts(merged.Key.Target, edge.MemberName, edge.MemberKind);

            _memberEdges.Merge((merged.Key.Source, edge.MemberSymbolId), merged.Instance, edge.Sites);
        }

        private void MergeCatchEdge(FragmentBinding binding, FragmentCatchEdge edge)
        {
            if (MergeSimpleEdge(binding, edge.SourceFullName, edge.CaughtFullName, _catchEdges, edge.Sites)
                is not { } merged) return;

            // Both narrowings union under the same key, the same guard and the same INSTANCE key, so a rule that owns
            // one copy's instance of the edge reads that copy's sites. Unioning is the truthful answer for a ban
            // across `#if`-divergent fragments: a (file, line) filtered in one and unfiltered in another reads
            // unfiltered, one that rethrows in one and swallows in another reads swallowing, and per instance too,
            // since the frameworks that disagree are one project and so one instance.
            _catchEdgeUnfiltered.Merge(merged.Key, merged.Instance, edge.UnfilteredSites);

            _catchEdgeSwallowing.Merge(merged.Key, merged.Instance, edge.SwallowingSites);
        }

        private void MergeRegistration(FragmentServiceRegistration registration)
        {
            SortedSet<FragmentSite> sites = _registrationSites.SitesFor(
                (registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName));
            foreach (FragmentSite site in registration.Sites) sites.Add(site);
        }

        /// <summary>
        ///     The node <paramref name="nodeKey" /> denotes, minting a shallow external from the first-fragment-wins
        ///     facts table on first ask. Every key reachable here was recorded by whichever fragment references
        ///     it, so the table always has an entry when the key names no declaration.
        /// </summary>
        private TypeNode ResolveNode(NodeKey nodeKey)
        {
            if (_nodes.TryGetValue(nodeKey, out TypeNode? node)) return node;

            FragmentExternal external = _externalFacts[nodeKey];
            node = external.Facts.ToTypeNode(external.AssemblyName, isExternal: true);
            _nodes[nodeKey] = node;
            return node;
        }

        private CodebaseModel Materialize()
        {
            foreach (DeclarerRoster roster in _rosters.Values)
            {
                TypeNode node = roster.Node;
                node.DeclarationSites = roster.Sites.ToLocations();
                node.FilePaths = roster.Sites.FilePaths();

                if (roster.Losers is { } losers) node.AlsoDeclaredBy = losers.ToList();

                if (roster.SitesByDeclarer is not { } byDeclarer) continue;

                node.DeclarationSitesByDeclarer = byDeclarer.ToDictionary(
                    entry => entry.Key,
                    entry => FragmentSiteSets.Subsequence(roster.Sites, node.DeclarationSites, entry.Value),
                    StringComparer.Ordinal);
            }

            // One name can denote a source declaration and a referenced assembly's type of that name, so FullName
            // alone is not a total order: the declaration sorts first, then the supplying assemblies ordinal, and
            // without the tie-break the remainder falls to dictionary enumeration order, costing every rendered
            // document its byte-stability.
            List<TypeNode> types = _nodes.Values
                .OrderBy(n => n.FullName, StringComparer.Ordinal)
                .ThenBy(n => n.IsExternal ? 1 : 0)
                .ThenBy(n => n.ProjectName, StringComparer.Ordinal)
                .ToList();

            List<ReferenceEdge> edges = BuildEdges(
                _edges, (src, tgt, evidence) => new ReferenceEdge(src, tgt, evidence));

            List<MemberEdge> memberEdges = BuildMemberEdges();

            List<ConstructorEdge> constructorEdges = BuildEdges(
                _constructorEdges, (src, ctor, evidence) => new ConstructorEdge(src, ctor, evidence));

            List<InjectionEdge> injectionEdges = BuildEdges(
                _injectionEdges,
                (src, injected, evidence) => new InjectionEdge(src, injected, evidence));

            List<CatchEdge> catchEdges = _catchEdges.Sites.OrderedPairs(
                NodeKey.Ordinal, NodeKey.Ordinal, BuildCatchEdge);

            List<ThrowEdge> throwEdges = BuildEdges(
                _throwEdges, (src, thrown, evidence) => new ThrowEdge(src, thrown, evidence));

            List<ExposureEdge> exposureEdges = BuildEdges(
                _exposureEdges, (src, exposed, evidence) => new ExposureEdge(src, exposed, evidence));

            List<ServiceRegistration> serviceRegistrations =
                _registrationSites.OrderedRegistrations((lifetime, service, impl, sites) =>
                    new ServiceRegistration(lifetime, service, impl, sites.ToLocations()));

            Dictionary<string, ProjectArtifacts> artifactsByProject = ArtifactsByProject();

            // In the order CodebaseModel.MergeNotes documents, each kind sorted on the key it groups by, so the list
            // is stable across runs regardless of the order the distinct notes were first raised.
            IEnumerable<string> frameworkCollapseNotes = _multiFrameworkWinners.Keys
                .OrderBy(projectName => projectName, StringComparer.Ordinal)
                .Select(projectName => FrameworkCollapseNote(projectName, artifactsByProject[projectName]));
            IEnumerable<string> shadowedNameNotes = ShadowedNamesByProject()
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => ShadowedNamesNote(entry.Key, entry.Value));
            IEnumerable<string> conflationNotes = ConflatedTypesByDeclarerSet()
                .OrderBy(entry => entry.Key.Winner, StringComparer.Ordinal)
                .ThenBy(entry => entry.Key.LosersKey, StringComparer.Ordinal)
                .Select(entry => ConflationNote(entry.Key, entry.Value.ToList()));
            List<string> mergeNotes = frameworkCollapseNotes
                .Concat(shadowedNameNotes)
                .Concat(conflationNotes)
                .ToList();

            return new CodebaseModel(
                types, edges, memberEdges, constructorEdges, injectionEdges, catchEdges, throwEdges,
                exposureEdges, serviceRegistrations, BuildProjects(artifactsByProject), BuildShadowedNames(), mergeNotes);
        }

        private List<TEdge> BuildEdges<TEdge>(
            EdgeTable<NodeKey> table, Func<TypeNode, TypeNode, EdgeEvidence, TEdge> make)
        {
            return table.Sites.OrderedPairs(
                NodeKey.Ordinal, NodeKey.Ordinal,
                (src, tgt, sites) => make(_nodes[src], _nodes[tgt], table.EvidenceOf((src, tgt), sites)));
        }

        // All three site lists come out of SortedSets, so each is (file, line) ordered and a subset of the one
        // before it; an edge with no unfiltered (or no swallowing) site materializes the empty list.
        private CatchEdge BuildCatchEdge(NodeKey src, NodeKey caught, SortedSet<FragmentSite> sites)
        {
            (NodeKey Source, NodeKey Caught) key = (src, caught);

            EdgeEvidence evidence = _catchEdges.EvidenceOf(key, sites);
            EdgeEvidence unfiltered = _catchEdgeUnfiltered.NarrowingOf(key);
            EdgeEvidence swallowing = _catchEdgeSwallowing.NarrowingOf(key);

            return new CatchEdge(_nodes[src], _nodes[caught], evidence, unfiltered, swallowing);
        }

        // The binder roster is indexed rather than looked up defensively: an entry in _shadowingAssemblies
        // exists only because some fragment's external minted it, and that same fragment's supplier pass
        // records it as a binder, so a shadowed name with no binder is not a state the merge can reach.
        private List<ShadowedName> BuildShadowedNames()
        {
            return _shadowingAssemblies
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ShadowedName(
                    entry.Key,
                    _rosters[entry.Key].Winner,
                    entry.Value.ToList(),
                    _shadowBinders[entry.Key].ToList()))
                .ToList();
        }

        // A single MemberReference is minted per distinct SymbolId, so equal members across edges share the instance.
        private List<MemberEdge> BuildMemberEdges()
        {
            var memberReferences = new Dictionary<string, MemberReference>(StringComparer.Ordinal);

            MemberReference MemberReferenceFor(string symbolId)
            {
                if (memberReferences.TryGetValue(symbolId, out MemberReference? reference)) return reference;

                MemberEdgeFacts facts = _memberFacts[symbolId];
                reference = new MemberReference(_nodes[facts.ContainingNodeKey], facts.Name, symbolId, facts.Kind);
                memberReferences[symbolId] = reference;
                return reference;
            }

            return _memberEdges.Sites.OrderedPairs(
                NodeKey.Ordinal, StringComparer.Ordinal,
                (src, symbolId, sites) => new MemberEdge(
                    _nodes[src], MemberReferenceFor(symbolId), _memberEdges.EvidenceOf((src, symbolId), sites)));
        }

        // Every fragment folded into its project's artifacts, one entry per project.
        private Dictionary<string, ProjectArtifacts> ArtifactsByProject()
        {
            Dictionary<string, ProjectArtifacts> artifactsByProject = new(StringComparer.Ordinal);
            foreach (CodebaseFragment fragment in _fragments)
            {
                if (!artifactsByProject.TryGetValue(fragment.ProjectName, out ProjectArtifacts? artifacts))
                    artifactsByProject[fragment.ProjectName] = artifacts = new ProjectArtifacts();

                artifacts.Absorb(fragment);
            }

            return artifactsByProject;
        }

        // Every per-project table the merge keeps reaches the model here or nowhere: a table this method does not
        // read is lost.
        private List<ProjectNode> BuildProjects(Dictionary<string, ProjectArtifacts> artifactsByProject)
        {
            Dictionary<string, SortedSet<string>> refsByProject = new(StringComparer.Ordinal);
            Dictionary<string, SortedSet<string>> undeclaredByProject = new(StringComparer.Ordinal);
            foreach (CodebaseFragment fragment in _fragments)
            {
                SortedSet<string> refs = refsByProject.OrdinalNamesFor(fragment.ProjectName);
                foreach (string reference in fragment.ProjectReferences) refs.Add(reference);

                SortedSet<string> undeclared = undeclaredByProject.OrdinalNamesFor(fragment.ProjectName);
                foreach (string reference in fragment.UndeclaredProjectReferences ?? []) undeclared.Add(reference);
            }

            return refsByProject
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => BuildProject(kv.Key, kv.Value, undeclaredByProject[kv.Key], artifactsByProject[kv.Key]))
                .ToList();
        }

        // A name one compilation of a multi-targeted project left undeclared and another declared is declared:
        // the row states the project, not one compilation of it, and must not name a reference in both lists.
        private ProjectNode BuildProject(
            string projectName,
            SortedSet<string> projectReferences,
            SortedSet<string> undeclaredReferences,
            ProjectArtifacts artifacts)
        {
            _multiFrameworkWinners.TryGetValue(projectName, out SortedSet<string>? winners);
            undeclaredReferences.ExceptWith(projectReferences);

            return new ProjectNode(
                name: projectName,
                projectReferences: projectReferences.ToList(),
                undeclaredProjectReferences: undeclaredReferences.ToList(),
                solutionMember: artifacts.SolutionMember,
                targetFrameworks: artifacts.TargetFrameworksOrCarried(),
                targetFrameworksSite: artifacts.TargetFrameworksSite.ToLocation(),
                factsFollow: winners?.Min,
                packageReferences: artifacts.PackageReferences(),
                isPackable: artifacts.IsPackable,
                isPackableSite: artifacts.IsPackableSite.ToLocation(),
                locksPackages: artifacts.LocksPackages,
                locksPackagesSite: artifacts.LocksPackagesSite.ToLocation(),
                assemblyNames: artifacts.AssemblyNames(),
                evaluations: artifacts.Evaluations());
        }

        /// <summary>
        ///     One tri-state artifact fact part-way through a fold: the verdict so far, <see langword="null" />
        ///     while no fragment has stated one, and the site of the fragment that decided it, which travel
        ///     together because the site means nothing apart from the verdict it belongs to.
        /// </summary>
        private readonly record struct ArtifactVerdict(bool? Value, FragmentSite? Site)
        {
            /// <summary>
            ///     <paramref name="incoming" /> folded in: adopt the first verdict any fragment states, then
            ///     upgrade to <paramref name="decisive" /> and take that fragment's site with it. A fragment
            ///     that states nothing changes nothing, and a fold already at the decisive value is settled —
            ///     so a union and an intersection are this one algorithm read from opposite ends.
            /// </summary>
            internal ArtifactVerdict Fold(bool? incoming, FragmentSite? incomingSite, bool decisive)
            {
                if (incoming is not { } verdict) return this;
                if (Value == decisive) return this;
                if (Value is null || verdict == decisive) return new ArtifactVerdict(verdict, incomingSite);

                return this;
            }
        }

        /// <summary>
        ///     One project's artifact facts folded across its fragments: the evaluated half of a
        ///     <see cref="ProjectNode" />.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         Each fact folds in the direction that cannot invent a clean answer. A package any framework
        ///         declares is a package the project declares, so the references union, and which frameworks
        ///         declared it unions beside them, from each evaluation whose framework is known. Packability unions
        ///         for the same reason read the other way round: if one framework produces a package, the project
        ///         ships one. The lock policy <em>intersects</em>: a framework whose restore writes no lock file
        ///         leaves that restore unlocked however the others are configured.
        ///     </para>
        ///     <para>
        ///         Unknown loses to either verdict: a fragment nothing evaluated has nothing to contradict one that
        ///         was, so a project is unknown only when every one of its fragments was. Solution membership folds
        ///         the same way: a multi-targeted project arrives as one fragment per framework under one name, so
        ///         declared by any fragment is declared.
        ///     </para>
        ///     <para>
        ///         The site follows the fragment that decides the verdict, so the packable project's site is the
        ///         framework that packs and the unlocked project's site is the framework that does not: the
        ///         <c>file:line</c> a violation about either would have to name. Fragments arrive in a fixed order,
        ///         so this is deterministic even where every fragment agrees.
        ///     </para>
        /// </remarks>
        private sealed class ProjectArtifacts
        {
            private readonly SortedSet<string> _assemblyNames = new(StringComparer.Ordinal);
            private readonly SortedSet<string> _frameworks = new(StringComparer.Ordinal);
            private readonly List<FragmentPackageReference> _packages = [];
            private readonly Dictionary<string, SortedSet<string>> _packageFrameworks = new(StringComparer.Ordinal);

            // One evaluation per framework told apart, keyed by the moniker and by the empty string for an evaluation
            // no framework was told apart in. Every compilation of one framework read one project file under the
            // same conditions, so the first to report stands for the rest.
            private readonly Dictionary<string, (string? Framework, FragmentProperties Properties)> _evaluations =
                new(StringComparer.Ordinal);

            private ArtifactVerdict _packable;
            private ArtifactVerdict _locks;
            private ArtifactVerdict _member;

            internal bool? IsPackable => _packable.Value;

            internal FragmentSite? IsPackableSite => _packable.Site;

            internal bool? LocksPackages => _locks.Value;

            internal FragmentSite? LocksPackagesSite => _locks.Site;

            internal bool? SolutionMember => _member.Value;

            internal FragmentSite? TargetFrameworksSite { get; private set; }

            // Every target framework any of the project's fragments carried, so a note can name the project's whole
            // framework set.
            internal SortedSet<string> CarriedFrameworks { get; } = new(StringComparer.Ordinal);

            internal void Absorb(CodebaseFragment fragment)
            {
                foreach (string framework in fragment.DeclaredTargetFrameworks ?? []) _frameworks.Add(framework);
                if (fragment.TargetFramework is { } carried) CarriedFrameworks.Add(carried);
                if (fragment.TargetFrameworksSite is { } frameworksSite && TargetFrameworksSite is null)
                    TargetFrameworksSite = frameworksSite;

                // A project states one assembly name per compilation, collapsed to a set here. A fragment that
                // never learned its own name (a hand-built input, or a cached fragment that lacks the fact)
                // contributes nothing rather than an empty name.
                if (!string.IsNullOrWhiteSpace(fragment.AssemblyName)) _assemblyNames.Add(fragment.AssemblyName!);

                string? evaluated = EvaluatedFramework(fragment);
                _packages.AddRange(fragment.PackageReferences ?? []);
                if (evaluated is not null)
                    foreach (FragmentPackageReference package in fragment.PackageReferences ?? [])
                        _packageFrameworks.OrdinalNamesFor(package.Name).Add(evaluated);

                _packable = _packable.Fold(fragment.IsPackable, fragment.IsPackableSite, decisive: true);
                _locks = _locks.Fold(fragment.LocksPackages, fragment.LocksPackagesSite, decisive: false);

                // Membership has no site of its own, so it folds against a null site.
                _member = _member.Fold(fragment.SolutionMember, null, decisive: true);

                if (fragment.Properties is { } properties && !_evaluations.ContainsKey(evaluated ?? string.Empty))
                    _evaluations[evaluated ?? string.Empty] = (evaluated, properties);
            }

            // What the project declares, whatever it compiled to, so a solution filter that extracted one of a
            // project's two frameworks still reports both. The frameworks its fragments carried are only the fallback:
            // they exist only where a load told the compilations apart, so a project nothing evaluated says what it can
            // and a hand-built input says nothing.
            internal IReadOnlyList<string>? TargetFrameworksOrCarried()
            {
                if (_frameworks.Count > 0) return _frameworks.ToList();

                return CarriedFrameworks.Count > 0 ? CarriedFrameworks.ToList() : null;
            }

            internal IReadOnlyList<string> AssemblyNames()
            {
                return _assemblyNames.ToList();
            }

            internal IReadOnlyList<PackageReference> PackageReferences()
            {
                return FragmentSiteSets.OrderedPackages(_packages)
                    .Select(package => new PackageReference(
                        package.Name,
                        package.Site.ToLocation(),
                        package.IsPrivate,
                        _packageFrameworks.TryGetValue(package.Name, out SortedSet<string>? frameworks)
                            ? frameworks.ToList()
                            : null))
                    .ToList();
            }

            internal IReadOnlyList<ProjectEvaluation> Evaluations()
            {
                return _evaluations
                    .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => Evaluation(entry.Value.Framework, entry.Value.Properties))
                    .ToList();
            }

            private static ProjectEvaluation Evaluation(string? framework, FragmentProperties properties)
            {
                List<EvaluatedProperty> declared = properties.Declared
                    .Select(property => new EvaluatedProperty(
                        property.Name, property.Value, framework, property.Site.ToLocation()))
                    .ToList();

                return new ProjectEvaluation(framework, properties.ProjectFile.ToLocation(), declared);
            }

            // An evaluation run with no framework chosen answers for the project's own framework where it declares
            // exactly one. A multi-targeted project's compilations that a replay could not tell apart are evaluated
            // that way too, and that evaluation saw no framework's conditions at all, so its declarations belong to
            // no framework in particular and stay unattributed.
            private static string? EvaluatedFramework(CodebaseFragment fragment)
            {
                if (fragment.TargetFramework is { } requested) return requested;

                return fragment.DeclaredTargetFrameworks is { Count: 1 } single ? single[0] : null;
            }
        }

        /// <summary>The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId.</summary>
        private readonly record struct MemberEdgeFacts(NodeKey ContainingNodeKey, string Name, MemberKind Kind);

        /// <summary>Everything the merge records about the projects that declare one name.</summary>
        private sealed class DeclarerRoster
        {
            internal DeclarerRoster(TypeNode node, FragmentType winning, int winningFragment)
            {
                Node = node;
                Winning = winning;
                WinningFragment = winningFragment;
            }

            /// <summary>The winner's node: the one the node table holds under the name, unshadowed.</summary>
            internal TypeNode Node { get; }

            /// <summary>The first declarer's project, which the node's facts and attribution follow.</summary>
            internal string Winner => Node.ProjectName;

            /// <summary>The winning declaration, whose hierarchy and members the node carries.</summary>
            internal FragmentType Winning { get; }

            /// <summary>
            ///     The index of the fragment that made <see cref="Winning" />, whose own bindings its hierarchy is
            ///     resolved through and whose framework its facts are.
            /// </summary>
            internal int WinningFragment { get; }

            /// <summary>
            ///     Every project that declared the name after the winner under a different project name, one entry
            ///     per project, or <see langword="null" /> while only one project declares it.
            /// </summary>
            internal SortedSet<string>? Losers { get; private set; }

            internal bool IsShared => Losers is not null;

            /// <summary>Every declarer's declaration sites in one union: what the model's node carries.</summary>
            internal SortedSet<FragmentSite> Sites { get; } = [];

            /// <summary>
            ///     Each declaring project's own share of <see cref="Sites" />, kept only once a second project
            ///     declares the name.
            /// </summary>
            internal Dictionary<string, SortedSet<FragmentSite>>? SitesByDeclarer { get; private set; }

            /// <summary>
            ///     Every assembly name a fragment declaring the name was compiled into: the declaration half of
            ///     the shadow test.
            /// </summary>
            /// <remarks>
            ///     EVERY declarer's, not just the winner's, because a name one project wins on ordinal order
            ///     can be bound from a sibling project that declares it too, and that is an ordinary
            ///     intra-solution reference rather than a shadow. An unknown assembly name contributes nothing,
            ///     so a set that stays empty answers "unknown" and the test fails open.
            /// </remarks>
            internal HashSet<string> Assemblies { get; } = new(StringComparer.Ordinal);

            internal bool Declares(string project)
            {
                return string.Equals(Winner, project, StringComparison.Ordinal) || Losers?.Contains(project) == true;
            }

            // The first later declarer seeds the per-declarer table with every site recorded so far, all of them the
            // winner's project's. Called before that declaration's sites join the union, so the seed is exactly the
            // earlier ones.
            internal void AddLoser(string project)
            {
                Losers ??= new SortedSet<string>(StringComparer.Ordinal);
                Losers.Add(project);
                SitesByDeclarer ??= new Dictionary<string, SortedSet<FragmentSite>>(StringComparer.Ordinal)
                {
                    [Winner] = new(Sites, Sites.Comparer)
                };
            }

            // Several frameworks of one project fill one share, and a linked file lands in the share of every project
            // that compiles it.
            internal void RecordSites(string project, IReadOnlyList<FragmentSite> sites)
            {
                if (SitesByDeclarer is null) return;

                SortedSet<FragmentSite> share = SitesByDeclarer.SitesFor(project);
                foreach (FragmentSite site in sites) share.Add(site);
            }
        }

        /// <summary>
        ///     One edge family's merge tables, both keyed on the edge's source node and the target half of its key:
        ///     the sites every edge carries, and — for an edge an end of which several projects declare — the
        ///     sites each of the instances the compilations made carries on its own.
        /// </summary>
        /// <typeparam name="TTarget">
        ///     What the edge reaches: the target's <see cref="NodeKey" /> for the families that pair two types,
        ///     and the member's SymbolId for a member use, whose instances are attributed to the member's
        ///     declaring type.
        /// </typeparam>
        /// <remarks>
        ///     <para>
        ///         An instance's own sites are recorded as they arrive rather than derived later, because the union
        ///         alone cannot say which copy made which line. Fragments recording under one instance key union
        ///         their sites, which is how the frameworks of one multi-targeted project stay one instance.
        ///     </para>
        ///     <para>
        ///         The instance table is minted on the first record, so a family whose edges all run between
        ///         names one project each declares allocates nothing beside its sites. Instances materialize
        ///         ordinal by the project that made them, then by the project whose copy they reached.
        ///     </para>
        /// </remarks>
        private sealed class EdgeTable<TTarget>
            where TTarget : notnull
        {
            private Dictionary<(NodeKey Source, TTarget Target), Dictionary<InstanceKey, SortedSet<FragmentSite>>>?
                _instances;

            internal Dictionary<(NodeKey Source, TTarget Target), SortedSet<FragmentSite>> Sites { get; } = new();

            // One call feeds the edge's own union and, where this merge keeps a record, the instance's, so a site
            // an instance made is always a site the edge has, which lets the materialized instance share the
            // edge's own list.
            internal void Merge(
                (NodeKey Source, TTarget Target) key, InstanceKey? instance, IReadOnlyList<FragmentSite> sites)
            {
                SortedSet<FragmentSite> merged = Sites.SitesFor(key);
                foreach (FragmentSite site in sites) merged.Add(site);

                if (instance is not { } made) return;

                _instances ??= new Dictionary<(NodeKey Source, TTarget Target), Dictionary<InstanceKey, SortedSet<FragmentSite>>>();
                if (!_instances.TryGetValue(key, out Dictionary<InstanceKey, SortedSet<FragmentSite>>? byInstance))
                    _instances[key] = byInstance = new Dictionary<InstanceKey, SortedSet<FragmentSite>>();

                SortedSet<FragmentSite> instanceSites = byInstance.SitesFor(made);
                foreach (FragmentSite site in sites) instanceSites.Add(site);
            }

            /// <summary>
            ///     One edge's evidence: its materialized site list, and the instances recorded for it, each
            ///     carrying its own share of that list, or <see langword="null" /> where nothing was recorded —
            ///     which is what <see cref="EdgeInstances" /> reads as the edge's own endpoints.
            /// </summary>
            /// <remarks>
            ///     <paramref name="sites" /> must be this key's entry in <see cref="Sites" />. The list is
            ///     materialized ONCE for both halves, because an instance's share is a subsequence of it by
            ///     reference.
            /// </remarks>
            internal EdgeEvidence EvidenceOf((NodeKey Source, TTarget Target) key, SortedSet<FragmentSite> sites)
            {
                IReadOnlyList<SourceLocation> locations = sites.ToLocations();
                return new EdgeEvidence(locations, InstancesOf(key, sites, locations));
            }

            /// <summary>
            ///     One NARROWING of another table's edge, materialized whole: its own site list and its own
            ///     per-instance shares of that list, in the instance order every table materializes in — so a
            ///     rule that owns the edge's i-th instance reads this list's i-th entry.
            /// </summary>
            /// <remarks>
            ///     The alignment holds because <see cref="Merge" /> creates an instance's set before adding any
            ///     site, so a copy that made none of the narrowing's sites still carries an entry, and its share is
            ///     the empty list.
            /// </remarks>
            internal EdgeEvidence NarrowingOf((NodeKey Source, TTarget Target) key)
            {
                return Sites.TryGetValue(key, out SortedSet<FragmentSite>? sites)
                    ? EvidenceOf(key, sites)
                    : new EdgeEvidence([]);
            }

            private IReadOnlyList<EdgeInstance>? InstancesOf(
                (NodeKey Source, TTarget Target) key, SortedSet<FragmentSite> all,
                IReadOnlyList<SourceLocation> allLocations)
            {
                if (_instances is null
                    || !_instances.TryGetValue(key, out Dictionary<InstanceKey, SortedSet<FragmentSite>>? byInstance))
                    return null;

                return byInstance
                    .OrderBy(entry => entry.Key.SourceProject, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.TargetProject, StringComparer.Ordinal)
                    .Select(entry => new EdgeInstance(
                        entry.Key.SourceProject, entry.Key.TargetProject,
                        FragmentSiteSets.Subsequence(all, allLocations, entry.Value)))
                    .ToList();
            }
        }

        /// <summary>
        ///     What one fragment's edges are merged through: the assembly it bound each name in dispute from, and
        ///     the project that compiled it.
        /// </summary>
        private readonly record struct FragmentBinding(IReadOnlyDictionary<string, string> Suppliers, string ProjectName);

        /// <summary>
        ///     Which instance of an edge one fragment's compilation made: the project that compiled the
        ///     reference, and the project whose copy of the target it reached (GRAMMAR §4.1).
        /// </summary>
        private readonly record struct InstanceKey(string SourceProject, string TargetProject);

        /// <summary>
        ///     One fragment's edge with its endpoints resolved to nodes: the endpoint pair, which a family pairing
        ///     two types records under, and the instance this fragment's compilation made of it. A member use and a
        ///     narrowing record with both rather than recomputing either.
        /// </summary>
        /// <remarks>
        ///     Both catch subsets key on this NODE pair, not on the names, because the materialization looks them up
        ///     with the keys the edge table holds: a subset keyed on anything else would silently miss on a
        ///     shadowed name and read as "no unfiltered site here".
        /// </remarks>
        private readonly record struct EdgeMerge((NodeKey Source, NodeKey Target) Key, InstanceKey? Instance);

        /// <summary>
        ///     The projects that declare one conflated type: the winner and, as one ordinal-joined string, every
        ///     loser. Two types share a note exactly when their sets are equal, and the join is what makes the set
        ///     a value: <c>'\n'</c> cannot occur in a project name, so equal keys mean equal loser lists.
        /// </summary>
        private readonly record struct DeclarerSet(string Winner, string LosersKey);

        /// <summary>
        ///     Which node a mention of a name denotes: the fully-qualified name, plus the assembly supplying it
        ///     where the name is one a project declares and a referenced assembly shadows.
        /// </summary>
        /// <remarks>
        ///     The merge's two key spaces, told apart by type. A table indexed by <em>name</em> — the rosters,
        ///     <c>_shadowingAssemblies</c> — stays keyed by string, so every crossing into the node tables spells
        ///     <see cref="Unshadowed" /> and a crossing left out does not compile. Equality is ordinal on both fields
        ///     (record struct equality over strings), so the tables keyed on it take no comparer argument.
        /// </remarks>
        private readonly record struct NodeKey(string FullName, string? SupplyingAssembly)
        {
            /// <summary>
            ///     The node a name denotes where nothing shadows the binding: the declaration wherever anything
            ///     declares the name, else the one external node minted for it.
            /// </summary>
            public static NodeKey Unshadowed(string fullName)
            {
                return new NodeKey(fullName, null);
            }

            /// <summary>
            ///     The order every table pairing endpoints materializes in: ordinal by name, then a name's
            ///     declaration ahead of its shadows and those ordinal by supplying assembly.
            /// </summary>
            public static IComparer<NodeKey> Ordinal { get; } = Comparer<NodeKey>.Create(CompareOrdinal);

            // Null sorts first, which is exactly what puts the declaration ahead of the assembly's half
            // (string.CompareOrdinal answers -1 for a null left operand).
            private static int CompareOrdinal(NodeKey left, NodeKey right)
            {
                int byName = string.CompareOrdinal(left.FullName, right.FullName);
                return byName != 0 ? byName : string.CompareOrdinal(left.SupplyingAssembly, right.SupplyingAssembly);
            }
        }
    }
}
