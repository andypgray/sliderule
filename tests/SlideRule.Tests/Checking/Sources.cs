using SlideRule.Baselines;
using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.Tests.Extraction;

namespace SlideRule.Tests.Checking;

/// <summary>
///     Shared source strings for the fast-path checker tests, most beside the one
///     <see cref="CodebaseModel" /> extracted from it.
/// </summary>
/// <remarks>
///     Two jobs, and the second is why the catch scenes live here. A scene many rows check is compiled
///     once rather than once per row — extraction is the expensive half of a fast-path test. And a scene
///     several <em>classes</em> check is held once rather than copied into each, because those copies are
///     the claim: sibling verbs asserting different verdicts over the same input only mean that while the
///     input is literally the same, and a copy edited in one class desynchronises the others silently.
/// </remarks>
internal static class Sources
{
    /// <summary>
    ///     A layered app: <c>App.Domain</c> types reference <c>App.Web</c> (and one BCL type), plus a
    ///     same-layer edge (Service → Model) and two extra violators (Apple, Zebra → Controller) for
    ///     the ordering pin.
    /// </summary>
    public const string Layered = """
                                  namespace App.Web { public class Controller {} public class Helper {} }
                                  namespace App.Domain
                                  {
                                      using System.Text;
                                      public class Service
                                      {
                                          public App.Web.Controller C;
                                          public App.Web.Helper H;
                                          public App.Domain.Model M;
                                          public StringBuilder Log;
                                      }
                                      public class Model {}
                                      public class Apple { public App.Web.Controller C; }
                                      public class Zebra { public App.Web.Controller C; }
                                  }
                                  """;

    /// <summary>
    ///     A quarantined legacy scope with a facade: <c>App.Client.User</c> references both the facade
    ///     (<c>IFacade</c>, sanctioned) and an interior type (<c>Internal</c>, not sanctioned).
    /// </summary>
    public const string Containment = """
                                      namespace App.Legacy { public interface IFacade {} public class Impl {} public class Internal {} }
                                      namespace App.Client
                                      {
                                          public class User
                                          {
                                              public App.Legacy.Internal Direct;
                                              public App.Legacy.IFacade Via;
                                          }
                                      }
                                      """;

    /// <summary>
    ///     A quarantined legacy scope whose sanctioned surface is named without loading it: a region
    ///     (<c>App.Legacy.Contracts.*</c>) and a consumer that lives outside the scope entirely
    ///     (<c>Sanctioned</c>). <c>Gateway</c> reaches the interior as a facade must; <c>Sanctioned</c> and
    ///     <c>Other</c> reach it identically from outside and differ only in their names; <c>Caller</c>
    ///     reaches the facade region.
    /// </summary>
    public const string ContainmentRegion = """
                                            namespace App.Legacy { public class Internal {} }
                                            namespace App.Legacy.Contracts { public class Gateway { public App.Legacy.Internal Inner; } }
                                            namespace App.Client
                                            {
                                                public class Sanctioned { public App.Legacy.Internal Direct; }
                                                public class Other { public App.Legacy.Internal Direct; }
                                                public class Caller { public App.Legacy.Contracts.Gateway Via; }
                                            }
                                            """;

    /// <summary>
    ///     The hierarchy fixture: the reflectable <c>Targets</c> types (mirrored from
    ///     <see cref="Targets" />) plus subjects exercising every hierarchy verb — one implementer,
    ///     one deriver, one attributed type, two <c>IHandler</c> constructions, and their negatives.
    /// </summary>
    public const string Hierarchy = """
                                    using System;
                                    namespace SlideRule.Tests.Checking.Targets;
                                    public interface IThing {}
                                    public interface IHandler<T> {}
                                    public class ThingBase {}
                                    public sealed class MarkAttribute : Attribute {}
                                    public class Order {}
                                    public class Widget : IThing {}
                                    public class Gizmo {}
                                    public class SubType : ThingBase {}
                                    public class FreeType {}
                                    public class OrderHandler : IHandler<Order> {}
                                    public class TextHandler : IHandler<string> {}
                                    [Mark] public class Tagged {}
                                    public class Plain {}
                                    """;

    /// <summary>
    ///     Transitivity / type-argument-substitution / declared-only-attribute fixture for the negative
    ///     hierarchy verbs (GRAMMAR §5.2–§5.3): a transitive interface implementer (via a base class), a
    ///     substitution handler (a class extending a generic base that implements <c>IHandler&lt;T&gt;</c>),
    ///     and an attributed base with an un-attributed derived type. Anchors are the reflectable
    ///     <see cref="Targets" /> types, re-declared here in lockstep (a separate compilation) — the same
    ///     discipline as <see cref="Hierarchy" />.
    /// </summary>
    public const string HierarchyTransitive = """
                                              using System;
                                              namespace SlideRule.Tests.Checking.Targets;
                                              public interface IThing {}
                                              public interface IHandler<T> {}
                                              public class Order {}
                                              public sealed class MarkAttribute : Attribute {}
                                              public class Widget : IThing {}
                                              public class WidgetChild : Widget {}
                                              public class HandlerBase<T> : IHandler<T> {}
                                              public class SubstHandler : HandlerBase<Order> {}
                                              [Mark] public class AttrBase {}
                                              public class AttrDerived : AttrBase {}
                                              """;

    /// <summary>
    ///     The external-anchor fixture (GRAMMAR §5.2): subjects whose base chain leaves the compilation —
    ///     one deriving from a BCL type directly, one reaching it through an intermediate <em>external</em>
    ///     base (<c>ArgumentException : SystemException : Exception</c>) — plus a non-deriver. The shallow
    ///     hierarchy external types carry is a fact about the <em>subject</em> position: a declared type's
    ///     base chain is walked through metadata, so an external <em>anchor</em> is matchable in both anchor
    ///     forms. Its own compilation because the <see cref="Hierarchy" /> pins assert exact selected sets.
    /// </summary>
    public const string ExternalBaseHierarchy = """
                                                namespace SlideRule.Tests.Checking.Targets;
                                                public class DirectDeriver : System.Exception {}
                                                public class IndirectDeriver : System.ArgumentException {}
                                                public class Unrelated {}
                                                """;

    /// <summary>
    ///     The generic-attribute fixture for string attribute anchors (GRAMMAR §5.2): one generic attribute
    ///     applied at two constructions, plus a non-generic one and a bare type. A definition-name anchor
    ///     must reach both constructions while a constructed spelling reaches neither. Its own compilation
    ///     because the <see cref="Hierarchy" /> pins assert exact selected sets — a new attributed type
    ///     there would move them.
    /// </summary>
    public const string GenericAttributes = """
                                            using System;
                                            namespace SlideRule.Tests.Checking.Targets;
                                            public sealed class MarkAttribute<T> : Attribute {}
                                            public sealed class PlainAttribute : Attribute {}
                                            [Mark<int>] public class TaggedInt {}
                                            [Mark<string>] public class TaggedText {}
                                            [Plain] public class TaggedPlain {}
                                            public class Untagged {}
                                            """;

    /// <summary>
    ///     The exact-FQN matching scene the three catch verbs share: <c>Broad</c> catches
    ///     <c>System.Exception</c> while <c>Narrow</c> catches <c>System.IO.IOException</c>, so a ban on
    ///     <c>typeof(Exception)</c> reds the first and never the second — the narrow catch is the good state
    ///     each verb rewards, and Broad's presence proves the ban is live rather than vacuously empty.
    /// </summary>
    public const string CatchBroadVsNarrow = """
                                             namespace App
                                             {
                                                 public class Broad
                                                 {
                                                     public void Run() { try { } catch (System.Exception) { } }
                                                 }
                                                 public class Narrow
                                                 {
                                                     public void Run() { try { } catch (System.IO.IOException) { } }
                                                 }
                                             }
                                             """;

    /// <summary>
    ///     The hierarchy-adjective operand scene the three catch verbs share: <c>Worker</c> catches its own
    ///     <c>N.AppError</c> — solution-declared and derived from <c>Exception</c>, so an
    ///     <c>arch.Types.DerivedFrom</c> operand matches it — and an external
    ///     <c>System.InvalidOperationException</c>, which such an operand never matches, external types
    ///     carrying a shallow hierarchy.
    /// </summary>
    public const string CatchDerivedFrom = """
                                           namespace N
                                           {
                                               public class AppError : System.Exception {}
                                               public class Worker
                                               {
                                                   public void Run()
                                                   {
                                                       try { } catch (N.AppError) { }
                                                       try { } catch (System.InvalidOperationException) { }
                                                   }
                                               }
                                           }
                                           """;

    /// <summary>
    ///     The type-pair ratchet scene the three catch verbs share: one <c>Handler</c> catching both
    ///     <c>Errors.AErr</c> and <c>Errors.BErr</c>, so a baseline blessing the first edge leaves the
    ///     second a distinct identity and red (GRAMMAR §4.3).
    /// </summary>
    public const string CatchRatchet = """
                                       namespace Errors { public class AErr : System.Exception {} public class BErr : System.Exception {} }
                                       namespace App
                                       {
                                           public class Handler
                                           {
                                               public void Run()
                                               {
                                                   try { } catch (Errors.AErr) { }
                                                   try { } catch (Errors.BErr) { }
                                               }
                                           }
                                       }
                                       """;

    /// <summary>
    ///     A controller opening the data layer directly — one forbidden edge
    ///     (<c>OldController -&gt; App.Data.Db</c>).
    /// </summary>
    public const string OneController = """
                                        namespace App.Web { public class OldController { public App.Data.Db Load() => new App.Data.Db(); } }
                                        namespace App.Data { public class Db {} }
                                        """;

    /// <summary>
    ///     The same forbidden edge reached from two distinct lines. Sites are deduped per
    ///     <c>file:line</c>, so this is two sites under one identity — the shape the measure counts.
    /// </summary>
    public const string TwoSiteController = """
                                            namespace App.Web
                                            {
                                                public class OldController
                                                {
                                                    public App.Data.Db A() => new App.Data.Db();
                                                    public App.Data.Db B() => new App.Data.Db();
                                                }
                                            }
                                            namespace App.Data { public class Db {} }
                                            """;

    /// <summary>
    ///     A web layer that reaches nothing, beside a data layer that exists: the live half of
    ///     <see cref="PartlyInertDataAccess" />'s target has types to match and no edge to find.
    /// </summary>
    public const string CleanWebBesideData = """
                                             namespace App.Web { public class HomeController {} }
                                             namespace App.Data { public class Db {} }
                                             """;

    /// <summary>
    ///     The Migrate rule the controller scenes are checked against: Web controllers must not
    ///     reference the data layer. It omits <c>.Baseline</c>, so its conventional path is
    ///     <c>arch/baselines/data/x.json</c> (GRAMMAR §4.4).
    /// </summary>
    public static void NoDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "Controllers open the data layer directly (legacy Active Record style).",
                arch.Namespace("App.Web.*").WithSuffix("Controller").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("Repository pattern for testability.");
    }

    /// <summary>
    ///     A hermetic quarantine over <see cref="Containment" />: with no sanctioned surface every inbound
    ///     reference is red, the facade's included, so the desugared containment rule fails without a facade
    ///     stub having to be named.
    /// </summary>
    public static void HermeticContainment(Arch arch)
    {
        arch.Scope("legacy/quarantined")
            .Quarantine(arch.Namespace("App.Legacy.*"))
            .Dragons("Internal is structural.")
            .Because("Replacement scheduled.");
    }

    /// <summary>
    ///     The data-access ban with a second target pattern that names nothing: <c>App.Data.*</c> is live and
    ///     <c>App.Ghost.*</c> has gone dead, so the rule measures only part of what it names.
    /// </summary>
    public static void PartlyInertDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "old",
                arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*"), arch.Namespace("App.Ghost.*")))
            .Because("b");
    }

    /// <summary>
    ///     The control for <see cref="PartlyInertDataAccess" />: the same rule without its dead pattern, so it is
    ///     measured in full.
    /// </summary>
    public static void LiveDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate("old", arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("b");
    }

    /// <summary>
    ///     The stricter twin of <see cref="PartlyInertDataAccess" />: an allow-list whose second pattern names
    ///     nothing, so the rule is stricter than written while its unmatched entries are still tested.
    /// </summary>
    public static void StricterDataAccess(Arch arch)
    {
        arch.Rule("data/x")
            .Migrate(
                "old",
                arch.Namespace("App.Web.*").MustOnlyReference(arch.Namespace("App.Data.*"), arch.Namespace("App.Ghost.*")))
            .Because("b");
    }

    /// <summary>The one extracted model of <see cref="Layered" />.</summary>
    public static readonly CodebaseModel LayeredModel = CompilationFactory.Extract(Layered);

    /// <summary>The solution directory <see cref="FolderModel" />'s files sit under.</summary>
    public static readonly SolutionRoot FolderRoot = new("/repo");

    /// <summary>
    ///     One namespace laid out in folders, the way a codebase whose folders are its only structure is: widgets
    ///     two deep, a console with an Internal folder its writer reaches into, a partial type with one part
    ///     vendored, and a file outside the solution directory. Every type is in <c>App</c>, so only a path can
    ///     tell them apart.
    /// </summary>
    public static readonly CodebaseModel FolderModel = CompilationFactory.Extract("App",
        ("/repo/src/Widgets/Button.cs", "namespace App; public class Button {}"),
        ("/repo/src/Widgets/Deep/Panel.cs", "namespace App; public class Panel {}"),
        ("/repo/src/Console/Internal/Ansi.cs", "namespace App; public class Ansi {}"),
        ("/repo/src/Console/Writer.cs", "namespace App; public class Writer { public Ansi Codes; public System.Text.StringBuilder Buffer; }"),
        ("/repo/src/Split/Split.cs", "namespace App; public partial class Split {}"),
        ("/repo/vendor/Split.Vendored.cs", "namespace App; public partial class Split { public Button Face; }"),
        ("/elsewhere/Shared.cs", "namespace App; public class Shared {}"));

    /// <summary>The one extracted model of <see cref="Containment" />.</summary>
    public static readonly CodebaseModel ContainmentModel = CompilationFactory.Extract(Containment);

    /// <summary>The one extracted model of <see cref="ContainmentRegion" />.</summary>
    public static readonly CodebaseModel ContainmentRegionModel = CompilationFactory.Extract(ContainmentRegion);

    /// <summary>The one extracted model of <see cref="Hierarchy" />.</summary>
    public static readonly CodebaseModel HierarchyModel = CompilationFactory.Extract(Hierarchy);

    /// <summary>The one extracted model of <see cref="CatchBroadVsNarrow" />.</summary>
    public static readonly CodebaseModel CatchBroadVsNarrowModel = CompilationFactory.Extract(CatchBroadVsNarrow);

    /// <summary>The one extracted model of <see cref="CatchDerivedFrom" />.</summary>
    public static readonly CodebaseModel CatchDerivedFromModel = CompilationFactory.Extract(CatchDerivedFrom);

    /// <summary>The one extracted model of <see cref="CatchRatchet" />.</summary>
    public static readonly CodebaseModel CatchRatchetModel = CompilationFactory.Extract(CatchRatchet);

    /// <summary>The one extracted model of <see cref="OneController" />.</summary>
    public static readonly CodebaseModel OneControllerModel = CompilationFactory.Extract(OneController);

    /// <summary>The one extracted model of <see cref="TwoSiteController" />.</summary>
    public static readonly CodebaseModel TwoSiteControllerModel = CompilationFactory.Extract(TwoSiteController);

    /// <summary>The one extracted model of <see cref="CleanWebBesideData" />.</summary>
    public static readonly CodebaseModel CleanWebBesideDataModel = CompilationFactory.Extract(CleanWebBesideData);

    /// <summary>Two sections no rule names, in the order a scan found them.</summary>
    public static readonly BaselineOrphanScan TwoOrphans = new(
        [
            new BaselineOrphan("data/old-x", "arch/baselines/data/old-x.json", 1),
            new BaselineOrphan("legacy/old", "arch/shared.json", 3)
        ],
        []);

    /// <summary>The first of <see cref="TwoOrphans" /> alone.</summary>
    public static readonly BaselineOrphanScan OneOrphan = new([new BaselineOrphan("data/old-x", "arch/baselines/data/old-x.json", 1)], []);
}
