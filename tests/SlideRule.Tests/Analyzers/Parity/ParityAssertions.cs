using Shouldly;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The Shouldly surface over one leg of the parity pin: what the build reported, what the whole-solution
///     check says it may report, what the same build reported document by document, and which claim failed.
/// </summary>
/// <remarks>
///     <para>
///         Every failure message prints both sets whole, as sorted key lines, with the difference first:
///         a parity red is never about one key, and the second question after "which key" is always "what
///         did each side say".
///     </para>
///     <para>
///         The two claims are deliberately separate methods rather than one with a flag. The subset claim
///         has no exception list and never will: a key the build reports that the whole-solution check does
///         not is the defect the whole design exists to prevent. The equality claim carries the
///         under-reports the design accepts, each named, and a named gap that covers nothing is as much a
///         red as an unexplained missing key — a gap nobody reaches is a gap that stopped being true.
///     </para>
///     <para>
///         Both claims are made of the batch report, and the per-document report is then held to that
///         report rather than to the expectation again: a document answers for the file it is, so the
///         whole-solution check has nothing to say about any one of them, and what a reader needs to know
///         is that the union over the documents is the build's own set and that each key landed in the
///         document that holds its site.
///     </para>
///     <para>
///         Deliberately not attributed <c>[ShouldlyMethods]</c>, for the reason
///         <c>RuleResultAssertions</c> records: leaving it off makes an inner failure name the read under
///         test rather than the caller's local.
///     </para>
/// </remarks>
internal static class ParityAssertions
{
    /// <summary>
    ///     Asserts every claim, on both legs and through both drivers, for one solution under one spec: the
    ///     build reported nothing the whole-solution check does not, everything it does bar
    ///     <paramref name="gaps" />, exactly the ratcheted rules it was entitled to name as unchecked,
    ///     nothing twice — and, document by document, the same set again with each key in the document that
    ///     holds its site.
    /// </summary>
    /// <param name="subject">The solution and spec under test.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    /// <param name="gaps">The under-reports this design accepts here, each named and keyed.</param>
    internal static async Task ShouldHoldOnBothLegsAsync(
        this ParitySubject subject, CancellationToken cancellationToken, params ParityGap[] gaps)
    {
        subject.Compilations.ShouldNotBeEmpty($"{subject.Name}: compilations to analyse");

        foreach (ParityLeg leg in Legs)
        {
            IReadOnlyList<ParityCompilation> compilations = subject.On(leg);
            ParityReport batch = await subject.ReportAsync(compilations, ParityDriver.Batch, cancellationToken);

            batch.ShouldReportEverythingBut(subject.Expectation, subject.Name, leg, gaps);
            batch.ShouldNameExactlyTheseUncheckedRules(
                subject.Expectation.UncheckedRules(compilations), subject.Name, leg);
            batch.ShouldHoldNoDuplicate(subject.Name, leg);

            ParityReport perDocument = await subject.ReportAsync(
                compilations, ParityDriver.PerDocument, cancellationToken);

            perDocument.ShouldEqualTheBatchLessToolState(batch, subject.Name, leg);
            perDocument.ShouldPlaceEveryKeyInItsOwnTree(subject.Name, leg);
        }
    }

    /// <summary>
    ///     Asserts the tier the build declares decides which action does the work and nothing else: over the
    ///     whole compilation, a project on the build tier reports what the same project on the live tier
    ///     reports, finding for finding and unchecked rule for unchecked rule.
    /// </summary>
    /// <remarks>
    ///     Held out of <see cref="ShouldHoldOnBothLegsAsync" /> and asked of a chosen few subjects, because
    ///     it doubles the analysis of every compilation and the claim is about the switch rather than about
    ///     any one solution: the in-memory corpus reaches every verb the table decides, and one bed states
    ///     it over a real project system.
    /// </remarks>
    /// <param name="subject">The solution and spec under test.</param>
    /// <param name="cancellationToken">The token the analysis runs under.</param>
    internal static async Task ShouldAgreeWithTheBuildTierInBatchAsync(
        this ParitySubject subject, CancellationToken cancellationToken)
    {
        subject.Compilations.ShouldNotBeEmpty($"{subject.Name}: compilations to analyse");

        foreach (ParityLeg leg in Legs)
        {
            IReadOnlyList<ParityCompilation> compilations = subject.On(leg);
            ParityReport onBuild = await subject.ReportAsync(
                compilations, ParityDriver.Batch, cancellationToken, "build");
            ParityReport onLive = await subject.ReportAsync(
                compilations, ParityDriver.Batch, cancellationToken, "live");

            onBuild.Strangers.ShouldBeEmpty($"{Header(subject.Name, leg)}: the build tier said more than it reports");
            onLive.Strangers.ShouldBeEmpty($"{Header(subject.Name, leg)}: the live tier said more than it reports");

            List<string> fromBuild = Describe(onBuild.Keys);
            List<string> fromLive = Describe(onLive.Keys);
            fromLive.ShouldBe(
                fromBuild,
                $"{Header(subject.Name, leg)}: the two tiers report different findings over one compilation"
                + Sets("the live tier reported", fromLive, "the build tier reported", fromBuild));

            List<string> namedByBuild = Pairs(onBuild.Unchecked);
            List<string> namedByLive = Pairs(onLive.Unchecked);
            namedByLive.ShouldBe(
                namedByBuild,
                $"{Header(subject.Name, leg)}: the two tiers name different ratcheted rules as unchecked"
                + Sets("the live tier named", namedByLive, "the build tier named", namedByBuild));
        }
    }

    /// <summary>The two forms a compilation sees a sibling project in, in the order the pin runs them.</summary>
    internal static IReadOnlyList<ParityLeg> Legs { get; } =
        [ParityLeg.CompilationReference, ParityLeg.MetadataImage];

    /// <summary>
    ///     Asserts the build reported nothing the whole-solution check does not — the one claim with no
    ///     exception list.
    /// </summary>
    /// <param name="report">What the build tier said over this leg.</param>
    /// <param name="expectation">What the whole-solution check says.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    internal static void ShouldReportNothingTheSolutionDoesNot(
        this ParityReport report, ParityExpectation expectation, string subject, ParityLeg leg)
    {
        report.Strangers.ShouldBeEmpty($"{Header(subject, leg)}: the build tier said more than it reports");

        List<ParityKey> surplus = report.Keys
            .Except(expectation.Keys)
            .ToList();

        Describe(surplus).ShouldBeEmpty(
            $"{Header(subject, leg)}: warning(s) the build invented — no check over the whole solution gives "
            + $"them{Sets(report, expectation)}");
    }

    /// <summary>
    ///     Asserts the build reported everything the whole-solution check does, bar the named gaps, and that
    ///     each named gap is reached.
    /// </summary>
    /// <param name="report">What the build tier said over this leg.</param>
    /// <param name="expectation">What the whole-solution check says.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    /// <param name="gaps">The under-reports this design accepts here, each named and keyed.</param>
    internal static void ShouldReportEverythingBut(
        this ParityReport report, ParityExpectation expectation, string subject, ParityLeg leg,
        params ParityGap[] gaps)
    {
        report.ShouldReportNothingTheSolutionDoesNot(expectation, subject, leg);

        HashSet<ParityKey> reported = report.Keys.ToHashSet();
        List<ParityKey> missing = expectation.Keys
            .Where(key => !reported.Contains(key))
            .ToList();

        List<string> unexplained = missing
            .Where(key => !gaps.Any(gap => gap.Covers(key)))
            .Select(key => key.Describe())
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        unexplained.ShouldBeEmpty(
            $"{Header(subject, leg)}: warning(s) the build left out that no named difference "
            + $"explains{Sets(report, expectation)}");

        List<string> unreached = gaps
            .Where(gap => !missing.Any(gap.Covers))
            .Select(gap => gap.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        unreached.ShouldBeEmpty(
            $"{Header(subject, leg)}: named difference(s) nothing reached, so the bed no longer holds the "
            + $"case they were written for{Sets(report, expectation)}");
    }

    /// <summary>
    ///     Asserts the build named exactly <paramref name="expected" /> as ratcheted rules it could not
    ///     check, one line per compilation and rule.
    /// </summary>
    /// <param name="report">What the build tier said over this leg.</param>
    /// <param name="expected">The pairs a whole-solution check against an empty baseline gives.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    internal static void ShouldNameExactlyTheseUncheckedRules(
        this ParityReport report, IReadOnlyList<(string Compilation, string Rule)> expected, string subject,
        ParityLeg leg)
    {
        List<string> reported = Pairs(report.Unchecked);
        List<string> wanted = Pairs(expected);

        reported.ShouldBe(
            wanted,
            $"{Header(subject, leg)}: which ratcheted rules the build named as unchecked\n"
            + $"  expected:\n{Indent(wanted)}\n  reported:\n{Indent(reported)}");
    }

    /// <summary>
    ///     Asserts the build said each thing once: no compilation reported one finding twice, none named one
    ///     ratcheted rule unchecked twice, and none faulted.
    /// </summary>
    /// <remarks>
    ///     Per compilation rather than over the report, because a name two compilations of one csproj both
    ///     declare is reported by each of them and the union is what the equality claim compares — what
    ///     cannot happen is one compilation saying a thing twice, which is what a document answering for a
    ///     file another document owns would look like.
    /// </remarks>
    /// <param name="report">What the build tier said over this leg.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    internal static void ShouldHoldNoDuplicate(this ParityReport report, string subject, ParityLeg leg)
    {
        List<string> twice = report.Found
            .GroupBy(finding => $"{finding.Compilation}: {finding.Key.Describe()}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} (reported {group.Count()} times)")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        twice.ShouldBeEmpty($"{Header(subject, leg)}: finding(s) one compilation reported more than once");

        List<string> namedTwice = report.Unchecked
            .GroupBy(pair => $"{pair.Compilation}: {pair.Rule}", StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} (named {group.Count()} times)")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        namedTwice.ShouldBeEmpty(
            $"{Header(subject, leg)}: ratcheted rule(s) one compilation named unchecked more than once");

        List<string> faults = report.Strangers
            .Where(stranger => stranger.Contains("SR2093", StringComparison.Ordinal))
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
        faults.ShouldBeEmpty($"{Header(subject, leg)}: the analyzer faulted, so some of its findings are missing");
    }

    /// <summary>
    ///     Asserts the union over the documents is exactly what the whole compilation reported, and that the
    ///     documents were told none of the tool's own state.
    /// </summary>
    /// <remarks>
    ///     The three warnings about the tool rather than about code — no model file, an unchecked ratcheted
    ///     rule, a fault — carry no location and so are local to no document. Neither can a document be told
    ///     about a file it does not hold. What is left is the findings, and those must be the same set.
    /// </remarks>
    /// <param name="perDocument">What the documents said, taken one at a time.</param>
    /// <param name="batch">What the same compilations said asked whole.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    internal static void ShouldEqualTheBatchLessToolState(
        this ParityReport perDocument, ParityReport batch, string subject, ParityLeg leg)
    {
        perDocument.Strangers.ShouldBeEmpty(
            $"{Header(subject, leg)}: a document was told the tool's own state, which carries no location "
            + "and so can reach no document");
        perDocument.Unchecked.ShouldBeEmpty(
            $"{Header(subject, leg)}: a document named a ratcheted rule unchecked, which is the compilation's "
            + "to name");

        List<string> byDocument = Describe(perDocument.Keys);
        List<string> whole = Describe(batch.Keys);

        byDocument.ShouldBe(
            whole,
            $"{Header(subject, leg)}: the union over the documents is not what the whole compilation reported"
            + Sets("the documents reported", byDocument, "the whole compilation reported", whole));
    }

    /// <summary>
    ///     Asserts every key the documents reported names the document it was reported in — the claim that a
    ///     document answers for its own file and for no other.
    /// </summary>
    /// <param name="report">What the documents said, taken one at a time.</param>
    /// <param name="subject">What a failure calls the solution and spec under test.</param>
    /// <param name="leg">Which form the siblings arrived in.</param>
    internal static void ShouldPlaceEveryKeyInItsOwnTree(this ParityReport report, string subject, ParityLeg leg)
    {
        List<string> elsewhere = report.Placed
            .Where(placed => !string.Equals(placed.Key.File, placed.TreePath, StringComparison.Ordinal))
            .Select(placed => $"{placed.Compilation}: {placed.TreePath} reported {placed.Key.Describe()}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();

        elsewhere.ShouldBeEmpty(
            $"{Header(subject, leg)}: warning(s) a document reported about a file it does not hold");
    }

    private static string Header(string subject, ParityLeg leg)
    {
        return $"{subject} ({Name(leg)})";
    }

    /// <summary>The words a failure message names <paramref name="leg" /> by.</summary>
    internal static string Name(ParityLeg leg)
    {
        return leg == ParityLeg.MetadataImage ? "siblings as metadata images" : "siblings as compilations";
    }

    private static List<string> Describe(IEnumerable<ParityKey> keys)
    {
        return keys.Select(key => key.Describe())
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
    }

    private static List<string> Pairs(IEnumerable<(string Compilation, string Rule)> pairs)
    {
        return pairs.Select(pair => $"{pair.Compilation}: {pair.Rule}")
            .OrderBy(line => line, StringComparer.Ordinal)
            .ToList();
    }

    private static string Sets(ParityReport report, ParityExpectation expectation)
    {
        return Sets(
            "the build reported", Describe(report.Keys), "the whole-solution check gives",
            Describe(expectation.Keys));
    }

    // Both sides whole, under the words for what each one is: a red about two sets is read by comparing
    // them, and every claim here is about two sets.
    private static string Sets(
        string reported, IReadOnlyList<string> reportedLines, string against, IReadOnlyList<string> againstLines)
    {
        return $".\n  {reported}:\n{Indent(reportedLines)}\n  {against}:\n{Indent(againstLines)}";
    }

    private static string Indent(IReadOnlyList<string> lines)
    {
        return lines.Count == 0 ? "    (nothing)" : string.Join("\n", lines.Select(line => $"    {line}"));
    }
}
