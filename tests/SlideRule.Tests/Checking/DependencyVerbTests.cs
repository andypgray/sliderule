using System.Text;
using Shouldly;
using SlideRule.Checking;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The four dependency verbs over the fast path (GRAMMAR §4.1, §4.3, §5.3): forbidden-set and
///     allow-set, outbound and inbound. Pins the reference universe (external exemption), implicit
///     self-allowance and what "self" means under it, and the Source/Target orientation of inbound
///     verbs.
/// </summary>
public sealed class DependencyVerbTests
{
    // A subject an adjective refines rather than a noun: two Controller-suffixed types and three edges —
    // one to a type the rule lists, one to a type it does not, and one between the controllers
    // themselves. The three answers a self-allowing allow-list owes are readable off one run.
    private const string ControllersAndData = """
                                              namespace App.Domain { public class Order {} }
                                              namespace App.Data { public class Db {} }
                                              namespace App.Web
                                              {
                                                  public class OrderController
                                                  {
                                                      public App.Domain.Order O;
                                                      public HomeController H;
                                                  }
                                                  public class HomeController { public App.Data.Db D; }
                                              }
                                              """;

    [Fact]
    public void MustNotReference_DomainReferencesWeb_FailsWithSourceTargetSites()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Layer("Domain", "App.Domain.*").MustNotReference(arch.Layer("Web", "App.Web.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Reference, "App.Domain.Service -> App.Web.Controller");
        result.Violations.First(v => v.Source!.FullName == "App.Domain.Service" && v.Target!.FullName == "App.Web.Controller")
            .Sites.ShouldNotBeEmpty();
    }

    [Fact]
    public void MustNotReference_CleanDirection_Passes()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("layering/x")
                    .Enforce(arch.Layer("Web", "App.Web.*").MustNotReference(arch.Layer("Domain", "App.Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassed();
        result.Violations.ShouldBeEmpty();
    }

    [Fact]
    public void MustNotReference_TypeofExternalTarget_Fails()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("no-sql/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustNotReference(typeof(StringBuilder)))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Domain.Service -> System.Text.StringBuilder"]);
    }

    [Fact]
    public void MustNotBeReferencedBy_WebByDomain_OrientsSourceAtReferencingType()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("inbound/x")
                    .Enforce(arch.Namespace("App.Web.*").MustNotBeReferencedBy(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        // Source is the referencing Domain type (where the edit happens); Target is the referenced Web type.
        result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Reference, "App.Domain.Service -> App.Web.Controller");
        result.Violations.ShouldAllBe(v => v.Target!.Namespace == "App.Web");
    }

    [Fact]
    public void MustOnlyReference_ExternalTarget_IsExemptFromComplementUniverse()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        // The BCL reference (StringBuilder) is exempt; the same-layer reference (Model) is allowed. Stated as
        // one edge in and one edge out rather than as the whole set: exhaustively, this rule's violations are
        // the four-edge literal ViolationOrder_IsOrdinalBySourceThenTarget pins, and a second copy of it here
        // would couple this row to types that exist for that pin.
        result.Violations.ShouldAllBe(v => !v.Target!.IsExternal);
        IReadOnlyList<string> referenced = result.ReferencePairs();
        referenced.ShouldNotContain("App.Domain.Service -> App.Domain.Model");
        referenced.ShouldContain("App.Domain.Service -> App.Web.Controller");
    }

    [Fact]
    public void MustOnlyReference_SameLayerReference_IsImplicitlySelfAllowed()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        // Written allow-set = Web; the subject joins it, so the Domain→Domain edge (Service→Model) is
        // allowed without being named. Every other outbound edge is into Web or external, so the whole
        // rule is green and no author had to write `MustOnlyReference(domain, web)` to get here.
        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyReference_AnAdjectiveRefinedSubject_AllowsItselfWithoutGoingVacuous()
    {
        RuleResult result = Checker.Run(ControllersAndData, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Types.WithSuffix("Controller").MustOnlyReference(arch.Namespace("App.Domain.*")))
                    .Because("b"))
            .Single();

        // "Self" is the refined subject, never the noun head: were it `arch.Types` the allow-set would
        // swallow the solution and the rule would say nothing. Exhaustive, so it carries both halves —
        // the controller-to-controller edge is allowed, and the one edge out of the two named regions is
        // still red.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Web.HomeController -> App.Data.Db"]);
    }

    [Fact]
    public void MustOnlyReference_SubjectNarrowedByExcept_DoesNotAllowWhatTheExceptRemoved()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").Except(arch.Types.WithSuffix("Model"))
                        .MustOnlyReference(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        // The other end of the same pin: an Except narrows what the rule allows exactly as it narrows what
        // the rule governs, so Model leaves the subject and the Service→Model edge goes red. Listing Model
        // among the targets is how an author gets it back.
        result.ShouldHaveFailedWithEdges(ViolationKind.Reference, ["App.Domain.Service -> App.Domain.Model"]);
    }

    [Fact]
    public void MustOnlyBeReferencedBy_ReferenceFromWithinTheSubject_IsImplicitlySelfAllowed()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("only/x")
                    .Enforce(arch.Namespace("App.Domain.*").MustOnlyBeReferencedBy(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        // The inbound verb carries the same default, with the subject at the far end of the test: the only
        // reference into Domain comes from Domain itself (Service→Model), and the subject allows it.
        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustOnlyBeReferencedBy_InboundFromOutsideAllowSet_IsRed()
    {
        RuleResult result = Checker.Run(Sources.LayeredModel, arch =>
                arch.Rule("contain/x")
                    .Enforce(arch.Namespace("App.Web.*").MustOnlyBeReferencedBy(arch.Namespace("App.Web.*")))
                    .Because("b"))
            .Single();

        // Web may be referenced only by Web; the inbound Domain→Web edge is a violation.
        result.ShouldHaveFailedWithEdgesIncluding(ViolationKind.Reference, "App.Domain.Service -> App.Web.Controller");
    }
}
