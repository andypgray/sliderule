using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;

namespace SlideRule.Tests.Checking;

/// <summary>
///     The Shouldly surface over <see cref="RuleResult" />: each assertion is phrased as the outcome a
///     rule reported (GRAMMAR §4.1) rather than as the property reads that establish it, and every one
///     of them carries the whole result into its failure message.
/// </summary>
/// <remarks>
///     <para>
///         A bare <c>Status.ShouldBe(RuleStatus.Failed)</c> reds with two enum names and not one word
///         about which violation actually fired, or whether any did. Passing
///         <see cref="Describe(RuleResult)" /> as the Shouldly reason puts the violations, the warnings
///         and the ratchet counts in front of the reader instead, which is the first thing a red raises.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>. Shouldly derives the "actual"
///         half of its message from the first stack frame after the last attributed one and reads that
///         line off disk, so leaving the attribute off makes an inner failure read
///         <c>violation.Target!.FullName should be …</c> — naming the property under test. Attributed,
///         the same failure would name the caller's receiver and say nothing about which of its properties
///         failed.
///     </para>
/// </remarks>
internal static class RuleResultAssertions
{
    private const string InertTargetMessage = "This rule is inert: its target selection matched no types.";

    private const string NothingPopulated = "(nothing populated)";

    /// <summary>Asserts the rule passed. Says nothing about warnings — an inert-target warning still passes.</summary>
    internal static RuleResult ShouldHavePassed(this RuleResult result)
    {
        result.Status.ShouldBe(RuleStatus.Passed, Describe(result));
        return result;
    }

    /// <summary>Asserts the rule passed with nothing to say: no violations and no warnings.</summary>
    internal static RuleResult ShouldHavePassedClean(this RuleResult result)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Passed, report);

        result.ShouldSatisfyAllConditions(
            () => result.Violations.ShouldBeEmpty(report),
            () => result.Warnings.ShouldBeEmpty(report));

        return result;
    }

    /// <summary>
    ///     Asserts the rule passed carrying exactly the inert-target warning — a target selection that
    ///     matched no types is a warning, never a failure (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Holds the warning's pinned wording. The cure beside it is held to being <em>there</em> rather than
    ///     to its words: which cure a target earns depends on its shape, and every caller here is about its
    ///     verb.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedInertTarget(this RuleResult result)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Passed, report);
        result.Violations.ShouldBeEmpty(report);
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem(report);

        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(CheckWarningKind.InertTarget, report),
            () => warning.Message.ShouldBe(InertTargetMessage, report),
            () => warning.Hint.ShouldNotBeNullOrWhiteSpace(report));

        return result;
    }

    /// <summary>
    ///     Asserts the rule carries exactly the partly-inert warning naming
    ///     <paramref name="operandReference" /> — one target operand that matched nothing, beside at
    ///     least one that did (GRAMMAR §4.1, §5.1).
    /// </summary>
    /// <remarks>
    ///     Says nothing about status, and deliberately: whether the rule passed turns on what its
    ///     <em>live</em> operands found, which is a fact about the codebase rather than about the dead
    ///     operand this is asserting. A caller that cares asserts it beside this.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedPartlyInertOnOperand(
        this RuleResult result, string operandReference)
    {
        return result.ShouldHaveWarnedPartlyInertOnOperands([operandReference]);
    }

    /// <summary>
    ///     Asserts the rule carries exactly one partly-inert warning per entry of
    ///     <paramref name="operandReferences" />, in that order — each dead target operand named in the
    ///     message and carried as <see cref="CheckWarning.Key" />, of its own kind, with a cure beside it
    ///     (GRAMMAR §4.1, §5.1).
    /// </summary>
    /// <remarks>
    ///     Each warning is compared as one string of kind, operand and message, so a red shows every warning
    ///     the rule carried against every one expected, and an extra warning of any kind reds as surely as a
    ///     missing one. Ordered, because operand order is what the checker walks and what a reader sees. Says
    ///     nothing about status, for <see cref="ShouldHaveWarnedPartlyInertOnOperand" />'s reason.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedPartlyInertOnOperands(
        this RuleResult result, string[] operandReferences)
    {
        return result.ShouldHaveWarnedPerPart(
            operandReferences, CheckWarningKind.DeadOperand, PartlyInertMessage);
    }

    /// <summary>
    ///     Asserts the rule carries exactly the stricter-than-written warning naming
    ///     <paramref name="operandReference" /> — one allow-list operand of a <c>MustOnly</c> verb that matched
    ///     nothing, so what it was meant to allow is reported as violations (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Compared as <see cref="ShouldHaveWarnedPartlyInertOnOperands" /> compares its warnings: the same kind,
    ///     keyed on the same operand, with its own message. Says nothing about status, for
    ///     <see cref="ShouldHaveWarnedPartlyInertOnOperand" />'s reason.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedStricterOnOperand(this RuleResult result, string operandReference)
    {
        return result.ShouldHaveWarnedPerPart(
            [operandReference], CheckWarningKind.DeadOperand, StricterMessage);
    }

    /// <summary>
    ///     Asserts the rule carries exactly the stricter-than-written warning naming
    ///     <paramref name="membershipReference" /> — one membership of <c>MustBelongTo</c>, or the reside pair's
    ///     glob or project name, that matched nothing, so each subject it was meant to hold is reported as a
    ///     violation (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Compared as <see cref="ShouldHaveWarnedPartlyInertOnOperands" /> compares its warnings. Says nothing
    ///     about status, for <see cref="ShouldHaveWarnedPartlyInertOnOperand" />'s reason.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedStricterOnMembership(this RuleResult result, string membershipReference)
    {
        return result.ShouldHaveWarnedPerPart(
            [membershipReference], CheckWarningKind.DeadOperand, MembershipMessage);
    }

    /// <summary>
    ///     Asserts the rule carries exactly the not-measured-as-written warning naming
    ///     <paramref name="amongReference" /> — one part of <c>MustHaveExactlyOneCounterpart</c>'s <c>among:</c>
    ///     that matched nothing (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     Compared as <see cref="ShouldHaveWarnedPartlyInertOnOperands" /> compares its warnings. Says nothing
    ///     about status, for <see cref="ShouldHaveWarnedPartlyInertOnOperand" />'s reason.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedAmongOperand(this RuleResult result, string amongReference)
    {
        return result.ShouldHaveWarnedPerPart([amongReference], CheckWarningKind.DeadOperand, AmongMessage);
    }

    // The per-part warnings (a dead target, allow-list, membership or among operand, a dead exemplar part) share
    // one comparison, keyed on the part each names, and differ only in their kind and their message.
    private static RuleResult ShouldHaveWarnedPerPart(
        this RuleResult result, string[] references, CheckWarningKind kind, Func<string, string> messageOf)
    {
        string report = Describe(result);
        RequireExpectation(references, report);

        IReadOnlyList<string> rendered = result.Warnings
            .Select(warning => $"{warning.Kind} {warning.Key ?? NothingPopulated}: {warning.Message}")
            .ToList();
        IReadOnlyList<string> expected = references
            .Select(reference => $"{kind} {reference}: {messageOf(reference)}")
            .ToList();

        rendered.ShouldBe(expected, customMessage: report);
        result.Warnings.ShouldAllBe(warning => !string.IsNullOrWhiteSpace(warning.Hint), report);

        return result;
    }

    // The pinned partly-inert wording, spelled here rather than read off the evaluator, so the assertion holds
    // the product to a string instead of to itself.
    private static string PartlyInertMessage(string operandReference)
    {
        return $"This rule is partly inert: its target selection operand \"{operandReference}\" matched no types.";
    }

    // The pinned allow-list wording, spelled here for PartlyInertMessage's reason.
    private static string StricterMessage(string operandReference)
    {
        return $"This rule is stricter than written: its allow-list operand \"{operandReference}\" matched no types.";
    }

    // The pinned membership wording, spelled here for PartlyInertMessage's reason.
    private static string MembershipMessage(string membershipReference)
    {
        return $"This rule is stricter than written: its membership \"{membershipReference}\" matched no types.";
    }

    // The pinned among wording, spelled here for PartlyInertMessage's reason.
    private static string AmongMessage(string amongReference)
    {
        return $"This rule is not measured as written: its among operand \"{amongReference}\" matched no types.";
    }


    /// <summary>
    ///     Asserts the rule carries exactly one dead-exemplar warning per entry of <paramref name="references" />,
    ///     in that order, and no warning of any other kind: each part of the exemplar that matched no type,
    ///     named in the message and carried as <see cref="CheckWarning.Key" />, with a cure beside it.
    /// </summary>
    /// <remarks>
    ///     Says nothing about status, and deliberately: the exemplar never decides it, which is the claim the
    ///     callers assert beside this. Compared as one string per warning, for
    ///     <see cref="ShouldHaveWarnedPartlyInertOnOperands" />'s reason.
    /// </remarks>
    internal static RuleResult ShouldHaveWarnedDeadExemplars(this RuleResult result, params string[] references)
    {
        return result.ShouldHaveWarnedPerPart(references, CheckWarningKind.DeadExemplar, DeadExemplarMessage);
    }

    /// <summary>
    ///     Asserts the rule's exemplar resolved to exactly the types named <paramref name="fullNames" />, in that
    ///     order; with none given, that it resolved to nothing.
    /// </summary>
    internal static RuleResult ShouldHaveResolvedExemplar(this RuleResult result, params string[] fullNames)
    {
        result.Exemplar
            .Select(type => type.FullName)
            .ShouldBe(fullNames, customMessage: Describe(result));

        return result;
    }

    // The pinned dead-exemplar wording, spelled here for PartlyInertMessage's reason.
    private static string DeadExemplarMessage(string reference)
    {
        return $"This rule's exemplar \"{reference}\" matched no solution-declared types.";
    }

    /// <summary>
    ///     Asserts the rule passed carrying exactly one warning of <paramref name="kind" />, saying
    ///     <paramref name="message" /> about <paramref name="file" /> — the shape a scope tripwire that
    ///     fired reports in, with the path carried structurally as well as in the prose.
    /// </summary>
    internal static RuleResult ShouldHaveWarnedOnce(
        this RuleResult result, CheckWarningKind kind, string message, string file)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Passed, report);
        result.Violations.ShouldBeEmpty(report);
        CheckWarning warning = result.Warnings.ShouldHaveSingleItem(report);

        warning.ShouldSatisfyAllConditions(
            () => warning.Kind.ShouldBe(kind, report),
            () => warning.Message.ShouldBe(message, report),
            () => warning.File.ShouldBe(file, report));

        return result;
    }

    /// <summary>
    ///     Asserts the rule carries exactly one orphaned-baseline warning per entry of
    ///     <paramref name="orphanIds" />, in that order, each naming its section in
    ///     <see cref="CheckWarning.Key" />; with none given, that it carries no such warning at all.
    /// </summary>
    /// <remarks>
    ///     Other kinds of warning are left alone: a rule with a dead target pattern carries that warning too, and
    ///     whether it does is its own row's question. Says nothing about status, since the orphan warning fails
    ///     nothing.
    /// </remarks>
    internal static RuleResult ShouldHaveOrphanWarnings(this RuleResult result, params string[] orphanIds)
    {
        string report = Describe(result);

        result.Warnings
            .Where(warning => warning.Kind == CheckWarningKind.OrphanedBaseline)
            .Select(warning => warning.Key)
            .ShouldBe(orphanIds, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the run reached no verdict for the rule and said why: <see cref="RuleStatus.Skipped" />
    ///     carrying <paramref name="reason" />, with nothing red to show for it.
    /// </summary>
    /// <remarks>
    ///     Says nothing about the ratchet counts — a skip that zeroed them and a skip that did not are both
    ///     Skipped, so the rows whose subject is those counts read them in their own right.
    /// </remarks>
    internal static RuleResult ShouldHaveSkipped(this RuleResult result, string reason)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Skipped, report);

        result.ShouldSatisfyAllConditions(
            () => result.SkipReason.ShouldBe(reason, report),
            () => result.Violations.ShouldBeEmpty(report));

        return result;
    }

    /// <summary>
    ///     Asserts the ratchet grandfathered exactly <paramref name="count" /> violations (GRAMMAR §4.3).
    ///     Says nothing about status: a grandfathered rule that also has a new red is Failed and still
    ///     carries its blessed debt.
    /// </summary>
    internal static RuleResult ShouldHaveGrandfathered(this RuleResult result, int count)
    {
        result.Grandfathered.Count.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the ratchet found exactly <paramref name="count" /> grown entries — grandfathered pairs
    ///     carrying more sites than the baseline recorded for them (GRAMMAR §4.3).
    /// </summary>
    /// <remarks>
    ///     Says nothing about status either, for the same reason as
    ///     <see cref="ShouldHaveGrandfathered" /> — though growth is red by construction, the rows whose
    ///     subject is the red violation list read it in its own right.
    /// </remarks>
    internal static RuleResult ShouldHaveGrown(this RuleResult result, int count)
    {
        result.GrownBaselineEntries.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the ratchet found exactly <paramref name="count" /> stale entries — captured entries no
    ///     current violation matched, fixed debt awaiting acceptance (GRAMMAR §4.3).
    /// </summary>
    internal static RuleResult ShouldHaveStale(this RuleResult result, int count)
    {
        result.StaleBaselineEntries.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the ratchet found exactly <paramref name="count" /> shrunk entries — matched edge entries
    ///     observed under the site count they record (GRAMMAR §4.3).
    /// </summary>
    internal static RuleResult ShouldHaveShrunk(this RuleResult result, int count)
    {
        result.ShrunkBaselineEntries.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the ratchet found exactly <paramref name="count" /> uncounted entries — matched edge
    ///     entries recording no site count, grandfathered at pair grain (GRAMMAR §4.3).
    /// </summary>
    internal static RuleResult ShouldHaveUncounted(this RuleResult result, int count)
    {
        result.UncountedBaselineEntries.ShouldBe(count, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the violations key exactly <paramref name="identities" /> as baseline entries, in report order,
    ///     compared on identity as baseline matching compares them.
    /// </summary>
    internal static RuleResult ShouldHaveBaselineIdentities(this RuleResult result, BaselineEntry[] identities)
    {
        string report = Describe(result);
        string[] expected = identities.Select(IdentityOf).ToArray();
        RequireExpectation(expected, report);
        string[] actual = result.Violations
            .Select(violation => IdentityOf(violation.BaselineIdentity()))
            .ToArray();

        actual.ShouldBe(expected, customMessage: report);
        return result;
    }

    /// <summary>Asserts the ratchet grandfathered exactly <paramref name="entries" />, in report order.</summary>
    internal static RuleResult ShouldHaveGrandfatheredEntries(this RuleResult result, BaselineEntry[] entries)
    {
        string report = Describe(result);
        string[] expected = entries.Select(IdentityOf).ToArray();
        RequireExpectation(expected, report);
        string[] actual = result.GrandfatheredEntries
            .Select(IdentityOf)
            .ToArray();

        actual.ShouldBe(expected, customMessage: report);
        return result;
    }

    // An entry's identity as one line, or the placeholder for a violation no baseline can hold.
    private static string IdentityOf(BaselineEntry? entry)
    {
        return entry?.SymbolForm ?? NothingPopulated;
    }

    /// <summary>
    ///     Asserts the rule failed. Says nothing about which violations did it — for the rows whose subject is
    ///     the violation list itself, which then read it in its own right.
    /// </summary>
    internal static RuleResult ShouldHaveFailed(this RuleResult result)
    {
        result.Status.ShouldBe(RuleStatus.Failed, Describe(result));
        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on an edge violation of <paramref name="kind" />, running from
    ///     <paramref name="source" /> to <paramref name="target" /> and carrying at least one site.
    /// </summary>
    /// <remarks>
    ///     One method rather than one per verb, because the kind is often what is under test — a verb that
    ///     regresses emits a <see cref="ViolationKind.Reference" /> where a <see cref="ViolationKind.Catch" />
    ///     belongs, so it belongs in the argument list. Which slot a kind fills is the same mapping
    ///     <see cref="Violation.BaselineIdentity" /> reads, so no second copy of it lives here.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithSingleEdge(
        this RuleResult result, ViolationKind kind, string source, string target)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(kind, report),
            () => violation.Source!.FullName.ShouldBe(source, report),
            () => violation.Target!.FullName.ShouldBe(target, report),
            () => violation.Sites.ShouldNotBeEmpty(report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one edge violation of <paramref name="kind" />, running from
    ///     <paramref name="source" /> to <paramref name="target" /> and evidenced by <paramref name="sites" />,
    ///     each rendered <c>file:line</c> and in order (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     <see cref="ShouldHaveFailedWithSingleEdge" /> states the same shape while saying only that the
    ///     violation carries some site, which is right wherever the evidence is not the claim. This one is for
    ///     the rows where it is: which lines a violation cites is the whole of what parts one declarer's copy of
    ///     a shared name from another's, so a superset has to red here rather than pass as non-empty. Order is
    ///     asserted for the reason <see cref="RenderedSites" /> gives. The sites claim is asserted after the
    ///     shape group rather than inside it, so a row wrong in both reports the shape first.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithEdgeAtSites(
        this RuleResult result, ViolationKind kind, string source, string target, string[] sites)
    {
        Violation violation = result.ShouldHaveFailedWithSingleEdge(kind, source, target);
        RenderedSites(violation)
            .ShouldBe(sites, Describe(result));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one member-use violation, from <paramref name="source" /> on
    ///     <paramref name="member" /> (rendered <c>DeclaringType.Name</c>) and evidenced by
    ///     <paramref name="sites" />, each rendered <c>file:line</c> and in order (GRAMMAR §4.1, §4.5).
    /// </summary>
    /// <remarks>
    ///     The member-use twin of <see cref="ShouldHaveFailedWithEdgeAtSites" />, which cannot serve here because
    ///     <see cref="ShouldHaveFailedWithSingleEdge" /> dereferences <see cref="Violation.Target" />, and a
    ///     member-use violation leaves it null and names <see cref="Violation.Member" /> instead. The sites claim
    ///     is asserted after the shape group, for the reason that helper gives.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithMemberUseAtSites(
        this RuleResult result, string source, string member, string[] sites)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(ViolationKind.MemberUse, report),
            () => violation.Source!.FullName.ShouldBe(source, report),
            () => $"{violation.Member!.ContainingType.FullName}.{violation.Member.Name}".ShouldBe(member, report));
        RenderedSites(violation)
            .ShouldBe(sites, report);

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the edge violations of <paramref name="kind" /> that
    ///     <paramref name="edges" /> names, each rendered <c>Source -&gt; Target</c>. In any order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The set is exhaustive for the kind and silent about every other kind, which is what lets one
    ///         row state a desugared rule's whole inbound edge set without spelling its shape violations too.
    ///     </para>
    ///     <para>
    ///         Unordered, because <see cref="Describe(RuleResult)" /> already prints every violation in report
    ///         order: an ordered comparison would add nothing to the message and would report every later
    ///         index as wrong when one early element is missing. Report order is a product behaviour pinned in
    ///         its own right by <c>CheckerSemanticsTests.ViolationOrder_IsOrdinalBySourceThenTarget</c>, over a
    ///         kind-agnostic sort, so nothing goes uncovered by this tolerance.
    ///     </para>
    ///     <para>
    ///         Takes an array rather than <c>params</c>, so the brackets carry the exhaustiveness at the call
    ///         site: under <c>params</c> a one-edge assertion is written as a bare string, which reads as
    ///         naming one edge among however many the rule found. <c>["A -&gt; B"]</c> reads as the whole set,
    ///         which is what it claims.
    ///     </para>
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithEdges(
        this RuleResult result, ViolationKind kind, string[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Rendered(result, kind)
            .ShouldBe(edges, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the edge violations of <paramref name="kind" /> that
    ///     <paramref name="edges" /> names, each beside the sites it cites — the edge rendered
    ///     <c>Source -&gt; Target</c>, its sites <c>file:line</c>. Exhaustive for the kind, edges in any order,
    ///     each edge's sites in order (GRAMMAR §4.1).
    /// </summary>
    /// <remarks>
    ///     The plural of <see cref="ShouldHaveFailedWithEdgeAtSites" />, for a verb that reaches several edges
    ///     at once and has to cite the owning copy's lines at every one of them. Edge and sites are asserted
    ///     together, as <see cref="ShouldHaveFailedWithDetailedEdges" /> asserts edge and circle together: an
    ///     edge set with the wrong lines beside it is a different finding, and splitting the two would let a red
    ///     name half of it. Each violation is compared as one string for the same reason — a site list on its
    ///     own leaves the reader to work out which edge carried it.
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithEdgesAtSites(
        this RuleResult result, ViolationKind kind, (string Edge, string[] Sites)[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges.Select(edge => edge.Edge).ToArray(), report);
        result.Status.ShouldBe(RuleStatus.Failed, report);

        IReadOnlyList<string> rendered = result.Violations.Where(violation => violation.Kind == kind)
            .Select(violation => $"{Edge(violation) ?? NothingPopulated} @ {Joined(RenderedSites(violation))}")
            .ToList();
        IReadOnlyList<string> expected = edges
            .Select(edge => $"{edge.Edge} @ {Joined(edge.Sites)}")
            .ToList();
        rendered.ShouldBe(expected, ignoreOrder: true, customMessage: report);

        return result;
    }

    // Both halves of the pairing above are rendered through one spelling, so an expectation and the report it
    // reds beside cannot drift apart over a separator.
    private static string Joined(IReadOnlyList<string> sites)
    {
        return string.Join(", ", sites);
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the edge violations of <paramref name="kind" /> that
    ///     <paramref name="edges" /> names, each paired with the detail that violation carries — the shape
    ///     the circular-references verb reports in, a reference violation carrying the circle its pair lies
    ///     on (GRAMMAR §5.3). Exhaustive for the kind, in any order, as
    ///     <see cref="ShouldHaveFailedWithEdges" /> is.
    /// </summary>
    /// <remarks>
    ///     Edge and detail are asserted together rather than in two rows, because the claim is that this
    ///     pair lies on that circle: an edge set with the wrong circle beside it is a different finding, and
    ///     splitting the two would let a red name only half of it.
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithDetailedEdges(
        this RuleResult result, ViolationKind kind, (string Edge, string Detail)[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges.Select(edge => edge.Edge).ToArray(), report);
        result.Status.ShouldBe(RuleStatus.Failed, report);

        IReadOnlyList<(string Edge, string Detail)> rendered = result.Violations
            .Where(violation => violation.Kind == kind)
            .Select(violation => (Edge(violation) ?? NothingPopulated, violation.Detail ?? NothingPopulated))
            .ToList();
        rendered.ShouldBe(edges, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the shape violations <paramref name="subjects" /> names, each
    ///     rendered as the offending type's FullName (GRAMMAR §4.3). In any order, for the reason given on
    ///     <see cref="ShouldHaveFailedWithEdges" />.
    /// </summary>
    internal static RuleResult ShouldHaveFailedWithSubjects(this RuleResult result, string[] subjects)
    {
        string report = Describe(result);
        RequireExpectation(subjects, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Rendered(result, ViolationKind.Shape)
            .ShouldBe(subjects, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one shape violation, naming <paramref name="subject" /> and
    ///     evidenced by <paramref name="sites" /> — the sites the verb points at, each rendered
    ///     <c>file:line</c> (GRAMMAR §4.3); for most shape verbs those are the offending type's own
    ///     declarations, while the correspondence verb's ambiguous arm cites the colliding counterparts.
    /// </summary>
    /// <remarks>
    ///     A shape violation has no edge to cite, so its sites are the whole of what an agent can jump to,
    ///     and the two facts are asserted together because a subject without its evidence is half the
    ///     claim. Exactly one violation of any kind, which is stricter than
    ///     <see cref="ShouldHaveFailedWithSubjects" /> over a single-element set: nothing rides along
    ///     unnamed.
    /// </remarks>
    internal static Violation ShouldHaveFailedWithSubjectAtSites(
        this RuleResult result, string subject, string[] sites)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(ViolationKind.Shape, report),
            () => violation.Subject!.FullName.ShouldBe(subject, report),
            () => RenderedSites(violation).ShouldBe(sites, report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly the project violations <paramref name="projects" /> names,
    ///     each rendered as the offending project's name — or, for a per-item violation, as
    ///     <c>{project} -&gt; {item}</c>, the item a package, a framework, or a property at its framework
    ///     (<c>Nullable@net8.0</c>) (GRAMMAR §4.10). In any order, for the reason given on
    ///     <see cref="ShouldHaveFailedWithEdges" />.
    /// </summary>
    internal static RuleResult ShouldHaveFailedWithProjectSubjects(this RuleResult result, string[] projects)
    {
        string report = Describe(result);
        RequireExpectation(projects, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Rendered(result, ViolationKind.ProjectShape)
            .ShouldBe(projects, ignoreOrder: true, customMessage: report);

        return result;
    }

    /// <summary>
    ///     The project twin of <see cref="ShouldHaveFailedWithSubjectAtSites" />: exactly one project
    ///     violation, naming <paramref name="project" /> and evidenced by <paramref name="sites" /> — the
    ///     <c>file:line</c> that declared the fact the verb read, which is regularly a props file above the
    ///     project and is empty where nothing declared it (GRAMMAR §4.10). <paramref name="project" /> is the
    ///     offending project's name, or <c>{project} -&gt; {item}</c> for a per-item violation, as
    ///     <see cref="ShouldHaveFailedWithProjectSubjects" /> reads it.
    /// </summary>
    internal static Violation ShouldHaveFailedWithProjectAtSites(
        this RuleResult result, string project, string[] sites)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(ViolationKind.ProjectShape, report),
            () => Edge(violation).ShouldBe(project, report),
            () => RenderedSites(violation).ShouldBe(sites, report));

        return violation;
    }

    /// <summary>
    ///     The member twin of <see cref="ShouldHaveFailedWithSubjectAtSites" />: exactly one member-shape
    ///     violation, naming <paramref name="symbolId" /> — the member's own DocId (GRAMMAR §4.6) — and
    ///     evidenced by <paramref name="sites" />.
    /// </summary>
    internal static Violation ShouldHaveFailedWithMemberAtSites(
        this RuleResult result, string symbolId, string[] sites)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(ViolationKind.MemberShape, report),
            () => violation.SubjectMember!.SymbolId.ShouldBe(symbolId, report),
            () => RenderedSites(violation).ShouldBe(sites, report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed carrying every one of <paramref name="edges" /> as a violation of
    ///     <paramref name="kind" /> — and says nothing about what else it carries, for the rows whose fixture
    ///     is a whole solution and whose claim is that one edge is in the report.
    /// </summary>
    /// <remarks>
    ///     Batched, so a row naming several edges reds on all the absent ones at once rather than on the
    ///     first — the same reason <see cref="ShouldHaveFailedWithDetailContaining" /> batches its fragments,
    ///     and why this one keeps <c>params</c> as that verb does.
    /// </remarks>
    internal static RuleResult ShouldHaveFailedWithEdgesIncluding(
        this RuleResult result, ViolationKind kind, params string[] edges)
    {
        string report = Describe(result);
        RequireExpectation(edges, report);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        IReadOnlyList<string> rendered = Rendered(result, kind);

        Action[] checks = edges
            .Select<string, Action>(edge => () => rendered.ShouldContain(edge, report))
            .ToArray();
        result.ShouldSatisfyAllConditions(checks);

        return result;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one violation of <paramref name="kind" /> whose detail is
    ///     <paramref name="detail" /> — the shape of an empty subject or a rule error, which carry free
    ///     text and no edge (GRAMMAR §4.1, §5.1).
    /// </summary>
    internal static Violation ShouldHaveFailedWithDetail(
        this RuleResult result, ViolationKind kind, string detail)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);

        violation.ShouldSatisfyAllConditions(
            () => violation.Kind.ShouldBe(kind, report),
            () => violation.Detail.ShouldBe(detail, report));

        return violation;
    }

    /// <summary>
    ///     Asserts the rule failed on exactly one violation of <paramref name="kind" /> whose detail
    ///     contains every one of <paramref name="fragments" /> — for a refusal whose text names a symbol
    ///     or an exception the test cannot spell in full.
    /// </summary>
    internal static Violation ShouldHaveFailedWithDetailContaining(
        this RuleResult result, ViolationKind kind, params string[] fragments)
    {
        string report = Describe(result);
        result.Status.ShouldBe(RuleStatus.Failed, report);
        Violation violation = result.Violations.ShouldHaveSingleItem(report);
        violation.Kind.ShouldBe(kind, report);
        string detail = violation.Detail.ShouldNotBeNull(report);

        Action[] checks = fragments
            .Select<string, Action>(fragment => () => detail.ShouldContain(fragment, customMessage: report))
            .ToArray();
        violation.ShouldSatisfyAllConditions(checks);

        return violation;
    }

    /// <summary>
    ///     The violations of one <paramref name="kind" />, each rendered the way the failure text renders it,
    ///     in report order.
    /// </summary>
    /// <remarks>
    ///     Renders through <see cref="Edge" /> rather than dereferencing the slots a kind is expected to fill,
    ///     for the reason <see cref="Edge" /> exists: a violation whose slots are not the ones the caller
    ///     expected has to red by mismatch, naming what it does carry, rather than by
    ///     <see cref="NullReferenceException" />. For every kind these assertions take, this is the same
    ///     string <c>Checker</c>'s readers project.
    /// </remarks>
    private static IReadOnlyList<string> Rendered(RuleResult result, ViolationKind kind)
    {
        return result.Violations.Where(violation => violation.Kind == kind)
            .Select(violation => Edge(violation) ?? NothingPopulated)
            .ToList();
    }

    /// <summary>
    ///     A violation's sites, each rendered <c>file:line</c>, in the order the violation carries them —
    ///     the same rendering <see cref="Describe(Violation)" /> puts in the failure text, so an expectation
    ///     and the report it reds beside are spelled alike.
    /// </summary>
    /// <remarks>
    ///     Order is asserted rather than tolerated: site order is the extractor's declaration order, pinned
    ///     in its own right, so a shuffled list is a finding and not noise.
    /// </remarks>
    private static IReadOnlyList<string> RenderedSites(Violation violation)
    {
        return violation.Sites.Select(site => site.ToString())
            .ToList();
    }

    /// <summary>Reds when the caller expected nothing, which no collection claim here could red on.</summary>
    /// <remarks>
    ///     An empty expectation is a silent total pass: a rule that failed on some <em>other</em> kind
    ///     satisfies the status assertion and then matches empty against empty. The rows that mean it —
    ///     "failed, but not on this kind" — have <see cref="ShouldHaveFailed" /> to say so.
    /// </remarks>
    private static void RequireExpectation(string[] expected, string report)
    {
        if (expected.Length > 0) return;

        throw new ShouldAssertException(
            "the expected set is empty, which cannot red: a rule that failed on a different kind would "
            + "satisfy it. Assert ShouldHaveFailed() if the status is the whole claim." + Environment.NewLine
            + report);
    }

    /// <summary>
    ///     The whole result as failure text: what the rule reported, the subject it swept, every violation
    ///     and warning in report order, the exemplar it resolved, and the ratchet counts.
    /// </summary>
    /// <remarks>
    ///     The subject line is carried unconditionally, including its zero — unlike the report, which stays
    ///     silent when nothing is generated. A red raises the question "what was this rule even looking at",
    ///     and "0 types" is the answer that ends the search fastest.
    /// </remarks>
    private static string Describe(RuleResult result)
    {
        var lines = new List<string> { $"Rule '{result.Rule.Id}' ({result.Rule.Posture}) reported {result.Status}." };

        if (result.SkipReason is not null)
            lines.Add($"  skipped: {result.SkipReason}");

        lines.Add($"  subject: {result.SubjectTypes} types, {result.SubjectGeneratedTypes} generated");
        lines.Add($"  violations ({result.Violations.Count}):");
        lines.AddRange(result.Violations.Select(Describe));
        List<string> exemplar = result.Exemplar
            .Select(type => type.FullName)
            .ToList();
        lines.Add($"  exemplar: {(exemplar.Count == 0 ? "(none resolved)" : Joined(exemplar))}");
        lines.Add($"  warnings ({result.Warnings.Count}):");
        lines.AddRange(result.Warnings.Select(warning => $"    {warning.Kind}: {warning.Message}{Cure(warning.Hint)}"));
        lines.Add($"  ratchet: {result.Grandfathered.Count} grandfathered, {result.StaleBaselineEntries} stale, "
                  + $"{result.GrownBaselineEntries} grown, {result.ShrunkBaselineEntries} shrunk, "
                  + $"{result.UncountedBaselineEntries} uncounted, captured: {result.BaselineCaptured}");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>One violation as <c>Kind Source -&gt; Target @ file:line</c>, whichever slots it fills.</summary>
    /// <remarks>
    ///     Reads the slots rather than switching on <see cref="Violation.Kind" />: this renders the
    ///     failure text for assertions that are themselves about a wrong kind, so it has to stay true
    ///     for a violation whose slots are not the ones the caller expected.
    /// </remarks>
    private static string Describe(Violation violation)
    {
        string what = violation.Detail ?? Edge(violation) ?? NothingPopulated;
        string sites = violation.Sites.Count == 0
            ? " (no sites)"
            : " @ " + Joined(RenderedSites(violation));

        return $"    {violation.Kind} {what}{sites}{Cure(violation.Hint)}";
    }

    // The cure beside the diagnosis wherever a subject renders one: an assertion that disagreed about the
    // hint is unreadable if the failure prints only the message both sides already agree on.
    private static string Cure(string? hint)
    {
        return hint is null ? string.Empty : $" [hint: {hint}]";
    }

    /// <summary>The edge a violation names, from whichever slots it fills, or null when it fills none.</summary>
    /// <remarks>
    ///     Renders the same <c>Source -&gt; Target</c> string as <c>Checker</c>'s private <c>Pair</c> and must
    ///     not be merged with it: that one dereferences the slots its kind promises, which is right for a
    ///     reader whose caller chose the kind, while this one tolerates every slot being absent, which is
    ///     right for text that has to survive a violation of the wrong kind.
    /// </remarks>
    private static string? Edge(Violation violation)
    {
        string? source = violation.Source?.FullName
                         ?? violation.Subject?.FullName
                         ?? violation.SubjectMember?.SymbolId
                         ?? violation.SubjectProject?.Name;
        string? target = violation.Target?.FullName
                         ?? violation.Member?.SymbolId
                         ?? violation.ProjectItemName;

        if (source is null) return null;

        return target is null ? source : $"{source} -> {target}";
    }
}
