using Shouldly;
using SlideRule.Baselines;
using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Hosting;
using SlideRule.ModelFile;
using SlideRule.Prose;
using SlideRule.Tests.Checking;

namespace SlideRule.Tests.ModelFile;

/// <summary>
///     The Shouldly surface over a model file: that a model says the same thing after a trip through its
///     own text. The whole claim is three claims asserted together — the bytes are a fixed point, every
///     carried rule re-renders the sentence it was stored with, and the parsed rules reach the same verdict
///     as the live rules they came from — and the verbs that need no codebase assert the parts of it they
///     can reach.
/// </summary>
/// <remarks>
///     <para>
///         Together rather than as three verbs, because the useful claim is the conjunction. A file that
///         parses but checks differently is worse than one that refuses, and a file whose bytes round-trip
///         while its sentences drift is a file whose prose no longer describes its own rules; a caller
///         that could assert one of the three alone would eventually assert only that one.
///     </para>
///     <para>
///         The verdict is compared as the check verb's <c>--json</c> document rather than as a
///         <see cref="CheckReport" />, because that document is the product's own answer: two reports that
///         render alike are alike wherever it matters, and a slot that moved shows up as text a reader can
///         diff rather than as an object graph they cannot.
///     </para>
///     <para>
///         The live side is the report the caller already computed whenever nothing was omitted, because
///         then the carried rules <em>are</em> the model's rules and a third check would only cost time.
///         When something was omitted the carried subset is checked on its own, so the comparison is
///         between the same rule ids on both sides and never between a whole model and part of one.
///     </para>
///     <para>
///         Deliberately <em>not</em> attributed <c>[ShouldlyMethods]</c>, for the reason
///         <see cref="RuleResultAssertions" /> records. Left off, a red reads
///         <c>ModelDocumentComposer.Compose(parsed) should be …</c>, naming the claim under test;
///         attributed, it would name the caller's own local and say nothing about which of the three
///         claims broke.
///     </para>
/// </remarks>
internal static class ModelDocumentAssertions
{
    /// <summary>
    ///     Asserts that <paramref name="model" /> survives its own model file: composed, parsed and
    ///     composed again it reproduces its bytes; every carried rule re-renders its stored sentence; and
    ///     the rules the file carries reach the same verdict parsed as they do live, where
    ///     <paramref name="check" /> runs a rule list the way the caller's own run does.
    /// </summary>
    /// <param name="model">The built model to put through the file.</param>
    /// <param name="spec">The spec name the file records.</param>
    /// <param name="projects">The projects of the checked universe; see <see cref="ModelDocumentProject.ListOf" />.</param>
    /// <param name="check">
    ///     Runs a rule list with the caller's own codebase, baselines, diff, narrowing and partial-model
    ///     arguments — a delegate rather than those five parameters, so one verb serves every
    ///     <c>ArchChecker.Check</c> overload without restating any of them.
    /// </param>
    /// <param name="liveReport">
    ///     The report the caller already computed over the whole model, used as the live side when nothing
    ///     was omitted; null to have this compute it.
    /// </param>
    /// <returns>The document, for a caller that goes on to claim something about what it carries.</returns>
    internal static ModelDocument ShouldCheckIdentically(
        this ArchitectureModel model,
        string spec,
        IReadOnlyList<ModelDocumentProject> projects,
        Func<IReadOnlyList<ArchRule>, CheckReport> check,
        CheckReport? liveReport = null)
    {
        ModelDocument document = ModelDocument.From(model, spec, projects);
        ModelDocument parsed = document.ShouldRoundTrip();
        string report = Describe(document);

        CheckReport live = document.Omitted.Count == 0 && liveReport is not null
            ? liveReport
            : check(document.Rules);

        check(parsed.Rules)
            .JsonReport()
            .ShouldBe(live.JsonReport(), report);

        return document;
    }

    /// <summary>
    ///     Asserts <paramref name="model" /> survives its own model file against <paramref name="codebase" />
    ///     and carries something while doing it.
    /// </summary>
    /// <returns>The document, for a caller that goes on to claim something about what it carries.</returns>
    internal static ModelDocument ShouldSurviveItsFile(
        this ArchitectureModel model, string spec, CodebaseModel codebase, BaselineIndex baselines)
    {
        ModelDocument document = model.ShouldCheckIdentically(
            spec, ModelDocumentProject.ListOf(codebase),
            rules => ArchChecker.Check(rules, codebase, baselines, null));

        // A model whose every rule was omitted is a fixed point for free.
        document.Rules.ShouldNotBeEmpty(Describe(document));

        return document;
    }

    /// <summary>
    ///     Asserts the two halves that need no codebase: composing what was read reproduces the bytes, and
    ///     every carried rule with a constraint re-renders the sentence the file stored beside it.
    /// </summary>
    /// <returns>The document read back, whose rules are the parsed ones.</returns>
    internal static ModelDocument ShouldRoundTrip(this ModelDocument document)
    {
        string text = ModelDocumentComposer.Compose(document);
        string report = Describe(document);

        ModelDocument parsed = text.ShouldReadBack(report);

        ModelDocumentComposer.Compose(parsed)
            .ShouldBe(text, report);

        foreach (ArchRule rule in parsed.Rules)
        {
            if (rule.Constraint is not { } constraint) continue;

            SentenceRenderer.Sentence(constraint)
                .ShouldBe(rule.Sentence, $"rule '{rule.Id}'{Environment.NewLine}{report}");
        }

        return parsed;
    }

    /// <summary>Asserts <paramref name="text" /> reads back as a model file the reader does not refuse.</summary>
    /// <returns>The document read.</returns>
    internal static ModelDocument ShouldReadBack(this string text, string? customMessage = null)
    {
        ModelReadResult result = ModelDocumentReader.Read(text);
        result.Refusal?.Describe()
            .ShouldBeNull(customMessage);

        return result.Document.ShouldNotBeNull(customMessage);
    }

    /// <summary>
    ///     What the file carries and what it names but leaves behind, as failure text: the two lists a red
    ///     here is always read against, because every claim above is a claim about one of them.
    /// </summary>
    private static string Describe(ModelDocument document)
    {
        var lines = new List<string>
        {
            $"Spec '{document.Spec}': {document.Rules.Count} rule(s) carried, "
            + $"{document.Omitted.Count} omitted, {document.Layers.Count} layer(s) carried."
        };

        lines.Add("  carried:");
        lines.AddRange(document.Rules.Select(rule => $"    {rule.Id}"));
        lines.Add("  omitted:");
        lines.AddRange(document.Omitted.Select(entry => $"    {entry.Id}: {entry.Reason}"));

        return string.Join(Environment.NewLine, lines);
    }
}
