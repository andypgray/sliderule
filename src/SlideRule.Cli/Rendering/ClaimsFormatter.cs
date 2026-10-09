using SlideRule.Checking;
using SlideRule.Codebase;
using SlideRule.Internal;
using SlideRule.Rendering;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     Renders <c>explain --type</c> and <c>explain --path</c>: per type, a header and its declaring projects and
///     files, then each group of claimants with the trace that admitted the type, indented under it.
/// </summary>
/// <remarks>
///     The layout is rule <c>explain</c>'s: a header line, then two-space-indented <c>key: value</c> lines, so
///     the two forms of the verb read as one. A group with no claimant is left out, and a type nothing claims
///     says so in one line, above the rules whose except removed it.
/// </remarks>
internal static class ClaimsFormatter
{
    internal const string NothingClaims = "  claimed by: no layer, rule or scope";

    internal const string ExternalLine =
        "  claimed by: nothing; it is declared outside the solution, and every selection ranges over the types "
        + "the solution declares";

    public static IReadOnlyList<string> Lines(TypeClaims claims, string solutionDirectory)
    {
        TypeNode type = claims.Type;
        var lines = new List<string> { $"{type.FullName} ({KindWord(type)})" };
        if (type.IsExternal)
        {
            lines.Add(ExternalLine);
            return lines;
        }

        List<string> projects = type.Declarers.ToList();
        lines.Add($"  {Plurals.Noun(projects.Count, "project")}: {string.Join(", ", projects)}");

        List<string> files = type.DeclarationSites
            .Select(site => PathFormat.Relative(solutionDirectory, site.FilePath))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();
        if (files.Count > 0) lines.Add($"  {Plurals.Noun(files.Count, "file")}: {string.Join(", ", files)}");

        Group(lines, "layers", claims.Layers, false);
        Group(lines, "rules", claims.Rules, false);
        Group(lines, "scopes", claims.Scopes, false);
        if (!claims.ClaimedByAnything) lines.Add(NothingClaims);
        Group(lines, "excepted by", claims.ExceptedBy, true);
        Group(lines, "not evaluated", claims.Unevaluated, true);

        return lines;
    }

    // The last two groups mix layers, rules and scopes, so there each claimant says which it is; the claimed
    // groups are named by their heading.
    private static void Group(List<string> lines, string heading, IReadOnlyList<Claimant> claimants, bool named)
    {
        if (claimants.Count == 0) return;

        lines.Add($"  {heading}:");
        foreach (Claimant claimant in claimants)
        {
            string kind = named ? KindLabel(claimant.Kind) + " " : string.Empty;
            string subject = claimant.Subject is { } phrase ? $": {phrase}" : string.Empty;
            lines.Add($"    {kind}{claimant.Name}{subject}");
            Steps(lines, claimant.Trace.Steps, "      ");
        }
    }

    private static void Steps(List<string> lines, IReadOnlyList<ClaimStep> steps, string indent)
    {
        foreach (ClaimStep step in steps)
        {
            lines.Add(indent + step.Text);
            Steps(lines, step.Steps, indent + "  ");
        }
    }

    private static string KindLabel(ClaimantKind kind)
    {
        return kind switch
        {
            ClaimantKind.Layer => "layer",
            ClaimantKind.Scope => "scope",
            _ => "rule"
        };
    }

    private static string KindWord(TypeNode type)
    {
        if (type.IsExternal) return "external";

        return (type.Kind, type.IsRecord) switch
        {
            (TypeKind.Class, true) => "record",
            (TypeKind.Struct, true) => "record struct",
            _ => type.Kind.ToString().ToLowerInvariant()
        };
    }
}
