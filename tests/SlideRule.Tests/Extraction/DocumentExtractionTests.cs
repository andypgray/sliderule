using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Extraction.Passes;
using Xunit;

namespace SlideRule.Tests.Extraction;

/// <summary>
///     The per-document view: one syntax tree extracted as the types it holds a declaring part of, each
///     carried whole, plus a facts-only fragment per project-and-assembly it touched — the own project's
///     included, because a type another document of the same compilation declares is not in this
///     document's own fragment.
/// </summary>
/// <remarks>
///     <para>
///         The parity argument is what most of these rows are about, because it is what a per-document
///         check rests on: unioned over a compilation's documents, the own fragments are the one-project
///         view's own fragment — the same declared types with the same sites, and the same seven edge
///         families with the same sites — and every side fragment is a subset of the one-project view's
///         twin under the same key. Anything a document could add that the project does not have would be
///         a verdict the project-wide check never reaches.
///     </para>
///     <para>
///         Every row that involves a sibling runs on both forms a reference to another project can take,
///         for the reason <see cref="OneProjectExtractionTests" /> gives: the two disagree about a sibling
///         type and this view must not.
///     </para>
/// </remarks>
public sealed class DocumentExtractionTests
{
    private const string SiblingProjectName = "Domain";

    private const string SiblingAssemblyName = "Domain.Core";

    private const string OwnProjectName = "Web";

    // Deliberately not the project name, so a fragment that attributed the own project's own side fragment
    // to its assembly rather than its project would be visible rather than a coincidence.
    private const string OwnAssemblyName = "Web.Site";

    private const string ControllersPath = "Controllers.cs";

    private const string AuditPath = "Controllers.Audit.cs";

    private const string ServicesPath = "Services.cs";

    private const string ProgramPath = "Program.cs";

    private const string GeneratedPath = "Generated.cs";

    private const string EmptyPath = "Empty.cs";

    // The extractor's own display format, restated here so the lookup's facts spell nested and generic
    // names the way the fragments do.
    private static readonly SymbolDisplayFormat FullNameFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private const string DomainSource =
        """
        namespace Domain;

        public interface IAudited {}

        public abstract class Entity {}

        public class Store
        {
            public int Count;
        }

        public class Ledger {}

        public sealed class TaggedAttribute : System.Attribute {}

        [Tagged]
        public class Order : Entity, IAudited {}

        public class Boom : System.Exception
        {
            public int Code;
        }
        """;

    // The first part of the partial type, carrying an injected sibling, a construction, a throw, a catch
    // and a member use — so the union row has something to say about every edge family.
    private const string ControllersSource =
        """
        namespace Web;

        public partial class OrderController
        {
            public Domain.Store Store;

            public Domain.Order Placed;

            public OrderController(Domain.Order placed)
            {
                Placed = placed;
            }

            public int Run()
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

    // The second part of the same type, holding a nested type of its own: the nested type is declared by
    // THIS document alone while its container is declared by both.
    private const string AuditSource =
        """
        namespace Web;

        public partial class OrderController
        {
            public Domain.Ledger Ledger;

            public class Nested
            {
                public Domain.Entity Base;
            }
        }
        """;

    // References an own type declared in another document, and a sibling type.
    private const string ServicesSource =
        """
        namespace Web;

        public class Billing
        {
            public OrderController Controller;

            public Domain.Order Placed;
        }
        """;

    // Top-level statements, which the console output kind makes Roslyn synthesize a Program entry point
    // for. The call is GC.KeepAlive rather than Console.WriteLine because the core library is the only
    // metadata reference here, and System.Console lives in another assembly.
    private const string ProgramSource =
        """
        var store = new Domain.Store();
        System.GC.KeepAlive(store);
        """;

    private const string GeneratedSource =
        """
        // <auto-generated/>
        namespace Web;

        public class Wired
        {
            public Domain.Store Store;
        }
        """;

    private const string EmptySource =
        """
        // nothing declared here
        using System;
        """;

    // One source file compiled into both projects, and a sibling deriving from the type it declares.
    private const string SharedSource = "namespace Shared { public class Widget {} }";

    // The same, with the shared type nested inside another, so the name a sibling derives from is reached
    // through a containing type rather than through a namespace alone.
    private const string NestedSharedSource = "namespace Shared { public class Outer { public class Inner {} } }";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_UnionOverEveryDocument_EqualsTheOneProjectOwnFragment(bool asImage)
    {
        // Arrange
        CompilationInput own = OwnProject(asImage);

        // Act
        CodebaseFragment whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct)[0];
        IReadOnlyList<CodebaseFragment> perDocument = PerDocument(own)
            .Values.Select(fragments => fragments[0])
            .ToList();

        // Assert — the declared types first, then each family on its own so a red names the family.
        SerializedTypes(perDocument)
            .ShouldBe(
                SerializedTypes([whole]),
                "the documents' own fragments must declare between them exactly what the project's own fragment declares");

        foreach (string family in FragmentQuery.EdgeFamilies)
            Family(perDocument, family)
                .ShouldBe(Family([whole], family), $"{family} must union over the documents to the project's own {family}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_EverySideFragment_IsASubsetOfItsWholeCompilationTwin(bool asImage)
    {
        // Arrange
        CompilationInput own = OwnProject(asImage);
        IReadOnlyList<CodebaseFragment> whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);
        CodebaseFragment wholeOwn = whole[0];

        // Act
        Dictionary<string, IReadOnlyList<CodebaseFragment>> perDocument = PerDocument(own);

        // Assert — the two bed guards are the lists below: a subset claim over nothing holds vacuously, and
        // the own-project arm, over own types another document declares, is the half under test.
        var siblingSide = new List<string>();
        var ownSide = new List<string>();

        foreach ((string path, IReadOnlyList<CodebaseFragment> fragments) in perDocument)
        foreach (CodebaseFragment side in fragments.Skip(1))
        foreach (FragmentType declared in side.DeclaredTypes)
        {
            string fullName = declared.Facts.FullName;
            var row = $"{path}: {side.ProjectName}/{side.AssemblyName} {fullName}";

            if (side.ProjectName == OwnProjectName)
            {
                ownSide.Add(row);

                side.AssemblyName.ShouldBe(OwnAssemblyName, $"{row} is the own project's own side fragment");
                FragmentType twin = wholeOwn.DeclaredType(fullName);
                declared.Facts.ShouldBe(twin.Facts, $"{row} must state the facts the project's own fragment states");
                Hierarchy(declared)
                    .ShouldBe(Hierarchy(twin), $"{row} must state the hierarchy the project's own fragment states");
                declared.DeclarationSites.ShouldBeEmpty($"{row} is a facts-only fragment");
                declared.DeclaredMembers.ShouldBeEmpty($"{row} is a facts-only fragment");
            }
            else
            {
                siblingSide.Add(row);

                CodebaseFragment twinSide = whole.Single(fragment => fragment.ProjectName == side.ProjectName && fragment.AssemblyName == side.AssemblyName);
                string twinBytes = twinSide.DeclaredType(fullName)
                    .Serialized();
                declared.Serialized()
                    .ShouldBe(twinBytes, $"{row} must match its whole-compilation twin");
            }
        }

        siblingSide.ShouldNotBeEmpty("no document reached a sibling type, so the subset claim would hold over nothing");
        ownSide.ShouldNotBeEmpty("no document reached an own type another document declares, which is the arm under test");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_APartialTypeAcrossTwoDocuments_CarriesTheWholeTypeFromEither(bool asImage)
    {
        // Arrange
        CompilationInput own = OwnProject(asImage);
        CodebaseFragment whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct)[0];
        Dictionary<string, IReadOnlyList<CodebaseFragment>> perDocument = PerDocument(own);

        // Act
        CodebaseFragment fromControllers = perDocument[ControllersPath][0];
        CodebaseFragment fromAudit = perDocument[AuditPath][0];

        // Assert — the declaration sites of both parts, from either part.
        IReadOnlyList<string> sites = Sites(fromControllers.DeclaredType("Web.OrderController").DeclarationSites);
        sites.Select(site => site.Split(':')[0])
            .ShouldBe([AuditPath, ControllersPath], "both parts' files must be sited, ordered by file then line");
        sites.ShouldBe(
            Sites(fromAudit.DeclaredType("Web.OrderController").DeclarationSites),
            "the two documents must site the partial type identically");
        sites.ShouldBe(
            Sites(whole.DeclaredType("Web.OrderController").DeclarationSites),
            "and identically to the whole-compilation path");

        // And the edges each part contributes, from either part. Store is named in one file, Ledger in the
        // other, so a document carrying only its own part would lose one of them.
        foreach (string target in new[] { "Domain.Store", "Domain.Ledger" })
        {
            IReadOnlyList<string> edge = EdgeSites(fromControllers, "Web.OrderController", target);
            edge.ShouldNotBeEmpty($"Web.OrderController must hold its edge to {target} from {ControllersPath}");
            edge.ShouldBe(EdgeSites(fromAudit, "Web.OrderController", target), $"and the same edge from {AuditPath}");
            edge.ShouldBe(EdgeSites(whole, "Web.OrderController", target), "and the same edge on the whole-compilation path");
        }
    }

    [Fact]
    public void ExtractDocument_ANestedTypeInAPartialContainersSecondFile_IsDeclaredByThatDocument()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);

        // Act
        Dictionary<string, IReadOnlyList<CodebaseFragment>> perDocument = PerDocument(own);
        CodebaseFragment fromAudit = perDocument[AuditPath][0];
        CodebaseFragment fromControllers = perDocument[ControllersPath][0];

        // Assert
        Names(fromAudit.DeclaredTypes)
            .ShouldContain("Web.OrderController.Nested", $"{AuditPath} declares the nested type");
        EdgeSites(fromAudit, "Web.OrderController.Nested", "Domain.Entity")
            .ShouldNotBeEmpty("and carries its edge with it");
        Names(fromControllers.DeclaredTypes)
            .ShouldNotContain(
                "Web.OrderController.Nested",
                $"{ControllersPath} declares the container's other part, not the nested type");
    }

    [Fact]
    public void ExtractDocument_TopLevelStatements_YieldTheProgramTypesEdgeFromItsDocument()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);
        CodebaseFragment whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct)[0];

        // Act
        CodebaseFragment fromProgram = PerDocument(own)[ProgramPath][0];

        // Assert — a namespace walk is what reaches a type declared by no type-declaration syntax, and the
        // lookup keys it under the compilation unit that holds the statements.
        Names(fromProgram.DeclaredTypes)
            .ShouldContain("Program", $"the synthesized entry-point type is declared by {ProgramPath}");
        IReadOnlyList<string> constructed = ConstructorEdgeSites(fromProgram, "Program", "Domain.Store");
        constructed.ShouldNotBeEmpty("the top-level statement's construction is attributed to Program");
        constructed.ShouldBe(
            ConstructorEdgeSites(whole, "Program", "Domain.Store"),
            "and is the same edge the whole-compilation path records");
        fromProgram.DeclaredType("Program")
            .DeclarationSites.ShouldBeEmpty("Program's declaring syntax is the compilation unit, which has no type identifier");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_AFileLinkedIntoBothProjects_RaisesNoShadowAndNoConflationNote(bool asImage)
    {
        // Arrange — Web.cs declares neither half: it names the sibling that derives from Shared.Widget and
        // names Shared.Widget itself, which the own project declares in its other document. Homing the
        // sibling's copy of that name under the sibling's assembly would have the merge read one name as two
        // types and say so.
        CompilationInput own = LinkedFileProject(asImage, SharedSource, "Shared.Widget");
        Dictionary<string, IReadOnlyList<CodebaseFragment>> perDocument = PerDocument(own);

        // Act
        CodebaseModel fromWeb = FragmentMerger.Merge(perDocument["Web.cs"]);
        CodebaseModel fromShared = FragmentMerger.Merge(perDocument["Shared.cs"]);

        // Assert — the shadow first, by count rather than by asking for the single node: a split name is
        // exactly two nodes of one name, and a red that says "two" names the failure the guard exists for.
        fromWeb.Types.Count(type => type.FullName == "Shared.Widget")
            .ShouldBe(1, "the linked type must reach the merge as one name meaning one type");
        fromWeb.Type("Domain.Linked")
            .IsExternal.ShouldBeFalse();
        fromWeb.Type("Domain.Linked")
            .BaseType!.FullName()
            .ShouldBe("Shared.Widget");
        fromWeb.Type("Shared.Widget")
            .AlsoDeclaredBy.ShouldBeEmpty();
        fromWeb.MergeNotes.ShouldBeEmpty();

        fromShared.Type("Shared.Widget")
            .IsExternal.ShouldBeFalse();
        fromShared.Type("Shared.Widget")
            .ProjectName.ShouldBe(OwnProjectName);
        fromShared.MergeNotes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_AFileLinkedIntoBothProjects_HomesANestedSharedTypeUnderTheOwnAssembly(bool asImage)
    {
        // Arrange — the same bed one nesting level down. Asking whether this compilation declares a name it
        // bound from elsewhere walks that name's containing chain, and only a nested name walks past the
        // first segment: the linked type is reached here as Outer's member rather than as a namespace's.
        CompilationInput own = LinkedFileProject(asImage, NestedSharedSource, "Shared.Outer.Inner");
        Dictionary<string, IReadOnlyList<CodebaseFragment>> perDocument = PerDocument(own);

        // Act
        CodebaseModel fromWeb = FragmentMerger.Merge(perDocument["Web.cs"]);

        // Assert — by count first, for the reason the row above gives: a split name is two nodes of one name.
        fromWeb.Types.Count(type => type.FullName == "Shared.Outer.Inner")
            .ShouldBe(1, "the linked nested type must reach the merge as one name meaning one type");
        fromWeb.Type("Domain.Linked")
            .IsExternal.ShouldBeFalse();
        fromWeb.Type("Domain.Linked")
            .BaseType!.FullName()
            .ShouldBe("Shared.Outer.Inner");
        fromWeb.Type("Shared.Outer.Inner")
            .AlsoDeclaredBy.ShouldBeEmpty();
        fromWeb.MergeNotes.ShouldBeEmpty();
    }

    [Fact]
    public void ExtractDocument_AnOwnTypeWithAnErrorBase_KeepsItsInboundEdge()
    {
        // Arrange — the truncated-closure screen exists for a metadata sibling, and an own type whose base
        // will not resolve is not that: the compilation holds all of it, and the document that declares it
        // records it whole.
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary],
            ("Broken.cs",
                """
                namespace Web;

                public class Broken : Missing.Base {}
                """),
            ("Consumer.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Broken B;
                }
                """));

        // Act
        IReadOnlyList<CodebaseFragment> fragments = PerDocument(own)["Consumer.cs"];
        CodebaseModel model = FragmentMerger.Merge(fragments);

        // Assert
        EdgeSites(fragments[0], "Web.Consumer", "Web.Broken")
            .ShouldNotBeEmpty("the consumer's edge to the broken type is its own document's fact");
        CodebaseFragment side = fragments.Skip(1)
            .Single(fragment => fragment.ProjectName == OwnProjectName);
        Names(side.DeclaredTypes)
            .ShouldContain("Web.Broken", "an own type is not dropped for an unresolved base");
        model.Type("Web.Broken")
            .IsExternal.ShouldBeFalse();
    }

    [Fact]
    public void ExtractDocument_AGeneratedDocument_IsExtractedAndItsTypeReadsGenerated()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);
        CodebaseFragment whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct)[0];

        // Act
        CodebaseFragment fromGenerated = PerDocument(own)[GeneratedPath][0];

        // Assert
        FragmentType wired = fromGenerated.DeclaredType("Web.Wired");
        wired.Facts.IsGenerated.ShouldBeTrue($"{GeneratedPath} opens with the generated banner");
        wired.Facts.IsGenerated.ShouldBe(
            whole.DeclaredType("Web.Wired")
                .Facts.IsGenerated,
            "and the whole-compilation path must reach the same verdict");
    }

    [Fact]
    public void ExtractDocument_AnOwnBaseReachedOnlyThroughAnotherOwnType_ReadsGeneratedAsTheProjectDoes()
    {
        // Arrange — Consumer.cs names Derived, whose base sits in a bannered file Consumer.cs never names, so
        // the base reaches this document's view only as a hierarchy name of the own project's side fragment.
        CompilationInput own = Input(
            [CompilationFactory.CoreLibrary],
            ("Emitted.cs",
                """
                // <auto-generated/>
                namespace Web;

                public class Emitted {}
                """),
            ("Derived.cs",
                """
                namespace Web;

                public class Derived : Emitted {}
                """),
            ("Consumer.cs",
                """
                namespace Web;

                public class Consumer
                {
                    public Derived Item;
                }
                """));

        // Act
        CodebaseModel fromConsumer = FragmentMerger.Merge(PerDocument(own)["Consumer.cs"]);
        CodebaseModel whole = FragmentMerger.Merge(FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct));

        // Assert: the bed first, then the verdict.
        whole.Type("Web.Emitted")
            .IsGenerated.ShouldBeTrue("the project's own extraction reads the banner");
        fromConsumer.Type("Web.Derived")
            .BaseType!.FullName()
            .ShouldBe("Web.Emitted");
        TypeNode emitted = fromConsumer.Type("Web.Emitted");
        emitted.IsExternal.ShouldBeTrue("Consumer.cs names Derived, not its base");
        emitted.IsGenerated.ShouldBeTrue("an own type reads its files however a document reaches it");
    }

    [Fact]
    public void ExtractDocument_ADocumentDeclaringNothing_YieldsOneEmptyOwnFragment()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);

        // Act
        IReadOnlyList<CodebaseFragment> fragments = PerDocument(own)[EmptyPath];

        // Assert — total, not skipped: a caller may hand over any tree of the compilation.
        fragments.Count.ShouldBe(1, "a document declaring nothing touches nobody's types either");
        CodebaseFragment only = fragments[0];
        only.DeclaredTypes.ShouldBeEmpty();
        only.Externals.ShouldBeEmpty();

        foreach (string family in FragmentQuery.EdgeFamilies)
            Family([only], family)
                .ShouldBeEmpty($"{family} must be empty for a document that declares nothing");
    }

    [Fact]
    public void ExtractDocument_IsDeterministic_AcrossRepeatsAndOrder()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);

        // Act
        IReadOnlyList<string> first = SerializedPerDocument(own, reversed: false, withModels: false);
        IReadOnlyList<string> again = SerializedPerDocument(own, reversed: false, withModels: false);
        IReadOnlyList<string> reversed = SerializedPerDocument(own, reversed: true, withModels: false);
        IReadOnlyList<string> seeded = SerializedPerDocument(own, reversed: false, withModels: true);

        // Assert
        again.ShouldBe(first, "a second pass over the same compilation must write the same bytes");
        reversed.ShouldBe(first, "and so must a pass that visits the documents in the opposite order");
        seeded.ShouldBe(first, "and so must one handed the compiler's own semantic model for each document");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractDocument_LeavesTheWholeCompilationPathUnchanged(bool asImage)
    {
        foreach (CompilationInput own in new[] { OwnProject(asImage), LinkedFileProject(asImage, SharedSource, "Shared.Widget") })
        {
            // Act
            IReadOnlyList<CodebaseFragment> whole = FragmentExtractor.ExtractOneProject(own, SiblingMap(), Ct);

            // Assert — the own-assembly home in the side fragments cannot fire here, because the guard that
            // leaves a name this compilation declares homeless takes it first.
            IReadOnlyList<string> declared = Names(whole[0].DeclaredTypes);
            IReadOnlyList<string> homed = whole
                .SelectMany(fragment => fragment.Externals.Select(external => external.Facts.FullName))
                .Where(declared.Contains)
                .ToList();
            homed.ShouldBeEmpty("a name the own compilation declares must stay out of every fragment's externals");

            // And the ordinary extraction is untouched by the lookup, which reads the compilation and writes
            // nothing back to it.
            string before = FragmentExtractor.Extract(own, Ct)
                .Serialized();
            DeclaringTrees.Of(own.Compilation);
            string after = FragmentExtractor.Extract(own, Ct)
                .Serialized();
            after.ShouldBe(before, "building the lookup must have no effect on the compilation it read");
        }
    }

    [Fact]
    public void DeclaringTrees_KeysAPartialTypeUnderEveryPartAndANestedTypeUnderItsOwnTree()
    {
        // Arrange
        CompilationInput own = OwnProject(asImage: false);

        // Act
        DeclaringTrees declaringTrees = DeclaringTrees.Of(own.Compilation);

        // Assert
        TypesIn(declaringTrees, own, ControllersPath)
            .ShouldBe(["Web.OrderController"]);
        TypesIn(declaringTrees, own, AuditPath)
            .ShouldBe(["Web.OrderController", "Web.OrderController.Nested"], "a container is keyed under every part's tree");
        TypesIn(declaringTrees, own, ProgramPath)
            .ShouldBe(["Program"], "the synthesized entry-point type is keyed under its compilation unit");
        TypesIn(declaringTrees, own, EmptyPath)
            .ShouldBeEmpty();
    }

    // ── the beds ──────────────────────────────────────────────────────────────────────────────────────────

    // The own project as a console application, which is the output kind that makes Roslyn synthesize the
    // top-level-statements Program, over the sibling in whichever form the row asks for.
    private static CompilationInput OwnProject(bool asImage)
    {
        CSharpCompilation domain = Compilation(SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Data.cs", DomainSource));

        CSharpCompilation web = CompilationFactory.CreateCompilation(
            OwnAssemblyName,
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)],
            OutputKind.ConsoleApplication,
            (ControllersPath, ControllersSource),
            (AuditPath, AuditSource),
            (ServicesPath, ServicesSource),
            (ProgramPath, ProgramSource),
            (GeneratedPath, GeneratedSource),
            (EmptyPath, EmptySource));

        return new CompilationInput(web, OwnProjectName, []);
    }

    private static CompilationInput LinkedFileProject(bool asImage, string sharedSource, string sharedType)
    {
        CSharpCompilation domain = Compilation(
            SiblingAssemblyName, [CompilationFactory.CoreLibrary], ("Shared.cs", sharedSource), ("Domain.cs",
                $$"""
                  namespace Domain;

                  public class Linked : {{sharedType}} {}
                  """));

        return Input(
            [CompilationFactory.CoreLibrary, CompilationFactory.Reference(domain, asImage)],
            ("Shared.cs", sharedSource),
            ("Web.cs",
                $$"""
                  namespace Web;

                  public class Consumer
                  {
                      public Domain.Linked Linked;
                      public {{sharedType}} Direct;
                  }
                  """));
    }

    private static SiblingProjects SiblingMap()
    {
        return new SiblingProjects(
            [(SiblingProjectName, [SiblingAssemblyName]), (OwnProjectName, [OwnAssemblyName])], OwnProjectName);
    }

    private static CSharpCompilation Compilation(
        string assemblyName, IReadOnlyList<MetadataReference> references, params (string Path, string Source)[] files)
    {
        return CompilationFactory.CreateCompilation(assemblyName, references, files);
    }

    private static CompilationInput Input(IReadOnlyList<MetadataReference> references, params (string Path, string Source)[] files)
    {
        return new CompilationInput(
            CompilationFactory.CreateCompilation(OwnAssemblyName, references, files), OwnProjectName, []);
    }

    // ── the act ───────────────────────────────────────────────────────────────────────────────────────────

    // Every document of one compilation extracted on its own, keyed by file path. The lookup is built once
    // for the compilation, as the analyzer builds it.
    private static Dictionary<string, IReadOnlyList<CodebaseFragment>> PerDocument(CompilationInput input)
    {
        DeclaringTrees declaringTrees = DeclaringTrees.Of(input.Compilation);
        var byPath = new Dictionary<string, IReadOnlyList<CodebaseFragment>>(StringComparer.Ordinal);

        foreach (SyntaxTree tree in input.Compilation.SyntaxTrees)
            byPath[tree.FilePath] = FragmentExtractor.ExtractDocument(
                input, tree, semanticModel: null, declaringTrees, SiblingMap(), Ct);

        return byPath;
    }

    // The same, as one sorted block of "path then bytes" rows: sorted so a pass that visited the documents
    // in the opposite order is comparable to one that did not.
    private static IReadOnlyList<string> SerializedPerDocument(CompilationInput input, bool reversed, bool withModels)
    {
        DeclaringTrees declaringTrees = DeclaringTrees.Of(input.Compilation);
        List<SyntaxTree> trees = input.Compilation.SyntaxTrees.ToList();
        if (reversed) trees.Reverse();

        var rows = new List<string>();
        foreach (SyntaxTree tree in trees)
        {
            SemanticModel? model = withModels ? input.Compilation.GetSemanticModel(tree) : null;
            IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractDocument(
                input, tree, model, declaringTrees, SiblingMap(), Ct);
            rows.Add(tree.FilePath + "\n" + fragments.Serialized());
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    // ── the projections ───────────────────────────────────────────────────────────────────────────────────

    // Declared types unioned by what they SAY rather than by their name, so two documents that extracted one
    // partial type differently arrive as two rows and the red shows both.
    private static IReadOnlyList<string> SerializedTypes(IEnumerable<CodebaseFragment> fragments)
    {
        var types = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CodebaseFragment fragment in fragments)
        foreach (FragmentType type in fragment.DeclaredTypes)
            types.Add(type.Serialized());

        return types.ToList();
    }

    // One edge family of a set of fragments, as sorted "endpoints @ file:line" rows. The row is the grain
    // the union is taken at — one per (key, site) — because a merge of two documents' edges unions their
    // sites under one key, so a row missing from one side names both the edge and the site that differ.
    private static IReadOnlyList<string> Family(IEnumerable<CodebaseFragment> fragments, string family)
    {
        var rows = new SortedSet<string>(StringComparer.Ordinal);
        foreach (CodebaseFragment fragment in fragments)
        foreach ((string key, FragmentSite site) in FamilyRows(fragment, family))
            rows.Add($"{key} @ {site.File}:{site.Line}");

        return rows.ToList();
    }

    private static IEnumerable<(string Key, FragmentSite Site)> FamilyRows(CodebaseFragment fragment, string family)
    {
        return family switch
        {
            "Edges" => Rows(fragment.Edges, e => $"{e.SourceFullName} -> {e.TargetFullName}", e => e.Sites),
            "MemberEdges" => Rows(
                fragment.MemberEdges,
                e => $"{e.SourceFullName} -> {e.TargetContainingTypeFullName}.{e.MemberName} ({e.MemberKind} {e.MemberSymbolId})",
                e => e.Sites),
            "ConstructorEdges" => Rows(fragment.ConstructorEdges, e => $"{e.SourceFullName} -> new {e.ConstructedFullName}", e => e.Sites),
            "InjectionEdges" => Rows(fragment.InjectionEdges, e => $"{e.SourceFullName} -> injects {e.InjectedFullName}", e => e.Sites),
            "CatchEdges" => Rows(fragment.CatchEdges, e => $"{e.SourceFullName} -> catches {e.CaughtFullName}", e => e.Sites)
                .Concat(Rows(fragment.CatchEdges, e => $"{e.SourceFullName} -> catches {e.CaughtFullName} (unfiltered)", e => e.UnfilteredSites))
                .Concat(Rows(fragment.CatchEdges, e => $"{e.SourceFullName} -> catches {e.CaughtFullName} (swallowing)", e => e.SwallowingSites)),
            "ThrowEdges" => Rows(fragment.ThrowEdges, e => $"{e.SourceFullName} -> throws {e.ThrownFullName}", e => e.Sites),
            "ExposureEdges" => Rows(fragment.ExposureEdges, e => $"{e.SourceFullName} -> exposes {e.ExposedFullName}", e => e.Sites),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, "not one of the seven edge families")
        };
    }

    private static IEnumerable<(string Key, FragmentSite Site)> Rows<TEdge>(
        IEnumerable<TEdge> edges, Func<TEdge, string> keyOf, Func<TEdge, IReadOnlyList<FragmentSite>> sitesOf)
    {
        return edges.SelectMany(edge => sitesOf(edge)
            .Select(site => (keyOf(edge), site)));
    }

    // The hierarchy slots one type states, as one comparable block: a side fragment's type and an own
    // fragment's differ in the slots a side deliberately leaves empty, which are compared on their own.
    private static IReadOnlyList<string> Hierarchy(FragmentType type)
    {
        return
        [
            $"base {type.BaseTypeFullName}",
            .. type.Interfaces.Select(name => $"implements {name}"),
            .. type.Attributes.Select(name => $"attributed {name}"),
            .. type.AllInterfaces.Select(c => $"allInterfaces {c.DefinitionFullName} as {c.ConstructedName}"),
            .. type.BaseTypeChain.Select(c => $"chain {c.DefinitionFullName} as {c.ConstructedName}"),
            .. type.AttributeConstructions.Select(c => $"attributeConstruction {c.DefinitionFullName} as {c.ConstructedName}")
        ];
    }

    private static IReadOnlyList<string> Sites(IEnumerable<FragmentSite> sites)
    {
        return sites.Select(site => $"{site.File}:{site.Line}")
            .ToList();
    }

    private static IReadOnlyList<string> EdgeSites(CodebaseFragment fragment, string source, string target)
    {
        return fragment.Edges
            .Where(edge => edge.SourceFullName == source && edge.TargetFullName == target)
            .SelectMany(edge => Sites(edge.Sites))
            .ToList();
    }

    private static IReadOnlyList<string> ConstructorEdgeSites(CodebaseFragment fragment, string source, string constructed)
    {
        return fragment.ConstructorEdges
            .Where(edge => edge.SourceFullName == source && edge.ConstructedFullName == constructed)
            .SelectMany(edge => Sites(edge.Sites))
            .ToList();
    }

    private static IReadOnlyList<string> Names(IEnumerable<FragmentType> types)
    {
        return types.Select(type => type.Facts.FullName)
            .ToList();
    }

    private static IReadOnlyList<string> TypesIn(DeclaringTrees declaringTrees, CompilationInput input, string path)
    {
        SyntaxTree tree = input.Compilation.SyntaxTrees.Single(candidate => candidate.FilePath == path);

        return declaringTrees.In(tree)
            .Select(type => type.ToDisplayString(FullNameFormat))
            .ToList();
    }
}
