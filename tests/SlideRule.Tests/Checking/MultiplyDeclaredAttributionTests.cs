using Shared;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The second multi-declarer bed: one edge whose endpoints are declared by
///     <em>overlapping but different</em> project sets (GRAMMAR §4.1), which
///     <see cref="MultiplyDeclaredCodebase" /> cannot stage — there every shared type is declared by the
///     same three projects, so every instance of every shared edge is intra-project.
/// </summary>
/// <remarks>
///     <para>
///         Three projects, handed to extraction in ordinal order so the winners are the ordinally first
///         declarer of each shared type:
///     </para>
///     <list type="bullet">
///         <item><c>Anchor</c> — the Beta file alone, and the declarer whose facts <c>Split.Beta</c> carries.</item>
///         <item><c>Middle</c> — both files, so it declares both endpoints and compiles the edge into itself.</item>
///         <item>
///             <c>Shell</c> — the Alpha file, plus a real reference to <c>Anchor</c>'s compilation, so its
///             copy of Alpha binds Anchor's Beta.
///         </item>
///     </list>
///     <para>
///         So <c>Split.Alpha</c> is declared by <c>Middle</c> and <c>Shell</c>, <c>Split.Beta</c> by
///         <c>Anchor</c> and <c>Middle</c>, and the one <c>Split.Alpha → Split.Beta</c> edge stands for two
///         instances that reach different projects: Middle's own copy, and Anchor's declaration from Shell.
///         <c>Anchor</c> wins Beta's facts precisely so the cross-project instance names a project the
///         source is not declared by, which is the case a single edge-global intra/cross decision cannot
///         state.
///     </para>
/// </remarks>
internal static class MixedDeclarerCodebase
{
    // The edge's source, compiled by Middle and Shell. Beta resolves to Middle's own copy in one and to
    // Anchor's assembly in the other, which is what makes one model edge stand for two different reaches.
    private const string AlphaFile = """
                                     namespace Split
                                     {
                                         public class Alpha { public Beta B; }
                                     }
                                     """;

    // The edge's target, compiled by Anchor and Middle.
    private const string BetaFile = """
                                    namespace Split
                                    {
                                        public class Beta {}
                                    }
                                    """;

    /// <summary>The extracted bed, built once for every row that reads it.</summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput anchor = CompilationFactory.Compile("Anchor", ("Split/Beta.cs", BetaFile));
        CompilationInput middle = CompilationFactory.Compile(
            "Middle", ("Split/Alpha.cs", AlphaFile), ("Split/Beta.cs", BetaFile));
        CompilationInput shell = CompilationFactory.CompileReferencing(
            "Shell", anchor.Compilation, "Anchor", ("Split/Alpha.cs", AlphaFile));

        // Input order decides the winners, and it is ordinal here so both readings of "first declarer"
        // agree: Anchor wins Beta, Middle wins Alpha.
        return CodebaseExtractor.ExtractFromCompilations([anchor, middle, shell]);
    }
}

/// <summary>
///     The third multi-declarer bed: two projects declaring one full name in <em>separate</em> files whose
///     members have drifted apart (GRAMMAR §4.1). <see cref="MultiplyDeclaredCodebase" /> and
///     <see cref="MixedDeclarerCodebase" /> compile one source text into every declarer, so each declarer's
///     copy makes the same references; here each copy makes its own, and a project that declares no copy
///     binds the one copy it can see.
/// </summary>
/// <remarks>
///     <para>
///         Four projects, handed to extraction in ordinal order, so <c>Core</c> wins the shared type's facts:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Client</c> — a real reference to <c>Core</c>'s compilation, and <c>Client.Renderer</c>'s field
///             is the genuine cross-project reference into a type Core alone declares.
///         </item>
///         <item><c>Core</c> — its copy of <c>Drift.Formatter</c>, which reaches <c>Cells.Cell</c>, and that type.</item>
///         <item>
///             <c>Reader</c> — a real reference to <c>Satellite</c>'s compilation alone, and <c>Reader.Page</c>'s
///             field names <c>Drift.Formatter</c>, so it binds Satellite's copy: the one it can see, and not the
///             one whose facts the node carries.
///         </item>
///         <item>
///             <c>Satellite</c> — its own copy of <c>Drift.Formatter</c>, which reaches <c>Palettes.Palette</c>
///             instead, and that type. It references no project, and Core references no Satellite.
///         </item>
///     </list>
///     <para>
///         So <c>Drift.Formatter</c> is one node declared by Core and Satellite, and each of its outward edges was
///         made by one copy alone: <c>Drift.Formatter → Cells.Cell</c> by Core's, and
///         <c>Drift.Formatter → Palettes.Palette</c> by Satellite's. Each is a reference, a construction and an
///         exposure at once, so the verbs over those three families read the same two type pairs. The one edge
///         into it, <c>Reader.Page → Drift.Formatter</c>, reached Satellite. The field found the outward shape on
///         Spectre.Console, whose core and Ansi projects each declare the same seven helpers.
///     </para>
/// </remarks>
internal static class DriftedCopiesCodebase
{
    // Core's copy. The winner's, so a reference only Satellite's copy makes cannot hide behind the facts.
    private const string CoreCopy = """
                                    namespace Drift
                                    {
                                        public class Formatter
                                        {
                                            public Cells.Cell Measure() => new Cells.Cell();
                                        }
                                    }
                                    """;

    private const string CoreOwn = """
                                   namespace Cells
                                   {
                                       public class Cell {}
                                   }
                                   """;

    // Satellite's copy of the same name, in a file of its own with a member Core's copy does not have.
    private const string SatelliteCopy = """
                                         namespace Drift
                                         {
                                             public class Formatter
                                             {
                                                 public Palettes.Palette Colors() => new Palettes.Palette();
                                             }
                                         }
                                         """;

    private const string SatelliteOwn = """
                                        namespace Palettes
                                        {
                                            public class Palette {}
                                        }
                                        """;

    // The control: an edge between two projects that must survive whatever removes the phantom ones.
    private const string ClientOwn = """
                                     namespace Client
                                     {
                                         public class Renderer { public Cells.Cell C; }
                                     }
                                     """;

    // A consumer of the satellite alone. The name it writes is the shared one, and the copy it binds is Satellite's.
    private const string ReaderOwn = """
                                     namespace Reader
                                     {
                                         public class Page { public Drift.Formatter F; }
                                     }
                                     """;

    /// <summary>The extracted bed, built once for every row that reads it.</summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput core = CompilationFactory.Compile(
            "Core", ("Core/Drift/Formatter.cs", CoreCopy), ("Core/Cells/Cell.cs", CoreOwn));
        CompilationInput satellite = CompilationFactory.Compile(
            "Satellite", ("Satellite/Drift/Formatter.cs", SatelliteCopy), ("Satellite/Palettes/Palette.cs", SatelliteOwn));
        CompilationInput client = CompilationFactory.CompileReferencing(
            "Client", core.Compilation, "Core", ("Client/Renderer.cs", ClientOwn));
        CompilationInput reader = CompilationFactory.CompileReferencing(
            "Reader", satellite.Compilation, "Satellite", ("Reader/Page.cs", ReaderOwn));

        return CodebaseExtractor.ExtractFromCompilations([client, core, reader, satellite]);
    }
}

/// <summary>
///     The fourth multi-declarer bed, and the first whose drifted copies make the <em>same</em> edges
///     (GRAMMAR §4.1). Two projects declare one full name in separate files, and both copies reference,
///     construct, inject, expose, throw and catch the types of a third project that declares neither copy.
///     <see cref="DriftedCopiesCodebase" /> parts the copies by which edges they make; here every family has
///     one edge standing for two instances, so the only thing that parts them is the lines each made it at.
/// </summary>
/// <remarks>
///     <para>
///         Three projects, handed to extraction in ordinal order, so <c>Core</c> wins the shared type's facts:
///     </para>
///     <list type="bullet">
///         <item>
///             <c>Base</c> — <c>Base.Registry</c> and <c>Base.RegistryException</c>, which nothing else
///             declares. It references neither of the others.
///         </item>
///         <item>
///             <c>Core</c> — a real reference to <c>Base</c>'s compilation, and its own copy of
///             <c>Drift.Formatter</c>.
///         </item>
///         <item>
///             <c>Satellite</c> — the same reference to <c>Base</c>, and its own copy of the same full name,
///             with members the core's copy lacks.
///         </item>
///     </list>
///     <para>
///         So each of the six edge families holds one edge out of <c>Drift.Formatter</c> standing for two
///         instances — Core's and Satellite's, both reaching <c>Base</c> — whose sites lie in different files.
///         The two <c>catch</c> shapes differ deliberately: a catch edge's narrowings are facts about the
///         clause each copy spells, so Core's instance is both unfiltered and swallowing while Satellite's is
///         neither, and the two filter-aware bans read one instance's share where the plain ban reads both.
///     </para>
/// </remarks>
internal static class DriftedSitesCodebase
{
    // The types both copies reach, in the one project that declares neither copy — so every edge below runs
    // out of a multiply-declared name into a singly-declared one, and the merge records an instance per copy.
    private const string BaseOwn = """
                                   namespace Base
                                   {
                                       public class Registry {}
                                       public class RegistryException : System.Exception {}
                                   }
                                   """;

    // Core's copy. The winner's, and the one whose catch clause is bare: unfiltered, and swallowing because
    // the block does not end in a throw.
    private const string CoreCopy = """
                                    namespace Drift
                                    {
                                        public class Formatter
                                        {
                                            public Formatter(Base.Registry registry) {}

                                            public Base.Registry Lookup()
                                            {
                                                return new Base.Registry();
                                            }

                                            public void Refresh()
                                            {
                                                try { Lookup(); }
                                                catch (Base.RegistryException) {}
                                            }

                                            public void Fail()
                                            {
                                                throw new Base.RegistryException();
                                            }
                                        }
                                    }
                                    """;

    // Satellite's copy of the same name, in a file of its own, making every one of the same edges at lines of
    // its own. Its catch is filtered and rethrows, so it survives neither narrowing.
    private const string SatelliteCopy = """
                                         namespace Drift
                                         {
                                             public class Formatter
                                             {
                                                 private readonly Base.Registry _registry;

                                                 public Formatter(Base.Registry registry, int width)
                                                 {
                                                     _registry = registry;
                                                 }

                                                 public Base.Registry Current => _registry;

                                                 public Base.Registry Refresh()
                                                 {
                                                     try
                                                     {
                                                         return new Base.Registry();
                                                     }
                                                     catch (Base.RegistryException) when (_registry != null)
                                                     {
                                                         throw;
                                                     }
                                                 }

                                                 public void Fail()
                                                 {
                                                     throw new Base.RegistryException();
                                                 }
                                             }
                                         }
                                         """;

    /// <summary>The extracted bed, built once for every row that reads it.</summary>
    internal static CodebaseModel Model { get; } = Extract();

    private static CodebaseModel Extract()
    {
        CompilationInput @base = CompilationFactory.Compile("Base", ("Base/Registry.cs", BaseOwn));
        CompilationInput core = CompilationFactory.CompileReferencing(
            "Core", @base.Compilation, "Base", ("Core/Drift/Formatter.cs", CoreCopy));
        CompilationInput satellite = CompilationFactory.CompileReferencing(
            "Satellite", @base.Compilation, "Base", ("Satellite/Drift/Formatter.cs", SatelliteCopy));

        // Input order decides the winner, so Core leads Satellite.
        return CodebaseExtractor.ExtractFromCompilations([@base, core, satellite]);
    }
}

/// <summary>
///     Edge attribution where several projects declare one full name (GRAMMAR §4.1). Membership alone
///     decides every position that is not an edge end; at an edge end one model edge stands for one
///     reference per declarer of its source whose copy makes it, each reaching that declarer's own copy of
///     the target where it compiles one and otherwise the copy of the declarer it references. The subject
///     bounds which instances a rule owns and the operand decides the far end at each. Heads that are not
///     projects stay attribution-insensitive, and a <c>MustOnly*</c> subject is an allow entry like any
///     other: it allows a node at exactly the projects it names it at.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="MultiplyDeclaredMembershipTests" /> carries the membership half over
///         <see cref="MultiplyDeclaredCodebase" />.
///     </para>
///     <para>
///         Where the claim is about the target end alone, a row anchors its subject on a namespace, so the
///         outward edge of the shared type itself cannot ride into a verdict the row is not making. That
///         avoidance is also why the field found this class of defect and the suite did not, so the rows
///         naming a project-headed subject over both endpoints exist to make that very shape the subject.
///     </para>
/// </remarks>
public sealed class MultiplyDeclaredAttributionTests
{
    [Fact]
    public void MustNotReference_ProjectReferencesItsOwnCompiledInCopy_WinnerAnchoredTargetStaysGreen()
    {
        // Command and Widget are both declared by Tool, so Command's reference is intra-project and belongs
        // to Tool. A ban anchored on Core forbids Core's declaration, and this reference never reaches it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-core")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Project("Core")))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_CrossProjectReferenceIntoTheWinner_WinnerAnchoredTargetIsRed()
    {
        // Client declares no copy of the shared file, so its reference crosses a project boundary and is
        // attributed to the declarer whose facts the node carries. The ban anchored on Core catches it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-core")
                    .Enforce(arch.Namespace("Client.*").MustNotReference(arch.Project("Core")))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Client.Consumer", "Shared.Widget");
    }

    [Fact]
    public void MustNotReference_CrossProjectReferenceIntoTheWinner_LoserAnchoredTargetStaysGreen()
    {
        // The same edge under a ban anchored on Tool. Tool declares the type but did not compile this
        // reference, so the edge is none of Tool's business and the rule holds.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/client-not-tool")
                    .Enforce(arch.Namespace("Client.*").MustNotReference(arch.Project("Tool")))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_BothEndpointsCoDeclared_ProjectHeadedSubjectStaysGreen()
    {
        // The field's shape in miniature: a project-headed subject, and an edge BOTH of whose endpoints
        // every declarer shares. Anchored on Tool, the rule owns Tool's instance alone — Tool's Widget
        // reaching Tool's WidgetPart — which neither operand names, so nothing is forbidden. In earlier
        // builds this red on Shared.Widget -> Shared.WidgetPart, because the operand's own declarers were
        // tested against the source's roster instead of against the project the instance reached.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-core-or-stub")
                    .Enforce(arch.Project("Tool")
                        .MustNotReference(arch.Project("Core"), arch.Project("Stub")))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotBeReferencedBy_BothEndpointsCoDeclared_ProjectHeadedSubjectStaysGreen()
    {
        // The inbound mirror of the row above, and it needs both ends of one test: anchored on Core the
        // subject owns Core's instance of the shared edge, and Core's copy of Widget is not something
        // Project("Tool") names. In earlier builds the two ends were filtered independently — the subject
        // admitted the edge, then the source was tested by plain membership, which Project("Tool")
        // satisfies for a type Tool declares — and the same edge red.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-not-from-tool-project")
                    .Enforce(arch.Project("Core").MustNotBeReferencedBy(arch.Project("Tool")))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_IntraCopyEdge_TypeofTargetStillReds()
    {
        // A typeof target names the type rather than a project, so it asks nothing about attribution: the
        // very edge Project("Core") let through in the first row is forbidden here.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-widget")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(typeof(Widget)))
                    .Because("The command surface is built on parts, not on whole widgets."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotReference_IntraCopyEdge_NamespaceTargetStillReds()
    {
        // The second attribution-insensitive head, and the one a rule author is likelier to write: a
        // namespace operand forbids the shared namespace in every project that compiles it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-shared")
                    .Enforce(arch.Namespace("Tool.*").MustNotReference(arch.Namespace("Shared.*")))
                    .Because("The command surface is built on parts, not on whole widgets."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotConstruct_ProjectConstructsItsOwnCompiledInCopy_WinnerAnchoredTargetStaysGreen()
    {
        // The construction verb reads the same attribution off the same endpoint pair: Make()'s `new` is
        // Tool's own, so a ban anchored on Core does not reach it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("di/tool-not-core")
                    .Enforce(arch.Namespace("Tool.*").MustNotConstruct(arch.Project("Core")))
                    .Because("The tool ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();

        // The same `new` under a typeof ban, so the green above is attribution rather than a missing edge.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("di/tool-not-widget")
                    .Enforce(arch.Namespace("Tool.*").MustNotConstruct(typeof(Widget)))
                    .Because("Widgets come from the factory."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Construction, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotBeReferencedBy_WinnerAnchoredSubject_DoesNotCountALosersOwnCopyEdge()
    {
        // The inbound verb puts the subject at the edge's target end, so the same rule decides which edges
        // it counts. Anchored on Core, the subject does not count Tool's reference into Tool's own copy —
        // and Tool's namespace is the only forbidden source, so the rule has nothing left to fail on.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-not-from-tool")
                    .Enforce(arch.Project("Core").MustNotBeReferencedBy(arch.Namespace("Tool.*")))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotBeReferencedBy_LoserAnchoredSubject_CountsThatLosersOwnCopyEdge()
    {
        // The same edge, the same forbidden source, and the subject moved to the declarer that compiled it.
        // Tool's copy is the one Command reached, so anchored on Tool the reference counts and the rule reds.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-not-from-tool")
                    .Enforce(arch.Project("Tool").MustNotBeReferencedBy(arch.Namespace("Tool.*")))
                    .Because("A tool's types are reached through its command surface."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Tool.Command", "Shared.Widget");
    }

    [Fact]
    public void MustNotBeReferencedBy_TypeofSubject_CountsEveryEdgeWhoeverCompiledIt()
    {
        // A typeof subject asks nothing about attribution either, so every inbound reference counts: the
        // winner's own (User), a loser's into its own copy (Command), and the cross-project one (Consumer).
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/widget-is-sealed-off")
                    .Enforce(arch.Type(typeof(Widget)).MustNotBeReferencedBy(arch.Types))
                    .Because("Widgets are reached through their parts."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, [
                "Client.Consumer -> Shared.Widget",
                "Core.User -> Shared.Widget",
                "Tool.Command -> Shared.Widget"
            ]);
    }

    [Fact]
    public void MustNotBeReferencedBy_SourceOperandNamingALoserDeclarer_ReachesEdgesFromTheSharedType()
    {
        // An edge's source belongs to every declarer, because each one's compilation genuinely makes the
        // reference. So Project("Tool") in source position reaches Command's edge and the shared type's own
        // outward edge alike, while Core's and Client's references stay outside it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/shared-not-from-tool")
                    .Enforce(arch.Namespace("Shared.*").MustNotBeReferencedBy(arch.Project("Tool")))
                    .Because("The shared file answers to nothing the tool declares."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, [
                "Shared.Widget -> Shared.WidgetPart",
                "Tool.Command -> Shared.Widget"
            ]);
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_AllowEntryNamingTheCompilingProjectSatisfies()
    {
        // Attribution at the allow-set's far end: the entry names the project that compiled both ends,
        // which is the project the edge belongs to, so the reference is allowed. Nothing here rests on the
        // implicit self-allowance, the subject being a namespace that does not contain Widget at all.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-tool")
                    .Enforce(arch.Namespace("Tool.*").MustOnlyReference(arch.Project("Tool")))
                    .Because("The tool is self-contained."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_AProjectHeadedSubjectAllowsTheCopyItAnchorsOn()
    {
        // The same allow-list moved to the winner, and a project-headed subject, so both instances the
        // rule owns are Tool's. The entry naming Core allows neither, Command's reference having reached
        // Tool's copy of Widget and Tool's copy of Widget having reached Tool's copy of WidgetPart. The
        // subject allows both: Project("Tool") names each of those nodes at Tool, which is the project
        // every owned instance reached.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-core")
                    .Enforce(arch.Project("Tool").MustOnlyReference(arch.Project("Core")))
                    .Because("The tool builds on the core alone."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_ASubjectThatDoesNotNameTheTargetAllowsNothingExtra()
    {
        // The boundary of the row above, over the very same edge: the subject is Tool's own
        // namespace rather than Tool the project, so it contains Command and not Widget, and the implicit
        // entry has nothing to say about the far end. Project("Stub") names Widget only at Stub, and the
        // instance reached Tool.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-stub")
                    .Enforce(arch.Namespace("Tool.*").MustOnlyReference(arch.Project("Stub")))
                    .Because("The command surface reaches the stub's declarations alone."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, ["Tool.Command -> Shared.Widget"]);
    }

    [Fact]
    public void MustOnlyReference_IntraCopyEdge_TypeofAllowEntryContainingTheTypeSatisfies()
    {
        // The other way an intra-copy edge is satisfied: an allow entry that is not a project at all simply
        // contains the type, and containment is all this verb asks of it.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-widget")
                    .Enforce(arch.Namespace("Tool.*").MustOnlyReference(typeof(Widget)))
                    .Because("The command surface reaches widgets and nothing else."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_WinnerAnchoredContainment_DoesNotRedOnALosersOwnCopyEdge()
    {
        // The containment verb, with the subject at the target end again. Command is in neither allowed
        // project, so counting its reference into Tool's own copy would red the rule; anchored on Core the
        // subject does not own that instance, while User's and Consumer's references are owned and allowed.
        // Core is listed beside Client redundantly under the implicit self-allowance, and stays as written
        // because what this row is about is which instances a WINNER-anchored subject owns. Drop Client
        // and the owned cross-project reference goes red, which the second row below pins.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-only-from-client")
                    .Enforce(arch.Project("Core")
                        .MustOnlyBeReferencedBy(arch.AnyOf(arch.Project("Client"), arch.Project("Core"))))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_IntraCopyEdge_AProjectHeadedSubjectAllowsItsOwnCopysSources()
    {
        // The inbound twin of the project-headed MustOnlyReference row above. Anchored on Tool the subject
        // owns Tool's instance of both inbound edges, and Project("Core") allows neither source: Tool.Command
        // is in no allowed project, and Tool's copy of Widget is allowed only by an entry naming Tool. The
        // subject is that entry, naming Command at Tool and Widget at Tool, which is the project both owned
        // instances reached.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/tool-only-from-core")
                    .Enforce(arch.Project("Tool").MustOnlyBeReferencedBy(arch.Project("Core")))
                    .Because("The tool's types are reached through the core."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_OwnedCrossProjectSource_StaysRedWhenNoEntryNamesIt()
    {
        // The boundary for the inbound verb, in the same attribution context: anchored on Core the subject
        // owns Consumer's reference into Core's copy, and Consumer is declared by Client alone, outside the
        // subject and outside Project("Stub"). The two edges the subject owns at its own copy stay green on
        // the implicit entry, so the one violation names the source that is genuinely elsewhere.
        Checker.Run(MultiplyDeclaredCodebase.Model, arch =>
                arch.Rule("layering/core-only-from-stub")
                    .Enforce(arch.Project("Core").MustOnlyBeReferencedBy(arch.Project("Stub")))
                    .Because("The core's declarations are reached through the stub."))
            .Single()
            .ShouldHaveFailedWithEdges(ViolationKind.Reference, ["Client.Consumer -> Shared.Widget"]);
    }

    [Fact]
    public void MustNotReference_MixedDeclarerSets_ReadsEachInstanceAtTheProjectItReached()
    {
        // Overlapping-but-different declarer sets, which is where one intra/cross decision for the whole
        // edge cannot be right for both instances. Shell's copy of Alpha declares no Beta, so its
        // reference crosses into Anchor's declaration, and a ban on Anchor catches it. In earlier builds the
        // edge counted as intra-project outright — Middle declares both ends — and this stayed green.
        Checker.Run(MixedDeclarerCodebase.Model, arch =>
                arch.Rule("layering/shell-not-anchor")
                    .Enforce(arch.Project("Shell").MustNotReference(arch.Project("Anchor")))
                    .Because("The shell is built against its own sources."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Split.Alpha", "Split.Beta");

        // The same edge under a ban on the project it never reached. Middle's instance is Middle's own
        // business, and Shell's reached Anchor; in earlier builds this red, because the intra branch admitted
        // every head the SOURCE was declared by rather than the one each instance actually compiled into.
        Checker.Run(MixedDeclarerCodebase.Model, arch =>
                arch.Rule("layering/shell-not-middle")
                    .Enforce(arch.Project("Shell").MustNotReference(arch.Project("Middle")))
                    .Because("The shell is built against its own sources."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_DriftedCopies_AnotherCopysReferenceDoesNotCountAgainstTheSatellite()
    {
        // The field's own rule. Satellite's copy of Formatter reaches nothing outside Satellite; it is Core's
        // copy that reaches Cell. In earlier builds every declarer of Formatter was charged with every reference any
        // copy made, so this red on Drift.Formatter -> Cells.Cell, a line in a file Satellite never compiles.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/satellite-is-foundation")
                    .Enforce(arch.Project("Satellite").MustOnlyReference(arch.Project("Satellite")))
                    .Because("The satellite builds on nothing else in the solution."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_DriftedCopies_TheWinnerIsNotChargedWithALosersReference()
    {
        // The mirror, and the direction a facts-follow-the-winner reading gets wrong: the reference to Palette is
        // Satellite's copy's alone, so Core, whose facts the node carries, never made it.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/core-not-satellite")
                    .Enforce(arch.Project("Core").MustNotReference(arch.Project("Satellite")))
                    .Because("The core ships without the satellite."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_DriftedCopies_EachCopysOwnReferenceStillCountsAgainstItsDeclarer()
    {
        // The rows above are green because each edge moved to the copy that made it, not because the edge went.
        // Anchored on the declarer that made it, each reference still reds, and it reds alone.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/core-not-cells")
                    .Enforce(arch.Project("Core").MustNotReference(arch.Namespace("Cells")))
                    .Because("Formatting is measured elsewhere."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Drift.Formatter", "Cells.Cell");

        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/satellite-not-palettes")
                    .Enforce(arch.Project("Satellite").MustNotReference(arch.Namespace("Palettes")))
                    .Because("Colours are chosen elsewhere."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Drift.Formatter", "Palettes.Palette");
    }

    [Fact]
    public void MustNotConstructAndExpose_DriftedCopies_ReadTheSameAttributionAsTheReferenceVerbs()
    {
        // The construction and exposure families carry their own copy of the attribution rather than borrowing
        // the reference edge's, and both type pairs are built and exposed by one copy each.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("di/satellite-not-core")
                    .Enforce(arch.Project("Satellite").MustNotConstruct(arch.Project("Core")))
                    .Because("The satellite builds on nothing else in the solution."))
            .Single()
            .ShouldHavePassedClean();

        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("api/core-not-satellite")
                    .Enforce(arch.Project("Core").MustNotExpose(arch.Project("Satellite")))
                    .Because("The core's surface names nothing the satellite declares."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_ReferenceIntoALosersCopy_CountsAgainstTheLoserAndNotTheWinner()
    {
        // Reader declares no copy of Formatter and references Satellite alone, so the copy its field binds is
        // Satellite's. In earlier builds a reference from a project declaring no copy was attributed to the first
        // declarer, whose facts the node carries, and these two verdicts came out the other way round.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/reader-not-satellite")
                    .Enforce(arch.Project("Reader").MustNotReference(arch.Project("Satellite")))
                    .Because("The reader is built against the core."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Reader.Page", "Drift.Formatter");

        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/reader-not-core")
                    .Enforce(arch.Project("Reader").MustNotReference(arch.Project("Core")))
                    .Because("The reader ships without the core assembly."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_ReferenceIntoALosersCopy_IsTheLosersToAllow()
    {
        // The inbound reading of the same instance: the subject sits at the target end, so the copy the reference
        // reached decides which subject owns it. Anchored on Satellite the reference is owned and unallowed;
        // anchored on Core it is not Core's at all, and Client's genuine reference is the only one Core owns.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/satellite-used-by-itself")
                    .Enforce(arch.Project("Satellite").MustOnlyBeReferencedBy(arch.Project("Satellite")))
                    .Because("The satellite is consumed through the core."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Reader.Page", "Drift.Formatter");

        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/core-used-by-client")
                    .Enforce(arch.Project("Core").MustOnlyBeReferencedBy(arch.Project("Client")))
                    .Because("The core is consumed through the client."))
            .Single()
            .ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotReference_DriftedCopies_TheGenuineCrossProjectReferenceStillReds()
    {
        // The control: Client's reference crosses into Core, and nothing about the shared name touches it.
        Checker.Run(DriftedCopiesCodebase.Model, arch =>
                arch.Rule("layering/client-not-core")
                    .Enforce(arch.Project("Client").MustNotReference(arch.Project("Core")))
                    .Because("The client is built against a published contract."))
            .Single()
            .ShouldHaveFailedWithSingleEdge(ViolationKind.Reference, "Client.Renderer", "Cells.Cell");
    }
}
