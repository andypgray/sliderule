using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Roslyn;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Dogfood;

/// <summary>
///     No member of a <c>src/</c> project is kept alive by the tests alone: a non-private member that only
///     test-side code uses is either a seam named in <see cref="Allowed" /> with its reason, or a member to delete
///     or move into the tests.
/// </summary>
/// <remarks>
///     A test states this law because the grammar has no verb that says a member must be used by something. Member
///     edges propose the candidates and the compiler confirms each one. The edges record no use of a member from
///     inside its own type, and they record a call through an interface or base member against that member, not
///     against the members that implement or override it. A member that production reaches only in those ways
///     would therefore read as test-only on edges alone.
///     A member nothing uses at all is not a candidate: whether it is dead is a different question from whether the
///     tests alone keep it.
/// </remarks>
public sealed class SelfTestOnlyMemberTests
{
    private const string Seam = "a deliberate test seam; its declaration says what a test can state through it alone";
    private const string Probe = "a test probe over process-wide state, which only a test needs reset or read";
    private const string Counter = "a test observable counting work the product does, so a test can pin that it was not repeated";
    private const string Inventory = "an inventory a test holds to the code it lists, so a type added there cannot go unlisted";
    private const string EdgeSites = "the edge family's site list, which production reads on the reference, catch and throw edges";
    private const string CatchNarrowing = "a catch edge's narrowed site list, the view of its evidence that Sites is of the edge's own";

    private static readonly IReadOnlyDictionary<string, string> Allowed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["SlideRule.Checking.OneProjectDecidability.ClassifiedTypes"] = Inventory,
        ["SlideRule.Checking.OneProjectDecidability.Decides"] = Inventory,
        ["SlideRule.Cli.Mcp.Infrastructure.IdleTimeoutWatchdog.ResetForTests"] = Probe,
        ["SlideRule.Cli.Mcp.Infrastructure.ServerShutdown.HasExited"] = Probe,
        ["SlideRule.Cli.Mcp.Infrastructure.ServerShutdown.ResetForTests"] = Probe,
        ["SlideRule.Cli.Pipeline.MsBuildGate.RecordAcquisitionOnThisFlow"] = Probe,
        ["SlideRule.Cli.Rendering.ToolNamedHelpAction.MaxWidth"] = Seam,
        ["SlideRule.Cli.Verbs.BaselineRunner.CapturedEntries"] = Seam,
        ["SlideRule.Codebase.CatchEdge.SwallowingSites"] = CatchNarrowing,
        ["SlideRule.Codebase.CatchEdge.UnfilteredSites"] = CatchNarrowing,
        ["SlideRule.Codebase.MemberEdge.Sites"] = EdgeSites,
        ["SlideRule.ModelFile.NodeReaders.Discriminators"] = Inventory,
        ["SlideRule.ModelFile.NodeWriters.DelegateTypes"] = Inventory,
        ["SlideRule.ModelFile.NodeWriters.ShapeWrittenTypes"] = Inventory,
        ["SlideRule.ModelFile.NodeWriters.WritableTypes"] = Inventory,
        ["SlideRule.Rendering.ManagedBlock.ExtractBody"] = Seam,
        ["SlideRule.Roslyn.Caching.ExtractionCacheStore.CaptureStampCount"] = Counter,
        ["SlideRule.Roslyn.Caching.ExtractionCacheStore.ContentHashCount"] = Counter,
        ["SlideRule.Roslyn.Caching.LineMap.IsIdentity"] = Seam,
        ["SlideRule.Roslyn.MsBuild.MsBuildBootstrap.Initialize"] = Seam,
        ["SlideRule.Roslyn.WorkspaceLoader.CountLoadsOnThisFlow"] = Counter,
        ["SlideRule.Roslyn.WorkspaceLoader.LoadCount"] = Counter
    };

    [Fact]
    public async Task SelfCodebase_KeepsNoMemberAliveForTheTestsAlone()
    {
        CodebaseModel codebase = await SelfSpecTests.WholeCodebase.Value;
        IReadOnlySet<string> production = SolutionInventory.DrawnProjects
            .Select(static project => project.Name)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<string> shipping = SolutionInventory.SrcProjects
            .Select(static project => project.Name)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<TestOnlyMember> candidates = TestOnlyMembers(codebase, shipping, production);
        candidates.ShouldNotBeEmpty("member edges propose no src/ member only the test side uses, so the compiler would confirm nothing.");

        WorkspaceSnapshot snapshot = await WarmWorkspacePool.GetCurrentAsync(RepoRoot.Solution, Ct);
        IReadOnlyList<TestOnlyMember> hits = await ShouldConfirmAsync(candidates, snapshot.Solution, production, Ct);

        Unlisted(hits, Allowed)
            .ShouldBeEmpty("these src/ members are used only from the test side: delete or move each, or allow it with its reason.");
        Stale(hits, Allowed)
            .ShouldBeEmpty("these allowed members are no longer used only from the test side: remove their entries.");
    }

    [Fact]
    public void TestOnlyMembers_ProposesTheMemberOnlyTheTestSideUses()
    {
        TypeNode widget = SyntheticNodes.Type("Src.Widget", "T:Src.Widget", "Src");
        MemberNode probe = SyntheticNodes.MemberOf(widget, "Probe", MemberKind.Property);
        MemberNode used = SyntheticNodes.MemberOf(widget, "Used", MemberKind.Method);
        MemberNode unused = SyntheticNodes.MemberOf(widget, "Unused", MemberKind.Method);
        MemberNode hidden = SyntheticNodes.MemberOf(widget, "Hidden", MemberKind.Field, Accessibility.Private);
        widget.Members = [probe, used, unused, hidden];
        TypeNode test = SyntheticNodes.Type("Tests.WidgetTests", "T:Tests.WidgetTests", "Tests");
        TypeNode consumer = SyntheticNodes.Type("Spec.Consumer", "T:Spec.Consumer", "Spec");
        CodebaseModel codebase = new(
            [widget, test, consumer],
            [],
            [Use(test, probe), Use(test, used), Use(consumer, used), Use(test, hidden)],
            [], [], [], [], [], [], [], [], []);

        IReadOnlyList<TestOnlyMember> candidates = TestOnlyMembers(
            codebase,
            new HashSet<string>(["Src"], StringComparer.Ordinal),
            new HashSet<string>(["Src", "Spec"], StringComparer.Ordinal));

        candidates.Select(static candidate => candidate.ToString())
            .ShouldBe(["Src.Widget.Probe (used by Tests.WidgetTests)"]);
    }

    [Fact]
    public void AllowList_PassesAnAllowedHitAndRedsAnEntryNoHitMatches()
    {
        IReadOnlyList<TestOnlyMember> hits =
        [
            new("Src.Widget.Probe", "Src", "P:Src.Widget.Probe", ["Tests.WidgetTests"]),
            new("Src.Widget.Other", "Src", "M:Src.Widget.Other", ["Tests.WidgetTests"])
        ];
        var allowed = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Src.Widget.Probe"] = "a seam",
            ["Src.Widget.Gone"] = "a seam since deleted"
        };

        Unlisted(hits, allowed)
            .ShouldBe(["Src.Widget.Other (used by Tests.WidgetTests)"]);
        Stale(hits, allowed)
            .ShouldBe(["Src.Widget.Gone"]);
    }

    // A candidate is a non-private member declared in a shipping project that some code uses and no production
    // project does. An edge is matched to its member by the containing node and the symbol ID, so a same-named
    // member of another node is never conflated with it.
    private static IReadOnlyList<TestOnlyMember> TestOnlyMembers(
        CodebaseModel codebase, IReadOnlySet<string> shipping, IReadOnlySet<string> production)
    {
        ILookup<(TypeNode, string), TypeNode> users = codebase.MemberEdges
            .ToLookup(static edge => (edge.Member.ContainingType, edge.Member.SymbolId), static edge => edge.Source);

        return codebase.Types
            .Where(type => !type.IsExternal && shipping.Contains(type.ProjectName))
            .SelectMany(static type => type.Members)
            .Where(static member => member.Accessibility != Accessibility.Private)
            .Select(member => (Member: member, Users: users[(member.DeclaringType, member.SymbolId)].ToList()))
            .Where(entry => entry.Users.Count > 0 && entry.Users.All(user => !production.Contains(user.ProjectName)))
            .Select(static entry => new TestOnlyMember(
                $"{entry.Member.DeclaringType.FullName}.{entry.Member.Name}",
                entry.Member.DeclaringType.ProjectName,
                entry.Member.SymbolId,
                entry.Users
                    .Select(static user => user.FullName)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToList()))
            .OrderBy(static candidate => candidate.Identity, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.SymbolId, StringComparer.Ordinal)
            .ToList();
    }

    // Keeps the candidates the compiler agrees no production document references. Members a consumer outside the
    // solution can call, members a library finds by reflection and overrides a host calls are passed over: no
    // reference here can show their use. A reference through an interface or base member counts, because the
    // search cascades to it. A doc comment's cref does not: the search reports it as a reference, and counted, a
    // production doc that names a member would keep it alive.
    private static async Task<IReadOnlyList<TestOnlyMember>> ShouldConfirmAsync(
        IReadOnlyList<TestOnlyMember> candidates, Solution solution, IReadOnlySet<string> production, CancellationToken ct)
    {
        var confirmed = new List<TestOnlyMember>();
        foreach (TestOnlyMember candidate in candidates)
        {
            ISymbol symbol = await ShouldResolveAsync(candidate, solution, ct);
            if (IsShippedSurface(symbol) || IsRegisteredByReflection(symbol) || OverridesAHostMember(symbol)) continue;

            IEnumerable<ReferencedSymbol> references = await SymbolFinder.FindReferencesAsync(symbol, solution, ct);
            List<ReferenceLocation> productionLocations = references
                .SelectMany(static reference => reference.Locations)
                .Where(location => production.Contains(location.Document.Project.Name))
                .ToList();
            var usedInProduction = false;
            foreach (ReferenceLocation location in productionLocations)
                if (!await IsInDocumentationCommentAsync(location, ct))
                {
                    usedInProduction = true;
                    break;
                }

            if (!usedInProduction) confirmed.Add(candidate);
        }

        return confirmed;
    }

    private static async Task<bool> IsInDocumentationCommentAsync(ReferenceLocation location, CancellationToken ct)
    {
        SyntaxTree tree = location.Location.SourceTree!;
        SyntaxNode root = await tree.GetRootAsync(ct);
        SyntaxToken token = root.FindToken(location.Location.SourceSpan.Start, findInsideTrivia: true);

        return token.Parent?.IsPartOfStructuredTrivia() == true;
    }

    private static async Task<ISymbol> ShouldResolveAsync(TestOnlyMember candidate, Solution solution, CancellationToken ct)
    {
        Project? project = solution.Projects.FirstOrDefault(project => project.Name == candidate.Project);
        project.ShouldNotBeNull($"the solution has no project {candidate.Project} to resolve {candidate.SymbolId} in.");
        Compilation? compilation = await project.GetCompilationAsync(ct);
        compilation.ShouldNotBeNull($"{candidate.Project} yields no compilation.");
        ISymbol? symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(candidate.SymbolId, compilation);
        symbol.ShouldNotBeNull($"{candidate.SymbolId} resolves to no symbol in {candidate.Project}.");

        return symbol;
    }

    private static bool IsShippedSurface(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null; current = current.ContainingType)
            if (current.DeclaredAccessibility is not (Microsoft.CodeAnalysis.Accessibility.Public
                or Microsoft.CodeAnalysis.Accessibility.Protected
                or Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal))
                return false;

        return true;
    }

    // The MCP SDK registers a server's tools and prompts by walking these attributes.
    private static bool IsRegisteredByReflection(ISymbol symbol)
    {
        return symbol.GetAttributes()
            .Any(static attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "ModelContextProtocol.Server");
    }

    // The compiler or the editor calls an analyzer's or a completion provider's overrides through a base type it
    // declares, so no reference in this solution can show them used.
    private static bool OverridesAHostMember(ISymbol symbol)
    {
        ISymbol? overridden = symbol switch
        {
            IMethodSymbol method => method.OverriddenMethod,
            IPropertySymbol property => property.OverriddenProperty,
            IEventSymbol @event => @event.OverriddenEvent,
            _ => null
        };

        return overridden is not null && overridden.Locations.All(static location => location.IsInMetadata);
    }

    private static IReadOnlyList<string> Unlisted(IReadOnlyList<TestOnlyMember> hits, IReadOnlyDictionary<string, string> allowed)
    {
        return hits
            .Where(hit => !allowed.ContainsKey(hit.Identity))
            .Select(static hit => hit.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> Stale(IReadOnlyList<TestOnlyMember> hits, IReadOnlyDictionary<string, string> allowed)
    {
        HashSet<string> found = hits
            .Select(static hit => hit.Identity)
            .ToHashSet(StringComparer.Ordinal);

        return allowed.Keys
            .Where(identity => !found.Contains(identity))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static MemberEdge Use(TypeNode source, MemberNode member)
    {
        return new MemberEdge(
            source,
            new MemberReference(member.DeclaringType, member.Name, member.SymbolId, member.Kind),
            new EdgeEvidence([]));
    }

    private sealed record TestOnlyMember(string Identity, string Project, string SymbolId, IReadOnlyList<string> Users)
    {
        public override string ToString()
        {
            return $"{Identity} (used by {string.Join(", ", Users)})";
        }
    }
}
