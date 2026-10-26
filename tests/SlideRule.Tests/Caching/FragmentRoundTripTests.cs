using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using SlideRule.Codebase;
using SlideRule.Extraction;
using SlideRule.Extraction.Fragments;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Caching;

/// <summary>
///     Pins that persisting fragments cannot change the model: extracting a non-trivial multi-project
///     solution to <see cref="CodebaseFragment" />s, serializing them to JSON with the cache's own options,
///     deserializing, and merging yields a model whose every fact (via <see cref="ModelDump" />) equals a
///     direct merge of the in-memory fragments. This is the fidelity guarantee behind the persisted cache —
///     a hit replays these fragments through the same <see cref="FragmentMerger" /> the cold path uses.
/// </summary>
public sealed class FragmentRoundTripTests
{
    [Fact]
    public void SerializeDeserializeMerge_RichSolution_EqualsDirectMerge()
    {
        // Arrange — a solution shape that exercises most fragment DTO fields: kinds (class/interface/struct/
        // enum/delegate/record), modifiers (sealed/static/abstract), a base chain, direct + transitive +
        // constructed interfaces, attributes (with an external System.Attribute) on a type AND on a member, a
        // [GeneratedCode] type and the nested type that inherits the flag through the containing-type walk,
        // cross-project references, partials across files (declaration-site union), a multi-site edge,
        // externals, a multi-TFM project, and a carrier of every member mutability shape (const, static
        // readonly, writable field, get/set property, get-only property) beside the record's init-only one.
        IReadOnlyList<CodebaseFragment> fragments = ExtractRichSolution();

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);

        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the member-attribute fact is non-vacuous here: without a member that actually carries one,
        // both dumps would render an empty list and a dropped field would still round-trip equal.
        direct.Type("N.Handler")
            .Member("M:N.Handler.Handle(N.Msg)")
            .AttributeNames()
            .ShouldBe([("N.MarkAttribute", "N.MarkAttribute")]);
        fromCache.Type("N.Handler")
            .Member("M:N.Handler.Handle(N.Msg)")
            .AttributeNames()
            .ShouldBe([("N.MarkAttribute", "N.MarkAttribute")]);

        // The same non-vacuity guard for the four mutability facts: each is a bool whose absent value is
        // false, so a dropped field round-trips equal unless something in the solution reads true. One
        // assertion per fact, on both sides.
        direct.Member("N.Money", "P:N.Money.Amount")
            .HasInitOnlySetter.ShouldBeTrue();
        direct.Member("N.Budget", "F:N.Budget.Max")
            .IsConst.ShouldBeTrue();
        direct.Member("N.Budget", "F:N.Budget.Name")
            .IsReadOnly.ShouldBeTrue();
        direct.Member("N.Budget", "P:N.Budget.Mutable")
            .HasSetter.ShouldBeTrue();
        direct.Member("N.Budget", "P:N.Budget.Frozen")
            .HasSetter.ShouldBeFalse();
        fromCache.Member("N.Money", "P:N.Money.Amount")
            .HasInitOnlySetter.ShouldBeTrue();
        fromCache.Member("N.Budget", "F:N.Budget.Max")
            .IsConst.ShouldBeTrue();
        fromCache.Member("N.Budget", "F:N.Budget.Name")
            .IsReadOnly.ShouldBeTrue();
        fromCache.Member("N.Budget", "P:N.Budget.Mutable")
            .HasSetter.ShouldBeTrue();
        fromCache.Member("N.Budget", "P:N.Budget.Frozen")
            .HasSetter.ShouldBeFalse();

        // And for the two inheritance facts, which read false on every member that does not say the keyword.
        direct.Member("N.Dog", "M:N.Dog.Speak")
            .IsOverride.ShouldBeTrue();
        direct.Member("N.Puppy", "M:N.Puppy.Speak")
            .IsSealed.ShouldBeTrue();
        fromCache.Member("N.Dog", "M:N.Dog.Speak")
            .IsOverride.ShouldBeTrue();
        fromCache.Member("N.Puppy", "M:N.Puppy.Speak")
            .IsSealed.ShouldBeTrue();

        // Total-fact equality: the round-trip is invisible to the merged model.
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void Serialize_Sites_CarryNoSpan()
    {
        // A live extraction records the span of each reference beside its site, for the analyzer to put a
        // squiggle on. Nothing here can keep one: a stored site comes back through a line remap, and a
        // column cannot survive a digest that dropped the whitespace it counts. So the span sits outside the
        // site's public surface, the serializer never sees it, and the manifest carries no span.
        IReadOnlyList<CodebaseFragment> fragments = ExtractRichSolution();

        string json = fragments.Serialized();

        json.ShouldNotContain("\"Start\"", "a site's span is not persisted");
        json.ShouldNotContain("\"Length\"", "a site's span is not persisted");
    }

    [Fact]
    public void SerializeDeserializeMerge_MemberEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising every member-edge field and all four MemberKinds
        // (method, property, field, event), so a lost kind or dropped site would move the merged dump.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Api.cs", """
                       namespace N;
                       public class Api
                       {
                           public int Field;
                           public int Prop { get; set; }
                           public event System.Action Evt;
                           public void Do() {}
                       }
                       """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       namespace M;
                       public class User
                       {
                           public void Go(N.Api api)
                           {
                               api.Do();
                               int p = api.Prop;
                               int f = api.Field;
                               api.Evt += OnEvt;
                           }
                           private void OnEvt() {}
                       }
                       """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([lib, app], artifactFacts: null, Ct);

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = fragments.Serialized();
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the member kinds serialize as names, the edges are present, and the round-trip is invisible.
        json.ShouldContain("\"MemberKind\":\"Event\"");
        direct.MemberEdges.Select(e => e.Member.SymbolId)
            .ShouldBe(
                ["E:N.Api.Evt", "F:N.Api.Field", "M:N.Api.Do", "P:N.Api.Prop"]);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void SerializeDeserializeMerge_ConstructorEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source with explicit, target-typed, and constructed-generic creations, plus
        // a delegate creation as a must-not-mint control, so a lost edge or dropped site would move the dump.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Api.cs", """
                       namespace N;
                       public class Widget {}
                       public class Box<T> {}
                       public delegate void Notify();
                       """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       namespace M;
                       public class User
                       {
                           public object Explicit() => new N.Widget();
                           public N.Widget Implicit() { N.Widget w = new(); return w; }
                           public object Generic() => new N.Box<int>();
                           public N.Notify Del() => new N.Notify(H);
                           private static void H() {}
                       }
                       """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([lib, app], artifactFacts: null, Ct);

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the ctor edges are present (explicit+target-typed union to Widget, generic normalized to the
        // open definition, the delegate creation excluded), and the round-trip is invisible to the merged model.
        direct.ConstructorEdges.Select(e => (e.Source.FullName, e.Constructed.FullName))
            .ShouldBe(
                [("M.User", "N.Box<T>"), ("M.User", "N.Widget")]);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void SerializeDeserializeMerge_InjectionEdgesAndRegistrations_SurviveRoundTripByteStably()
    {
        // Arrange — a source exercising both DI fact families (GRAMMAR §4.7): a constructor-injection edge
        // and two registrations (a two-arg service/impl and a one-arg self), so a lost edge, dropped
        // registration, or mis-serialized lifetime would move the merged dump.
        CompilationInput app = CompilationFactory.CompileWithDi("App", ("Wire.cs", """
                                                                                   using Microsoft.Extensions.DependencyInjection;
                                                                                   namespace N;
                                                                                   public interface IFoo {}
                                                                                   public class Foo : IFoo {}
                                                                                   public class Svc { public Svc(IFoo foo) {} }
                                                                                   public static class Reg
                                                                                   {
                                                                                       public static void Configure(IServiceCollection services)
                                                                                       {
                                                                                           services.AddSingleton<IFoo, Foo>();
                                                                                           services.AddScoped<Svc>();
                                                                                       }
                                                                                   }
                                                                                   """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([app], artifactFacts: null, Ct);

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        string json = fragments.Serialized();
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — the lifetime enum serializes as a name, both fact families are present, and the round-trip
        // is invisible to the merged model.
        json.ShouldContain("\"Lifetime\":\"Singleton\"");
        direct.InjectionEdges.Select(e => (e.Source.FullName, e.Injected.FullName))
            .ShouldContain(("N.Svc", "N.IFoo"));
        direct.ServiceRegistrations
            .Any(r => r is { Lifetime: Lifetime.Singleton, ServiceFullName: "N.IFoo", ImplementationFullName: "N.Foo" })
            .ShouldBeTrue();
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void SerializeDeserializeMerge_CatchAndThrowEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising the catch and throw edges (GRAMMAR §4.8): a typed catch of a
        // declared exception, a bare catch (synthesized System.Exception), a throw of a declared exception, and
        // a throw of an external one — so a lost edge, dropped site, or mis-unified endpoint would move the dump.
        // The `when`-filtered clause keeps the unfiltered-site subset non-vacuous: without it every site would
        // be unfiltered and a subset dropped in serialization would still round-trip equal.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Errors.cs", """
                          namespace N;
                          public class DomainError : System.Exception {}
                          """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Use.cs", """
                       using System;
                       using N;
                       namespace M;
                       public class Handler
                       {
                           public void Do()
                           {
                               try { Work(); }
                               catch (DomainError e) when (e.Message.Length > 0) { }
                               catch (DomainError) { throw; }
                               catch { throw new DomainError(); }
                           }
                           public int Parse(string s) => int.TryParse(s, out int v) ? v : throw new FormatException();
                           private void Work() {}
                           public void Swallow()
                           {
                               try { Work(); }
                               catch (DomainError) { }
                           }
                       }
                       """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([lib, app], artifactFacts: null, Ct);

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — both families present (declared + external endpoints, bare catch → System.Exception), the
        // DomainError edge's two nested subsets are each proper subsets of the one before (the filtered clause at
        // line 9 is absent from the unfiltered set; the rethrowing clause at line 10 is unfiltered but not
        // swallowing; only the bare-block clause at line 18 swallows), the bare `catch` at line 11 counts as
        // unfiltered and its `throw new` keeps it out of the swallowing set, and the round-trip is invisible.
        direct.CatchEdges.Select(e => (e.Source.FullName, e.Caught.FullName))
            .ShouldBe(
                [("M.Handler", "N.DomainError"), ("M.Handler", "System.Exception")]);
        direct.ThrowEdges.Select(e => (e.Source.FullName, e.Thrown.FullName))
            .ShouldBe(
                [("M.Handler", "N.DomainError"), ("M.Handler", "System.FormatException")]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .Sites.Lines()
            .ShouldBe([9, 10, 18]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .UnfilteredSites.Lines()
            .ShouldBe([10, 18]);
        direct.CatchEdge("M.Handler", "N.DomainError")
            .SwallowingSites.Lines()
            .ShouldBe([18]);
        direct.CatchEdge("M.Handler", "System.Exception")
            .UnfilteredSites.Lines()
            .ShouldBe([11]);
        direct.CatchEdge("M.Handler", "System.Exception")
            .SwallowingSites.Lines()
            .ShouldBeEmpty();
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void SerializeDeserializeMerge_ExposureEdges_SurviveRoundTripByteStably()
    {
        // Arrange — a cross-project source exercising every exposure signature position (GRAMMAR §4.9): a
        // method's return + parameter types, a property/field/event type, plus a constructed-generic return that
        // decomposes to the open definition AND its argument (an external Task`1 endpoint riding along) — so a
        // lost edge, dropped site, or dropped decomposition endpoint would move the merged dump. Exposure edges
        // otherwise ride only ExtractRichSolution's omnibus fragment; this pins them on their own.
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Types.cs", """
                         namespace N;
                         public class Widget {}
                         public class Gadget {}
                         public class Cog {}
                         public class Sprocket {}
                         public delegate void Notify();
                         """));
        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Api.cs", """
                       namespace M;
                       public class Service
                       {
                           public N.Widget Make(N.Gadget g) => null;
                           public System.Threading.Tasks.Task<N.Widget> Load() => null;
                           public N.Cog Setting { get; set; }
                           public N.Sprocket Field;
                           public event N.Notify Ev;
                       }
                       """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([lib, app], artifactFacts: null, Ct);

        // Act
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);

        // Assert — every signature position surfaced (the constructed generic split to open definition + argument,
        // the external Task`1 endpoint kept whole), and the round-trip is invisible to the merged model.
        direct.ExposureEdges.Select(e => (e.Source.FullName, e.Exposed.FullName))
            .ShouldBe(
            [
                ("M.Service", "N.Cog"),
                ("M.Service", "N.Gadget"),
                ("M.Service", "N.Notify"),
                ("M.Service", "N.Sprocket"),
                ("M.Service", "N.Widget"),
                ("M.Service", "System.Threading.Tasks.Task<TResult>")
            ]);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void RoundTrip_MultiFrameworkFragments_PreserveTheirTargetFramework()
    {
        // Arrange — one project's two frameworks, both declaring Shared.Widget. The framework is the half of a
        // fragment's identity the project name does not supply, so it has to survive the cache.
        CompilationInput modern = CompilationFactory.SharedWidgetFramework("net10.0");
        CompilationInput legacy = CompilationFactory.SharedWidgetFramework("netstandard2.0");
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([modern, legacy], artifactFacts: null, Ct);

        // Act
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert — the field itself, and then the observable that depends on it. The merge's
        // multi-framework note is a pure function of the fragments' frameworks, so a dropped field would
        // replay as silence on a cache hit while the cold run spoke, which the dump comparison's notes section
        // sees; the premise keeps that comparison from passing over two empty lists.
        roundTripped.Select(fragment => fragment.TargetFramework)
            .ShouldBe(["net10.0", "netstandard2.0"]);
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);
        direct.MergeNotes.ShouldNotBeEmpty();
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void RoundTrip_AssemblyNames_SurviveOntoTheShadowedNameSplit()
    {
        // Arrange — a project declaring a name a referenced assembly also supplies. The assembly name is the
        // only fact that tells the merge those are two types rather than one, so a hit that dropped it would
        // replay the phantom edge into the declaring project while the cold run resolved to the assembly.
        CSharpCompilation vendor = CompilationFactory.CreateCompilation(
            "Vendor", [CompilationFactory.CoreLibrary], ("Vendor.cs", """
                                                                      namespace Vendor;
                                                                      public class Widget {}
                                                                      """));
        CompilationInput product = CompilationFactory.CompileAgainstPackages(
            "Product", [vendor], ("Service.cs", """
                                                namespace Product;
                                                public class Service { private Vendor.Widget widget; }
                                                """));
        CompilationInput tests = CompilationFactory.Compile("Product.Tests", ("Mocks.cs", """
                                                                                          namespace Vendor;
                                                                                          public class Widget {}
                                                                                          """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([product, tests], artifactFacts: null, Ct);

        // Act
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert — the field itself, then the two observables that depend on it, both read by the dump
        // comparison: the merge note, a pure function of the assembly names that a dropped field would replay
        // as silence on a hit while the cold run spoke, and the edge that would land on the declaring project
        // instead of the assembly.
        roundTripped.Select(fragment => fragment.AssemblyName)
            .ShouldBe(["Product", "Product.Tests"]);
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);
        direct.MergeNotes.ShouldNotBeEmpty();
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void RoundTrip_SolutionMembership_SurvivesAllThreeStatesOntoTheProjectNodes()
    {
        // Arrange — a declared member, a passenger, and a project nothing was read about. The cache is the
        // one place all three can be flattened into the same absent-or-default value, which is the failure
        // this pins: a hit replaying a passenger as a member would have `render` draw it.
        CompilationInput member = CompilationFactory.Compile("Member", ("Member.cs", """
                                                                                     namespace Member;
                                                                                     public class A {}
                                                                                     """)) with
        {
            SolutionMember = true
        };
        CompilationInput passenger = CompilationFactory.Compile("Passenger", ("Passenger.cs", """
                                                                                              namespace Passenger;
                                                                                              public class B {}
                                                                                              """)) with
        {
            SolutionMember = false
        };
        CompilationInput unread = CompilationFactory.Compile("Unread", ("Unread.cs", """
                                                                                     namespace Unread;
                                                                                     public class C {}
                                                                                     """));
        IReadOnlyList<CodebaseFragment> fragments = FragmentExtractor.ExtractAll([member, passenger, unread], artifactFacts: null, Ct);

        // Act
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert — the field, then the model it lands on, since the fragment is only a carrier.
        roundTripped.Select(fragment => fragment.SolutionMember)
            .ShouldBe([true, false, null]);
        FragmentMerger.Merge(roundTripped)
            .Projects.Select(project => (project.Name, project.SolutionMember))
            .ShouldBe([("Member", true), ("Passenger", false), ("Unread", null)]);
    }

    [Fact]
    public void RoundTrip_ArtifactFacts_SurviveOntoTheProjectNodesWithTheirSites()
    {
        // Arrange — one project's worth of every evaluated fact, with values no default could produce: two
        // frameworks, two packages of which one is private, an explicit packable-false and a lock policy
        // declared in a file the project does not name. Each site is distinct, so a hit that dropped one would
        // land somewhere visibly wrong rather than merely somewhere.
        CodebaseFragment fragment = FragmentExtractor.Extract(CompilationFactory.Compile("Widget", ("Widget.cs", """
                                                                                                                 namespace Widget;
                                                                                                                 public class W {}
                                                                                                                 """)), Ct) with
        {
            DeclaredTargetFrameworks = ["net10.0", "netstandard2.0"],
            TargetFrameworksSite = new FragmentSite("/repo/src/Widget/Widget.csproj", 3),
            PackageReferences =
            [
                new FragmentPackageReference(
                    "Newtonsoft.Json", new FragmentSite("/repo/src/Directory.Build.props", 11), IsPrivate: false),
                new FragmentPackageReference("Shouldly", new FragmentSite("/repo/src/Widget/Widget.csproj", 9), IsPrivate: true)
            ],
            IsPackable = false,
            IsPackableSite = new FragmentSite("/repo/src/Widget/Widget.csproj", 5),
            LocksPackages = true,
            LocksPackagesSite = new FragmentSite("/repo/src/Directory.Build.props", 18)
        };

        // Act
        IReadOnlyList<CodebaseFragment> fragments = [fragment];
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert — the model the fragment is only a carrier for.
        ProjectNode project = FragmentMerger.Merge(roundTripped)
            .Projects.ShouldHaveSingleItem();
        project.TargetFrameworks.ShouldBe(["net10.0", "netstandard2.0"]);
        project.TargetFrameworksSite.ShouldNotBeNull()
            .ToString()
            .ShouldBe("/repo/src/Widget/Widget.csproj:3");
        project.PackageReferences.Select(Describe)
            .ShouldBe([
                "Newtonsoft.Json @ /repo/src/Directory.Build.props:11 private=False",
                "Shouldly @ /repo/src/Widget/Widget.csproj:9 private=True"
            ]);
        project.IsPackable.ShouldBe(false);
        project.IsPackableSite.ShouldNotBeNull()
            .ToString()
            .ShouldBe("/repo/src/Widget/Widget.csproj:5");
        project.LocksPackages.ShouldBe(true);
        project.LocksPackagesSite.ShouldNotBeNull()
            .ToString()
            .ShouldBe("/repo/src/Directory.Build.props:18");
    }

    [Fact]
    public void RoundTrip_ArtifactFactsUnevaluated_ReplayAsAbsentRatherThanAsDefaults()
    {
        // Arrange — the hand-built input, which has no project file to evaluate. The cache is where "nobody
        // asked" and "the answer is no" can be flattened into one value, and a hit replaying an unevaluated
        // project as not-packable and not-locking would hand a rule a verdict nothing measured.
        CodebaseFragment fragment = FragmentExtractor.Extract(CompilationFactory.Compile("Bare", ("Bare.cs", """
                                                                                                             namespace Bare;
                                                                                                             public class B {}
                                                                                                             """)), Ct);

        // Act
        IReadOnlyList<CodebaseFragment> fragments = [fragment];
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert
        ProjectNode project = FragmentMerger.Merge(roundTripped)
            .Projects.ShouldHaveSingleItem();
        project.TargetFrameworks.ShouldBeEmpty();
        project.TargetFrameworksSite.ShouldBeNull();
        project.PackageReferences.ShouldBeEmpty();
        project.IsPackable.ShouldBeNull();
        project.IsPackableSite.ShouldBeNull();
        project.LocksPackages.ShouldBeNull();
        project.LocksPackagesSite.ShouldBeNull();
    }

    [Fact]
    public void Merge_ArtifactFactsAcrossOneProjectsFrameworks_FoldWhereNeitherCanInventACleanAnswer()
    {
        // Arrange — one project file, two frameworks, disagreeing on every fact a condition can move: one
        // packs, one does not; one locks, one does not; each declares a package the other does not.
        CodebaseFragment modern = WidgetFragment("net10.0", Declaration("Modern", 9, isPrivate: false)) with
        {
            IsPackable = true,
            IsPackableSite = new FragmentSite("/repo/Widget.csproj", 5),
            LocksPackages = true,
            LocksPackagesSite = new FragmentSite("/repo/Widget.csproj", 6)
        };
        CodebaseFragment legacy = modern with
        {
            TargetFramework = "netstandard2.0",
            PackageReferences = [Declaration("Legacy", 13, isPrivate: false)],
            IsPackable = false,
            IsPackableSite = new FragmentSite("/repo/Widget.csproj", 15),
            LocksPackages = false,
            LocksPackagesSite = new FragmentSite("/repo/Widget.csproj", 16)
        };

        // Act
        ProjectNode project = FragmentMerger.Merge([modern, legacy])
            .Projects.ShouldHaveSingleItem();

        // Assert — packable because one framework ships a package, unlocked because one framework's restore
        // writes no lock file, and both packages because either framework declaring one is the project
        // declaring it. Each site is the framework that decided the verdict, which is the line a violation
        // has to name.
        project.IsPackable.ShouldBe(true);
        project.IsPackableSite.ShouldNotBeNull()
            .Line.ShouldBe(5);
        project.LocksPackages.ShouldBe(false);
        project.LocksPackagesSite.ShouldNotBeNull()
            .Line.ShouldBe(16);
        project.PackageReferences.Select(package => package.Name)
            .ShouldBe(["Legacy", "Modern"]);
    }

    [Fact]
    public void Merge_PackagePrivateInOneFrameworkOnly_IsNotPrivateAtThatDeclaration()
    {
        // Arrange — one package, private where net10.0 declares it and shipped where netstandard2.0 does. The
        // private declaration sits on the earlier line, so a fold that kept the ordinally-first site whatever
        // the declarations said would cite a line that ships nothing.
        CodebaseFragment modern = WidgetFragment("net10.0", Declaration("Polyfill", 9, isPrivate: true));
        CodebaseFragment legacy = WidgetFragment("netstandard2.0", Declaration("Polyfill", 13, isPrivate: false));

        // Act
        ProjectNode project = FragmentMerger.Merge([modern, legacy])
            .Projects.ShouldHaveSingleItem();

        // Assert — one framework ships the package, so the project does, at the declaration that ships it.
        project.PackageReferences.Select(Describe)
            .ShouldBe(["Polyfill @ /repo/Widget.csproj:13 private=False"]);
    }

    [Fact]
    public void Merge_PackagePrivateInEveryFramework_IsPrivateAtItsOrdinallyFirstDeclaration()
    {
        // Arrange — private in both frameworks, and the framework that arrives first declares it on the later
        // line, so arrival order and site order disagree.
        CodebaseFragment modern = WidgetFragment("net10.0", Declaration("StyleCop.Analyzers", 13, isPrivate: true));
        CodebaseFragment legacy = WidgetFragment("netstandard2.0", Declaration("StyleCop.Analyzers", 9, isPrivate: true));

        // Act
        ProjectNode project = FragmentMerger.Merge([modern, legacy])
            .Projects.ShouldHaveSingleItem();

        // Assert
        project.PackageReferences.Select(Describe)
            .ShouldBe(["StyleCop.Analyzers @ /repo/Widget.csproj:9 private=True"]);
    }

    [Fact]
    public void Merge_OnePackageDeclaredTwiceInOneFragment_FoldsLikeFrameworksDo()
    {
        // Arrange — the same two declarations, once as one framework's duplicates and once split across two
        // frameworks. Restore keeps the first of two duplicates, which here is the private one; the model
        // answers as it does for frameworks instead, deliberately the stricter of the two.
        FragmentPackageReference privately = Declaration("Polyfill", 9, isPrivate: true);
        FragmentPackageReference shipped = Declaration("Polyfill", 13, isPrivate: false);
        CodebaseFragment duplicated = WidgetFragment("net10.0", privately, shipped);
        CodebaseFragment modern = WidgetFragment("net10.0", privately);
        CodebaseFragment legacy = WidgetFragment("netstandard2.0", shipped);

        // Act
        ProjectNode once = FragmentMerger.Merge([duplicated])
            .Projects.ShouldHaveSingleItem();
        ProjectNode split = FragmentMerger.Merge([modern, legacy])
            .Projects.ShouldHaveSingleItem();

        // Assert
        once.PackageReferences.Select(Describe)
            .ShouldBe(["Polyfill @ /repo/Widget.csproj:13 private=False"]);
        split.PackageReferences.Select(Describe)
            .ShouldBe(["Polyfill @ /repo/Widget.csproj:13 private=False"]);
    }

    [Fact]
    public void Merge_PackageDeclaredForOneFrameworkOnly_ListsThatFramework()
    {
        // Arrange
        IReadOnlyList<CodebaseFragment> fragments = PackageForOneFrameworkOnly();

        // Act
        ProjectNode project = FragmentMerger.Merge(fragments)
            .Projects.ShouldHaveSingleItem();

        // Assert — the frameworks union beside the name-keyed privacy fold, ordinally.
        project.PackageReferences.Select(DescribeFrameworks)
            .ShouldBe(["Shared: net10.0, netstandard2.0", "System.Memory: netstandard2.0"]);
    }

    [Fact]
    public void Merge_EvaluationWithNoFrameworkChosen_AttributesOnlyAProjectsOneFramework()
    {
        // Arrange — a single-targeted project is evaluated with no framework chosen, and its one declared
        // framework answers for it. A multi-targeted project evaluated that way, which is what a replay that
        // could not tell its compilations apart does, saw no framework's conditions, so it attributes nothing.
        CodebaseFragment single = WidgetFragment("net10.0", Declaration("Serilog", 9, isPrivate: false)) with
        {
            TargetFramework = null,
            DeclaredTargetFrameworks = ["net10.0"]
        };
        CodebaseFragment undiscriminated = single with { DeclaredTargetFrameworks = ["net10.0", "netstandard2.0"] };

        // Act
        ProjectNode attributed = FragmentMerger.Merge([single])
            .Projects.ShouldHaveSingleItem();
        ProjectNode unknown = FragmentMerger.Merge([undiscriminated])
            .Projects.ShouldHaveSingleItem();

        // Assert
        attributed.PackageReferences.Select(DescribeFrameworks)
            .ShouldBe(["Serilog: net10.0"]);
        unknown.PackageReferences.Select(DescribeFrameworks)
            .ShouldBe(["Serilog: "]);
    }

    [Fact]
    public void RoundTrip_PerFrameworkPackages_ReplayTheSameFrameworks()
    {
        // Arrange — the frameworks are computed at merge from facts every fragment already carries, so a cache
        // hit must replay them exactly as a fresh extraction would.
        IReadOnlyList<CodebaseFragment> fragments = PackageForOneFrameworkOnly();

        // Act
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);
        fromCache.ShouldModelTheSameAs(direct);
    }

    [Fact]
    public void Merge_PropertiesPerFramework_BecomeOneEvaluationEach()
    {
        // Arrange
        IReadOnlyList<CodebaseFragment> fragments = PropertyForOneFrameworkOnly();

        // Act
        ProjectNode project = FragmentMerger.Merge(fragments)
            .Projects.ShouldHaveSingleItem();

        // Assert
        project.Evaluations.Select(DescribeEvaluation)
            .ShouldBe(["net10.0 @ /repo/Widget.csproj:1: Nullable=enable", "netstandard2.0 @ /repo/Widget.csproj:1: "]);
        project.Properties.Select(property => $"{property.Name}@{property.TargetFramework} {property.Site}")
            .ShouldBe(["Nullable@net10.0 /repo/Widget.csproj:6"]);
    }

    [Fact]
    public void Merge_FragmentNothingEvaluated_HasNoEvaluation()
    {
        // No properties slot at all means no evaluation ran, which is a different fact from one that found
        // nothing set, and only the second can red a rule about a missing property.
        ProjectNode project = FragmentMerger.Merge([WidgetFragment("net10.0") with { Properties = null }])
            .Projects.ShouldHaveSingleItem();

        project.Evaluations.ShouldBeEmpty();
        project.Properties.ShouldBeEmpty();
    }

    [Fact]
    public void RoundTrip_Properties_ReplayAsTheyWereRecorded()
    {
        // Arrange
        IReadOnlyList<CodebaseFragment> fragments = PropertyForOneFrameworkOnly();

        // Act
        IReadOnlyList<CodebaseFragment> roundTripped = fragments.Persisted();

        // Assert
        CodebaseModel direct = FragmentMerger.Merge(fragments);
        CodebaseModel fromCache = FragmentMerger.Merge(roundTripped);
        fromCache.ShouldModelTheSameAs(direct);
    }

    // The shape a `$(TargetFramework)` condition produces: both frameworks declare the shared package, and only
    // netstandard2.0 declares the backport.
    private static IReadOnlyList<CodebaseFragment> PackageForOneFrameworkOnly()
    {
        CodebaseFragment modern = WidgetFragment("net10.0", Declaration("Shared", 9, isPrivate: false));
        CodebaseFragment legacy = WidgetFragment(
            "netstandard2.0", Declaration("Shared", 9, isPrivate: false), Declaration("System.Memory", 13, isPrivate: false));
        return [modern, legacy];
    }

    // A property a `$(TargetFramework)` condition sets for net10.0 alone. The netstandard2.0 evaluation set nothing,
    // and it is still an evaluation: that is what lets a rule say the property is unset there.
    private static IReadOnlyList<CodebaseFragment> PropertyForOneFrameworkOnly()
    {
        CodebaseFragment modern = WidgetFragment("net10.0") with { Properties = Evaluated(("Nullable", "enable", 6)) };
        CodebaseFragment legacy = WidgetFragment("netstandard2.0") with { Properties = Evaluated() };
        return [modern, legacy];
    }

    private static FragmentProperties Evaluated(params (string Name, string Value, int Line)[] properties)
    {
        return new FragmentProperties(
            new FragmentSite("/repo/Widget.csproj", 1),
            properties.Select(property => new FragmentProperty(
                    property.Name, property.Value, new FragmentSite("/repo/Widget.csproj", property.Line)))
                .ToList());
    }

    private static string DescribeEvaluation(ProjectEvaluation evaluation)
    {
        string properties = string.Join(", ", evaluation.Properties.Select(property => $"{property.Name}={property.Value}"));
        return $"{evaluation.TargetFramework} @ {evaluation.ProjectFile}: {properties}";
    }

    private static CodebaseFragment WidgetFragment(string framework, params FragmentPackageReference[] packages)
    {
        return FragmentExtractor.Extract(CompilationFactory.Compile("Widget", ("Widget.cs", """
                                                                                            namespace Widget;
                                                                                            public class W {}
                                                                                            """)), Ct) with
        {
            TargetFramework = framework,
            DeclaredTargetFrameworks = ["net10.0", "netstandard2.0"],
            PackageReferences = packages
        };
    }

    private static FragmentPackageReference Declaration(string name, int line, bool isPrivate)
    {
        return new FragmentPackageReference(name, new FragmentSite("/repo/Widget.csproj", line), isPrivate);
    }

    private static string Describe(PackageReference package)
    {
        return $"{package.Name} @ {package.Site} private={package.IsPrivate}";
    }

    private static string DescribeFrameworks(PackageReference package)
    {
        return $"{package.Name}: {string.Join(", ", package.Frameworks)}";
    }

    private static IReadOnlyList<CodebaseFragment> ExtractRichSolution()
    {
        CompilationInput lib = CompilationFactory.Compile("Lib",
            ("Contracts.cs", """
                             namespace N;
                             public interface IBase {}
                             public interface IHandler<T> {}
                             public interface IDerived<T> : IBase {}
                             public abstract class Animal { public virtual void Speak() {} }
                             public class Dog : Animal { public override void Speak() {} }
                             public sealed class Puppy : Dog { public sealed override void Speak() {} }
                             public static class Helpers {}
                             public record Money(decimal Amount);
                             public struct Point {}
                             public enum Color { Red }
                             public delegate void Notify();
                             public sealed class MarkAttribute : System.Attribute {}
                             public class Msg {}
                             [Mark] public class Handler : IHandler<Msg>, IDerived<Msg> { [Mark] public void Handle(Msg m) {} }
                             [System.CodeDom.Compiler.GeneratedCode("Tool", "1.0")] public class Emitted { public class Inner {} }
                             public class Budget
                             {
                                 public const int Max = 5;
                                 public static readonly string Name = "b";
                                 public static int Count;
                                 public int Mutable { get; set; }
                                 public int Frozen { get; }
                             }
                             """),
            ("SplitA.cs", """
                          namespace N;
                          public partial class Split { public System.Exception First; }
                          """),
            ("SplitB.cs", """
                          namespace N;
                          public partial class Split { public System.Exception Second; }
                          """));

        CompilationInput app = CompilationFactory.CompileReferencing("App", lib.Compilation, "Lib",
            ("Consumer.cs", """
                            namespace M;
                            public class Consumer
                            {
                                public N.Msg A;
                                public N.Msg B;
                                public N.Handler H;
                            }
                            """));

        // A multi-target-framework project: the same name twice with different references, so the merge's
        // BuildProjects union and cross-declarer declaration-site union are exercised on the round-trip too.
        (string Path, string Source) shared = ("Widget.cs", """
                                                            namespace P;
                                                            public class Widget {}
                                                            """);
        CompilationInput legacy = CompilationFactory.Compile("P", shared) with { ProjectReferences = ["Legacy"] };
        CompilationInput modern = CompilationFactory.Compile("P", shared) with { ProjectReferences = ["Modern"] };

        return FragmentExtractor.ExtractAll([lib, app, legacy, modern], artifactFacts: null, Ct);
    }
}
