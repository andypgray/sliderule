using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Tests.Checking;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The one-project view: one compilation extracted as its own fragment plus a facts-only fragment per
///     sibling project it touched, so the merge and the checker answer a rule about the reference between
///     two projects from inside one of them.
/// </summary>
/// <remarks>
///     <para>
///         Every fact runs on both forms a reference to another project can take — the other compilation's
///         source symbols, and a metadata image of it — because the two disagree about a sibling type and
///         the view must not. What they may legitimately disagree about is compared nowhere here: the
///         interface list an image carries is the flattened set, a <c>record struct</c> stops reporting as
///         a record, and the attribute set loses the pseudo-custom entries and gains compiler-synthesized
///         ones. So the comparisons are of check reports and of the facts a verdict reads, never of whole
///         models.
///     </para>
///     <para>
///         The direction under test throughout is that this view may say <em>less</em> than a check over
///         the whole solution and never more. Each row about a type the view cannot vouch for is a row
///         about that.
///     </para>
/// </remarks>
public sealed class OneProjectExtractionTests
{
    // The sibling project, whose assembly is deliberately not named after it: a side fragment that
    // attributed its types to the assembly rather than the project would put them outside every
    // project-headed selection, silently.
    private const string SiblingProjectName = "Domain";

    private const string SiblingAssemblyName = "Domain.Core";

    private const string OwnProjectName = "Web";

    private const string DomainSource =
        """
        namespace Domain;

        public interface IAudited {}

        public abstract class Entity {}

        public sealed class TaggedAttribute : System.Attribute {}

        [Tagged]
        public class Order : Entity, IAudited {}
        """;

    private const string WebSource =
        """
        namespace Web;

        public class OrderController
        {
            public Domain.Order Show()
            {
                return new Domain.Order();
            }
        }
        """;

    // A third assembly that is nobody's sibling — the reference closure a sibling's own hierarchy reaches
    // past the two projects in question.
    private const string ContractsSource =
        """
        namespace Contracts;

        public class Base {}

        public interface IUnit {}

        public sealed class MarkAttribute : System.Attribute {}
        """;

    // One sibling per hierarchy slot that can reach the third assembly, so each slot is dropped on its own.
    private const string DomainOverContractsSource =
        """
        namespace Domain;

        public class FromBase : Contracts.Base {}

        public class Implements : Contracts.IUnit {}

        [Contracts.Mark]
        public class Marked {}
        """;

    private const string WebOverContractsSource =
        """
        namespace Web;

        public class Consumer
        {
            public Domain.FromBase FromBase;
            public Domain.Implements Implements;
            public Domain.Marked Marked;
        }
        """;

    // Two exceptions Web puts through every edge family. Only Boom's hierarchy leaves the reference
    // closure, so Safe is the same bed's control: whatever silences Boom must leave Safe alone.
    private const string DomainEveryFamilySource =
        """
        namespace Domain;

        public class Safe : System.Exception
        {
            public int Code;
        }

        public class Boom : System.Exception, Contracts.IUnit
        {
            public int Code;
        }
        """;

    // Reference, member use, construction, constructor injection, catch, throw, and a public signature
    // position — the seven families, spelled twice over so each holds an edge to both types.
    private const string WebEveryFamilySource =
        """
        namespace Web;

        public class Consumer
        {
            private readonly Domain.Safe _safe;
            private readonly Domain.Boom _boom;

            public Consumer(Domain.Safe safe, Domain.Boom boom)
            {
                _safe = safe;
                _boom = boom;
            }

            public Domain.Safe ExposedSafe()
            {
                return _safe;
            }

            public Domain.Boom ExposedBoom()
            {
                return _boom;
            }

            public int RunSafe()
            {
                try
                {
                    throw new Domain.Safe();
                }
                catch (Domain.Safe safe)
                {
                    return safe.Code;
                }
            }

            public int RunBoom()
            {
                try
                {
                    throw new Domain.Boom();
                }
                catch (Domain.Boom boom)
                {
                    return boom.Code;
                }
            }
        }
        """;

    // A sibling whose interfaces and attributes are declared out of ordinal order, one interface reached
    // only through its base, so both sorted slots have something to sort.
    private const string DomainUnsortedSource =
        """
        namespace Domain;

        public interface IZulu {}

        public interface IAlpha {}

        public interface IMike {}

        public sealed class ZebraAttribute : System.Attribute {}

        public sealed class AntAttribute : System.Attribute {}

        public class Carrier : IMike {}

        [Zebra]
        [Ant]
        public class Sorted : Carrier, IZulu, IAlpha {}
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingTypeTheCompilationReferences_IsSolutionDeclaredUnderItsProject(bool asImage)
    {
        // Arrange
        CodebaseModel model = FragmentMerger.Merge(OneProjectView(asImage));

        // Assert
        TypeNode order = model.Type("Domain.Order");
        order.IsExternal.ShouldBeFalse();
        order.ProjectName.ShouldBe(SiblingProjectName);
        order.Members.ShouldBeEmpty();
        order.DeclarationSites.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingTypesHierarchy_AnswersTheSameRulesAsTheWholeSolutionMerge(bool asImage)
    {
        // Arrange
        CodebaseModel oneProject = FragmentMerger.Merge(OneProjectView(asImage));
        CodebaseModel wholeSolution = FragmentMerger.Merge(WholeSolutionView(asImage));

        // Act
        CheckReport narrowed = Checker.Run(oneProject, BaselineIndex.Empty, NarrowedUniverse.OneProject, HierarchyRules);
        CheckReport whole = Checker.Run(wholeSolution, BaselineIndex.Empty, (NarrowedUniverse?)null, HierarchyRules);

        // Assert: the facts the three adjectives read, then the verdicts themselves. Without the first the
        // two reports would agree by both finding nothing.
        TypeNode order = oneProject.Type("Domain.Order");
        Definitions(order.BaseTypeChain)
            .ShouldContain("Domain.Entity");
        Definitions(order.AllInterfaces)
            .ShouldContain("Domain.IAudited");
        Definitions(order.AttributeConstructions)
            .ShouldContain("Domain.TaggedAttribute");
        Verdict(whole).ShouldNotBeEmpty();
        Verdict(narrowed).ShouldBe(Verdict(whole));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingsBaseTheCompilationNeverNames_IsExternalRatherThanDeclared(bool asImage)
    {
        // Arrange — closure is first-order: Web names Domain.Order and nothing else of Domain, so Order's
        // own base is a name with a home rather than a second sibling to state the facts of.
        CodebaseModel model = FragmentMerger.Merge(OneProjectView(asImage));

        // Assert
        model.Type("Domain.Order")
            .IsExternal.ShouldBeFalse();
        model.Type("Domain.Entity")
            .IsExternal.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_HierarchyNamesFromEveryDirection_AllHaveAHomeInTheMerge(bool asImage)
    {
        // Arrange — a base in the sibling's own assembly, an interface from a third one, a generic base and
        // a nested type. A hierarchy name with no home is not a wrong answer but a thrown
        // KeyNotFoundException out of the merge, so the assertion is that the merge happens at all.
        CSharpCompilation contracts = Compilation("Contracts", [CompilationFactory.CoreLibrary], ("Contracts.cs", ContractsSource));
        CSharpCompilation domain = Compilation(
            SiblingAssemblyName, [CompilationFactory.CoreLibrary, CompilationFactory.Reference(contracts, asImage)], ("Domain.cs",
                """
                namespace Domain;

                public class Repo<T> {}

                public class Entity {}

                public class OrderRepo : Repo<Entity>, Contracts.IUnit {}

                public class Outer { public class Inner {} }
                """));
        CompilationInput own = Input(
            [
                CompilationFactory.CoreLibrary, CompilationFactory.Reference(contracts, asImage),
                CompilationFactory.Reference(domain, asImage)
            ],
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Domain.OrderRepo Repo;
                    public Domain.Outer.Inner Inner;
                }
                """));

        // Act
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);
        CodebaseModel model = Should.NotThrow(() => FragmentMerger.Merge(fragments));

        // Assert
        model.Type("Domain.OrderRepo")
            .IsExternal.ShouldBeFalse();
        model.Type("Domain.Outer.Inner")
            .IsExternal.ShouldBeFalse();
        model.Types.Select(type => type.FullName)
            .ShouldContain("Contracts.IUnit");
        model.Types.Select(type => type.FullName)
            .ShouldContain("Domain.Repo<T>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_AFileLinkedIntoBothProjects_RaisesNoShadowAndNoConflationNote(bool asImage)
    {
        // Arrange — one source file compiled into both projects, and a sibling deriving from the type it
        // declares. Homing that base in the side fragment's externals would attribute the sibling's copy to
        // the sibling's assembly beside Web's own declaration, which the merge reads as one name meaning two
        // types and says so in a note.
        const string sharedSource = "namespace Shared { public class Widget {} }";
        CSharpCompilation domain = Compilation(
            SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Shared.cs", sharedSource), ("Domain.cs",
                """
                namespace Domain;

                public class Linked : Shared.Widget {}
                """));
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)],
            ("Shared.cs", sharedSource),
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Domain.Linked Linked;
                }
                """));

        // Act
        CodebaseModel model = FragmentMerger.Merge(FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct));

        // Assert: the bed first — both halves have to be there, or a merge of nothing would be quiet too.
        TypeNode widget = model.Type("Shared.Widget");
        widget.IsExternal.ShouldBeFalse();
        widget.ProjectName.ShouldBe(OwnProjectName);
        model.Type("Domain.Linked")
            .BaseType!.FullName()
            .ShouldBe("Shared.Widget");
        widget.AlsoDeclaredBy.ShouldBeEmpty();
        model.MergeNotes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingDeclaredInABanneredFile_IsNotGeneratedOnEitherForm(bool asImage)
    {
        // Arrange — the banner arm of the generated verdict needs a parsed tree, which a sibling held as a
        // compilation reference has and a sibling held as an image does not. Consulting it would make the
        // same project's types generated under one form and authored under the other, and would parse
        // another project's source to do it.
        CompilationInput own = GeneratedDomainConsumer(asImage);

        // Act
        CodebaseModel oneProject = FragmentMerger.Merge(FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct));
        CodebaseModel wholeSolution = FragmentMerger.Merge(
        [
            FragmentExtractor.Extract(own),
            FragmentExtractor.Extract(new CompilationInput(GeneratedDomainCompilation(), SiblingProjectName, []))
        ]);

        // Assert: the banner is a signal at all — the whole-solution merge reads it from the file — and is
        // not one from here, while the attribute both forms carry still is.
        wholeSolution.Type("Domain.Emitted")
            .IsGenerated.ShouldBeTrue();
        oneProject.Type("Domain.Emitted")
            .IsGenerated.ShouldBeFalse();
        oneProject.Type("Domain.Stamped")
            .IsGenerated.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Extract_AnotherProjectsTypeInABanneredFile_IsAnAuthoredExternalOnEitherForm(bool asImage)
    {
        // Arrange — the same bed read by the ordinary extraction, which mints Web's externals through the
        // same path the view above does. A type of another assembly is judged by its attribute there too, so
        // the form Web holds Domain in cannot move its verdict.
        CompilationInput own = GeneratedDomainConsumer(asImage);

        // Act
        CodebaseModel ownAlone = FragmentMerger.Merge([FragmentExtractor.Extract(own)]);

        // Assert
        TypeNode emitted = ownAlone.Type("Domain.Emitted");
        emitted.IsExternal.ShouldBeTrue("Web alone declares nothing of Domain");
        emitted.IsGenerated.ShouldBeFalse();
        ownAlone.Type("Domain.Stamped")
            .IsGenerated.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_AnErrorTypeTheCompilationCannotResolve_IsOnTheFarEndOfNoEdge(bool asImage)
    {
        // Arrange — a name nothing supplies, reached where an unresolvable name can still be reached: a
        // declared type's base. The reference walker already refuses to mint an edge to an unmappable kind,
        // so this holds twice over; what it pins is that the view never hands the checker such a target.
        CSharpCompilation domain = Compilation(SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Domain.cs", DomainSource));
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)],
            ("Web.cs",
                """
                namespace Web;

                public class Broken : Missing.Base
                {
                    public Domain.Order Order;
                }
                """));

        // Act
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);
        CheckReport report = Checked(fragments, DropRules);

        // Assert: the bed reaches the sibling, so the extraction did run; then the error name is judged by
        // nothing.
        FamiliesHolding(fragments[0], "Domain.Order")
            .ShouldNotBeEmpty();
        FamiliesHolding(fragments[0], "Missing.Base")
            .ShouldBeEmpty();
        Verdict(report)
            .ShouldNotContain(verdict => verdict.Contains("Missing.Base", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_AnAssemblyTwoProjectsBothList_DropsEveryEdgeIntoIt(bool asImage)
    {
        // Arrange — an assembly the map cannot attribute to one project. Which project's types a reference
        // into it reached is unanswerable from here, so the types are dropped rather than attributed to
        // either.
        CSharpCompilation shared = Compilation(
            "Shared.Impl", [CompilationFactory.CoreLibrary], ("Shared.cs",
                """
                namespace Shared;

                public class Tool {}
                """));
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(shared, asImage)],
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Shared.Tool Tool;
                }
                """));
        var ambiguous = new SiblingProjects(
            [("ProjectOne", ["Shared.Impl"]), ("ProjectTwo", ["Shared.Impl"])], OwnProjectName);
        var attributed = new SiblingProjects([("ProjectOne", ["Shared.Impl"])], OwnProjectName);

        // Act
        IReadOnlyList<CodebaseFragment> dropped = FragmentExtractor.ExtractOneProject(own, ambiguous, Ct);
        IReadOnlyList<CodebaseFragment> kept = FragmentExtractor.ExtractOneProject(own, attributed, Ct);

        // Assert: the same bed under a map that CAN attribute the assembly still reports, so the silence
        // above is the ambiguity and not a bed that references nothing.
        FamiliesHolding(kept[0], "Shared.Tool")
            .ShouldNotBeEmpty();
        FamiliesHolding(dropped[0], "Shared.Tool")
            .ShouldBeEmpty();
        Verdict(Checked(kept, BanTool))
            .ShouldContain(verdict => verdict.Contains("Shared.Tool", StringComparison.Ordinal));
        Verdict(Checked(dropped, BanTool))
            .ShouldNotContain(verdict => verdict.Contains("Shared.Tool", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Domain.FromBase", false)]
    [InlineData("Domain.FromBase", true)]
    [InlineData("Domain.Implements", false)]
    [InlineData("Domain.Implements", true)]
    [InlineData("Domain.Marked", false)]
    [InlineData("Domain.Marked", true)]
    public void ExtractOneProject_ASiblingWhoseHierarchyLeavesTheReferenceClosure_DropsEveryEdgeToIt(
        string sibling, bool asImage)
    {
        // Arrange — the sibling's base, interface and attribute class live in a third assembly this
        // compilation was not given, which is what a project turning transitive references off produces.
        // Both forms answer alike, because a compilation resolves a referenced assembly's OWN references
        // against its own reference list: the chain truncates and AllInterfaces thins here, while a check
        // over the whole solution reads the sibling from its own compilation and sees all of it. The type is
        // dropped rather than judged on half a hierarchy.
        IReadOnlyList<CodebaseFragment> fragments = OverContracts(
            DomainOverContractsSource, WebOverContractsSource, asImage, ownReferencesContracts: false);

        // Act
        CheckReport report = Checked(fragments, DropRules);

        // Assert
        FamiliesHolding(fragments[0], sibling)
            .ShouldBeEmpty();
        FragmentMerger.Merge(fragments)
            .Types.Where(type => type.FullName == sibling)
            .ShouldAllBe(type => type.IsExternal);
        Verdict(report)
            .ShouldNotContain(verdict => verdict.Contains(sibling, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Domain.FromBase", false)]
    [InlineData("Domain.FromBase", true)]
    [InlineData("Domain.Implements", false)]
    [InlineData("Domain.Implements", true)]
    [InlineData("Domain.Marked", false)]
    [InlineData("Domain.Marked", true)]
    public void ExtractOneProject_TheSameSiblingsWithTheirThirdAssemblyReferenced_AreKept(string sibling, bool asImage)
    {
        // The difference guard for the row above: the same bed with the third assembly in this
        // compilation's own reference list resolves the whole hierarchy and keeps every one of them, so the
        // silence above is the truncation rather than a bed that references nothing.
        IReadOnlyList<CodebaseFragment> fragments = OverContracts(
            DomainOverContractsSource, WebOverContractsSource, asImage, ownReferencesContracts: true);

        FamiliesHolding(fragments[0], sibling)
            .ShouldNotBeEmpty();
        FragmentMerger.Merge(fragments)
            .Type(sibling)
            .IsExternal.ShouldBeFalse();
    }

    [Fact]
    public void ExtractOneProject_NoSiblingMap_ProducesTheFragmentTheOrdinaryExtractionDoes()
    {
        // Arrange
        CompilationInput input = CompilationFactory.Compile("TestProject", FragmentQuery.EveryPassSource);

        // Act
        IReadOnlyList<CodebaseFragment> view = FragmentExtractor.ExtractOneProject(input, null, CancellationToken.None);

        // Assert
        view.Count.ShouldBe(1);
        view[0].Serialized()
            .ShouldContain("N.Widget");
        view[0].Serialized()
            .ShouldBe(FragmentExtractor.Extract(input)
                .Serialized());
    }

    [Fact]
    public void ExtractOneProject_AProjectThatRegistersServices_RecordsNoRegistration()
    {
        // Arrange — every rule that reads a registration is outside the set this view can decide, so the
        // whole-compilation walk that finds them is skipped.
        CompilationInput input = CompilationFactory.CompileWithDi(OwnProjectName, ("Reg.cs",
            """
            using Microsoft.Extensions.DependencyInjection;

            namespace Web;

            public interface IFoo {}

            public class Foo : IFoo {}

            public static class Reg
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IFoo, Foo>();
                }
            }
            """));

        // Act
        IReadOnlyList<CodebaseFragment> view = FragmentExtractor.ExtractOneProject(input, SiblingMap(), Ct);

        // Assert: the ordinary extraction of the same input does record it, so the emptiness is the skip.
        FragmentExtractor.Extract(input)
            .ServiceRegistrations.ShouldNotBeEmpty();
        view[0]
            .ServiceRegistrations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingWhoseAssemblyIsNotItsProjectName_ReferencesItByProjectName(bool asImage)
    {
        // What a compiler hands an analysis is assemblies, and what a project selection names is projects,
        // so the input's own reference list — which says nothing at all for a hand-built input — cannot
        // answer this.
        IReadOnlyList<CodebaseFragment> view = OneProjectView(asImage);

        view[0]
            .ProjectReferences.ShouldBe([SiblingProjectName]);
        view[1]
            .ProjectName.ShouldBe(SiblingProjectName);
        view[1]
            .AssemblyName.ShouldBe(SiblingAssemblyName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASideFragment_MakesAProjectNodeStatingOnlyWhatThisViewRead(bool asImage)
    {
        // A side fragment is a project of the merged model like any other, and every fact about it that
        // nothing here read is left unknown rather than guessed: no framework, because this view never saw
        // the sibling's project file, and no solution membership, because it never saw a solution file. Both
        // are the answer every project verb passes on, and the framework being unknown is also what keeps
        // the merge from noting a framework collapse it never observed.
        CodebaseModel model = FragmentMerger.Merge(OneProjectView(asImage));

        model.Projects.Select(project => project.Name)
            .ShouldBe([SiblingProjectName, OwnProjectName]);
        ProjectNode sibling = model.Projects.Single(project => project.Name == SiblingProjectName);
        sibling.TargetFrameworks.ShouldBeEmpty();
        sibling.SolutionMember.ShouldBeNull();
        sibling.ProjectReferences.ShouldBeEmpty();
        model.MergeNotes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_SeveralSiblingsAndSeveralAssemblies_OrdersThemOrdinallyBehindTheOwnFragment(bool asImage)
    {
        // Arrange — three side fragments whose discovery order is neither of the two sort keys. They are
        // found in ordinal order of the type name that first reached each, so Zeta arrives first and
        // Alpha's two arrive Mid.Two before Mid.One; ordered, the project key moves Zeta last and the
        // assembly key swaps Alpha's pair back. A merge reads the order as declaration precedence, so it is
        // a fact about the model rather than about presentation.
        CSharpCompilation zeta = Compilation(
            "Alpha.Core", [CompilationFactory.CoreLibrary], ("Zeta.cs", "namespace Aaa { public class One {} }"));
        CSharpCompilation second = Compilation(
            "Mid.Two", [CompilationFactory.CoreLibrary], ("Second.cs", "namespace Bbb { public class Two {} }"));
        CSharpCompilation first = Compilation(
            "Mid.One", [CompilationFactory.CoreLibrary], ("First.cs", "namespace Ccc { public class Three {} }"));
        CompilationInput own = Input(
            [
                CompilationFactory.CoreLibrary, CompilationFactory.Reference(zeta, asImage),
                CompilationFactory.Reference(second, asImage), CompilationFactory.Reference(first, asImage)
            ],
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Aaa.One One;
                    public Bbb.Two Two;
                    public Ccc.Three Three;
                }
                """));
        var map = new SiblingProjects([("Zeta", ["Alpha.Core"]), ("Alpha", ["Mid.Two", "Mid.One"])], OwnProjectName);

        // Act
        IReadOnlyList<CodebaseFragment> view = FragmentExtractor.ExtractOneProject(own, map, Ct);

        // Assert: each side fragment carries the type that put it there, so the order below is an order over
        // a bed that was actually found rather than over three empty fragments.
        view.Skip(1)
            .Select(fragment => fragment.DeclaredTypes.Single()
                .Facts.FullName)
            .ShouldBe(["Ccc.Three", "Bbb.Two", "Aaa.One"]);
        view.Select(fragment => (fragment.ProjectName, fragment.AssemblyName))
            .ShouldBe(
            [
                (OwnProjectName, OwnProjectName), ("Alpha", "Mid.One"), ("Alpha", "Mid.Two"), ("Zeta", "Alpha.Core")
            ]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_ASiblingsSortedHierarchySlots_MatchItsOwnFragmentNameForName(bool asImage)
    {
        // Arrange — the two slots a rule reads through DerivedFrom's siblings, Implementing and
        // AttributedWith. Both are sorted ordinal by constructed name so a persisted fragment is stable
        // however Roslyn happened to order the list, and both have to carry what the sibling's own
        // extraction carries: a facts-only sibling that sorted differently, or dropped the interface only
        // its base supplies, would feed a rule a different hierarchy from the whole-solution run.
        CSharpCompilation domain = Compilation(
            SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Domain.cs", DomainUnsortedSource));
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)],
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Domain.Sorted Sorted;
                }
                """));

        // Act
        IReadOnlyList<CodebaseFragment> view = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);
        FragmentType side = view[1].DeclaredType("Domain.Sorted");
        FragmentType ownFragment = FragmentExtractor.Extract(new CompilationInput(domain, SiblingProjectName, []))
            .DeclaredType("Domain.Sorted");

        // Assert: the ordinal order, spelled out so a slot that stopped sorting reds here rather than
        // wherever a rule later reads it; then the same slots against the sibling's own extraction.
        Constructed(side.AllInterfaces)
            .ShouldBe(["Domain.IAlpha", "Domain.IMike", "Domain.IZulu"]);
        Constructed(side.AttributeConstructions)
            .ShouldBe(["Domain.AntAttribute", "Domain.ZebraAttribute"]);
        Constructed(side.AllInterfaces)
            .ShouldBe(Constructed(ownFragment.AllInterfaces), "the sibling's own extraction of AllInterfaces");
        Definitions(side.AllInterfaces)
            .ShouldBe(Definitions(ownFragment.AllInterfaces), "the sibling's own extraction of AllInterfaces");
        Constructed(side.AttributeConstructions)
            .ShouldBe(
                Constructed(ownFragment.AttributeConstructions), "the sibling's own extraction of AttributeConstructions");
        Definitions(side.AttributeConstructions)
            .ShouldBe(
                Definitions(ownFragment.AttributeConstructions), "the sibling's own extraction of AttributeConstructions");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_AnUnvouchedTarget_IsDroppedFromAllSevenEdgeFamilies(bool asImage)
    {
        // Arrange — one type put through every family Web can reach it by, and a second, vouched type put
        // through the same seven. This is the direction that must never fail: a rule of either polarity
        // judging an edge whose far end this view read half a hierarchy of would report what the
        // whole-solution check does not.
        IReadOnlyList<CodebaseFragment> view = OverContracts(
            DomainEveryFamilySource, WebEveryFamilySource, asImage, ownReferencesContracts: false);

        // Assert: the vouched twin still fills all seven, so the silence about Boom is the drop rather than
        // a bed that minted nothing.
        FamiliesHolding(view[0], "Domain.Safe")
            .ShouldBe(FragmentQuery.EdgeFamilies, "the vouched twin is reached by every family");
        FamiliesHolding(view[0], "Domain.Boom")
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractOneProject_TheSameTargetOnceVouched_IsKeptInAllSevenEdgeFamilies(bool asImage)
    {
        // The control for the row above: the same source with the third assembly in this compilation's own
        // reference list resolves Boom's hierarchy, and every family then holds the edge it minted.
        IReadOnlyList<CodebaseFragment> view = OverContracts(
            DomainEveryFamilySource, WebEveryFamilySource, asImage, ownReferencesContracts: true);

        FamiliesHolding(view[0], "Domain.Boom")
            .ShouldBe(FragmentQuery.EdgeFamilies, "every family Web reaches this type by");
    }

    [Fact]
    public void ExtractOneProject_TokenAlreadyCancelled_ThrowsRatherThanReturningAView()
    {
        CompilationInput input = CompilationFactory.Compile(OwnProjectName, WebSource);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Should.Throw<OperationCanceledException>(() => FragmentExtractor.ExtractOneProject(input, SiblingMap(), cancelled.Token));
    }

    // ── the bed ───────────────────────────────────────────────────────────────────────────────────────────

    private static void HierarchyRules(Arch arch)
    {
        arch.Rule("hierarchy/derived")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustNotReference(arch.Types.DerivedFrom("Domain.Entity")))
            .Because("A controller must not reach an entity directly.");

        arch.Rule("hierarchy/implementing")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustNotReference(arch.Types.Implementing("Domain.IAudited")))
            .Because("Audited types are reached through the audit service.");

        arch.Rule("hierarchy/attributed")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustNotReference(arch.Types.AttributedWith("Domain.TaggedAttribute")))
            .Because("Tagged types are reached through the tag registry.");
    }

    // The two verbs a type left merely external over-reports under: the throw allow-list, which has no
    // external exemption, and a ban whose exemption is read off a hierarchy an external does not carry.
    private static void DropRules(Arch arch)
    {
        arch.Rule("drop/only-throw")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustOnlyThrow(arch.Types.Named("Web.Nothing")))
            .Because("Every thrown type is named here.");

        arch.Rule("drop/banned-unless-exempt")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustNotReference(arch.Types.InNamespace("Domain.*")
                    .Except(arch.Types.DerivedFrom("Contracts.Base"))))
            .Because("Web reaches Domain only through the types Contracts sanctions.");
    }

    private static void BanTool(Arch arch)
    {
        arch.Rule("drop/no-shared-tool")
            .Enforce(arch.Types.InNamespace("Web.*")
                .MustNotReference(arch.Types.InNamespace("Shared.*")))
            .Because("Web owns no tooling.");
    }

    // Web and Domain as this view sees them: the own fragment first, then the side fragment.
    private static IReadOnlyList<CodebaseFragment> OneProjectView(bool asImage)
    {
        return FragmentExtractor.ExtractOneProject(OwnInput(asImage), SiblingMap(), Ct);
    }

    // The same two projects as a whole-solution run extracts them — both compilations in hand, each its own
    // ordinary fragment.
    private static IReadOnlyList<CodebaseFragment> WholeSolutionView(bool asImage)
    {
        return
        [
            FragmentExtractor.Extract(OwnInput(asImage)),
            FragmentExtractor.Extract(new CompilationInput(DomainCompilation(), SiblingProjectName, []))
        ];
    }

    private static CompilationInput OwnInput(bool asImage)
    {
        return Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(DomainCompilation(), asImage)],
            ("Web.cs", WebSource));
    }

    private static CSharpCompilation DomainCompilation()
    {
        return Compilation(SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Domain.cs", DomainSource));
    }

    // Domain with one type generated by each signal: Emitted by its file's banner alone, Stamped by the
    // attribute alone.
    private static CSharpCompilation GeneratedDomainCompilation()
    {
        return Compilation(
            SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Emitted.cs",
                """
                // <auto-generated/>
                namespace Domain;

                public class Emitted {}
                """), ("Stamped.cs",
                """
                namespace Domain;

                [System.CodeDom.Compiler.GeneratedCode("tool", "1.0")]
                public class Stamped {}
                """));
    }

    // Web naming both, over that Domain in whichever form the row asks for.
    private static CompilationInput GeneratedDomainConsumer(bool asImage)
    {
        return Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(GeneratedDomainCompilation(), asImage)],
            ("Web.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Domain.Emitted Emitted;
                    public Domain.Stamped Stamped;
                }
                """));
    }

    // The three-assembly bed: Domain's hierarchy reaches Contracts, and Web references Domain either with
    // that third assembly beside it or without.
    private static IReadOnlyList<CodebaseFragment> OverContracts(
        string domainSource, string webSource, bool asImage, bool ownReferencesContracts)
    {
        CSharpCompilation contracts = Compilation("Contracts", [CompilationFactory.CoreLibrary], ("Contracts.cs", ContractsSource));
        CSharpCompilation domain = Compilation(
            SiblingAssemblyName,
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(contracts, asImage)],
            ("Domain.cs", domainSource));
        List<MetadataReference> references = [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)];
        if (ownReferencesContracts) references.Add(CompilationFactory.Reference(contracts, asImage));

        CompilationInput own = Input(references, ("Web.cs", webSource));

        return FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);
    }

    private static SiblingProjects SiblingMap()
    {
        return new SiblingProjects([(SiblingProjectName, [SiblingAssemblyName])], OwnProjectName);
    }

    // Which of the seven families hold an edge whose far end is this name, named rather than counted so a
    // red says which family kept it.
    private static IReadOnlyList<string> FamiliesHolding(CodebaseFragment fragment, string target)
    {
        var families = new List<string>();
        if (fragment.Edges.Any(edge => edge.TargetFullName == target)) families.Add("Edges");
        if (fragment.MemberEdges.Any(edge => edge.TargetContainingTypeFullName == target)) families.Add("MemberEdges");
        if (fragment.ConstructorEdges.Any(edge => edge.ConstructedFullName == target)) families.Add("ConstructorEdges");
        if (fragment.InjectionEdges.Any(edge => edge.InjectedFullName == target)) families.Add("InjectionEdges");
        if (fragment.CatchEdges.Any(edge => edge.CaughtFullName == target)) families.Add("CatchEdges");
        if (fragment.ThrowEdges.Any(edge => edge.ThrownFullName == target)) families.Add("ThrowEdges");
        if (fragment.ExposureEdges.Any(edge => edge.ExposedFullName == target)) families.Add("ExposureEdges");

        return families;
    }

    // The definition names one of a type's three construction-preserving hierarchy facts carries — what a
    // DerivedFrom, an Implementing or an AttributedWith anchor is matched against.
    private static IReadOnlyList<string> Definitions(IEnumerable<TypeConstruction> constructions)
    {
        return constructions.Select(construction => construction.Definition.FullName)
            .ToList();
    }

    // The fragment-side twin of the one above, and its constructed-name companion: a FragmentConstruction
    // carries both names as strings, because nothing has resolved them to nodes yet.
    private static IReadOnlyList<string> Definitions(IEnumerable<FragmentConstruction> constructions)
    {
        return constructions.Select(construction => construction.DefinitionFullName)
            .ToList();
    }

    private static IReadOnlyList<string> Constructed(IEnumerable<FragmentConstruction> constructions)
    {
        return constructions.Select(construction => construction.ConstructedName)
            .ToList();
    }

    private static CSharpCompilation Compilation(
        string assemblyName, IReadOnlyList<MetadataReference> references, params (string Path, string Source)[] files)
    {
        return CompilationFactory.CreateCompilation(assemblyName, references, files);
    }

    private static CompilationInput Input(IReadOnlyList<MetadataReference> references, params (string Path, string Source)[] files)
    {
        return new CompilationInput(CompilationFactory.CreateCompilation(OwnProjectName, references, files), OwnProjectName, []);
    }

    private static CheckReport Checked(IReadOnlyList<CodebaseFragment> fragments, Action<Arch> define)
    {
        return Checker.Run(FragmentMerger.Merge(fragments), BaselineIndex.Empty, NarrowedUniverse.OneProject, define);
    }

    // What a verdict is made of: the status each rule reached and the endpoints of everything it reported.
    // Comparing this rather than two models is what keeps the rows honest, because a source symbol and the
    // same type read from an image differ in facts no rule here reads.
    private static IReadOnlyList<string> Verdict(CheckReport report)
    {
        var lines = new List<string>();
        foreach (RuleResult result in report.Results)
        {
            lines.Add($"{result.Rule.Id}: {result.Status}");
            foreach (Violation violation in result.Violations)
                lines.Add($"  {violation.Kind} {violation.Source?.FullName} -> {violation.Target?.FullName}");
        }

        return lines;
    }
}
