using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The filter-aware catch verb <c>MustNotCatchUnfiltered</c> over the fast path (GRAMMAR §4.8, §4.3, §5.3):
///     a catch edge trips only where a subject <c>catch</c>es a forbidden type at a clause that spells no
///     <c>when</c> filter.
/// </summary>
/// <remarks>
///     The two pins the sibling <c>MustNotCatch</c> cannot carry are here — an edge whose
///     every site is filtered is <b>green</b>, and a mixed edge's evidence is the <b>unfiltered sites only</b>,
///     so no reader is ever pointed at a site the rule considers good. The rows the catch verbs share
///     (FQN matching, a hierarchy operand, the absent typeof target, the ratchet, the JSON wire form) live in
///     <c>MustNotCatchVerbTests</c>: the catch verbs run one walk and differ only in the sites they read. The kind is
///     <see cref="ViolationKind.Catch" />, which names the fact family rather than the verb, so identity is the
///     (source, caught) type pair keyed by <see cref="BaselineEntry.ForEdge" /> and the unfiltered sites are
///     evidence, never identity.
/// </remarks>
public sealed class MustNotCatchUnfilteredVerbTests
{
    // Errors.DbError is the domain exception nobody may swallow blind. DataHandler catches it with no filter
    // (the red edge); FilteredHandler catches the very same type behind a `when` filter — a catch, and a catch of
    // a banned type, but the good state this verb exists to reward, so the verb is silent on it.
    private const string Scene = """
                                 namespace Errors
                                 {
                                     public class DbError : System.Exception {}
                                 }
                                 namespace App
                                 {
                                     public class DataHandler
                                     {
                                         public void Run()
                                         {
                                             try { }
                                             catch (Errors.DbError) { }
                                         }
                                     }
                                     public class FilteredHandler
                                     {
                                         public void Run(bool flag)
                                         {
                                             try { }
                                             catch (Errors.DbError) when (flag) { }
                                         }
                                     }
                                 }
                                 """;

    // One (source, caught) pair caught twice in one type — filtered at line 9, unfiltered at line 11. Extraction
    // records both as sites and only the second as unfiltered, which is what the evidence-subset pin reads.
    private const string MixedScene = """
                                      namespace Errors { public class DbError : System.Exception {} }
                                      namespace App
                                      {
                                          public class MixedHandler
                                          {
                                              public void Run(bool flag)
                                              {
                                                  try { }
                                                  catch (Errors.DbError) when (flag) { }
                                                  try { }
                                                  catch (Errors.DbError) { }
                                              }
                                          }
                                      }
                                      """;

    private static readonly CodebaseModel SceneModel = CompilationFactory.Extract(Scene);

    private static readonly CodebaseModel MixedModel = CompilationFactory.Extract(MixedScene);

    [Fact]
    public void MustNotCatchUnfiltered_SubjectCatchesBannedTypeUnfiltered_FailsWithSourceTargetSitesAndHumanLine()
    {
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        // The subject covers FilteredHandler too, and the single-item claim below is the verb's whole point:
        // its identical catch of the identical type is absent from the report.
        result.ShouldHaveFailedWithSingleEdge(ViolationKind.Catch, "App.DataHandler", "Errors.DbError");

        string block = result.HumanBlock();
        block.ShouldContain("App.DataHandler catches Errors.DbError");
        block.ShouldContain("Test.cs:");
    }

    [Fact]
    public void MustNotCatchUnfiltered_EveryCatchSiteFiltered_PassesCleanWithNoWarnings()
    {
        // The all-filtered-is-green pin. FilteredHandler catches the banned Errors.DbError — the edge exists, and
        // plain MustNotCatch would red it — but every site of that edge spells a `when` filter, so the unfiltered
        // subset is empty and the rule passes. The forbidden target resolves (Errors.DbError exists), so this is a
        // real pass, not an inert one.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").WithSuffix("FilteredHandler")
                        .MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotCatchUnfiltered_EdgeMixesFilteredAndUnfilteredSites_EvidenceIsTheUnfilteredSitesOnly()
    {
        // The evidence-subset pin. One (source, caught) edge, two catch sites: the filtered one at line 9 and the
        // unfiltered one at line 11. The edge violates because a site is unfiltered, and the violation carries the
        // unfiltered site ALONE — so every printed file:line is a site the rule actually objects to, which is what
        // lets the verb reuse the Catch kind and its "{source} catches {target}" line without printing a falsehood.
        MixedModel.CatchEdge("App.MixedHandler", "Errors.DbError")
            .Sites.Lines()
            .ShouldBe([9, 11]);

        RuleResult result = Checker.Run(MixedModel, arch =>
                arch.Rule("ex/filter-domain-catches")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Errors.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveFailed();
        Violation violation = result.Violations.ShouldHaveSingleItem();
        violation.Sites.Select(site => site.Line)
            .ShouldBe([11]);

        string block = result.HumanBlock();
        block.ShouldContain("Test.cs:11 — App.MixedHandler catches Errors.DbError");
        block.ShouldNotContain("Test.cs:9");
    }

    [Fact]
    public void MustNotCatchUnfiltered_InertPatternTarget_WarnsAndStillPasses()
    {
        // The forbidden target (a namespace glob) matches no types, so the rule can never fire — inert. The
        // pattern operand is the warning gate: the verb joins the forbidden-set inert-warn family (§4.1) exactly
        // as its sibling does.
        RuleResult result = Checker.Run(SceneModel, arch =>
                arch.Rule("ex/inert")
                    .Enforce(arch.Namespace("App.*").MustNotCatchUnfiltered(arch.Namespace("Nonexistent.*")))
                    .Because("b"))
            .Single();

        result.ShouldHaveWarnedInertTarget();
    }
}
