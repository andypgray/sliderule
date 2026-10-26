using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Tests.TestSupport;
using Xunit;

namespace SlideRule.Tests.Baselines;

/// <summary>
///     Value semantics for <see cref="BaselineEntry" /> — the identity a baseline keys on (GRAMMAR §4.3).
///     Edge and subject entries are ordinal-equal only when every slot matches, and equal entries share
///     a hash code (so a <see cref="HashSet{T}" /> answers membership correctly). An optional
///     <see cref="BaselineEntry.Because" /> attribution is excluded from equality, so an attributed entry
///     and its unattributed twin dedupe as one — and so is the <see cref="BaselineEntry.SiteCount" />
///     measure, which the ratchet compares but identity never forks on.
/// </summary>
public sealed class BaselineEntryTests
{
    [Fact]
    public void ForEdge_SamePair_AreEqualAndShareHashCode()
    {
        BaselineEntry a = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry b = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        a.ShouldBe(b);
        a.GetHashCode()
            .ShouldBe(b.GetHashCode());
        new HashSet<BaselineEntry> { a }.Contains(b)
            .ShouldBeTrue();
    }

    [Fact]
    public void ForEdge_DifferentTarget_AreNotEqual()
    {
        BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.Src", "T:N.Other"));
    }

    [Fact]
    public void ForEdge_DifferentSource_AreNotEqual()
    {
        // The source half of identity, on its own. Every other inequality row here varies the target — the
        // swapped-pair row varies both at once — so an Equals that compared target and subject and ignored
        // source would satisfy all of them. That is not a hypothetical slip: it is the whole of what keeps a
        // second type's reference to an already-grandfathered target red instead of quietly baselined.
        BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.Other", "T:N.Tgt"));
    }

    [Theory]
    [InlineData(nameof(Violation.Construction), "N.Factory", "N.Widget")]
    [InlineData(nameof(Violation.Injection), "N.Svc", "N.Dep")]
    public void ForEdge_ConstructionOrInjectionViolationIdentity_IsAnOrdinaryEdgeEntry(
        string factory, string source, string target)
    {
        // A construction or injection violation's identity is a plain (source, target) ForEdge entry
        // (GRAMMAR §4.3), the shape a reference uses, so grandfathering either needs no new baseline format:
        // the identity is value-equal to, hash-equal to, and set-dedupes with a hand-built ForEdge and its
        // attributed twin.
        TypeNode sourceNode = SyntheticNodes.Type(source);
        TypeNode targetNode = SyntheticNodes.Type(target);
        Violation violation = factory == nameof(Violation.Construction)
            ? Violation.Construction(sourceNode, targetNode, Array.Empty<SourceLocation>())
            : Violation.Injection(sourceNode, targetNode, Array.Empty<SourceLocation>());
        BaselineEntry identity = violation.BaselineIdentity()!;
        BaselineEntry expected = BaselineEntry.ForEdge($"T:{source}", $"T:{target}");

        identity.ShouldBe(expected);
        identity.GetHashCode()
            .ShouldBe(expected.GetHashCode());
        identity.Subject.ShouldBeNull();
        new HashSet<BaselineEntry> { identity }
            .Contains(expected.WithBecause("INC-1"))
            .ShouldBeTrue();
    }

    [Fact]
    public void ForEdge_SwappedSourceAndTarget_AreNotEqual()
    {
        BaselineEntry.ForEdge("T:N.A", "T:N.B")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.B", "T:N.A"));
    }

    [Fact]
    public void ForSubject_SameId_AreEqualAndShareHashCode()
    {
        BaselineEntry a = BaselineEntry.ForSubject("T:N.Type");
        BaselineEntry b = BaselineEntry.ForSubject("T:N.Type");

        a.ShouldBe(b);
        a.GetHashCode()
            .ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ForSubject_AndForEdge_WithOverlappingStrings_AreNotEqual()
    {
        // A subject "T:N.Src" must never collide with an edge whose source is "T:N.Src".
        BaselineEntry.ForSubject("T:N.Src")
            .ShouldNotBe(BaselineEntry.ForEdge("T:N.Src", ""));
    }

    [Fact]
    public void WithBecause_SameIdentity_AreEqualAndShareHashCode()
    {
        BaselineEntry plain = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry attributed = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithBecause("INC-1234");

        attributed.ShouldBe(plain);
        attributed.GetHashCode()
            .ShouldBe(plain.GetHashCode());
        new HashSet<BaselineEntry> { plain }.Contains(attributed)
            .ShouldBeTrue();
    }

    [Fact]
    public void WithBecause_PreservesIdentitySlots_AndSetsBecause()
    {
        BaselineEntry original = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        BaselineEntry attributed = original.WithBecause("keep until INC-1234");

        attributed.Source.ShouldBe("T:N.Src");
        attributed.Target.ShouldBe("T:N.Tgt");
        attributed.Subject.ShouldBeNull();
        attributed.Because.ShouldBe("keep until INC-1234");
        original.Because.ShouldBeNull();
    }

    [Fact]
    public void WithSiteCount_SameIdentity_AreEqualAndShareHashCode()
    {
        // The measure is excluded from identity for the reason the attribution is, and for one more: a
        // grown pair whose count moved would otherwise read as a stale entry beside a brand-new one, and
        // 'baseline accept-reductions' would then delete real debt.
        BaselineEntry plain = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry counted = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithSiteCount(3);

        counted.ShouldBe(plain);
        counted.GetHashCode()
            .ShouldBe(plain.GetHashCode());
        new HashSet<BaselineEntry> { plain }.Contains(counted)
            .ShouldBeTrue();
    }

    [Fact]
    public void WithSiteCount_DifferentCounts_AreStillEqual()
    {
        BaselineEntry two = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithSiteCount(2);
        BaselineEntry three = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithSiteCount(3);

        three.ShouldBe(two);
        three.SiteCount.ShouldBe(3);
        two.SiteCount.ShouldBe(2);
    }

    [Fact]
    public void WithSiteCount_PreservesIdentitySlots_AndLeavesTheOriginalUncounted()
    {
        BaselineEntry original = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        BaselineEntry counted = original.WithSiteCount(2);

        counted.Source.ShouldBe("T:N.Src");
        counted.Target.ShouldBe("T:N.Tgt");
        counted.Subject.ShouldBeNull();
        counted.SiteCount.ShouldBe(2);
        original.SiteCount.ShouldBeNull();
    }

    [Fact]
    public void WithSiteCountAndWithBecause_ComposeInEitherOrder()
    {
        // Each copy verb preserves what the other set, so recording a count never drops the attribution
        // that justified the entry and attributing one never drops its measure.
        BaselineEntry countedThenAttributed = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithSiteCount(2)
            .WithBecause("INC-1234");
        BaselineEntry attributedThenCounted = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithBecause("INC-1234")
            .WithSiteCount(2);

        countedThenAttributed.ShouldSatisfyAllConditions(
            () => countedThenAttributed.SiteCount.ShouldBe(2),
            () => countedThenAttributed.Because.ShouldBe("INC-1234"),
            () => attributedThenCounted.SiteCount.ShouldBe(2),
            () => attributedThenCounted.Because.ShouldBe("INC-1234"));
    }

    [Fact]
    public void WithSiteCount_ZeroOrNegative_Throws()
    {
        // An entry grandfathering nothing is a *stale* entry, which the ratchet reports for acceptance —
        // so zero is refused rather than stored as an allowance nothing can ever satisfy.
        BaselineEntry entry = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        Should.Throw<ArgumentOutOfRangeException>(() => entry.WithSiteCount(0))
            .Message.ShouldContain("at least 1");
        Should.Throw<ArgumentOutOfRangeException>(() => entry.WithSiteCount(-1))
            .Message.ShouldContain("at least 1");
    }

    [Fact]
    public void WithSiteCount_OnSubjectEntry_Throws()
    {
        // A subject entry's sites are declarations (GRAMMAR §4.3), so there is nothing for a count to
        // measure — and the correspondence verb swaps its site set outright when its arm flips.
        Should.Throw<InvalidOperationException>(() => BaselineEntry.ForSubject("T:N.Type")
                .WithSiteCount(2))
            .Message.ShouldContain("Only an edge entry carries a site count");
    }

    [Fact]
    public void IsEdge_PartsEdgeEntriesFromSubjectEntries()
    {
        BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .IsEdge.ShouldBeTrue();
        BaselineEntry.ForSubject("T:N.Type")
            .IsEdge.ShouldBeFalse();
        BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt")
            .WithBecause("INC-1234")
            .IsEdge.ShouldBeTrue();
    }

    [Fact]
    public void WithBecause_BlankOrMultiline_Throws()
    {
        BaselineEntry entry = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");

        Should.Throw<ArgumentException>(() => entry.WithBecause("   "))
            .Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause(""))
            .Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause("a\rb"))
            .Message.ShouldContain("non-blank single line");
        Should.Throw<ArgumentException>(() => entry.WithBecause("a\nb"))
            .Message.ShouldContain("non-blank single line");
    }

    [Fact]
    public void Collapsed_TwoCountsForOneEdge_KeepsTheSmallerInEitherOrder()
    {
        BaselineEntry five = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithSiteCount(5);
        BaselineEntry three = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithSiteCount(3);

        BaselineEntry.Collapsed([five, three]).ShouldHaveSingleItem().SiteCount.ShouldBe(3);
        BaselineEntry.Collapsed([three, five]).ShouldHaveSingleItem().SiteCount.ShouldBe(3);
    }

    [Fact]
    public void Collapsed_CountedAndUncountedCopies_KeepsTheCount()
    {
        // An entry with no count grandfathers any number of sites, so it is the loosest of all and never wins.
        BaselineEntry uncounted = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt");
        BaselineEntry counted = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithSiteCount(4);

        BaselineEntry.Collapsed([uncounted, counted]).ShouldHaveSingleItem().SiteCount.ShouldBe(4);
        BaselineEntry.Collapsed([counted, uncounted]).ShouldHaveSingleItem().SiteCount.ShouldBe(4);
    }

    [Fact]
    public void Collapsed_ReasonOnTheLargerCountLine_SurvivesBesideTheSmallerCount()
    {
        // The case a merge produces: one branch paid sites down, the other attributed the entry.
        BaselineEntry paidDown = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithSiteCount(2);
        BaselineEntry attributed = BaselineEntry.ForEdge("T:N.Src", "T:N.Tgt").WithSiteCount(5).WithBecause("legacy import");

        BaselineEntry kept = BaselineEntry.Collapsed([paidDown, attributed]).ShouldHaveSingleItem();

        kept.SiteCount.ShouldBe(2);
        kept.Because.ShouldBe("legacy import");
    }

    [Fact]
    public void Collapsed_TwoReasonsForOneSubject_KeepsTheFirst()
    {
        BaselineEntry first = BaselineEntry.ForSubject("T:N.Thing").WithBecause("first");
        BaselineEntry second = BaselineEntry.ForSubject("T:N.Thing").WithBecause("second");

        BaselineEntry kept = BaselineEntry.Collapsed([first, second]).ShouldHaveSingleItem();

        kept.Because.ShouldBe("first");
        kept.SiteCount.ShouldBeNull();
    }

    [Fact]
    public void Collapsed_DistinctIdentities_KeepsEachInFirstAppearanceOrder()
    {
        BaselineEntry b = BaselineEntry.ForSubject("T:N.B");
        BaselineEntry a = BaselineEntry.ForSubject("T:N.A");

        BaselineEntry.Collapsed([b, a, b]).ShouldBe([b, a]);
    }
}
