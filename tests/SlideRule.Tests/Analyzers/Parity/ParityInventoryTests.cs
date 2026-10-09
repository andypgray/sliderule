using Shouldly;
using Xunit;
using SlideRule.Checking;
using SlideRule.Hosting;

namespace SlideRule.Tests.Analyzers.Parity;

/// <summary>
///     The claim that the pin's corpus reaches every verb the build tier is allowed to report: each
///     constraint class the decidability table decides produced at least one diagnostic, on each leg.
/// </summary>
/// <remarks>
///     <para>
///         A row the classifier lets through and nothing ever reds is a row nobody has measured. The
///         table is a judgement about what one compilation can settle, and this is what holds each of its
///         positive answers to a build that really made it — on both reference forms, because a verb that
///         reds only where the sibling arrives as source symbols is a verb that stops working in a real
///         build.
///     </para>
///     <para>
///         The fact runs the corpus itself rather than collecting across the other facts. xunit gives
///         each fact its own life and no order, so an accumulator would answer whatever the run happened
///         to reach; and the corpus it runs is the in-memory half, which is where the remainder lives —
///         the beds can only add classes this set already holds, so sweeping them too would weaken the
///         claim rather than strengthen it.
///     </para>
///     <para>
///         Each subject's own fact runs it through <c>ShouldHoldOnBothLegsAsync</c>, which since the live
///         tier answers per document holds both drivers: the whole compilation at once, and then document
///         by document against what that said. The second fact here is the other half of the same switch —
///         that a project declaring the build tier reports what the same project on the live tier reports
///         — asked of the whole corpus at once because the claim is about the switch rather than about any
///         one solution.
///     </para>
/// </remarks>
public sealed class ParityInventoryTests
{
    [Fact]
    public async Task EveryDecidableVerb_ProducedADiagnosticSomewhereInTheCorpus_OnBothLegs()
    {
        // Arrange
        IReadOnlyList<ParitySubject> corpus = SiblingCorpus.ForInventory();
        List<string> decidable = DecidableConstraints();
        Dictionary<ParityLeg, HashSet<string>> reached =
            ParityAssertions.Legs.ToDictionary(leg => leg, _ => new HashSet<string>(StringComparer.Ordinal));

        // Act
        foreach (ParitySubject subject in corpus)
        {
            Dictionary<string, string> verbs = VerbsByRule(subject);
            foreach (ParityLeg leg in ParityAssertions.Legs)
            {
                ParityReport report = await subject.ReportAsync(subject.On(leg), ParityDriver.Batch, Ct);
                IEnumerable<string> named = report.Keys
                    .Select(key => verbs.GetValueOrDefault(key.Rule))
                    .Where(verb => verb is not null)
                    .Select(verb => verb!);
                reached[leg].UnionWith(named);
            }
        }

        // Assert — the floor first, so a sweep that reached nothing says so rather than reporting nothing.
        decidable.Count.ShouldBeGreaterThanOrEqualTo(44, "constraint classes the table decides");
        foreach (ParityLeg leg in ParityAssertions.Legs)
        {
            List<string> unreached = decidable
                .Where(verb => !reached[leg].Contains(verb))
                .OrderBy(verb => verb, StringComparer.Ordinal)
                .ToList();
            unreached.ShouldBeEmpty(
                "Verb(s) the classifier decides that nothing in the corpus reported with " + ParityAssertions.Name(leg));
        }
    }

    [Fact]
    public async Task EveryCorpusSubject_ReportsTheSameOnTheBuildTierAsOnTheLive()
    {
        // The tier decides which action does the work and nothing else, so a project that declares the
        // build tier has to print the log the live tier prints over the same compilation — warning for
        // warning and unchecked rule for unchecked rule, on both reference forms.
        IReadOnlyList<ParitySubject> corpus = SiblingCorpus.ForInventory();

        corpus.ShouldNotBeEmpty("subjects to run the two tiers over");

        foreach (ParitySubject subject in corpus) await subject.ShouldAgreeWithTheBuildTierInBatchAsync(Ct);
    }

    [Fact]
    public void TheRemainderSpec_IsWhatMakesTheInventoryWhole()
    {
        // The anti-vacuity half: take the remainder out and the sweep above has nothing to say about most
        // of the vocabulary, which is what makes the remainder a remainder rather than a second corpus.
        var withoutRemainder = new HashSet<string>(
            SiblingCorpus.ForInventoryWithoutRemainder()
                .SelectMany(subject => VerbsByRule(subject)
                    .Values),
            StringComparer.Ordinal);
        List<string> onlyTheRemainderReaches = DecidableConstraints()
            .Where(verb => !withoutRemainder.Contains(verb))
            .ToList();

        onlyTheRemainderReaches.Count.ShouldBeGreaterThan(
            30, "verbs no other in-memory subject states, which the remainder is there to state");
    }

    /// <summary>
    ///     Every constraint class the table answers decidable for, by name — the rows the sweep above
    ///     holds to a diagnostic.
    /// </summary>
    private static List<string> DecidableConstraints()
    {
        return OneProjectDecidability.ClassifiedTypes
            .Where(node => typeof(Constraint).IsAssignableFrom(node))
            .Where(OneProjectDecidability.Decides)
            .Select(node => node.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // Which verb each of a subject's rules states, keyed by rule ID: the diagnostics carry the rule and
    // the model carries the verb, and the sweep needs the two joined.
    private static Dictionary<string, string> VerbsByRule(ParitySubject subject)
    {
        var verbs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ArchRule rule in subject.Document.Rules)
            if (rule.Constraint is { } constraint)
                verbs[rule.Id] = constraint.GetType()
                    .Name;

        return verbs;
    }
}
