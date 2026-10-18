using SlideRule.Hosting;
using SlideRule.Model;
using SlideRule.Prose;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Formats one rule as the <c>explain</c> field dump: a <c>&lt;id&gt; (&lt;posture&gt;)</c> header, then each
///     field the rule carries, once, plus the posture payload.
/// </summary>
/// <remarks>
///     A scope posture's header carries the role, e.g. <c>(quarantine/containment)</c> or <c>(caution/tripwire)</c>.
///     The fields take check's lowercase-label style (<c>sentence:</c> / <c>because:</c> / <c>citation:</c> /
///     <c>fix:</c> / <c>exemplar:</c>). This is a data dump, not the voice templates: <c>dragons:</c> and
///     <c>from:</c> print verbatim, and <c>dragons-doc:</c> prints the linked path only (the spec stays the index).
///     Emitted line by line to match the check renderer.
/// </remarks>
internal static class ExplainFormatter
{
    public static IReadOnlyList<string> Lines(ArchRule rule)
    {
        var lines = new List<string> { Header(rule) };

        if (rule.Sentence.Length > 0) lines.Add($"  sentence: {rule.Sentence}");
        lines.Add($"  because: {rule.Because}");
        if (rule.Citation is { } citation) lines.Add($"  citation: {citation}");
        if (rule.Fix is { } fix) lines.Add($"  fix: {fix}");
        // As the spec names it: explaining a rule never extracts the solution, so there is no site to print.
        if (rule.Exemplar is { } exemplar) lines.Add($"  exemplar: {exemplar}");

        switch (rule.Posture)
        {
            case Posture.Migrate when rule.Migrate is { } migrate:
                lines.Add($"  from: {migrate.From}");
                lines.Add($"  policy: {migrate.Policy}");
                lines.Add($"  baseline: {migrate.BaselinePath}"); // never null post-build (GRAMMAR §4.4)
                break;
            // One arm for both scope postures: every line here already self-gates on presence, so a caution
            // — no boundary, no baseline — prints the scope and its dragons and nothing else.
            case Posture.Quarantine or Posture.Caution when rule.Scope is { } scope:
                lines.Add($"  scope: {scope.ScopeId}");
                if (scope.Surface.Count > 0) lines.Add($"  boundary: {ProseFormat.JoinInventory(scope.Surface)}");
                if (scope.BaselinePath is { } scopeBaseline) lines.Add($"  baseline: {scopeBaseline}");
                if (scope.Dragons is { } dragons) lines.Add($"  dragons: {dragons}");
                if (scope.DragonsDoc is { } dragonsDoc) lines.Add($"  dragons-doc: {dragonsDoc}");
                break;
        }

        return lines;
    }

    private static string Header(ArchRule rule)
    {
        // Keyed on the payload rather than the posture: only a scope's children carry one, so a posture
        // added later gets its role in the header with no edit here.
        string posture = rule.Posture.ToString().ToLowerInvariant();
        if (rule.Scope is { } scope) posture += "/" + scope.Role.ToString().ToLowerInvariant();

        return $"{rule.Id} ({posture})";
    }
}
