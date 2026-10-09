using Microsoft.CodeAnalysis;
using Xunit;

namespace SlideRule.Tests.SpecAnalyzers;

/// <summary>
///     The three warnings no single literal can draw: a rule or scope ID declared twice, a rule ID under a
///     scope's reserved prefix, and a layer name declared twice.
/// </summary>
/// <remarks>
///     <para>
///         The comparison is one method body, which is what makes these warnings arrive while the author
///         is typing rather than at the end of a build. It is also the whole of their reach: a second
///         method, a second file and a rule pack are each out of it, and each is a case where the spec
///         loader reports what this does not. Under-reporting is the direction that costs nothing, so the
///         fact about two methods below is a claim about silence rather than a gap.
///     </para>
///     <para>
///         Each warning sits on the declaration to change and points at the one it collided with, so a
///         reader who follows the squiggle is already at the line they have to rename.
///     </para>
/// </remarks>
public sealed class SpecLiteralCollisionTests
{
    [Fact]
    public async Task OneBody_ReportsAllThreeCollisionsWhereTheChangeGoesAndPointsAtWhatItMet()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Layer("Web", "Web.*");
                    arch.Layer("Web", "Api.*");

                    arch.Scope("legacy/billing")
                        .Quarantine(arch.Namespace("Legacy.*"))
                        .Dragons("The billing tables are structural and are not to be tidied.")
                        .Because("The estate still posts through it.");

                    arch.Rule("legacy/billing/extra")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because("The estate still posts through it.");

                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because("The estate still posts through it.");

                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Handler"))
                        .Because("The estate still posts through it.");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1010", "SR1001", "SR1000");

        Diagnostic layerName = reported.ShouldHaveSingle("SR1010");
        layerName.ShouldSitOn(line: 8, column: 20, "\"Web\"");
        layerName.ShouldPointAt(line: 7, column: 20, "\"Web\"");
        layerName.ShouldSay("Duplicate layer name 'Web'.");
        layerName.ShouldCarry(
            "code=DuplicateLayerName", "head=Duplicate layer name 'Web'.", "slot=LayerName", "tail=");

        Diagnostic underAScope = reported.ShouldHaveSingle("SR1001");
        underAScope.ShouldSitOn(line: 15, column: 19, "\"legacy/billing/extra\"");
        underAScope.ShouldPointAt(line: 10, column: 20, "\"legacy/billing\"");
        underAScope.ShouldSay(
            "Rule ID 'legacy/billing/extra' extends scope 'legacy/billing'; the 'legacy/billing/…' "
            + "namespace is reserved for its generated children.");

        Diagnostic duplicate = reported.ShouldHaveSingle("SR1000");
        duplicate.ShouldSitOn(line: 23, column: 19, "\"shape/suffix\"");
        duplicate.ShouldPointAt(line: 19, column: 19, "\"shape/suffix\"");
        duplicate.ShouldSay("Duplicate rule ID 'shape/suffix'.");
    }

    [Fact]
    public async Task TwoScopesDeclaredWithOneId_DrawTwoDuplicatesOnTheSecondLiteral()
    {
        // Every scope reserves both of the rules it could contribute, so one repeated scope ID collides
        // twice — under two IDs that appear nowhere in the source.
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Scope("legacy/billing")
                        .Quarantine(arch.Namespace("Legacy.*"))
                        .Dragons("The billing tables are structural and are not to be tidied.")
                        .Because("The estate still posts through it.");

                    arch.Scope("legacy/billing")
                        .Quarantine(arch.Namespace("Older.*"))
                        .Dragons("The older tables are structural and are not to be tidied.")
                        .Because("The estate still posts through that too.");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldReport("SR1000", "SR1000");

        foreach (Diagnostic duplicate in reported)
        {
            duplicate.ShouldSitOn(line: 12, column: 20, "\"legacy/billing\"");
            duplicate.ShouldPointAt(line: 7, column: 20, "\"legacy/billing\"");
        }

        reported[0]
            .ShouldSay("Duplicate rule ID 'legacy/billing/containment'.");
        reported[1]
            .ShouldSay("Duplicate rule ID 'legacy/billing/tripwire'.");
    }

    [Fact]
    public async Task DeclarationsInTwoMethods_AreNotComparedAgainstEachOther()
    {
        const string source =
            """
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because("The estate still posts through it.");

                    Alongside(arch);
                }

                private static void Alongside(Arch arch)
                {
                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Handler"))
                        .Because("The estate still posts through it.");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        reported.ShouldBeSilent();
    }

    [Fact]
    public async Task ARuleIdInsideALambda_IsComparedAgainstTheOnesAroundIt()
    {
        // A lambda body is part of the block of the method that holds it, which is what keeps a spec that
        // builds some of its rules through a closure inside one comparison.
        const string source =
            """
            using System;
            using SlideRule;

            public sealed class Spec
            {
                public void Define(Arch arch)
                {
                    Action inALambda = () => arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Controller"))
                        .Because("The estate still posts through it.");
                    inALambda();

                    arch.Rule("shape/suffix")
                        .Enforce(arch.Types.MustHaveSuffix("Handler"))
                        .Because("The estate still posts through it.");
                }
            }
            """;

        IReadOnlyList<Diagnostic> reported = await SpecSnippet.ShouldCompileAndDiagnoseAsync(source, cancellationToken: Ct);

        Diagnostic duplicate = reported.ShouldBeTheOnly("SR1000");
        duplicate.ShouldSitOn(line: 13, column: 19, "\"shape/suffix\"");
        duplicate.ShouldPointAt(line: 8, column: 44, "\"shape/suffix\"");
        duplicate.ShouldSay("Duplicate rule ID 'shape/suffix'.");
    }
}
