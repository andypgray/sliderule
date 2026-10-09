using System.Text.RegularExpressions;
using Shouldly;
using Xunit;
using SlideRule.Cli.Mcp.Prompts;
using SlideRule.Rendering;
using SlideRule.Tests.DocHygiene;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.Mcp;

/// <summary>
///     Holds the <c>derive_spec</c> recipe's authoring reference to two shipped inventories: every public
///     <see cref="Constraint" />-returning verb on the fluent surface, and every public adjective that maps
///     a selection to a selection, must be named in the served prompt's prose, or carry a
///     <see cref="WithheldVerbs" /> or <see cref="WithheldAdjectives" /> entry saying why it is deliberately
///     not taught.
/// </summary>
/// <remarks>
///     <para>
///         Exists because the vocabulary and the recipe drifted apart silently — verbs shipped across two
///         releases with the recipe never learning them, and nothing red; later, three accessibility adjectives
///         reached the recipe only because they were added by hand. Name presence is the whole contract: how a
///         name is taught stays a prose judgment, so this gate checks reachability, never wording. Each withheld
///         registry is two-sided like the doc-hygiene exemptions: an entry for a name that no longer ships is
///         dead, and an entry for a name the prompt does teach is stale — both red. The nouns and the member
///         projections are not inventoried here.
///     </para>
///     <para>
///         The gate reads the served body outside the generated vocabulary table. Inside the table every name
///         appears by construction, so a gate that read it would pass vacuously; the table's own completeness
///         is <see cref="Vocabulary.VocabularyCatalogTests" />' to hold.
///     </para>
/// </remarks>
public sealed class DeriveSpecVocabularyTests
{
    // A verb withheld from the recipe on purpose: name → the reason. Empty today — the recipe teaches
    // the whole shipped vocabulary and routes to GRAMMAR.md for depth.
    private static readonly IReadOnlyDictionary<string, string> WithheldVerbs =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // An adjective withheld from the recipe on purpose: name → the reason. Empty today, as for the verbs.
    private static readonly IReadOnlyDictionary<string, string> WithheldAdjectives =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void EveryShippedConstraintVerb_IsTaughtByTheRecipe_OrExplicitlyWithheld()
    {
        string body = Prose();

        List<string> missing = UntaughtNames(body, ShippedVocabulary.VerbNames, WithheldVerbs, VerbAppearsIn);

        missing.ShouldBeEmpty(
            "every shipped constraint verb must appear in derive-spec.md, or carry a WithheldVerbs entry saying why not");
    }

    [Fact]
    public void WithheldVerbs_EachStillShips()
    {
        IReadOnlyCollection<string> shipped = ShippedVocabulary.VerbNames;

        List<string> dead = WithheldVerbs.Keys
            .Where(name => !shipped.Contains(name, StringComparer.Ordinal))
            .ToList();

        dead.ShouldBeEmpty("a WithheldVerbs entry for a verb that no longer ships is dead — delete it");
    }

    [Fact]
    public void WithheldVerbs_EachStaysOutOfTheRecipe()
    {
        string body = Prose();

        List<string> stale = WithheldVerbs.Keys
            .Where(name => VerbAppearsIn(body, name))
            .ToList();

        stale.ShouldBeEmpty(
            "a WithheldVerbs entry for a verb the recipe teaches is stale — delete it so the verb is gated again");
    }

    [Fact]
    public void TheVerbSweep_SeesTheVocabulary()
    {
        // Blind-scanner guard: if the sweep's filter rots, the reachability fact above would pass
        // vacuously over an empty verb list rather than red.
        ShippedVocabulary.VerbNames
            .Count.ShouldBeGreaterThan(30);
    }

    [Fact]
    public void TheVerbGate_NamesTheVerbTheRecipeLost()
    {
        // Negative control: with every mention of one verb cut, the gate must red on that verb and on
        // nothing else, so that its failure message names the verb the recipe lost.
        string body = Prose()
            .Replace("MustNotReferenceEachOther", string.Empty, StringComparison.Ordinal);

        List<string> missing = UntaughtNames(body, ShippedVocabulary.VerbNames, WithheldVerbs, VerbAppearsIn);

        missing.ShouldBe(["MustNotReferenceEachOther"]);
    }

    [Fact]
    public void EveryShippedAdjective_IsTaughtByTheRecipe_OrExplicitlyWithheld()
    {
        string body = Prose();

        List<string> missing = UntaughtNames(body, ShippedVocabulary.AdjectiveNames, WithheldAdjectives, AdjectiveAppearsIn);

        missing.ShouldBeEmpty(
            "every shipped adjective must appear in derive-spec.md in its dotted form, or carry a WithheldAdjectives entry saying why not");
    }

    [Fact]
    public void WithheldAdjectives_EachStillShips()
    {
        IReadOnlyCollection<string> shipped = ShippedVocabulary.AdjectiveNames;

        List<string> dead = WithheldAdjectives.Keys
            .Where(name => !shipped.Contains(name, StringComparer.Ordinal))
            .ToList();

        dead.ShouldBeEmpty("a WithheldAdjectives entry for an adjective that no longer ships is dead — delete it");
    }

    [Fact]
    public void WithheldAdjectives_EachStaysOutOfTheRecipe()
    {
        string body = Prose();

        List<string> stale = WithheldAdjectives.Keys
            .Where(name => AdjectiveAppearsIn(body, name))
            .ToList();

        stale.ShouldBeEmpty(
            "a WithheldAdjectives entry for an adjective the recipe teaches is stale — delete it so the adjective is gated again");
    }

    [Fact]
    public void TheAdjectiveSweep_SeesEveryShape()
    {
        // Blind-scanner guard, one probe per shape the sweep must recognise: an instance method on a
        // selection type (Returning), an extension over a generic receiver constrained to one
        // (ThatAreStatic), and the project stratum (Packable). The count stops a sweep that finds only
        // the probes from passing.
        IReadOnlyCollection<string> adjectives = ShippedVocabulary.AdjectiveNames;

        adjectives.ShouldContain("Returning");
        adjectives.ShouldContain("ThatAreStatic");
        adjectives.ShouldContain("Packable");
        adjectives.Count.ShouldBeGreaterThan(15);
    }

    [Fact]
    public void TheAdjectiveGate_NamesTheAdjectiveTheRecipeLost()
    {
        // Negative control: the recipe calls .Packable once, and with that call cut the gate must red on
        // Packable and on nothing else.
        string body = Prose()
            .Replace(".Packable", string.Empty, StringComparison.Ordinal);

        List<string> missing = UntaughtNames(body, ShippedVocabulary.AdjectiveNames, WithheldAdjectives, AdjectiveAppearsIn);

        missing.ShouldBe(["Packable"]);
    }

    [Fact]
    public void BothGates_ReadTheProse_AndIgnoreTheTable()
    {
        // Negative control: the table names every verb and adjective by construction, so with both names cut
        // from the prose alone and the untouched table appended, each gate must red on its name and nothing
        // else. That is what shows the narrowing reads the prose rather than the table.
        string block = CommittedBlock();
        block.ShouldContain("MustNotReferenceEachOther");
        block.ShouldContain(".Packable");
        string prose = Prose()
            .Replace("MustNotReferenceEachOther", string.Empty, StringComparison.Ordinal)
            .Replace(".Packable", string.Empty, StringComparison.Ordinal);
        string body = OutsideTheTable(prose + block, block);

        List<string> missingVerbs = UntaughtNames(body, ShippedVocabulary.VerbNames, WithheldVerbs, VerbAppearsIn);
        List<string> missingAdjectives =
            UntaughtNames(body, ShippedVocabulary.AdjectiveNames, WithheldAdjectives, AdjectiveAppearsIn);

        missingVerbs.ShouldBe(["MustNotReferenceEachOther"]);
        missingAdjectives.ShouldBe(["Packable"]);
        TextNormalization.Occurrences(Prose(), block)
            .ShouldBe(0, "Prose() must cut the table from the served recipe, or every gate here passes vacuously");
    }

    // The served recipe outside its generated vocabulary table.
    private static string Prose()
    {
        return OutsideTheTable(ArchPrompts.DeriveSpec(), CommittedBlock());
    }

    // The committed recipe's table body, which VocabularyBlockSyncTests holds equal to the composed one.
    private static string CommittedBlock()
    {
        return ManagedBlock.ExtractBody(RepoRoot.ReadText(DocPaths.DeriveSpecPrompt))
               ?? throw new InvalidOperationException($"{DocPaths.DeriveSpecPrompt} carries no managed-block markers.");
    }

    // The served body with its one copy of the table cut out; an empty table cuts nothing.
    private static string OutsideTheTable(string served, string block)
    {
        if (block.Length == 0) return served;

        int occurrences = TextNormalization.Occurrences(served, block);
        if (occurrences != 1)
            throw new InvalidOperationException(
                $"The served recipe carries the committed vocabulary table {occurrences} times; expected exactly once.");

        int start = served.IndexOf(block, StringComparison.Ordinal);

        return served.Remove(start, block.Length);
    }

    // The inventory's names that the body neither teaches nor withholds, in inventory order.
    private static List<string> UntaughtNames(
        string body, IEnumerable<string> inventory, IReadOnlyDictionary<string, string> withheld,
        Func<string, string, bool> appearsIn)
    {
        return inventory
            .Where(name => !withheld.ContainsKey(name))
            .Where(name => !appearsIn(body, name))
            .ToList();
    }

    // Word-bounded so a verb whose name prefixes another (MustNotCatch, MustNotCatchUnfiltered) is
    // only satisfied by its own mention.
    private static bool VerbAppearsIn(string body, string verbName)
    {
        return Regex.IsMatch(body, $@"\b{Regex.Escape(verbName)}\b");
    }

    // Dotted, because Where, Matching, Named and Except are also English words the recipe's prose uses
    // on their own ("Where the repository manages…", "Matching is declaration-level"), and a bare word
    // match would be satisfied by a sentence with no call in it.
    private static bool AdjectiveAppearsIn(string body, string adjectiveName)
    {
        return Regex.IsMatch(body, $@"\.{Regex.Escape(adjectiveName)}\b");
    }
}
