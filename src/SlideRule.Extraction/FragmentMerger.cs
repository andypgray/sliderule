using SlideRule.Codebase;
using SlideRule.Extraction.Fragments;
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
///         Two rules run across every axis, and each step below states only how its own axis instantiates
///         them. Per-type <b>facts</b> — a node's shape and <c>ProjectName</c>, its hierarchy, its member
///         inventory — are <em>winner-only</em>: fragments are visited in input (ordinal-project) order and
///         the first declarer wins, so a later fragment never overwrites what an earlier one established.
///         Per-edge <b>evidence</b> is <em>unioned</em>: each axis keys its site-set on the edge's endpoint
///         pair, drops the self-edge, and resolves both endpoints to the same <see cref="TypeNode" />
///         instances <see cref="CodebaseModel.Types" /> holds — so an external endpoint is one shared node
///         however many fragments referenced it.
///     </para>
///     <para>
///         Declaring the nodes fully precedes populating their hierarchy, and that ordering is
///         essential: it is what makes a type any fragment declares beat every fragment's external view
///         of it — with one qualification, which is the whole of the rule below. A fragment's external view
///         is only <em>of</em> that declaration when the assembly it bound the name from is one the declaring
///         fragments produce. Where it is not, the two are different types wearing one name.
///     </para>
///     <para>
///         So a fully-qualified name can denote two nodes: the source declaration, and a shallow external
///         attributed to the supplying assembly, where the assembly a fragment bound the name from is one
///         no declaring fragment produces. Each fragment's endpoints resolve to whichever its compilation
///         actually bound, on every axis including the hierarchy, and the split is disclosed as an advisory
///         <see cref="CodebaseModel.MergeNotes">merge note</see> and again in the survey. Internally every
///         table pairing endpoints keys on a <c>NodeKey</c> — the name, plus the supplying assembly where
///         it is the shadow — never on the name alone.
///     </para>
///     <para>
///         A later declarer under a <em>different</em> project name is same-FQN cross-project conflation:
///         facts still follow the first declarer, recorded as a merge note and as
///         <see cref="TypeNode.AlsoDeclaredBy" /> on the winning node — the queryable form a consumer that
///         must act on the conflation reads instead of the prose. Declaration sites union across the
///         declarers, and each declarer's own sites are kept beside the union, because a card's placement
///         counts only the copy a project-headed selection names. An edge either end of which is such a name
///         keeps its instances beside its sites — which project's compilation made it, and whose copy of the
///         target that compilation bound — because copies that drifted apart make different references, and a
///         project that declares no copy binds the copy of whichever declarer it references.
///     </para>
///     <para>
///         A later declarer under the <em>same</em> project name is one project file's several target
///         frameworks, which union into one project. Their types union too, and their references union like
///         every edge, whichever framework's compilation made them; but a type two frameworks both declare
///         can only carry one framework's members and hierarchy — the first extracted — so where that
///         actually happens a second kind of merge note records it, one line per project rather than per
///         type. A framework-exclusive type (a <c>#if</c>-guarded class) keeps its own framework's facts and
///         is not what the note is about, so a project whose frameworks share no type stays silent.
///     </para>
///     <para>
///         Where one external FQN carries different facts across compilations (mixed assembly
///         versions), the winner is the first fragment in input order that <em>references</em> it. With a
///         uniform reference closure — the norm — the facts are identical whichever fragment wins, so no
///         model observable distinguishes them; the rule exists to make the tie deterministic rather than
///         dictionary-iteration order.
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
    ///     <paramref name="excludeProjectNames" /> (its spec project and the private plumbing only that
    ///     references, or nothing for the spec-less survey).
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
    ///     It lives beside <see cref="Retain" /> because the two have to agree: the key identifies a merge
    ///     exactly as far as <c>Retain</c> distinguishes one, and both memos in the product — the warm
    ///     session store's and the per-run source's — would strand a wrong model if they diverged.
    /// </remarks>
    internal static string ExclusionKey(IReadOnlyCollection<string> excludeProjectNames)
    {
        if (excludeProjectNames.Count == 0) return "";

        return string.Join("\n", excludeProjectNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    private sealed class MergeState
    {
        // The supplier map every fragment shares when no name is shadowed or declared by several projects.
        // NodeKeyFor consults a supplier map only for a shadowed name, and ReachedCopy only for a name several
        // projects declare, so with neither there is no lookup left to answer and one map serves the whole
        // merge. Never written to.
        private static readonly Dictionary<string, string> EmptySuppliers = new(StringComparer.Ordinal);

        private readonly EdgeTable<NodeKey> _catchEdges = new();

        // The two catch narrowings are edge tables of their own rather than plain site maps, because each has to
        // carry its own per-instance shares: which of a copy's `catch` clauses spell a filter, and which of those
        // hold the failure, are facts about that copy and not derivable from the edge's union.
        private readonly EdgeTable<NodeKey> _catchEdgeSwallowing = new();
        private readonly EdgeTable<NodeKey> _catchEdgeUnfiltered = new();
        private readonly EdgeTable<NodeKey> _constructorEdges = new();
        private readonly EdgeTable<NodeKey> _edges = new();
        private readonly EdgeTable<NodeKey> _exposureEdges = new();
        private readonly Dictionary<NodeKey, FragmentExternal> _externalFacts = new();

        // Project name → every target framework its fragments carried, ordinal-sorted. Built over all
        // fragments, not just the declaring ones, so a note can name the project's whole framework set.
        private readonly Dictionary<string, SortedSet<string>> _frameworksByProject = new(StringComparer.Ordinal);

        private readonly EdgeTable<NodeKey> _injectionEdges = new();

        // Keyed on (source node, member SymbolId) rather than a node pair: a use names a member, and the member's
        // declaring type is the target its instances are attributed to.
        private readonly EdgeTable<string> _memberEdges = new();
        private readonly Dictionary<string, MemberEdgeFacts> _memberFacts = new(StringComparer.Ordinal);

        // Project name → every framework that won the facts of a type two or more of the project's frameworks
        // declared, ordinal-sorted. Each shared type follows the first framework that declares it, so where the
        // frameworks share different sets of types there is more than one winner, and the note has to say so.
        // An entry exists only where such a collapse actually happened, which is the note's gate: without it a
        // note would claim a winner for a project whose frameworks share nothing.
        private readonly Dictionary<string, SortedSet<string>> _multiFrameworkWinners = new(StringComparer.Ordinal);
        private readonly Dictionary<NodeKey, TypeNode> _nodes = new();

        // Assembly name → the project whose fragments produce it, read to tell which declarer's copy of a shared
        // name a compilation bound. An assembly two projects both produce has no producer: a name that does not
        // say which project it came from concludes nothing, and the reach falls back to the first declarer.
        private readonly AssemblyProducers _projectsByAssembly = new();
        private readonly Dictionary<(Lifetime Lifetime, string Service, string? Impl), SortedSet<FragmentSite>> _registrationSites = new();

        // Declared FQN → its roster: who declares it, which declaration won its facts, and where each declarer
        // declares it. Every name a fragment declares has one, and nothing else does.
        private readonly Dictionary<string, DeclarerRoster> _rosters = new(StringComparer.Ordinal);

        // Shadowed FQN → every project whose own compilation bound that name from a shadowing assembly
        // rather than from the declaration, ordinal-sorted. Recorded where the answer is already in hand —
        // the supplier pass walks exactly these externals — rather than re-derived from the edges, which can
        // only see the names an edge was minted for: a name reached solely as a member's parameter or return
        // type mints none at all (GRAMMAR §4.6), and a bare `catch` or a `throw expr;` spells no type name to
        // mint one from. Written only from that pass, so a solution with no shadowed name builds none.
        private readonly Dictionary<string, SortedSet<string>> _shadowBinders = new(StringComparer.Ordinal);

        // Shadowed FQN → every assembly that supplied that name to a fragment which did NOT compile it and
        // which none of its declarers produce, ordinal-sorted. One entry per type rather than per
        // (type, assembly) pair, the grouping a roster's later declarers use, so one line can name them all. This
        // table is both the shadow node's gate and the merge note's content: a split the report does not
        // disclose is untypeable.
        private readonly Dictionary<string, SortedSet<string>> _shadowingAssemblies = new(StringComparer.Ordinal);
        private readonly EdgeTable<NodeKey> _throwEdges = new();

        // Whether any roster has a later declarer: the one fact the supplier pass's fast path needs of them all.
        private bool _anyShared;

        // The inputs, held for the passes that ask something of the fragment behind an index rather than of the
        // tables built from it: which framework a type's facts came from, and which projects the model states.
        private IReadOnlyList<CodebaseFragment> _fragments = [];

        public CodebaseModel Run(IReadOnlyList<CodebaseFragment> fragments)
        {
            _fragments = fragments;

            // The project → target-framework index the multi-framework note reads, indexed before any
            // declaration so a note can name every framework of a project, not only the ones that collapsed.
            // The assembly → project index rides the same pass: an edge into a shared name reads it later.
            foreach (CodebaseFragment fragment in fragments)
            {
                RecordTargetFramework(fragment);
                _projectsByAssembly.Record(fragment.AssemblyName, fragment.ProjectName);
            }

            // Declared nodes: first declarer (input order) wins facts/ProjectName; sites union. The index
            // rides along because the winner's own bindings are what its hierarchy is later resolved through.
            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                foreach (FragmentType declared in fragment.DeclaredTypes)
                    DeclareMerged(declared, fragment, index);
            }

            // External facts table: the first fragment (input order) referencing a not-declared-anywhere
            // FQN wins its facts. Built after declaration so the pass below can ask "does anything declare
            // this?" of the rosters alone, which is what splits an ordinary external from a shadowed name.
            foreach (CodebaseFragment fragment in fragments)
            foreach (FragmentExternal external in fragment.Externals)
                RecordExternal(external);

            // Every shadow gets its node here rather than waiting to be resolved by an endpoint, so the
            // report and the model cannot disagree: a name is noted as meaning two types exactly when Types
            // carries two. Without this a name referenced only where extraction keeps its facts as strings —
            // a member's parameter or return type (GRAMMAR §4.6) — would be noted and then not be there.
            foreach (KeyValuePair<string, SortedSet<string>> entry in _shadowingAssemblies)
            {
                string fqn = entry.Key;
                SortedSet<string> shadowing = entry.Value;

                foreach (string assembly in shadowing)
                    ResolveNode(new NodeKey(fqn, assembly));
            }

            // Each fragment's own answer to "which assembly did I bind this name from", built once here and
            // read by every pass below. It has to come after the two passes above, because a supplier is
            // only interesting once the tables know which names are declared, which of those several projects
            // declare, and which are shadowed. That is also the whole cost model: a map is consulted for a
            // shadowed name — which node a mention denotes — and for a name several projects declare — whose
            // copy an edge reached — and for nothing else. A solution with neither, which is nearly every
            // solution, consults none and builds none, and one with either records the handful of names in
            // question rather than every external. No roster with a later declarer is the second half of that
            // test, which is why the flag is kept rather than asked of every roster here.
            List<Dictionary<string, string>> suppliers = _shadowingAssemblies.Count == 0 && !_anyShared
                ? Enumerable.Repeat(EmptySuppliers, fragments.Count)
                    .ToList()
                : fragments.Select(SuppliersOf)
                    .ToList();

            // Hierarchy from the winning fragment only, resolved through THAT fragment's bindings — a
            // declarer whose own compilation bound a shadowed name from an assembly reaches the assembly's
            // node, exactly as its edges do. Declared members (GRAMMAR §4.6) are winner-only in the same way
            // — the winning fragment's inventory becomes the node's MemberNode list — so they are taken in
            // the same pass: the inventory is a function of the fragment and the node alone, and nothing
            // between here and the model reads or writes it. Externals keep their empty default; the member
            // axis is solution-declared-only. Every declared name has a roster, so the rosters are the whole
            // of this pass, and each already holds the winner's node, declaration and fragment index.
            foreach (DeclarerRoster roster in _rosters.Values)
            {
                PopulateHierarchy(roster.Node, roster.Winning, suppliers[roster.WinningFragment]);
                PopulateMembers(roster.Node, roster.Winning);
            }

            // Every edge family, one fragment at a time rather than one family at a time, so each family
            // reads the supplier index and the project of the fragment that observed it. Interleaving the
            // families changes only the order the work runs in, and nothing here is order-sensitive: every site
            // and instance set is a SortedSet, every map keys on its endpoint pair, the external facts table was
            // frozen in the pass above so ResolveNode mints the same facts whenever it first runs, _memberFacts
            // is first-wins over facts that are a function of the SymbolId, and Materialize re-sorts the nodes.
            for (var index = 0; index < fragments.Count; index++)
            {
                CodebaseFragment fragment = fragments[index];
                Dictionary<string, string> fragmentSuppliers = suppliers[index];
                string projectName = fragment.ProjectName;

                // Edge site-sets union per (src, tgt).
                foreach (FragmentEdge edge in fragment.Edges)
                    MergeSimpleEdge(
                        fragmentSuppliers, projectName, edge.SourceFullName, edge.TargetFullName, _edges, edge.Sites);

                // Member-use edges (GRAMMAR §4.5) key on (src, member SymbolId) rather than a type pair, and
                // the self-drop is therefore a same-type guard; one shared MemberReference per distinct SymbolId.
                // They keep instances like every other family, with the member's declaring type as the target.
                foreach (FragmentMemberEdge memberEdge in fragment.MemberEdges)
                    MergeMemberEdge(fragmentSuppliers, projectName, memberEdge);

                // Construction edges (GRAMMAR §4.5) key on (src, constructed).
                foreach (FragmentConstructorEdge constructorEdge in fragment.ConstructorEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, projectName, constructorEdge.SourceFullName,
                        constructorEdge.ConstructedFullName, _constructorEdges, constructorEdge.Sites);

                // Injection edges (GRAMMAR §4.7) key on (src, injected).
                foreach (FragmentInjectionEdge injectionEdge in fragment.InjectionEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, projectName, injectionEdge.SourceFullName, injectionEdge.InjectedFullName,
                        _injectionEdges, injectionEdge.Sites);

                // Registration facts (GRAMMAR §4.7) are the one axis with no node resolution: they key on
                // (lifetime, service, impl?) string-side, because registration is many-to-many and membership
                // is resolved model-side at evaluation. That is also why a shadowed name needs nothing here —
                // there is no attribution to get wrong, only a name, and both nodes wear it.
                foreach (FragmentServiceRegistration registration in fragment.ServiceRegistrations)
                    MergeRegistration(registration);

                // Catch edges (GRAMMAR §4.8) key on (src, caught), and their unfiltered- and swallowing-site
                // subsets union under that same key and guard — so a file:line any fragment reports unfiltered
                // (or swallowing) keeps that standing in the merged edge.
                foreach (FragmentCatchEdge catchEdge in fragment.CatchEdges)
                    MergeCatchEdge(fragmentSuppliers, projectName, catchEdge);

                // Throw edges (GRAMMAR §4.8) key on (src, thrown).
                foreach (FragmentThrowEdge throwEdge in fragment.ThrowEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, projectName, throwEdge.SourceFullName, throwEdge.ThrownFullName, _throwEdges,
                        throwEdge.Sites);

                // Exposure edges (GRAMMAR §4.9) key on (src, exposed).
                foreach (FragmentExposureEdge exposureEdge in fragment.ExposureEdges)
                    MergeSimpleEdge(
                        fragmentSuppliers, projectName, exposureEdge.SourceFullName, exposureEdge.ExposedFullName,
                        _exposureEdges, exposureEdge.Sites);
            }

            return Materialize();
        }

        private void RecordTargetFramework(CodebaseFragment fragment)
        {
            if (fragment.TargetFramework is not { } targetFramework) return;

            FragmentSiteSets.OrdinalNamesFor(_frameworksByProject, fragment.ProjectName)
                .Add(targetFramework);
        }

        private void DeclareMerged(FragmentType declared, CodebaseFragment fragment, int fragmentIndex)
        {
            string fqn = declared.Facts.FullName;
            if (!_rosters.TryGetValue(fqn, out DeclarerRoster? roster))
            {
                // The winning (first) declarer supplies the node's facts and, later, its hierarchy.
                var node = declared.Facts.ToTypeNode(fragment.ProjectName, isExternal: false);
                _nodes[NodeKey.Unshadowed(fqn)] = node;
                _rosters[fqn] = roster = new DeclarerRoster(node, declared, fragmentIndex);
            }
            else
            {
                // The one discriminant every fact a re-declaration records turns on: a differing project name
                // is cross-project conflation, a matching one is a project's own several target frameworks.
                bool sameProject = string.Equals(roster.Winner, fragment.ProjectName, StringComparison.Ordinal);
                if (sameProject) NoteFrameworkCollapse(roster, fragment.TargetFramework);
                else NoteConflation(roster, fragment.ProjectName);

                roster.RecordSites(fragment.ProjectName, declared.DeclarationSites);
            }

            // Recorded for EVERY declarer, winner or not: a name this fragment compiles is satisfied by
            // this fragment's assembly, so a reference bound from it is an ordinary intra-solution
            // reference however the attribution fell out. An unknown assembly name adds nothing, leaving
            // the set empty and the shadow test with nothing to disagree with.
            if (fragment.AssemblyName is { Length: > 0 } assemblyName) roster.Assemblies.Add(assemblyName);

            foreach (FragmentSite site in declared.DeclarationSites) roster.Sites.Add(site);
        }

        // A later declarer under a project name the winner's differs from: same-FQN cross-project conflation.
        // The facts and ProjectName keep following the first declarer, while selections and edge attribution
        // reach every declarer through the roster — record the loser on it. The loser set collapses a
        // multi-framework loser to one entry.
        private void NoteConflation(DeclarerRoster roster, string laterProjectName)
        {
            roster.AddLoser(laterProjectName);
            _anyShared = true;
        }

        // The same-project half: one project file's several target frameworks both declaring this FQN, so the
        // type collapses onto whichever framework was extracted first. A differing project name is cross-project
        // conflation, which the caller's one discriminant sends to the sibling above. Each case that records no
        // note is a correctness matter rather than an optimisation:
        //   • an unknown framework on either side (a single-framework project, a hand-built input) leaves
        //     nothing truthful to say about which framework won;
        //   • a type only ONE framework declares — a #if-guarded class, a framework-conditional <Compile> —
        //     never collapsed at all, and never reaches here, since only a second declaration does: its facts
        //     follow its own framework, and a note would be false about it;
        //   • two declarations from ONE framework collapsed nothing across frameworks, and the note would name
        //     a framework as winning over itself. One project name covers them where two project files share
        //     a file name and a framework, or where a hand-built input lists one compilation twice.
        //
        // The winning framework is the winning FRAGMENT's, read through the index the roster keeps for the
        // hierarchy: both were recorded together when the name was first declared, so the fragment that supplies a
        // type's facts is also the one whose framework they are.
        private void NoteFrameworkCollapse(DeclarerRoster roster, string? laterFramework)
        {
            if (laterFramework is null) return;
            if (_fragments[roster.WinningFragment].TargetFramework is not { } winningFramework) return;
            if (string.Equals(winningFramework, laterFramework, StringComparison.Ordinal)) return;

            // Every winner is kept, not only the first collapse's: which collapse the merge meets first depends
            // on the order a fragment lists its types, and a project whose frameworks share different sets of
            // types has a winner per set.
            FragmentSiteSets.OrdinalNamesFor(_multiFrameworkWinners, roster.Winner)
                .Add(winningFramework);
        }

        // One note per declarer set, naming every project in the set and every type it shares. The list
        // joiner degrades to "'A' and 'B'" at two items, so a single loser or a single type reads as a plain
        // sentence. Every type in the set has the same later declarers, so the first type's roster names them.
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

        // One note per multi-framework project whose frameworks share a type, naming every framework it
        // targets and each framework whose members and hierarchy a shared type carries. Per project rather
        // than per type because one rule covers all of them, the first framework that declares a type wins it,
        // and a project sharing two hundred types would otherwise drown the channel it is trying to be noticed
        // in. A single winner, which is every project targeting two frameworks, keeps the plain sentence.
        private string FrameworkCollapseNote(string projectName)
        {
            SortedSet<string> frameworks = _frameworksByProject[projectName];
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

        // The conflated types regrouped under the declarer set they share: the winner and its losers. One
        // note per set rather than per type is the FrameworkCollapseNote grain again, chosen because the
        // answer is the same for every type a set of projects compiles together, and a source-only package
        // compiled into five projects put a hundred lines of it in front of the violations.
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
                FragmentSiteSets.OrdinalNamesFor(bySet, set)
                    .Add(fqn);
            }

            return bySet;
        }

        // The shadowed names regrouped under the project that declares each of them, with that project's
        // supplying assemblies unioned. Grouping by PROJECT rather than by type is the FrameworkCollapseNote
        // grain, chosen because the answer is the same for all of them, and a solution carrying a dozen
        // netstandard shims under BCL names would otherwise spend a dozen lines drowning the channel it is
        // trying to be noticed in.
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

        // One note per project whose declarations share a full name with something a referenced assembly
        // supplies. It states the split rather than a suppression, because nothing is suppressed: both types
        // are in the model, and which one a reference reaches is decided by what that reference actually
        // bound. The reader's question is the one the last clause answers — why a project selection over the
        // declaring project does not reach every use of the name.
        private static string ShadowedNamesNote(string projectName, (SortedSet<string> Types, SortedSet<string> Assemblies) shadowed)
        {
            string types = ProseFormat.JoinQuotedAnd([.. shadowed.Types]);
            string assemblies = ProseFormat.JoinQuotedAnd([.. shadowed.Assemblies]);
            string noun = shadowed.Assemblies.Count == 1 ? "assembly" : "assemblies";
            string verb = shadowed.Assemblies.Count == 1 ? "supplies" : "supply";

            return $"Project '{projectName}' declares {types}, which referenced {noun} {assemblies} also {verb}; "
                   + "every reference resolves to whichever of the two the referencing compilation bound, so "
                   + $"a selection over project '{projectName}' reaches the declared one alone.";
        }

        // Either nothing in the merge declares this FQN — an ordinary external, one node for it, facts from
        // the first fragment that referenced it — or something does, and the question becomes whether the
        // assembly this fragment bound the name FROM is one of the declaring assemblies. Where it is not,
        // the name denotes two different types and the model carries both.
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

        // The shadow test, stated once. Every guard is a fail-open rather than an optimisation:
        //   • the supplying assembly unknown — an error symbol whose ContainingAssembly is null reaches
        //     extraction as "" — so there is nothing to compare;
        //   • no declaring assembly known at all (a hand-built fragment, a cache written before the
        //     fragment carried the fact), so the same;
        //   • the supplier IS one of the declaring assemblies, which is every ordinary reference into a
        //     project of this solution, whichever project won the attribution.
        // Only a KNOWN disagreement mints anything. Doubt never does.
        private void NoteShadowIfForeign(string fqn, DeclarerRoster roster, FragmentExternal external)
        {
            string supplying = external.AssemblyName;
            if (supplying.Length == 0) return;

            HashSet<string> declaring = roster.Assemblies;
            if (declaring.Count == 0 || declaring.Contains(supplying)) return;

            FragmentSiteSets.OrdinalNamesFor(_shadowingAssemblies, fqn)
                .Add(supplying);
            var shadowKey = new NodeKey(fqn, supplying);
            if (!_externalFacts.ContainsKey(shadowKey)) _externalFacts[shadowKey] = external;
        }

        // FQN → the assembly THIS fragment bound it from, for the FQNs in dispute it referenced but did not
        // declare: a shadowed name, and a name several projects declare. The fragment's externals are exactly
        // that answer: extraction mints one per referenced-not-declared FQN carrying the ContainingAssembly of
        // the symbol it bound. A fragment that declared the FQN itself has no entry — which is what leaves a test
        // project's reference to its OWN stand-in alone, and a declarer's reference to its own copy. Any other
        // name is left out because nothing would ever ask about it: NodeKeyFor hands such a name straight back
        // without reaching the map, and ReachedCopy asks only of a name several projects declare, so the map
        // holds the handful of names in dispute rather than one entry per external reference of every fragment.
        //
        // The binder roster is taken in the same walk, on the same test NodeKeyFor makes: an external whose
        // assembly is one recorded as shadowing the name IS this fragment's compilation reaching the
        // assembly's type rather than the declaration, which is the whole of what the roster claims.
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
            FragmentSiteSets.OrdinalNamesFor(_shadowBinders, fqn)
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
                .Select(f => (ITypeInfo)ResolveNode(NodeKeyFor(suppliers, f)))
                .ToList();

            node.Attributes = declared.Attributes
                .Select(f => (ITypeInfo)ResolveNode(NodeKeyFor(suppliers, f)))
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

        // The endpoint protocol, stated once for every edge family: each endpoint mapped to the node THIS
        // fragment's compilation bound it to, the self-edge guard (extraction already dropped these, so it is
        // defensive), ResolveNode on BOTH endpoints — the rule that keeps an external endpoint one shared node
        // however many fragments referenced it — and the instance this fragment's compilation made of the edge.
        // A dropped self-edge resolves to null rather than to a pair.
        //
        // A source endpoint is always a type this fragment declared, so NodeKeyFor hands it straight back;
        // mapping it anyway is what makes the self-edge guard compare nodes rather than names, which is the
        // honest comparison once one name can denote two.
        private EdgeMerge? ResolveEdge(
            IReadOnlyDictionary<string, string> suppliers, string projectName, string source, string target)
        {
            NodeKey sourceKey = NodeKeyFor(suppliers, source);
            NodeKey targetKey = NodeKeyFor(suppliers, target);
            if (sourceKey == targetKey) return null;

            ResolveNode(sourceKey);
            TypeNode targetNode = ResolveNode(targetKey);
            InstanceKey? instance = InstanceOf(suppliers, projectName, sourceKey, targetKey, targetNode);
            return new EdgeMerge((sourceKey, targetKey), instance);
        }

        // The one edge merge for every axis that keys on an endpoint pair: the endpoints resolved, then the site
        // union, made under the instance this fragment's compilation made so the edge's sites and that instance's
        // own share are one fact recorded once. Reports the key it merged under and the instance key it kept —
        // null for the dropped self-edge — so an axis carrying parallel narrowings (catch) gates them on the
        // same guard and records them under the same key rather than restating either.
        private EdgeMerge? MergeSimpleEdge(
            IReadOnlyDictionary<string, string> suppliers,
            string projectName,
            string source,
            string target,
            EdgeTable<NodeKey> table,
            IReadOnlyList<FragmentSite> sites)
        {
            if (ResolveEdge(suppliers, projectName, source, target) is not { } merged) return null;

            table.Merge(merged.Key, merged.Instance, sites);
            return merged;
        }

        // The instance this fragment's compilation made of one edge (GRAMMAR §4.1), kept only where an end of the
        // edge is a name several projects declare. The declarers' copies need not make the same references — a copy
        // that drifted apart makes its own — so the edge's source node, whose facts are the first declarer's, cannot
        // say which declarers made it; and a project that declares no copy of a shared target binds the copy of
        // whichever declarer it references, which the target node cannot say either. The fragment's project made the
        // reference, so both halves are facts here and nowhere after. An edge between names one project each declares
        // keeps no record — null — because its two nodes already name its one instance, which made every site it has.
        //
        // A target this fragment bound from an assembly shadowing the name is the assembly's node rather than the
        // declaration, so it is not a shared target however many projects declare the name: its one copy is the
        // assembly's, which is what the node's ProjectName says.
        private InstanceKey? InstanceOf(
            IReadOnlyDictionary<string, string> suppliers,
            string projectName,
            NodeKey sourceKey,
            NodeKey targetKey,
            TypeNode targetNode)
        {
            bool sharedSource = SharedRoster(sourceKey.FullName) is not null;
            DeclarerRoster? sharedTarget = targetKey.SupplyingAssembly is null ? SharedRoster(targetKey.FullName) : null;
            if (!sharedSource && sharedTarget is null) return null;

            // A shared target has no supplying assembly, so its node IS the first declarer's — the winner every
            // unknown falls back to, which its roster holds.
            string reached = sharedTarget is not null
                ? ReachedCopy(suppliers, projectName, targetKey.FullName, sharedTarget)
                : targetNode.ProjectName;

            return new InstanceKey(projectName, reached);
        }

        // The roster of a name several projects declare, or null: for a name one project declares, and for a name
        // nothing declares, which has no roster at all. Until some name gains a later declarer no roster is shared,
        // so a solution that declares no name twice answers from one flag and hashes no name per edge.
        private DeclarerRoster? SharedRoster(string fqn)
        {
            if (!_anyShared) return null;

            return _rosters.TryGetValue(fqn, out DeclarerRoster? roster) && roster.IsShared ? roster : null;
        }

        // Whose copy of a name several projects declare one compilation bound. The fragment's supplier map answers it
        // per compilation rather than per project: a compilation that declares the name records no external for it,
        // and binds its own copy, since a compilation binds its own source ahead of any referenced assembly's; one
        // that records an external bound the copy of the declarer producing that assembly. Asking the project instead
        // would be wrong for a multi-targeted project whose #if declares the name under one framework only, and whose
        // other framework binds a referenced declarer's copy. Every unknown — the assembly unrecorded, produced by two
        // projects, or by no declarer of the name — falls back to the first declarer, the copy whose facts the node
        // carries, which is where an edge with nothing better to go on is attributed.
        private string ReachedCopy(
            IReadOnlyDictionary<string, string> suppliers, string projectName, string fqn, DeclarerRoster roster)
        {
            if (!suppliers.TryGetValue(fqn, out string? assembly))
                return roster.Declares(projectName) ? projectName : roster.Winner;

            if (!_projectsByAssembly.TryGetProducer(assembly, out string? supplier)) return roster.Winner;

            return roster.Declares(supplier!) ? supplier! : roster.Winner;
        }

        // A use is attributed the way an edge into the member's declaring type is: the instance this fragment's
        // compilation made, through the same ResolveEdge every other family calls, so a rule owning one copy's
        // instances reads that copy's uses.
        private void MergeMemberEdge(
            IReadOnlyDictionary<string, string> suppliers, string projectName, FragmentMemberEdge edge)
        {
            if (ResolveEdge(suppliers, projectName, edge.SourceFullName, edge.TargetContainingTypeFullName)
                is not { } merged) return;

            // Member facts are functions of the SymbolId, so the first mention wins and every later one agrees.
            // The containing type is stored as the NODE key rather than the name, because that is what
            // BuildMemberEdges resolves with — a member on a shadowed name would otherwise always be hung on
            // the source declaration, whichever assembly the reference actually bound.
            if (!_memberFacts.ContainsKey(edge.MemberSymbolId))
                _memberFacts[edge.MemberSymbolId] =
                    new MemberEdgeFacts(merged.Key.Target, edge.MemberName, edge.MemberKind);

            _memberEdges.Merge((merged.Key.Source, edge.MemberSymbolId), merged.Instance, edge.Sites);
        }

        private void MergeCatchEdge(
            IReadOnlyDictionary<string, string> suppliers, string projectName, FragmentCatchEdge edge)
        {
            if (MergeSimpleEdge(
                    suppliers, projectName, edge.SourceFullName, edge.CaughtFullName, _catchEdges, edge.Sites)
                is not { } merged) return;

            // The unfiltered subset unions under the same key, the same guard and the same INSTANCE key, so a
            // rule that owns one copy's instance of the edge reads that copy's unfiltered sites. Unioning the
            // UNFILTERED sites is what keeps a `#if`-divergent filter honest: a (file, line) filtered in one
            // fragment and unfiltered in another reads unfiltered here, the truthful answer for a ban — and
            // per instance too, since the frameworks that disagree are one project and so one instance.
            _catchEdgeUnfiltered.Merge(merged.Key, merged.Instance, edge.UnfilteredSites);

            // The swallowing subset unions the same way, and for the same reason: a (file, line) that rethrows
            // in one fragment and swallows in another reads swallowing here.
            _catchEdgeSwallowing.Merge(merged.Key, merged.Instance, edge.SwallowingSites);
        }

        // Registrations are string-side (never resolved to nodes): the union key is the whole
        // (lifetime, service FQN, implementation FQN) triple, so two fragments (or two call sites) that name
        // the identical registration collapse to one fact with unioned sites.
        private void MergeRegistration(FragmentServiceRegistration registration)
        {
            SortedSet<FragmentSite> sites = FragmentSiteSets.For(
                _registrationSites, (registration.Lifetime, registration.ServiceFullName, registration.ImplementationFullName));
            foreach (FragmentSite site in registration.Sites) sites.Add(site);
        }

        /// <summary>
        ///     The node <paramref name="nodeKey" /> denotes: the declared-anywhere node for a plain
        ///     fully-qualified name, else a shallow external node minted once from the first-fragment-wins
        ///     facts table — which is both an ordinary external and, under a shadow key, the assembly's
        ///     half of a name a project also declares. Every key reachable here was recorded by whichever
        ///     fragment references it, so the table always has an entry when the key names no declaration.
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
                node.DeclarationSites = FragmentSiteSets.Locations(roster.Sites);
                node.FilePaths = FragmentSiteSets.FilePaths(roster.Sites);

                // The conflation kept as a fact rather than only as the sentence ConflationNote composes from the
                // same roster. A consumer that has to ACT on it — the survey, which must not render a project's
                // reference to its own compiled-in copy as an edge to the declarer that won — cannot read prose.
                if (roster.Losers is { } losers) node.AlsoDeclaredBy = losers.ToList();

                // The same conflation's sites per declarer, stamped beside the list that names the declarers. Each
                // declarer's share is a subsequence of the union materialized just above, by reference, so a
                // declarer that made every site gets the very list the node already carries.
                if (roster.SitesByDeclarer is not { } byDeclarer) continue;

                node.DeclarationSitesByDeclarer = byDeclarer.ToDictionary(
                    entry => entry.Key,
                    entry => FragmentSiteSets.Subsequence(roster.Sites, node.DeclarationSites, entry.Value),
                    StringComparer.Ordinal);
            }

            // Ordered by name, then by the two facts that tell a shadowed name's two nodes apart — the
            // declaration first, then the supplying assemblies ordinal. One name can denote a source
            // declaration and a referenced assembly's type of that name, so FullName alone is not a total
            // order, and without the tie-break the remainder falls through to dictionary enumeration
            // order, costing every rendered document its byte-stability.
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

            List<CatchEdge> catchEdges = FragmentSiteSets.OrderedPairs(
                _catchEdges.Sites, NodeKey.Ordinal, NodeKey.Ordinal, BuildCatchEdge);

            List<ThrowEdge> throwEdges = BuildEdges(
                _throwEdges, (src, thrown, evidence) => new ThrowEdge(src, thrown, evidence));

            List<ExposureEdge> exposureEdges = BuildEdges(
                _exposureEdges, (src, exposed, evidence) => new ExposureEdge(src, exposed, evidence));

            List<ServiceRegistration> serviceRegistrations = FragmentSiteSets.OrderedRegistrations(
                _registrationSites,
                (lifetime, service, impl, sites) => new ServiceRegistration(
                    lifetime, service, impl, FragmentSiteSets.Locations(sites)));

            // The advisory notes: the two project-level kinds first, each ordinal by project, then the
            // conflation kind, one note per declarer set, ordinal by winner and then by its losers — coarse
            // fact before fine, and each sorted on the key it groups by, so the list is stable across runs
            // regardless of the order the distinct notes were first raised.
            IEnumerable<string> frameworkCollapseNotes = _multiFrameworkWinners.Keys
                .OrderBy(projectName => projectName, StringComparer.Ordinal)
                .Select(FrameworkCollapseNote);
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
                exposureEdges, serviceRegistrations, BuildProjects(), BuildShadowedNames(), mergeNotes);
        }

        // The one simple-edge materialization, stated once for every axis that keys on an endpoint pair; the
        // table mints each edge's evidence (EdgeTable.EvidenceOf says why in one piece).
        private List<TEdge> BuildEdges<TEdge>(
            EdgeTable<NodeKey> table, Func<TypeNode, TypeNode, EdgeEvidence, TEdge> make)
        {
            return FragmentSiteSets.OrderedPairs(
                table.Sites, NodeKey.Ordinal, NodeKey.Ordinal,
                (src, tgt, sites) => make(_nodes[src], _nodes[tgt], table.EvidenceOf((src, tgt), sites)));
        }

        // One catch edge and its two narrowings, built together because they share more than a key: all three site
        // lists come out of SortedSets, so each is (file, line) ordered and each is a subset of the one before it;
        // an edge with no unfiltered (or no swallowing) site materializes the empty list. Each narrowing carries
        // its OWN materialized list and its own per-instance shares of that list, aligned to the edge's instances,
        // so a rule that owns one copy's instance cites that copy's unfiltered lines and no other copy's.
        private CatchEdge BuildCatchEdge(NodeKey src, NodeKey caught, SortedSet<FragmentSite> sites)
        {
            (NodeKey Source, NodeKey Caught) key = (src, caught);

            EdgeEvidence evidence = _catchEdges.EvidenceOf(key, sites);
            EdgeEvidence unfiltered = _catchEdgeUnfiltered.NarrowingOf(key);
            EdgeEvidence swallowing = _catchEdgeSwallowing.NarrowingOf(key);

            return new CatchEdge(_nodes[src], _nodes[caught], evidence, unfiltered, swallowing);
        }

        // The split kept as a fact rather than only as the sentence ShadowedNamesNote composes from the same
        // tables, for the reason AlsoDeclaredBy exists beside ConflationNote: the consumers that must ACT on
        // it — the survey's coverage statement, a rule author asking whose facts won — cannot read prose.
        //
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

        // Member edges ordered by (source FullName, member SymbolId). A single MemberReference is minted per
        // distinct SymbolId (facts from the first-mention table, containing node from the merged node table), so
        // equal members across edges share the instance. Each edge's evidence is minted by its table, as BuildEdges'
        // is.
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

            return FragmentSiteSets.OrderedPairs(
                _memberEdges.Sites, NodeKey.Ordinal, StringComparer.Ordinal,
                (src, symbolId, sites) => new MemberEdge(
                    _nodes[src], MemberReferenceFor(symbolId), _memberEdges.EvidenceOf((src, symbolId), sites)));
        }

        // The only ProjectNode construction site outside the tests, so every per-project table the merge keeps
        // reaches the model here or nowhere.
        private List<ProjectNode> BuildProjects()
        {
            Dictionary<string, SortedSet<string>> refsByProject = new(StringComparer.Ordinal);
            Dictionary<string, SortedSet<string>> undeclaredByProject = new(StringComparer.Ordinal);
            Dictionary<string, ProjectArtifacts> artifactsByProject = new(StringComparer.Ordinal);
            foreach (CodebaseFragment fragment in _fragments)
            {
                SortedSet<string> refs = FragmentSiteSets.OrdinalNamesFor(refsByProject, fragment.ProjectName);
                foreach (string reference in fragment.ProjectReferences) refs.Add(reference);

                SortedSet<string> undeclared = FragmentSiteSets.OrdinalNamesFor(undeclaredByProject, fragment.ProjectName);
                foreach (string reference in fragment.UndeclaredProjectReferences ?? []) undeclared.Add(reference);

                if (!artifactsByProject.TryGetValue(fragment.ProjectName, out ProjectArtifacts? artifacts))
                    artifactsByProject[fragment.ProjectName] = artifacts = new ProjectArtifacts();

                artifacts.Absorb(fragment);
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
                targetFrameworks: TargetFrameworksOf(projectName, artifacts),
                targetFrameworksSite: FragmentSiteSets.Location(artifacts.TargetFrameworksSite),
                factsFollow: winners?.Min,
                packageReferences: artifacts.PackageReferences(),
                isPackable: artifacts.IsPackable,
                isPackableSite: FragmentSiteSets.Location(artifacts.IsPackableSite),
                locksPackages: artifacts.LocksPackages,
                locksPackagesSite: FragmentSiteSets.Location(artifacts.LocksPackagesSite),
                assemblyNames: artifacts.AssemblyNames());
        }

        // What the project declares, whatever it compiled to — so a single-framework project states its one
        // framework like any other, and a solution filter that extracted one of a project's two frameworks
        // still reports both. The frameworks its fragments actually carried are the fallback rather than the
        // answer: they exist only where a load discriminated the compilations, which is the multi-framework
        // case, so a project nothing evaluated says what it can and a hand-built input says nothing.
        private IReadOnlyList<string>? TargetFrameworksOf(string projectName, ProjectArtifacts artifacts)
        {
            IReadOnlyList<string> declared = artifacts.TargetFrameworks();
            if (declared.Count > 0) return declared;

            _frameworksByProject.TryGetValue(projectName, out SortedSet<string>? carried);
            return carried is { Count: > 0 } ? carried.ToList() : null;
        }

        /// <summary>
        ///     One tri-state artifact fact part-way through a fold: the verdict so far and the site of the
        ///     fragment that decided it, which travel together because the site means nothing apart from the
        ///     verdict it belongs to.
        /// </summary>
        /// <param name="Value">The verdict, or <see langword="null" /> while no fragment has stated one.</param>
        /// <param name="Site">Where the fragment that decided <paramref name="Value" /> declared it.</param>
        private readonly record struct ArtifactVerdict(bool? Value, FragmentSite? Site)
        {
            /// <summary>
            ///     <paramref name="incoming" /> folded in: adopt the first verdict any fragment states, then
            ///     upgrade to <paramref name="decisive" /> and take that fragment's site with it. A fragment
            ///     that states nothing changes nothing, and a fold already at the decisive value is settled —
            ///     so a union and an intersection are this one algorithm read from opposite ends.
            /// </summary>
            /// <param name="incoming">The fragment's own verdict, or <see langword="null" /> where it has none.</param>
            /// <param name="incomingSite">Where that fragment declared it.</param>
            /// <param name="decisive">
            ///     The value that wins as soon as any fragment states it: <see langword="true" /> for
            ///     packability, because one framework that packs makes the project pack, and
            ///     <see langword="false" /> for the lock policy, because one framework whose restore writes no
            ///     lock file leaves that restore unlocked.
            /// </param>
            internal ArtifactVerdict Fold(bool? incoming, FragmentSite? incomingSite, bool decisive)
            {
                if (incoming is not { } verdict) return this;
                if (Value == decisive) return this;
                if (Value is null || verdict == decisive) return new ArtifactVerdict(verdict, incomingSite);

                return this;
            }
        }

        /// <summary>
        ///     One project's artifact facts folded across its fragments — the evaluated half of a
        ///     <see cref="ProjectNode" />, which a multi-targeted project states once per framework because a
        ///     condition on <c>$(TargetFramework)</c> can make them differ.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Each fact folds in the direction that cannot invent a clean answer.</b> A package any
        ///         framework declares is a package the project declares, so the references union, and the package
        ///         is private only where every framework's declaration of it is. Packability
        ///         unions for the same reason read the other way round: if one framework produces a package,
        ///         the project ships one. The lock policy <em>intersects</em> — a framework whose restore
        ///         writes no lock file leaves that restore unlocked however the others are configured — and
        ///         that opposition between the two flags is the fact rather than an inconsistency, because
        ///         one is a property of what gets built and the other of how it is resolved.
        ///     </para>
        ///     <para>
        ///         <b>Unknown loses to either verdict</b>, which is the fold's first rule read the other way
        ///         round: a fragment nothing evaluated has nothing to contradict one that was, so a project is
        ///         unknown only when every one of its fragments was. Solution membership folds the same way —
        ///         a multi-targeted project arrives as one fragment per framework under the one name the load
        ///         boundary normalized them to, so declared by any fragment is declared.
        ///     </para>
        ///     <para>
        ///         <b>The site follows the fact that decides the verdict.</b> Where a fragment changes the
        ///         fold it brings its own site with it, so the packable project's site is the framework that
        ///         packs and the unlocked project's site is the framework that does not — which is the
        ///         <c>file:line</c> a violation about either would have to name. Fragments arrive in a fixed
        ///         order, so this is deterministic even where every fragment agrees.
        ///     </para>
        /// </remarks>
        private sealed class ProjectArtifacts
        {
            // Ordinal-sorted and distinct by construction, which is the order and the shape the model states
            // them in. A multi-targeted project normally contributes the one name several times over.
            private readonly SortedSet<string> _assemblyNames = new(StringComparer.Ordinal);
            private readonly SortedSet<string> _frameworks = new(StringComparer.Ordinal);
            private readonly List<FragmentPackageReference> _packages = [];
            private ArtifactVerdict _packable;
            private ArtifactVerdict _locks;
            private ArtifactVerdict _member;

            internal bool? IsPackable => _packable.Value;

            internal FragmentSite? IsPackableSite => _packable.Site;

            internal bool? LocksPackages => _locks.Value;

            internal FragmentSite? LocksPackagesSite => _locks.Site;

            internal bool? SolutionMember => _member.Value;

            internal FragmentSite? TargetFrameworksSite { get; private set; }

            internal void Absorb(CodebaseFragment fragment)
            {
                foreach (string framework in fragment.DeclaredTargetFrameworks ?? []) _frameworks.Add(framework);
                if (fragment.TargetFrameworksSite is { } frameworksSite && TargetFrameworksSite is null)
                    TargetFrameworksSite = frameworksSite;

                // Beside the frameworks because it is the same axis read one step further: a project states
                // one assembly name per compilation, and they collapse to a set here. A fragment that never
                // learned its own name — a hand-built input, a cache written before the fact was carried —
                // contributes nothing rather than an empty name.
                if (!string.IsNullOrWhiteSpace(fragment.AssemblyName)) _assemblyNames.Add(fragment.AssemblyName!);

                _packages.AddRange(fragment.PackageReferences ?? []);

                _packable = _packable.Fold(fragment.IsPackable, fragment.IsPackableSite, decisive: true);
                _locks = _locks.Fold(fragment.LocksPackages, fragment.LocksPackagesSite, decisive: false);

                // Membership has no site of its own — nothing reads one — so it folds against a null site.
                _member = _member.Fold(fragment.SolutionMember, null, decisive: true);
            }

            internal IReadOnlyList<string> TargetFrameworks()
            {
                return _frameworks.ToList();
            }

            internal IReadOnlyList<string> AssemblyNames()
            {
                return _assemblyNames.ToList();
            }

            internal IReadOnlyList<PackageReference> PackageReferences()
            {
                return FragmentSiteSets.OrderedPackages(_packages)
                    .Select(package => new PackageReference(
                        package.Name, FragmentSiteSets.Location(package.Site), package.IsPrivate))
                    .ToList();
            }
        }

        /// <summary>
        ///     The merge-side facts a <see cref="MemberReference" /> needs beyond its SymbolId: the declaring type's
        ///     node key — the key rather than the bare name, because a name a project declares and a referenced
        ///     assembly supplies denotes two nodes and the member hangs on the one its reference bound — plus name
        ///     and kind.
        /// </summary>
        private readonly record struct MemberEdgeFacts(NodeKey ContainingNodeKey, string Name, MemberKind Kind);

        /// <summary>Everything the merge records about the projects that declare one name.</summary>
        /// <remarks>
        ///     One record rather than a table per fact, because they are one name's facts about one set of
        ///     declarers, written together as each declaration arrives and read together at every site that
        ///     asks who declares the name, whose copy an edge reached, or which declarer's files a selection
        ///     counts. Tables kept apart could each be right and still disagree about the set.
        /// </remarks>
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
            ///     Every project that declared the name after the winner under a different project name,
            ///     ordinal-sorted, or <see langword="null" /> while only one project declares it. One entry per
            ///     project, so a note can name all of them in one line and a multi-targeted later declarer
            ///     counts once.
            /// </summary>
            internal SortedSet<string>? Losers { get; private set; }

            /// <summary>Whether several projects declare the name.</summary>
            internal bool IsShared => Losers is not null;

            /// <summary>Every declarer's declaration sites in one union: what the model's node carries.</summary>
            internal SortedSet<FragmentSite> Sites { get; } = [];

            /// <summary>
            ///     Each declaring project's own share of <see cref="Sites" />, kept only once a second project
            ///     declares the name. A card's placement counts these shares, which the union can no longer
            ///     tell apart.
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
            ///     so a set that stays empty answers "unknown" and the test fails open — which is why it is
            ///     minted with the roster rather than on first use.
            /// </remarks>
            internal HashSet<string> Assemblies { get; } = new(StringComparer.Ordinal);

            /// <summary>Whether a project is among the declarers: the winner or a later one.</summary>
            internal bool Declares(string project)
            {
                return string.Equals(Winner, project, StringComparison.Ordinal) || Losers?.Contains(project) == true;
            }

            // The first later declarer also mints the per-declarer table, seeded with every site recorded so far:
            // until then only the winner's project had declared the name, so all of them are its own. Called before
            // this declaration's sites join the union, so the seed is exactly the earlier ones.
            internal void AddLoser(string project)
            {
                Losers ??= new SortedSet<string>(StringComparer.Ordinal);
                Losers.Add(project);
                SitesByDeclarer ??= new Dictionary<string, SortedSet<FragmentSite>>(StringComparer.Ordinal)
                {
                    [Winner] = new(Sites, Sites.Comparer)
                };
            }

            // Which project compiled each declaring file, kept only for a name a second project declares, so nothing
            // is recorded before AddLoser mints the table. Several frameworks of one project fill one share, and a
            // linked file lands in the share of every project that compiles it.
            internal void RecordSites(string project, IReadOnlyList<FragmentSite> sites)
            {
                if (SitesByDeclarer is null) return;

                SortedSet<FragmentSite> share = FragmentSiteSets.For(SitesByDeclarer, project);
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
        ///     declaring type. One table for both keeps one instance mechanism rather than a second spelling of it.
        /// </typeparam>
        /// <remarks>
        ///     <para>
        ///         An instance's own sites are what a rule anchored on one declarer may cite, so they are
        ///         recorded here rather than derived later: the union alone cannot say which copy made which
        ///         line, and two copies of one name that drifted apart make different ones. Two fragments
        ///         recording under one instance key union their sites, which is how the frameworks of one
        ///         multi-targeted project stay one instance.
        ///     </para>
        ///     <para>
        ///         The instance table is minted on the first record, so a family whose edges all run between
        ///         names one project each declares allocates nothing beside its sites. Instances materialize
        ///         ordinal by the project that made them, then by the project whose copy they reached, so one
        ///         merge always materializes them in one order.
        ///     </para>
        /// </remarks>
        private sealed class EdgeTable<TTarget>
            where TTarget : notnull
        {
            private Dictionary<(NodeKey Source, TTarget Target), Dictionary<InstanceKey, SortedSet<FragmentSite>>>?
                _instances;

            internal Dictionary<(NodeKey Source, TTarget Target), SortedSet<FragmentSite>> Sites { get; } = new();

            // One fragment's sites for one edge: into the edge's own union always, and into the instance's where
            // this merge keeps a record. One call for both, because a site an instance made is by construction a
            // site the edge has — which is what lets the materialized instance share the edge's own list.
            internal void Merge(
                (NodeKey Source, TTarget Target) key, InstanceKey? instance, IReadOnlyList<FragmentSite> sites)
            {
                SortedSet<FragmentSite> merged = FragmentSiteSets.For(Sites, key);
                foreach (FragmentSite site in sites) merged.Add(site);

                if (instance is not { } made) return;

                _instances ??= new Dictionary<(NodeKey Source, TTarget Target), Dictionary<InstanceKey, SortedSet<FragmentSite>>>();
                if (!_instances.TryGetValue(key, out Dictionary<InstanceKey, SortedSet<FragmentSite>>? byInstance))
                    _instances[key] = byInstance = new Dictionary<InstanceKey, SortedSet<FragmentSite>>();

                SortedSet<FragmentSite> instanceSites = FragmentSiteSets.For(byInstance, made);
                foreach (FragmentSite site in sites) instanceSites.Add(site);
            }

            /// <summary>
            ///     One edge's evidence: its materialized site list, and the instances recorded for it, each
            ///     carrying its own share of that list, or <see langword="null" /> where nothing was recorded —
            ///     which is what <see cref="EdgeInstances" /> reads as the edge's own endpoints.
            /// </summary>
            /// <remarks>
            ///     <paramref name="sites" /> is this key's entry in <see cref="Sites" />, handed in by the caller
            ///     that is already walking them. The list is materialized ONCE and handed to both halves: an
            ///     instance's share is a subsequence of it by reference, so a second materialization of the same
            ///     sites would leave the two spelling the same order in different objects.
            /// </remarks>
            internal EdgeEvidence EvidenceOf((NodeKey Source, TTarget Target) key, SortedSet<FragmentSite> sites)
            {
                IReadOnlyList<SourceLocation> locations = FragmentSiteSets.Locations(sites);
                return new EdgeEvidence(locations, InstancesOf(key, sites, locations));
            }

            /// <summary>
            ///     One NARROWING of another table's edge, materialized whole: its own site list and its own
            ///     per-instance shares of that list, in the instance order every table materializes in — so a
            ///     rule that owns the edge's i-th instance reads this list's i-th entry.
            /// </summary>
            /// <remarks>
            ///     A narrowing is a table of its own recorded under the same key and the same instance key as the
            ///     edge, so <see cref="EvidenceOf" /> answers it: <see cref="Merge" /> creates an instance's set
            ///     before adding any site, so a copy that made none of the narrowing's sites still carries an
            ///     entry, and its share is the empty list.
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
                if (ByInstance(key) is not { } byInstance) return null;

                return byInstance
                    .OrderBy(entry => entry.Key.SourceProject, StringComparer.Ordinal)
                    .ThenBy(entry => entry.Key.TargetProject, StringComparer.Ordinal)
                    .Select(entry => new EdgeInstance(
                        entry.Key.SourceProject, entry.Key.TargetProject,
                        FragmentSiteSets.Subsequence(all, allLocations, entry.Value)))
                    .ToList();
            }


            private Dictionary<InstanceKey, SortedSet<FragmentSite>>? ByInstance((NodeKey Source, TTarget Target) key)
            {
                if (_instances is null) return null;

                _instances.TryGetValue(key, out Dictionary<InstanceKey, SortedSet<FragmentSite>>? byInstance);
                return byInstance;
            }
        }

        /// <summary>
        ///     Which instance of an edge one fragment's compilation made: the project that compiled the
        ///     reference, and the project whose copy of the target it reached (GRAMMAR §4.1).
        /// </summary>
        private readonly record struct InstanceKey(string SourceProject, string TargetProject);

        /// <summary>
        ///     One fragment's edge with its endpoints resolved to nodes: the endpoint pair, which a family pairing
        ///     two types records under, and the instance this fragment's compilation made of it. A member use and a
        ///     narrowing record with both rather than recomputing either. An edge whose endpoints resolve to one
        ///     node reports nothing at all, which is the self-edge guard read as a null.
        /// </summary>
        /// <remarks>
        ///     Carrying the key is what makes the agreement structural: both subsets key on the same NODE pair
        ///     the edge itself did, not on the names, because the materialization looks them up with the keys
        ///     the edge table holds — a subset keyed on anything else would silently miss on a shadowed name and
        ///     read as "no unfiltered site here".
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
        ///     <para>
        ///         The merge's two key spaces, told apart by type. A table indexed by <em>name</em> — the
        ///         rosters, <c>_shadowingAssemblies</c> — stays keyed by string, so every crossing into the node
        ///         tables spells <see cref="Unshadowed" /> and a crossing left out does not compile.
        ///     </para>
        ///     <para>
        ///         Its equality is ordinal on both fields by construction (a record struct over
        ///         <see langword="string" /> fields compares them with
        ///         <see cref="EqualityComparer{T}.Default" />), so the tables keyed on it take no comparer
        ///         argument at all — one fewer thing that can be spelled differently on one axis.
        ///     </para>
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
            /// <remarks>
            ///     Spelled once here rather than per axis, for the reason
            ///     <see cref="FragmentSiteSets.OrderedPairs{TFirst,TSecond,TValue,TOut}" /> exists at all: a
            ///     sort restated per table is a sort that can come to be spelled two ways, and a rendered
            ///     document's byte-stability is what pays for it.
            /// </remarks>
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
