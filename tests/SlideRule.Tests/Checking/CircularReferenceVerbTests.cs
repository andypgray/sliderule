using Shouldly;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.Extraction;
using Xunit;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The cycle gate over a family of layers (GRAMMAR §5.1, §5.3): an arrow runs from cell to cell
///     wherever an owned reference crosses between them, components are taken over those arrows, and every
///     type pair on an arrow inside a component is red, carrying the circle it lies on.
/// </summary>
/// <remarks>
///     Three beds separate the three things that could be confused. <c>Cycle</c> parts an arrow that lies
///     <em>on</em> a circle from one that merely points <em>into</em> it, and holds the two shapes that are
///     not nodes at all — an external target and a type outside every cell. <c>Ring</c> is a circle of
///     three, which is where declaration order becomes visible in the detail. <c>Twin</c> holds two
///     disjoint circles in one family, so each violation has to name its own.
/// </remarks>
public sealed class CircularReferenceVerbTests
{
    // Alpha reaches Beta, Beta reaches back — the circle. Gamma reaches into it and nothing reaches Gamma,
    // so C lies on no circle however many cells the family declares. Alpha's other two edges are the
    // non-nodes: Outside.Free belongs to no cell, and StringBuilder is external.
    private const string Cycle = """
                                 namespace Outside { public class Free {} }
                                 namespace Cyc.A
                                 {
                                     public class Alpha
                                     {
                                         public Cyc.B.Beta Beta;
                                         public Outside.Free Free;
                                         public System.Text.StringBuilder External;
                                     }
                                 }
                                 namespace Cyc.B { public class Beta { public Cyc.A.Alpha Back; } }
                                 namespace Cyc.C { public class Gamma { public Cyc.A.Alpha Into; } }
                                 """;

    // A three-cell circle: every cell is both a source and a target of the one component.
    private const string Ring = """
                                namespace Ring.A { public class A1 { public Ring.B.B1 Next; } }
                                namespace Ring.B { public class B1 { public Ring.C.C1 Next; } }
                                namespace Ring.C { public class C1 { public Ring.A.A1 Next; } }
                                """;

    // Two two-cell circles that never touch, so one family carries two components.
    private const string Twin = """
                                namespace Twin.A { public class A1 { public Twin.B.B1 Over; } }
                                namespace Twin.B { public class B1 { public Twin.A.A1 Back; } }
                                namespace Twin.C { public class C1 { public Twin.D.D1 Over; } }
                                namespace Twin.D { public class D1 { public Twin.C.C1 Back; } }
                                """;

    private const string AmongAB = "circular references among the A and B layers";

    private static readonly CodebaseModel CycleModel = CompilationFactory.Extract(Cycle);

    private static readonly CodebaseModel RingModel = CompilationFactory.Extract(Ring);

    private static readonly CodebaseModel TwinModel = CompilationFactory.Extract(Twin);

    [Fact]
    public void MustNotHaveCircularReferences_TwoCellsPointingBothWays_RedBothArrowsWithTheCircleNamed()
    {
        RuleResult result = Checker.Verdict(
            CycleModel, arch => arch.Each(arch.Layer("A", "Cyc.A.*"), arch.Layer("B", "Cyc.B.*"))
                .MustNotHaveCircularReferences());

        // Exhaustive: the two arrows of the circle and nothing else. Alpha's Outside edge names no cell and
        // its StringBuilder edge is external, so neither is a node of the cell graph at all.
        result.ShouldHaveFailedWithDetailedEdges(ViolationKind.Reference,
        [
            ("Cyc.A.Alpha -> Cyc.B.Beta", AmongAB),
            ("Cyc.B.Beta -> Cyc.A.Alpha", AmongAB)
        ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_AnArrowIntoTheCircle_IsGreenAndUnnamed()
    {
        RuleResult result = Checker.Verdict(CycleModel, arch =>
            arch.Each(arch.Layer("A", "Cyc.A.*"), arch.Layer("B", "Cyc.B.*"), arch.Layer("C", "Cyc.C.*"))
                .MustNotHaveCircularReferences());

        // Adding C to the family changes nothing: Gamma → Alpha lies on no circle, because nothing leads
        // back to C. The detail is the same two-cell one, and C's name is not in it.
        result.ShouldHaveFailedWithDetailedEdges(ViolationKind.Reference,
        [
            ("Cyc.A.Alpha -> Cyc.B.Beta", AmongAB),
            ("Cyc.B.Beta -> Cyc.A.Alpha", AmongAB)
        ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_OneArrowWithNoWayBack_PassesClean()
    {
        RuleResult result = Checker.Verdict(
            CycleModel, arch => arch.Each(arch.Layer("A", "Cyc.A.*"), arch.Layer("C", "Cyc.C.*"))
                .MustNotHaveCircularReferences());

        // C → A and nothing back: the cells MAY reference each other, which is exactly what parts this verb
        // from the cross-cell ban over the same subject.
        result.ShouldHavePassedClean();
    }

    [Fact]
    public void MustNotHaveCircularReferences_ACircleOfThree_RedsEveryArrowOnIt()
    {
        RuleResult result = Checker.Verdict(RingModel, arch =>
            arch.Each(arch.Layer("A", "Ring.A.*"), arch.Layer("B", "Ring.B.*"), arch.Layer("C", "Ring.C.*"))
                .MustNotHaveCircularReferences());

        const string amongABC = "circular references among the A, B and C layers";
        result.ShouldHaveFailedWithDetailedEdges(ViolationKind.Reference,
        [
            ("Ring.A.A1 -> Ring.B.B1", amongABC),
            ("Ring.B.B1 -> Ring.C.C1", amongABC),
            ("Ring.C.C1 -> Ring.A.A1", amongABC)
        ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_TheDetail_NamesTheCellsInDeclarationOrder()
    {
        RuleResult result = Checker.Verdict(RingModel, arch =>
            arch.Each(arch.Layer("C", "Ring.C.*"), arch.Layer("A", "Ring.A.*"), arch.Layer("B", "Ring.B.*"))
                .MustNotHaveCircularReferences());

        // The same circle, the same three arrows: only the order the family declares its cells in moves,
        // and the detail follows it, because that is the order the rule's own sentence names them in.
        const string amongCAB = "circular references among the C, A and B layers";
        result.ShouldHaveFailedWithDetailedEdges(ViolationKind.Reference,
        [
            ("Ring.A.A1 -> Ring.B.B1", amongCAB),
            ("Ring.B.B1 -> Ring.C.C1", amongCAB),
            ("Ring.C.C1 -> Ring.A.A1", amongCAB)
        ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_TwoCirclesInOneFamily_EachPairNamesItsOwn()
    {
        RuleResult result = Checker.Verdict(TwinModel, arch => arch.Each(
                arch.Layer("A", "Twin.A.*"),
                arch.Layer("B", "Twin.B.*"),
                arch.Layer("C", "Twin.C.*"),
                arch.Layer("D", "Twin.D.*"))
            .MustNotHaveCircularReferences());

        // Components, not the family: A/B and C/D are two circles that never meet, so a reader of either
        // pair is told which cells to redraw and is not handed the whole family.
        const string amongCD = "circular references among the C and D layers";
        result.ShouldHaveFailedWithDetailedEdges(ViolationKind.Reference,
        [
            ("Twin.A.A1 -> Twin.B.B1", AmongAB),
            ("Twin.B.B1 -> Twin.A.A1", AmongAB),
            ("Twin.C.C1 -> Twin.D.D1", amongCD),
            ("Twin.D.D1 -> Twin.C.C1", amongCD)
        ]);
    }

    [Fact]
    public void MustNotHaveCircularReferences_OneCellFamily_PassesVacuouslyAndReadsAsTheCell()
    {
        RuleResult result = Checker.Verdict(CycleModel, arch => arch.Each(arch.Layer("A", "Cyc.A.*"))
            .MustNotHaveCircularReferences());

        // One cell has no arrow to any other, so no component holds two — and the sentence is the cell's
        // own (GRAMMAR §5.1's identity), exactly as the cross-cell ban's is.
        result.ShouldHavePassedClean();
        result.Rule.Sentence.ShouldBe("The A layer must not have circular references with the others.");
    }
}
