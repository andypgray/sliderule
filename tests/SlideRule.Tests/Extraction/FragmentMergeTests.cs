using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Roslyn;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The cross-input unification semantics of the model build, over the MSBuild-free fast path with
///     several synthetic <see cref="CompilationInput" />s. The first declarer of a name wins its node's
///     facts. Where several projects declare one name, or one project compiles for several frameworks, the
///     merge keeps what that rule cannot carry beside the node: the declaration sites, the merge notes and
///     the edge instances each compilation recorded.
/// </summary>
public sealed class FragmentMergeTests
{
    private const string DupSource =
        """
        namespace N;
        public class Dup {}
        """;

    // The name two projects each compile a copy of, in every drifted-copy bed below.
    private const string FormatterSource = "namespace Drift { public class Formatter {} }";

    // One file two projects both compile, declaring a type and the type it references.
    private const string LinkedWidgetSource =
        "namespace Shared { public class Widget { public WidgetPart Part; } public class WidgetPart {} }";

    [Fact]
    public void ExtractFromCompilations_DuplicateFqnInTwoInputs_FirstInputWinsProjectNameAndShapeFacts()
    {
        // Both inputs declare N.Dup; the first (ordinal-project input order) owns the node facts.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public sealed class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        TypeNode dup = model.Type("N.Dup");
        dup.ProjectName.ShouldBe("Aproj");
        dup.IsSealed.ShouldBeFalse();
    }

    [Fact]
    public void ExtractFromCompilations_DuplicateFqnInTwoInputs_UnionsDeclarationSitesAcrossInputs()
    {
        // Partial-across-projects shape: one FQN declared by two inputs unions its declaration sites.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Type("N.Dup")
            .DeclarationSites.Select(s => (s.FilePath, s.Line))
            .ShouldBe([("A.cs", 2), ("B.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_DuplicateFqnInTwoInputs_KeepsEachDeclarersOwnSites()
    {
        // The union above is what every rule reads; a card's placement needs each project's own copy as well.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", DupSource));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", DupSource));

        TypeNode dup = CodebaseExtractor.ExtractFromCompilations([first, second])
            .Type("N.Dup");

        SitesOf(dup, "Aproj")
            .ShouldBe([("A.cs", 2)]);
        SitesOf(dup, "Bproj")
            .ShouldBe([("B.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_TwoFrameworksOfOneDeclarer_ShareOneEntry()
    {
        CompilationInput older = CompilationFactory.Compile("Aproj", ("A1.cs", DupSource)) with { TargetFramework = "net8.0" };
        CompilationInput newer = CompilationFactory.Compile("Aproj", ("A2.cs", DupSource)) with { TargetFramework = "net10.0" };
        CompilationInput other = CompilationFactory.Compile("Bproj", ("B.cs", DupSource));

        TypeNode dup = CodebaseExtractor.ExtractFromCompilations([older, newer, other])
            .Type("N.Dup");

        dup.DeclarationSitesByDeclarer.ShouldNotBeNull()
            .Keys.Order(StringComparer.Ordinal)
            .ShouldBe(["Aproj", "Bproj"]);
        SitesOf(dup, "Aproj")
            .ShouldBe([("A1.cs", 2), ("A2.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_OneFileInTwoProjects_LandsInBothShares()
    {
        CompilationInput core = CompilationFactory.Compile("Core", ("Shared/Dup.cs", DupSource));
        CompilationInput tool = CompilationFactory.Compile(
            "Tool", ("Shared/Dup.cs", DupSource), ("Command.cs", "namespace N; public class Command {}"));

        TypeNode dup = CodebaseExtractor.ExtractFromCompilations([core, tool])
            .Type("N.Dup");

        SitesOf(dup, "Core")
            .ShouldBe([("Shared/Dup.cs", 2)]);
        SitesOf(dup, "Tool")
            .ShouldBe([("Shared/Dup.cs", 2)]);
        dup.DeclarationSites.ShouldHaveSingleItem();
    }

    [Fact]
    public void ExtractFromCompilations_WinnersLaterFrameworkAfterASecondDeclarer_JoinsTheWinnersShare()
    {
        // Inputs can interleave projects, so the winning project's second framework can arrive after the share
        // table exists; its sites belong in the winner's share, not in a share of their own or nowhere.
        CompilationInput older = CompilationFactory.Compile("Aproj", ("A1.cs", DupSource)) with { TargetFramework = "net8.0" };
        CompilationInput other = CompilationFactory.Compile("Bproj", ("B.cs", DupSource));
        CompilationInput newer = CompilationFactory.Compile("Aproj", ("A2.cs", DupSource)) with { TargetFramework = "net10.0" };

        TypeNode dup = CodebaseExtractor.ExtractFromCompilations([older, other, newer])
            .Type("N.Dup");

        SitesOf(dup, "Aproj")
            .ShouldBe([("A1.cs", 2), ("A2.cs", 2)]);
        SitesOf(dup, "Bproj")
            .ShouldBe([("B.cs", 2)]);
    }

    [Fact]
    public void ExtractFromCompilations_FqnReferencedByEarlierInputDeclaredByLater_MaterializesAsDeclaredNode()
    {
        // The referencer is ordered BEFORE the declarer, so this only holds if declare-all fully
        // precedes hierarchy/edges: N.Late resolves to the declared node, not a shallow external.
        CompilationInput declarer = CompilationFactory.Compile("Bproj", ("Late.cs", """
                                                                                    namespace N;
                                                                                    public class Late {}
                                                                                    """));
        CompilationInput referencer = CompilationFactory.CompileReferencing(
            "Aproj", declarer.Compilation, "Bproj", ("Early.cs", """
                                                                 namespace N2;
                                                                 public class Early { public N.Late L; }
                                                                 """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([referencer, declarer]);

        TypeNode target = model.Edge("N2.Early", "N.Late")
            .Target;
        target.IsExternal.ShouldBeFalse();
        target.ProjectName.ShouldBe("Bproj");
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputConstruction_DefinitionIsSameInstanceAsTypesNode()
    {
        // The construction's Definition must be the very TypeNode instance held by Types (the
        // documented reference-equality contract), even when definition and user cross inputs.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IHandler<T> {}
                                                                              public class Msg {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Handler : N.IHandler<N.Msg> {}
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode handlerDef = model.Type("N.IHandler<T>");
        TypeConstruction construction = model.Type("N2.Handler")
            .AllInterfaces
            .Single(c => c.Definition.FullName == "N.IHandler<T>");
        construction.Definition.ShouldBeSameAs(handlerDef);
        construction.FullName.ShouldBe("N.IHandler<N.Msg>");
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputEdges_ReferenceSameNodeInstancesAsTypes()
    {
        // Edge Source/Target are the same instances as Types (reference equality, not name equality).
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IHandler<T> {}
                                                                              public class Msg {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Handler : N.IHandler<N.Msg> {}
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode handler = model.Type("N2.Handler");
        TypeNode handlerDef = model.Type("N.IHandler<T>");
        TypeNode msg = model.Type("N.Msg");
        ReferenceEdge toInterface = model.Edge("N2.Handler", "N.IHandler<T>");
        ReferenceEdge toArgument = model.Edge("N2.Handler", "N.Msg");

        toInterface.Source.ShouldBeSameAs(handler);
        toInterface.Target.ShouldBeSameAs(handlerDef);
        toArgument.Target.ShouldBeSameAs(msg);
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsProjectReferences()
    {
        // Two compilations sharing a project name model one project's two TFMs; BuildProjects collapses
        // them to one node whose references are the ordinal union of both TFMs' references.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file) with { ProjectReferences = ["Legacy"] };
        CompilationInput second = CompilationFactory.Compile("P", file) with { ProjectReferences = ["Modern"] };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Projects.ShouldContain(p => p.Name == "P", expectedCount: 1);
        model.Projects.Single(p => p.Name == "P")
            .ProjectReferences.ShouldBe(["Legacy", "Modern"]);
    }

    [Fact]
    public void ExtractFromCompilations_ExternalReferencedByTwoInputs_IsSingleSharedExternalNode()
    {
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class CA { public System.Exception E; }
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N2;
                                                                               public class CB { public System.Exception E; }
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.Types.ShouldContain(t => t.FullName == "System.Exception", expectedCount: 1);
        TypeNode shared = model.Type("System.Exception");
        shared.IsExternal.ShouldBeTrue();
        model.Edge("N.CA", "System.Exception")
            .Target.ShouldBeSameAs(shared);
        model.Edge("N2.CB", "System.Exception")
            .Target.ShouldBeSameAs(shared);
    }

    [Fact]
    public void ExtractFromCompilations_CrossInputConstruction_ConstructedIsSameInstanceAsTypesNode()
    {
        // A construction whose constructed type is declared by another input resolves to the declared node
        // (reference equality), never a shallow external — the ctor analog of the cross-input edge contract.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public class Widget {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Maker { public object M() => new N.Widget(); }
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode widget = model.Type("N.Widget");
        ConstructorEdge edge = model.ConstructorEdge("N2.Maker", "N.Widget");
        edge.Constructed.ShouldBeSameAs(widget);
        edge.Constructed.IsExternal.ShouldBeFalse();
        edge.Source.ShouldBeSameAs(model.Type("N2.Maker"));
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsDuplicateConstructorEdgesToOneSite()
    {
        // Two compilations sharing a project name and source path model one project's two TFMs; the merge
        // unions the identical ctor edge (and its single site) rather than duplicating it.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            public class B { public object M() => new A(); }
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file);
        CompilationInput second = CompilationFactory.Compile("P", file);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.ConstructorEdges.ShouldContain(
            e => e.Source.FullName == "P.B" && e.Constructed.FullName == "P.A", expectedCount: 1);
        model.ConstructorEdge("P.B", "P.A")
            .Sites.ShouldHaveSingleItem();
    }

    // ── Injection edges / registration facts (GRAMMAR §4.7) ───────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_CrossInputInjection_InjectedIsSameInstanceAsTypesNode()
    {
        // An injected parameter type declared by another input resolves to the declared node (reference
        // equality), never a shallow external — the injection analog of the cross-input edge contract.
        CompilationInput lib = CompilationFactory.Compile("Aproj", ("Lib.cs", """
                                                                              namespace N;
                                                                              public interface IDep {}
                                                                              """));
        CompilationInput app = CompilationFactory.CompileReferencing(
            "Bproj", lib.Compilation, "Aproj", ("App.cs", """
                                                          namespace N2;
                                                          public class Svc { public Svc(N.IDep d) {} }
                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([lib, app]);

        TypeNode dep = model.Type("N.IDep");
        InjectionEdge edge = model.InjectionEdge("N2.Svc", "N.IDep");
        edge.Injected.ShouldBeSameAs(dep);
        edge.Injected.IsExternal.ShouldBeFalse();
        edge.Source.ShouldBeSameAs(model.Type("N2.Svc"));
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectNameTwice_UnionsDuplicateInjectionEdgesToOneSite()
    {
        // Two compilations sharing a project name and source path model one project's two TFMs; the merge
        // unions the identical injection edge (and its single site) rather than duplicating it.
        var file = ("P.cs", """
                            namespace P;
                            public interface IDep {}
                            public class Svc { public Svc(IDep d) {} }
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file);
        CompilationInput second = CompilationFactory.Compile("P", file);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.InjectionEdges.ShouldContain(
            e => e.Source.FullName == "P.Svc" && e.Injected.FullName == "P.IDep", expectedCount: 1);
        model.InjectionEdge("P.Svc", "P.IDep")
            .Sites.ShouldHaveSingleItem();
    }

    [Fact]
    public void ExtractFromCompilations_SameRegistrationInTwoFrameworks_UnionsToOneFactPerKey()
    {
        // One project's two target frameworks make the identical registration; the merge unions per
        // (lifetime, service, implementation) key to a single fact (its one shared site deduped).
        var file = ("Reg.cs", """
                              using Microsoft.Extensions.DependencyInjection;
                              namespace P;
                              public interface IFoo {}
                              public class Foo : IFoo {}
                              public static class Reg
                              {
                                  public static void Configure(IServiceCollection services) => services.AddSingleton<IFoo, Foo>();
                              }
                              """);
        CompilationInput first = CompilationFactory.CompileWithDi("P", file) with { ProjectReferences = ["Legacy"] };
        CompilationInput second = CompilationFactory.CompileWithDi("P", file) with { ProjectReferences = ["Modern"] };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.ServiceRegistrations.ShouldContain(r => r.ServiceFullName == "P.IFoo", expectedCount: 1);
        model.Registration(Lifetime.Singleton, "P.IFoo", "P.Foo")
            .Sites.ShouldHaveSingleItem();
    }

    // ── Same-FQN cross-project conflation notes ───────────────────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_SameFqnDeclaredByTwoDifferentProjects_RecordsCrossProjectMergeNote()
    {
        // Both Aproj and Bproj declare N.Dup. Aproj wins the facts (input order) while arch.Project("Bproj")
        // still selects the type — the merge records one advisory note naming winner, loser, and consequence.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class Dup {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBe([
            "Projects 'Aproj' and 'Bproj' each declare 'N.Dup'; its facts and project attribution follow "
            + "'Aproj' (the first declarer), but arch.Project('Bproj') selections include it too, and each "
            + "declarer's reference to its own compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_SameFqnSameProjectNameTwice_RecordsNoMergeNote()
    {
        // One project's two target frameworks declare the same FQN under the same name — the legitimate
        // multi-TFM union, not a conflation. It must stay silent.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file) with { ProjectReferences = ["Legacy"] };
        CompilationInput second = CompilationFactory.Compile("P", file) with { ProjectReferences = ["Modern"] };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_DistinctFqnsAcrossProjects_HasNoMergeNotes()
    {
        // The common case: two projects, no shared FQN → nothing to conflate → the notes list stays empty.
        CompilationInput first = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                              namespace N;
                                                                              public class A {}
                                                                              """));
        CompilationInput second = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                               namespace N;
                                                                               public class B {}
                                                                               """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_CrossProjectLoserDeclaredInTwoFrameworks_RecordsExactlyOneNote()
    {
        // Winner Aproj declares N.Dup once; loser Bproj declares it in two frameworks. The loser set
        // collapses the loser's two declarations to a single entry, and so to a single note.
        CompilationInput winner = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        var loserFirst = new CompilationInput(
            CompilationFactory.Compile("Bproj", ("B.cs", """
                                                         namespace N;
                                                         public class Dup {}
                                                         """))
                .Compilation, "Bproj", ["Legacy"]);
        var loserSecond = new CompilationInput(
            CompilationFactory.Compile("Bproj", ("B.cs", """
                                                         namespace N;
                                                         public class Dup {}
                                                         """))
                .Compilation, "Bproj", ["Modern"]);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, loserFirst, loserSecond]);

        model.MergeNotes.ShouldHaveSingleItem()
            .ShouldContain("Projects 'Aproj' and 'Bproj' each declare 'N.Dup'");
    }

    [Fact]
    public void ExtractFromCompilations_SameFqnLostByThreeProjects_RecordsOneNoteNamingEveryLoser()
    {
        // The shape a spec-fixture layout produces when several sibling spec projects declare name-carrier
        // stubs so their typeof() anchors compile: one type shadowed several times over. No fixture here has
        // that layout — this repo's fixtures reference the projects they govern — but the shape is what any
        // real solution with stubbed anchors reports, so it stays pinned here. Grouping by
        // declarer set keeps it to one line naming every loser rather than a line per loser: nothing dropped,
        // and the channel stays legible enough that a reader still notices a line arriving.
        CompilationInput winner = CompilationFactory.Compile("App.Web", ("W.cs", """
                                                                                 namespace N;
                                                                                 public class Dup {}
                                                                                 """));
        CompilationInput stubB = CompilationFactory.Compile("Spec.B", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        CompilationInput stubA = CompilationFactory.Compile("Spec.A", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        // Losers arrive out of order; the note orders them ordinal so the line is stable across runs.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, stubB, stubA]);

        model.MergeNotes.ShouldBe([
            "Projects 'App.Web', 'Spec.A' and 'Spec.B' each declare 'N.Dup'; its facts and project "
            + "attribution follow 'App.Web' (the first declarer), but arch.Project('Spec.A') and "
            + "arch.Project('Spec.B') selections include it too, and each declarer's reference to its own "
            + "compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_TwoTypesSharedByOneDeclarerSet_RecordsOneNoteNamingBoth()
    {
        // The grain the note groups at. Two projects compile the same pair of types in — a source-only
        // package, a shared folder — and the answer is word for word the same for both types, so it is said
        // once and names them both. A package of a hundred types costs this one line rather than a hundred.
        var shared = ("Shared.cs", """
                                   namespace N;
                                   public class Dup {}
                                   public class Other {}
                                   """);
        CompilationInput winner = CompilationFactory.Compile("Aproj", shared);
        CompilationInput loser = CompilationFactory.Compile("Bproj", shared);

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, loser]);

        model.MergeNotes.ShouldBe([
            "Projects 'Aproj' and 'Bproj' each declare 'N.Dup' and 'N.Other'; their facts and project "
            + "attribution follow 'Aproj' (the first declarer), but arch.Project('Bproj') selections include "
            + "them too, and each declarer's reference to its own compiled-in copy counts against that "
            + "declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_TwoTypesWithDifferentDeclarerSets_RecordsOneNotePerSetOrderedByWinnerThenLosers()
    {
        // Two sets, so two notes: grouping coarsens the channel without merging answers that differ. The
        // order is the group's own key — winner, then its losers — never the order the sets were raised in,
        // which is why Cproj arrives before Bproj here and its note still comes second.
        CompilationInput both = CompilationFactory.Compile("Aproj", ("A.cs", """
                                                                             namespace N;
                                                                             public class Dup {}
                                                                             public class Other {}
                                                                             """));
        CompilationInput otherOnly = CompilationFactory.Compile("Cproj", ("C.cs", """
                                                                                  namespace N;
                                                                                  public class Other {}
                                                                                  """));
        CompilationInput dupOnly = CompilationFactory.Compile("Bproj", ("B.cs", """
                                                                                namespace N;
                                                                                public class Dup {}
                                                                                """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([both, otherOnly, dupOnly]);

        model.MergeNotes.ShouldBe([
            "Projects 'Aproj' and 'Bproj' each declare 'N.Dup'; its facts and project attribution follow "
            + "'Aproj' (the first declarer), but arch.Project('Bproj') selections include it too, and each "
            + "declarer's reference to its own compiled-in copy counts against that declarer alone.",
            "Projects 'Aproj' and 'Cproj' each declare 'N.Other'; its facts and project attribution follow "
            + "'Aproj' (the first declarer), but arch.Project('Cproj') selections include it too, and each "
            + "declarer's reference to its own compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_OverlappingButUnequalLoserSets_RecordOneNoteEach()
    {
        // The near miss grouping has to survive: both types share a winner and both are lost by Bproj, and
        // only N.Other is lost by Cproj as well. Merging them would put Cproj in front of a reader looking at
        // N.Dup, which it never declared. The '\n' join over the sorted loser set is what keeps {Bproj} and
        // {Bproj, Cproj} apart, because a project name cannot contain the joiner.
        var both = ("Both.cs", """
                               namespace N;
                               public class Dup {}
                               public class Other {}
                               """);
        CompilationInput winner = CompilationFactory.Compile("Aproj", both);
        CompilationInput everyType = CompilationFactory.Compile("Bproj", both);
        CompilationInput otherOnly = CompilationFactory.Compile("Cproj", ("C.cs", """
                                                                                  namespace N;
                                                                                  public class Other {}
                                                                                  """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, everyType, otherOnly]);

        model.MergeNotes.ShouldBe([
            "Projects 'Aproj' and 'Bproj' each declare 'N.Dup'; its facts and project attribution follow "
            + "'Aproj' (the first declarer), but arch.Project('Bproj') selections include it too, and each "
            + "declarer's reference to its own compiled-in copy counts against that declarer alone.",
            "Projects 'Aproj', 'Bproj' and 'Cproj' each declare 'N.Other'; its facts and project attribution "
            + "follow 'Aproj' (the first declarer), but arch.Project('Bproj') and arch.Project('Cproj') "
            + "selections include it too, and each declarer's reference to its own compiled-in copy counts "
            + "against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_SetNoteBesideAFrameworkNote_KeepsTheKindsOrder()
    {
        // The two kinds in one model, and the order they render in is the kind's, not the order either was
        // raised: the project-level framework note first, the declarer-set note after it. P's two frameworks
        // share P.A — one project, so a collapse rather than a conflation — and Q declares P.A as well, which
        // is a conflation P wins.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput modern = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };
        CompilationInput legacy = CompilationFactory.Compile("P", file) with { TargetFramework = "netstandard2.0" };
        CompilationInput other = CompilationFactory.Compile("Q", ("Q.cs", """
                                                                          namespace P;
                                                                          public class A {}
                                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy, other]);

        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net10.0' and 'netstandard2.0'; the types they share take their members and "
            + "hierarchy from 'net10.0' (the first extracted) but keep every framework's references, so a rule "
            + "about their members or hierarchy is checked against that framework alone and a rule about what "
            + "they reference sees every framework.",
            "Projects 'P' and 'Q' each declare 'P.A'; its facts and project attribution follow 'P' (the first "
            + "declarer), but arch.Project('Q') selections include it too, and each declarer's reference to "
            + "its own compiled-in copy counts against that declarer alone."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_ConflatedType_CarriesEveryLosingProjectOnTheWinningNode()
    {
        // The same conflation as the note above, kept as a fact rather than only as a sentence: the survey
        // has to suppress an edge on it, and no consumer can act on prose. Losers only — ProjectName is the
        // winner, so repeating it here would make the node say the same thing twice.
        CompilationInput winner = CompilationFactory.Compile("App.Web", ("W.cs", """
                                                                                 namespace N;
                                                                                 public class Dup {}
                                                                                 """));
        CompilationInput stubB = CompilationFactory.Compile("Spec.B", ("B.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));
        CompilationInput stubA = CompilationFactory.Compile("Spec.A", ("A.cs", """
                                                                               namespace N;
                                                                               public class Dup {}
                                                                               """));

        // Losers arrive out of order; the list orders them ordinal like the note that reads the same table.
        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([winner, stubB, stubA]);

        TypeNode dup = model.Type("N.Dup");
        dup.ProjectName.ShouldBe("App.Web");
        dup.AlsoDeclaredBy.ShouldBe(["Spec.A", "Spec.B"]);
    }

    [Fact]
    public void ExtractFromCompilations_TypesNoOtherProjectDeclares_CarryNoOtherDeclarers()
    {
        // The overwhelming common case, and what keeps the survey's coverage statement absent rather than
        // empty: an ordinary type says nothing, and so does one whose own project declared it twice under
        // two target frameworks — one project is never a conflation.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput modern = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };
        CompilationInput legacy = CompilationFactory.Compile("P", file) with { TargetFramework = "netstandard2.0" };
        CompilationInput other = CompilationFactory.Compile("Q", ("Q.cs", """
                                                                          namespace Q;
                                                                          public class B {}
                                                                          """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy, other]);

        model.Type("P.A")
            .AlsoDeclaredBy.ShouldBeEmpty();
        model.Type("Q.B")
            .AlsoDeclaredBy.ShouldBeEmpty();
    }

    // ── Multi-target-framework collapse notes ─────────────────────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_SameProjectTwoFrameworksSharingAType_RecordsOneMultiFrameworkNote()
    {
        // One project file's two frameworks both declare P.A, so the type carries the first-extracted
        // framework's facts and only its facts. That is not a conflation — the project is one project — but
        // it is not free either, so it gets a note of its own naming both frameworks and the winner.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput modern = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };
        CompilationInput legacy = CompilationFactory.Compile("P", file) with { TargetFramework = "netstandard2.0" };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net10.0' and 'netstandard2.0'; the types they share take their members and "
            + "hierarchy from 'net10.0' (the first extracted) but keep every framework's references, so a rule "
            + "about their members or hierarchy is checked against that framework alone and a rule about what "
            + "they reference sees every framework."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectTwoFrameworksDeclaringDisjointTypes_RecordsNoNote()
    {
        // The gate, and it is a correctness matter rather than an economy: a type only ONE framework
        // declares (a #if-guarded class, a framework-conditional <Compile>) keeps its own framework's facts.
        // Nothing collapsed, so a note saying the facts came from the other framework would be false.
        var modern = new CompilationInput(
            CompilationFactory.Compile("P", ("Modern.cs", """
                                                          namespace P;
                                                          public class Modern {}
                                                          """))
                .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(
            CompilationFactory.Compile("P", ("Legacy.cs", """
                                                          namespace P;
                                                          public class Legacy {}
                                                          """))
                .Compilation, "P", [], "netstandard2.0");

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractFromCompilations_SameProjectOneFrameworkDeclaringATypeTwice_RecordsNoNote()
    {
        // Two compilations under one project name and one framework, which is how two project files sharing a
        // file name and a framework arrive here. Nothing collapsed across frameworks, so a note would name the
        // framework as winning over itself, and FactsFollow would name a framework nothing followed.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput first = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };
        CompilationInput second = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
        model.Projects.Single(project => project.Name == "P")
            .FactsFollow.ShouldBeNull();
    }

    [Fact]
    public void ExtractFromCompilations_TwoProjectFilesSharingANameAndNoFramework_MergeAsOneProjectWithNoNote()
    {
        // The remark on NormalizeProjectNames states this: nothing downstream discloses the shared name.
        CompilationInput first = CompilationFactory.Compile("P", ("First/A.cs", """
                                                                                namespace P;
                                                                                public class A {}
                                                                                """));
        CompilationInput second = CompilationFactory.Compile("P", ("Second/A.cs", """
                                                                                  namespace P;
                                                                                  public class A {}
                                                                                  """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([first, second]);

        model.MergeNotes.ShouldBeEmpty();
        model.Projects.Select(project => project.Name)
            .ShouldBe(["P"]);
        TypeNode node = model.Type("P.A");
        node.ShouldSatisfyAllConditions(
            () => node.AlsoDeclaredBy.ShouldBeEmpty(),
            () => node.FilePaths.ShouldBe(["First/A.cs", "Second/A.cs"]));
    }

    [Fact]
    public void ExtractFromCompilations_MultiFrameworkProjectSharingManyTypes_RecordsExactlyOneNote()
    {
        // Per project, not per type: one rule covers every shared type, so a project sharing three types
        // costs one line and a project sharing two hundred still costs one — which is what keeps the channel
        // legible enough that a reader notices a line arriving at all.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            public class B {}
                            public class C {}
                            """);
        CompilationInput modern = CompilationFactory.Compile("P", file) with { TargetFramework = "net10.0" };
        CompilationInput legacy = CompilationFactory.Compile("P", file) with { TargetFramework = "netstandard2.0" };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.MergeNotes.ShouldHaveSingleItem()
            .ShouldContain("Project 'P' targets 'net10.0' and 'netstandard2.0'");
    }

    [Fact]
    public void ExtractFromCompilations_ReferenceOnlyTheLosingFrameworkMakes_IsInTheModelWhileItsMemberIsNot()
    {
        // The two axes parting company on one type, which is what the note has to say. Both frameworks
        // declare P.A; only netstandard2.0's compilation gives it a field, and only netstandard2.0 compiles
        // the P.Legacy that field is typed as. net10.0 is extracted first and wins, so the member inventory
        // is empty — and the reference the dropped field made survives anyway, because every edge unions
        // whichever framework's compilation observed it. The note stays a single line: P.Legacy is declared
        // by one framework alone, so it collapsed nothing and must add nothing.
        var modern = new CompilationInput(
            CompilationFactory.Compile("P", ("Modern.cs", """
                                                          namespace P;
                                                          public class A {}
                                                          """))
                .Compilation, "P", [], "net10.0");
        var legacy = new CompilationInput(
            CompilationFactory.Compile("P", ("Legacy.cs", """
                                                          namespace P;
                                                          public class A { public Legacy Field; }
                                                          public class Legacy {}
                                                          """))
                .Compilation, "P", [], "netstandard2.0");

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.Edge("P.A", "P.Legacy")
            .Sites.ShouldHaveSingleItem();
        model.Type("P.A")
            .Members.ShouldBeEmpty();
        model.Type("P.Legacy")
            .ProjectName.ShouldBe("P");
        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net10.0' and 'netstandard2.0'; the types they share take their members and "
            + "hierarchy from 'net10.0' (the first extracted) but keep every framework's references, so a rule "
            + "about their members or hierarchy is checked against that framework alone and a rule about what "
            + "they reference sees every framework."
        ]);
    }

    [Fact]
    public void ExtractFromCompilations_ThreeFrameworksSharingDifferentTypeSets_NamesEveryWinner()
    {
        // net48 and net6.0 share nothing, and net8.0 shares a type with each: P.Beta with net48, P.Alpha with
        // net6.0. Each shared type follows the first framework that declares it, so the two follow different
        // frameworks and the note names both rather than one winner for all of them. net8.0 meets P.Alpha
        // first, so the first collapse's winner would be net6.0; FactsFollow is the earliest winner instead.
        CompilationInput legacy = CompilationFactory.Compile("P", ("Beta.cs", """
                                                                              namespace P;
                                                                              public class Beta { public void FortyEight() {} }
                                                                              """)) with
        {
            TargetFramework = "net48"
        };
        CompilationInput modern = CompilationFactory.Compile("P", ("Alpha.cs", """
                                                                               namespace P;
                                                                               public class Alpha { public void Six() {} }
                                                                               """)) with
        {
            TargetFramework = "net6.0"
        };
        CompilationInput latest = CompilationFactory.Compile("P", ("Both.cs", """
                                                                              namespace P;
                                                                              public class Alpha { public void Eight() {} }
                                                                              public class Beta { public void Later() {} }
                                                                              """)) with
        {
            TargetFramework = "net8.0"
        };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern, latest]);

        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net48', 'net6.0' and 'net8.0'; each type they share takes its members and "
            + "hierarchy from the first of them that declares it ('net48' or 'net6.0') but keeps every framework's "
            + "references, so a rule about its members or hierarchy is checked against that framework alone and a "
            + "rule about what it references sees every framework."
        ]);
        model.Projects.Single(project => project.Name == "P")
            .FactsFollow.ShouldBe("net48");
        model.Type("P.Alpha")
            .MemberIds()
            .ShouldContain("M:P.Alpha.Six");
        model.Type("P.Alpha")
            .MemberIds()
            .ShouldNotContain("M:P.Alpha.Eight");
        model.Type("P.Beta")
            .MemberIds()
            .ShouldContain("M:P.Beta.FortyEight");
        model.Type("P.Beta")
            .MemberIds()
            .ShouldNotContain("M:P.Beta.Later");
    }

    [Fact]
    public void ExtractFromCompilations_ThreeFrameworksSharingOneTypeSet_KeepsTheOneWinnerSentence()
    {
        // Three frameworks are not enough to change the note: every shared type here is one the first framework
        // declares, so one framework won them all and the note says so in the sentence two frameworks get.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput legacy = CompilationFactory.Compile("P", file) with { TargetFramework = "net48" };
        CompilationInput modern = CompilationFactory.Compile("P", file) with { TargetFramework = "net6.0" };
        CompilationInput latest = CompilationFactory.Compile("P", file) with { TargetFramework = "net8.0" };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern, latest]);

        model.MergeNotes.ShouldBe([
            "Project 'P' targets 'net48', 'net6.0' and 'net8.0'; the types they share take their members and "
            + "hierarchy from 'net48' (the first extracted) but keep every framework's references, so a rule "
            + "about their members or hierarchy is checked against that framework alone and a rule about what "
            + "they reference sees every framework."
        ]);
    }

    // ── Multi-target-framework membership union ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, null, true)]
    [InlineData(null, true, true)]
    [InlineData(false, null, false)]
    [InlineData(null, false, false)]
    [InlineData(false, false, false)]
    [InlineData(null, null, null)]
    public void ExtractFromCompilations_MultiFrameworkProject_UnionsMembershipWithUnknownLosing(
        bool? first, bool? second, bool? expected)
    {
        // One project file, several compilations, one ProjectNode — so the label has to union like the
        // reference edges do. Declared by either wins; unknown never overrides a fragment that actually read
        // the solution, and only an all-unknown project stays unlabeled.
        var file = ("P.cs", """
                            namespace P;
                            public class A {}
                            """);
        CompilationInput modern = CompilationFactory.Compile("P", file)
            with
            {
                TargetFramework = "net10.0", SolutionMember = first
            };
        CompilationInput legacy = CompilationFactory.Compile("P", file)
            with
            {
                TargetFramework = "netstandard2.0", SolutionMember = second
            };

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([modern, legacy]);

        model.Projects.Single()
            .SolutionMember.ShouldBe(expected);
    }

    [Fact]
    public void ExtractFromCompilations_OneDeclarer_AnswersFromTheUnionWithoutShares()
    {
        // One declarer records no shares: its own project reads every site, and any other project reads none.
        CompilationInput only = CompilationFactory.Compile("Aproj", ("A.cs", DupSource));

        TypeNode dup = CodebaseExtractor.ExtractFromCompilations([only])
            .Type("N.Dup");

        dup.DeclarationSitesByDeclarer.ShouldBeNull();
        SitesOf(dup, "Aproj")
            .ShouldBe([("A.cs", 2)]);
        SitesOf(dup, "Bproj")
            .ShouldBeEmpty();
    }

    // ── Edge instances where several projects declare a name ─────────────────────────────────────────────

    [Fact]
    public void ExtractFromCompilations_DriftedCopies_RecordEachFamilysEdgeAgainstTheCopyThatMadeIt()
    {
        // Two copies of one name in two files. Core's reaches Cell and CellException six ways; Satellite's only
        // exposes a string. Every family records its own instances, because the copies share none of these edges.
        CompilationInput core = CompilationFactory.Compile("Core", ("Core/Formatter.cs", """
                                                                                         namespace Drift
                                                                                         {
                                                                                             public class Formatter
                                                                                             {
                                                                                                 public Formatter(Cells.Cell seed) {}
                                                                                                 public Cells.Cell Measure()
                                                                                                 {
                                                                                                     try { return new Cells.Cell(); }
                                                                                                     catch (Cells.CellException) { throw new Cells.CellException(); }
                                                                                                 }
                                                                                             }
                                                                                         }
                                                                                         namespace Cells
                                                                                         {
                                                                                             public class Cell {}
                                                                                             public class CellException : System.Exception {}
                                                                                         }
                                                                                         """));
        CompilationInput satellite = CompilationFactory.Compile("Satellite", ("Satellite/Formatter.cs", """
                                                                                                        namespace Drift
                                                                                                        {
                                                                                                            public class Formatter { public string Name; }
                                                                                                        }
                                                                                                        """));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([core, satellite]);

        ReferenceEdge reference = model.Edge("Drift.Formatter", "Cells.Cell");
        ConstructorEdge construction = model.ConstructorEdge("Drift.Formatter", "Cells.Cell");
        InjectionEdge injection = model.InjectionEdge("Drift.Formatter", "Cells.Cell");
        CatchEdge caught = model.CatchEdge("Drift.Formatter", "Cells.CellException");
        ThrowEdge thrown = model.ThrowEdge("Drift.Formatter", "Cells.CellException");
        ExposureEdge exposure = model.ExposureEdge("Drift.Formatter", "Cells.Cell");

        (string, string)[] coreOnly = [("Core", "Core")];
        InstancesOf(reference.Evidence.Instances)
            .ShouldBe(coreOnly);
        InstancesOf(construction.Evidence.Instances)
            .ShouldBe(coreOnly);
        InstancesOf(injection.Evidence.Instances)
            .ShouldBe(coreOnly);
        InstancesOf(caught.Evidence.Instances)
            .ShouldBe(coreOnly);
        InstancesOf(thrown.Evidence.Instances)
            .ShouldBe(coreOnly);
        InstancesOf(exposure.Evidence.Instances)
            .ShouldBe(coreOnly);

        // The string is an external node, so the reach is the supplying assembly's name, and the maker is Satellite.
        ExposureEdge exposesString = model.ExposureEdge("Drift.Formatter", "System.String");
        InstancesOf(exposesString.Evidence.Instances)
            .ShouldBe([("Satellite", exposesString.Exposed.ProjectName)]);
    }

    [Fact]
    public void ExtractFromCompilations_OneFileInTwoProjects_RecordsEveryDeclarersOwnInstance()
    {
        // A linked file: every declarer's compilation makes the edge, each into its own copy of the target.
        CodebaseModel model = LinkedFileInTwoProjects();

        ReferenceEdge part = model.Edge("Shared.Widget", "Shared.WidgetPart");
        InstancesOf(part.Evidence.Instances)
            .ShouldBe([("Core", "Core"), ("Tool", "Tool")]);
    }

    [Fact]
    public void ExtractFromCompilations_ReferenceIntoASharedName_RecordsTheCopyItsCompilationBound()
    {
        // Neither referencing project declares Formatter, so each binds the copy of the declarer it references:
        // Client binds Core's, the first declarer's, and Reader binds Satellite's.
        CompilationInput core = CompilationFactory.Compile("Core", ("Core/Formatter.cs", FormatterSource));
        CompilationInput satellite = CompilationFactory.Compile("Satellite", ("Satellite/Formatter.cs", FormatterSource));
        CompilationInput client = CompilationFactory.CompileReferencing(
            "Client", core.Compilation, "Core", ("Client/View.cs", "namespace Client { public class View { public Drift.Formatter F; } }"));
        CompilationInput reader = CompilationFactory.CompileReferencing(
            "Reader", satellite.Compilation, "Satellite", ("Reader/Page.cs", "namespace Reader { public class Page { public Drift.Formatter F; } }"));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([client, core, reader, satellite]);

        ReferenceEdge fromClient = model.Edge("Client.View", "Drift.Formatter");
        ReferenceEdge fromReader = model.Edge("Reader.Page", "Drift.Formatter");
        InstancesOf(fromClient.Evidence.Instances)
            .ShouldBe([("Client", "Core")]);
        InstancesOf(fromReader.Evidence.Instances)
            .ShouldBe([("Reader", "Satellite")]);
    }

    [Fact]
    public void ExtractFromCompilations_SharedNameBoundFromAnAssemblyTwoProjectsProduce_ReachesTheFirstDeclarer()
    {
        // Both declarers compile to one assembly name, so the assembly Reader bound says nothing about whose copy
        // it is. An unknown concludes nothing and the reach stays with the first declarer.
        var core = new CompilationInput(
            CompilationFactory.CreateCompilation("Drift", [CompilationFactory.CoreLibrary], ("Core/Formatter.cs", FormatterSource)), "Core", []);
        var satellite = new CompilationInput(
            CompilationFactory.CreateCompilation("Drift", [CompilationFactory.CoreLibrary], ("Satellite/Formatter.cs", FormatterSource)), "Satellite", []);
        CompilationInput reader = CompilationFactory.CompileReferencing(
            "Reader", satellite.Compilation, "Satellite", ("Reader/Page.cs", "namespace Reader { public class Page { public Drift.Formatter F; } }"));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([core, reader, satellite]);

        ReferenceEdge fromReader = model.Edge("Reader.Page", "Drift.Formatter");
        InstancesOf(fromReader.Evidence.Instances)
            .ShouldBe([("Reader", "Core")]);
    }

    [Fact]
    public void ExtractFromCompilations_TwoFrameworksOfOneDeclarerMakingOneEdge_RecordOneInstance()
    {
        // Several frameworks of one project are one project: the edge both make is one instance, not two.
        const string copy = "namespace Drift { public class Formatter { public Cells.Cell C; } } namespace Cells { public class Cell {} }";
        CompilationInput older = CompilationFactory.Compile("Core", ("Core/Formatter.cs", copy)) with { TargetFramework = "net8.0" };
        CompilationInput newer = CompilationFactory.Compile("Core", ("Core/Formatter.cs", copy)) with { TargetFramework = "net10.0" };
        CompilationInput satellite = CompilationFactory.Compile("Satellite", ("Satellite/Formatter.cs", FormatterSource));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([older, newer, satellite]);

        ReferenceEdge reference = model.Edge("Drift.Formatter", "Cells.Cell");
        InstancesOf(reference.Evidence.Instances)
            .ShouldBe([("Core", "Core")]);
    }

    [Fact]
    public void ExtractFromCompilations_FrameworkThatLeavesOutItsProjectsCopy_RecordsTheCopyThatFrameworkBound()
    {
        // One project, two frameworks: the older declares Formatter itself, as a #if-guarded polyfill would, and the
        // newer leaves it out and binds Core's copy through a real reference. Each compilation reached a different
        // copy, so the one edge both make is two instances, and asking the project whether it declares the name
        // would have folded the newer framework's reference into the project's own copy.
        CodebaseModel model = FrameworkWithoutItsProjectsCopy();

        ReferenceEdge fromPage = model.Edge("App.Page", "Drift.Formatter");
        InstancesOf(fromPage.Evidence.Instances)
            .ShouldBe([("App", "App"), ("App", "Core")]);
    }

    [Fact]
    public void ExtractFromCompilations_EdgeBetweenNamesOneProjectEachDeclares_RecordsNoInstances()
    {
        // The degenerate case, beside a shared name so the merge is recording at all: an edge neither of whose
        // ends is shared keeps no record, and reads as the one instance its two nodes name.
        CodebaseModel model = OrdinaryEdgeBesideASharedName();

        ReferenceEdge ordinary = model.Edge("Core.User", "Core.Cell");
        ordinary.Evidence.Instances.ShouldBeNull();
    }

    [Fact]
    public void ExtractFromCompilations_DriftedCopies_GiveEachInstanceTheSitesItsOwnCopyMade()
    {
        // Two copies of one name in files of their own, both reaching a type a third project declares. The edge
        // unions three sites, and each instance carries the run its own compilation made — BY REFERENCE into
        // that union rather than as an equal copy of part of it, which is what lets a rule owning one instance
        // cite a subsequence of the very list a rule owning all of them cites whole.
        const string coreCopy = """
                                namespace Drift
                                {
                                    public class Formatter
                                    {
                                        public Cells.Cell Measured;
                                    }
                                }
                                """;
        const string satelliteCopy = """
                                     namespace Drift
                                     {
                                         public class Formatter
                                         {
                                             public string Name;
                                             public Cells.Cell Coloured;
                                             public Cells.Cell Fallback;
                                         }
                                     }
                                     """;
        CompilationInput cells = CompilationFactory.Compile(
            "Cells", ("Cells/Cell.cs", "namespace Cells { public class Cell {} }"));
        CompilationInput core = CompilationFactory.CompileReferencing(
            "Core", cells.Compilation, "Cells", ("Core/Formatter.cs", coreCopy));
        CompilationInput satellite = CompilationFactory.CompileReferencing(
            "Satellite", cells.Compilation, "Cells", ("Satellite/Formatter.cs", satelliteCopy));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([cells, core, satellite]);

        ReferenceEdge reference = model.Edge("Drift.Formatter", "Cells.Cell");
        IReadOnlyList<EdgeInstance> instances = reference.Evidence.Instances.ShouldNotBeNull();
        InstancesOf(reference.Evidence.Instances)
            .ShouldBe([("Core", "Cells"), ("Satellite", "Cells")]);
        Rendered(reference.Sites)
            .ShouldBe(["Core/Formatter.cs:5", "Satellite/Formatter.cs:6", "Satellite/Formatter.cs:7"]);
        Rendered(instances[0].Sites)
            .ShouldBe(["Core/Formatter.cs:5"]);
        Rendered(instances[1].Sites)
            .ShouldBe(["Satellite/Formatter.cs:6", "Satellite/Formatter.cs:7"]);

        ShouldBeTheEdgesOwnSiteObjects(instances[0].Sites, reference.Sites);
        ShouldBeTheEdgesOwnSiteObjects(instances[1].Sites, reference.Sites);
    }

    [Fact]
    public void ExtractFromCompilations_OneFileInTwoProjects_GiveEveryInstanceTheEdgesOwnSiteList()
    {
        // The other end of the same contract. Every declarer of a linked file makes the edge at the same
        // file:line, so each instance's share is the whole union — and is the union's own list object rather
        // than an equal copy, which is what makes two findings over one linked edge render byte for byte alike.
        CodebaseModel model = LinkedFileInTwoProjects();

        ReferenceEdge part = model.Edge("Shared.Widget", "Shared.WidgetPart");
        IReadOnlyList<EdgeInstance> instances = part.Evidence.Instances.ShouldNotBeNull();
        Rendered(part.Sites)
            .ShouldBe(["Shared/Widget.cs:1"]);
        instances[0].Sites.ShouldBeSameAs(part.Sites);
        instances[1].Sites.ShouldBeSameAs(part.Sites);
    }

    [Fact]
    public void ExtractFromCompilations_TwoFrameworksOfOneDeclarerMakingOneEdge_UnionTheirSitesUnderIt()
    {
        // Several frameworks of one project are one project, so the edge both make is one instance — and that
        // instance holds what BOTH compilations made, in (file, line) order. The two sources are one file under
        // a #if, each naming the cell on a line the other does not.
        const string older = """
                             namespace Drift
                             {
                                 public class Formatter
                                 {
                                     public Cells.Cell Legacy;
                                 }
                             }
                             namespace Cells { public class Cell {} }
                             """;
        const string newer = """
                             namespace Drift
                             {
                                 public class Formatter
                                 {
                                     public string Name;
                                     public Cells.Cell Modern;
                                 }
                             }
                             namespace Cells { public class Cell {} }
                             """;
        CompilationInput legacy = CompilationFactory.Compile("Core", ("Core/Formatter.cs", older))
            with
            {
                TargetFramework = "net8.0"
            };
        CompilationInput modern = CompilationFactory.Compile("Core", ("Core/Formatter.cs", newer))
            with
            {
                TargetFramework = "net10.0"
            };
        CompilationInput satellite = CompilationFactory.Compile(
            "Satellite", ("Satellite/Formatter.cs", FormatterSource));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([legacy, modern, satellite]);

        ReferenceEdge reference = model.Edge("Drift.Formatter", "Cells.Cell");
        IReadOnlyList<EdgeInstance> instances = reference.Evidence.Instances.ShouldNotBeNull();
        InstancesOf(reference.Evidence.Instances)
            .ShouldBe([("Core", "Core")]);
        Rendered(instances[0].Sites)
            .ShouldBe(["Core/Formatter.cs:5", "Core/Formatter.cs:6"]);
        instances[0].Sites.ShouldBeSameAs(reference.Sites);
    }

    [Fact]
    public void ExtractFromCompilations_FrameworkThatLeavesOutItsProjectsCopy_ShareTheSiteBothFrameworksMade()
    {
        // The same reference under two frameworks that bound DIFFERENT copies: two instances, and one site made
        // from one line of one file by both compilations. Each instance holds that site — the same object, not
        // a copy — because the merge unions sites and never partitions them, which is the invariant the
        // checker's by-reference filter over several instances' evidence rests on.
        CodebaseModel model = FrameworkWithoutItsProjectsCopy();

        ReferenceEdge fromPage = model.Edge("App.Page", "Drift.Formatter");
        IReadOnlyList<EdgeInstance> instances = fromPage.Evidence.Instances.ShouldNotBeNull();
        InstancesOf(fromPage.Evidence.Instances)
            .ShouldBe([("App", "App"), ("App", "Core")]);
        Rendered(fromPage.Sites)
            .ShouldBe(["App/Page.cs:1"]);
        instances[0].Sites[0].ShouldBeSameAs(fromPage.Sites[0]);
        instances[1].Sites[0].ShouldBeSameAs(fromPage.Sites[0]);
    }

    [Fact]
    public void EdgeInstancesOf_AnEdgeTheMergeKeptNoRecordFor_YieldsOneInstanceHoldingTheEdgesOwnSiteList()
    {
        // The unrecorded edge read back. Its one instance is the pair its two nodes name, and the sites it
        // answers with are the edge's own list — the same object — so a caller may ask any instance which sites
        // it made without first asking whether a record exists.
        CodebaseModel model = OrdinaryEdgeBesideASharedName();

        ReferenceEdge ordinary = model.Edge("Core.User", "Core.Cell");
        EdgeInstance only = EdgeInstances.Of(ordinary).ShouldHaveSingleItem();
        only.SourceProject.ShouldBe("Core");
        only.TargetProject.ShouldBe("Core");
        only.Sites.ShouldBeSameAs(ordinary.Sites);
    }

    [Fact]
    public void ExtractFromCompilations_DriftedCopiesCatchingOneType_NarrowEachInstanceOnItsOwnClauses()
    {
        // A catch edge's two narrowings are facts about the clause each copy spells, so neither is derivable
        // from the main record and both carry per-instance shares of their own, aligned to it on the same keys
        // in the same order. Core's two clauses are unfiltered and one of them holds the failure; Satellite's
        // single clause carries a `when`, so its share of both narrowings is empty rather than absent.
        const string coreCopy = """
                                namespace Drift
                                {
                                    public class Formatter
                                    {
                                        public void Rethrow()
                                        {
                                            try { }
                                            catch (Cells.CellException) { throw; }
                                        }

                                        public void Swallow()
                                        {
                                            try { }
                                            catch (Cells.CellException) { }
                                        }
                                    }
                                }
                                """;
        const string satelliteCopy = """
                                     namespace Drift
                                     {
                                         public class Formatter
                                         {
                                             public void Guarded(bool ready)
                                             {
                                                 try { }
                                                 catch (Cells.CellException) when (ready) { }
                                             }
                                         }
                                     }
                                     """;
        CompilationInput cells = CompilationFactory.Compile(
            "Cells", ("Cells/Cell.cs", "namespace Cells { public class CellException : System.Exception {} }"));
        CompilationInput core = CompilationFactory.CompileReferencing(
            "Core", cells.Compilation, "Cells", ("Core/Formatter.cs", coreCopy));
        CompilationInput satellite = CompilationFactory.CompileReferencing(
            "Satellite", cells.Compilation, "Cells", ("Satellite/Formatter.cs", satelliteCopy));

        CodebaseModel model = CodebaseExtractor.ExtractFromCompilations([cells, core, satellite]);

        CatchEdge caught = model.CatchEdge("Drift.Formatter", "Cells.CellException");
        (string, string)[] bothCopies = [("Core", "Cells"), ("Satellite", "Cells")];
        InstancesOf(caught.Evidence.Instances)
            .ShouldBe(bothCopies);
        InstancesOf(caught.UnfilteredEvidence.Instances)
            .ShouldBe(bothCopies);
        InstancesOf(caught.SwallowingEvidence.Instances)
            .ShouldBe(bothCopies);

        IReadOnlyList<EdgeInstance> unfiltered = caught.UnfilteredEvidence.Instances.ShouldNotBeNull();
        IReadOnlyList<EdgeInstance> swallowing = caught.SwallowingEvidence.Instances.ShouldNotBeNull();
        Rendered(caught.Sites)
            .ShouldBe(["Core/Formatter.cs:8", "Core/Formatter.cs:14", "Satellite/Formatter.cs:8"]);
        Rendered(unfiltered[0].Sites)
            .ShouldBe(["Core/Formatter.cs:8", "Core/Formatter.cs:14"]);
        Rendered(unfiltered[1].Sites)
            .ShouldBeEmpty();
        Rendered(swallowing[0].Sites)
            .ShouldBe(["Core/Formatter.cs:14"]);
        Rendered(swallowing[1].Sites)
            .ShouldBeEmpty();

        // Each narrowing's shares are subsequences of that narrowing's OWN list, never of the main one.
        ShouldBeTheEdgesOwnSiteObjects(unfiltered[0].Sites, caught.UnfilteredSites);
        ShouldBeTheEdgesOwnSiteObjects(swallowing[0].Sites, caught.SwallowingSites);
    }

    // One file compiled by two projects, each declaring the pair of types it holds — the scene two rows read,
    // one for the instances the merge records and one for the site list they share.
    private static CodebaseModel LinkedFileInTwoProjects()
    {
        CompilationInput core = CompilationFactory.Compile("Core", ("Shared/Widget.cs", LinkedWidgetSource));
        CompilationInput tool = CompilationFactory.Compile("Tool", ("Shared/Widget.cs", LinkedWidgetSource));

        return CodebaseExtractor.ExtractFromCompilations([core, tool]);
    }

    // One project under two frameworks, the older declaring Formatter itself as a #if-guarded polyfill would and
    // the newer leaving it out to bind Core's copy through a real reference — so the two frameworks of one
    // project bound different copies of one name, at one file:line.
    private static CodebaseModel FrameworkWithoutItsProjectsCopy()
    {
        const string user = "namespace App { public class Page { public Drift.Formatter F; } }";
        CompilationInput core = CompilationFactory.Compile("Core", ("Core/Formatter.cs", FormatterSource));
        CompilationInput older = CompilationFactory.Compile("App", ("App/Formatter.cs", FormatterSource), ("App/Page.cs", user))
            with
            {
                TargetFramework = "net8.0"
            };
        CompilationInput newer = CompilationFactory.CompileReferencing("App", core.Compilation, "Core", ("App/Page.cs", user))
            with
            {
                TargetFramework = "net10.0"
            };

        return CodebaseExtractor.ExtractFromCompilations([older, newer, core]);
    }

    // An edge neither of whose ends is shared, sitting beside a name two projects declare so that the merge is
    // recording instances at all.
    private static CodebaseModel OrdinaryEdgeBesideASharedName()
    {
        CompilationInput core = CompilationFactory.Compile("Core", ("Core.cs", """
                                                                               namespace Drift { public class Formatter {} }
                                                                               namespace Core { public class User { public Core.Cell C; } public class Cell {} }
                                                                               """));
        CompilationInput satellite = CompilationFactory.Compile("Satellite", ("Satellite.cs", FormatterSource));

        return CodebaseExtractor.ExtractFromCompilations([core, satellite]);
    }

    // A site list as file:line strings, the shape every per-instance site pin in this class reads.
    private static List<string> Rendered(IReadOnlyList<SourceLocation> sites)
    {
        return sites.Select(site => site.ToString())
            .ToList();
    }

    // Each of a share's sites against the element of the edge-level list it renders the same as. The invariant
    // is BY REFERENCE — a share is a subsequence of that list, not an equal copy of one — so asserting
    // containment by value would pass over a rebuilt list and say nothing.
    private static void ShouldBeTheEdgesOwnSiteObjects(
        IReadOnlyList<SourceLocation> share, IReadOnlyList<SourceLocation> all)
    {
        List<string> rendered = Rendered(all);
        foreach (SourceLocation site in share)
        {
            int at = rendered.IndexOf(site.ToString());
            at.ShouldBeGreaterThanOrEqualTo(0, $"{site} is not among [{string.Join(", ", rendered)}]");
            site.ShouldBeSameAs(all[at]);
        }
    }

    // One declarer's share as (file, line) pairs, the shape every site pin in this class reads.
    private static List<(string FilePath, int Line)> SitesOf(TypeNode node, string project)
    {
        return node.DeclarationSitesOf(project)
            .Select(site => (site.FilePath, site.Line))
            .ToList();
    }

    // A recorded instance list as (maker, reached) pairs, the shape every instance pin in this class reads.
    private static List<(string SourceProject, string TargetProject)> InstancesOf(IReadOnlyList<EdgeInstance>? instances)
    {
        return instances.ShouldNotBeNull()
            .Select(instance => (instance.SourceProject, instance.TargetProject))
            .ToList();
    }
}
