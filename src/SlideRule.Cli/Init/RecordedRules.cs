using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using SlideRule.Codebase;

namespace SlideRule.Cli.Init;

/// <summary>
///     The rules <c>init --from-graph</c> records: a layer per product project, and for each project a rule that it
///     must not reference the projects it is not connected to either way.
/// </summary>
/// <remarks>
///     <para>
///         Two projects are connected when either reaches the other through the references the projects declare or the
///         ones the code makes, followed transitively, since the declared graph can omit a reference the compiler
///         sees. A reference that follows an existing path is allowed by every rule written here, and a project added
///         later is bound by none.
///     </para>
///     <para>
///         A product project is a solution member that declares a type and references no test framework. A project
///         with no type would fail its own rule, because an empty subject fails.
///     </para>
/// </remarks>
internal static class RecordedRules
{
    private static readonly string[] TestFrameworkPackages = ["Microsoft.NET.Test.Sdk", "xunit", "NUnit", "MSTest", "TUnit"];

    /// <summary>
    ///     The statements for <c>Define</c>'s body, one line apiece with blank lines between groups, or none when no
    ///     two product projects are unconnected.
    /// </summary>
    /// <param name="summary">The survey of the solution.</param>
    /// <param name="recordedOn">The day written into each rule's reason.</param>
    internal static IReadOnlyList<string> Statements(GraphSummary summary, DateOnly recordedOn)
    {
        List<string> products = summary.Projects.Where(IsProduct)
            .Select(project => project.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        IReadOnlyDictionary<string, HashSet<string>> reach = Reachability(summary);

        List<(string Project, List<string> Unconnected)> rules = products
            .Select(project => (project, products.Where(other => other != project && !Connected(reach, project, other))
                .ToList()))
            .Where(rule => rule.Item2.Count > 0)
            .ToList();
        if (rules.Count == 0) return [];

        IReadOnlyDictionary<string, string> variables = Variables(rules.Select(rule => rule.Project));
        IReadOnlyDictionary<string, string> ids = RuleIds(rules.Select(rule => rule.Project));
        var date = recordedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var lines = new List<string>
        {
            $"// Recorded by sliderule init --from-graph on {date} from the project graph, not reasoned: rewrite each",
            "// rule's reason, or delete the rule, once someone has decided whether the boundary is meant."
        };
        lines.AddRange(variables.Select(pair => $"Layer {pair.Value} = arch.Layer({Literal(pair.Key)}, arch.Project({Literal(pair.Key)}));"));

        foreach ((string project, List<string> unconnected) in rules)
        {
            IEnumerable<string> targets = unconnected.Select(other => variables[other]);
            lines.Add("");
            lines.Add($"arch.Rule({Literal(ids[project])})");
            lines.Add($"    .Enforce({variables[project]}.MustNotReference({string.Join(", ", targets)}))");
            lines.Add(
                $"    .Because(\"Recorded from the project graph on {date}: no reference either way then. Replace this "
                + "with the reason the boundary exists.\");");
        }

        return lines;
    }

    private static bool IsProduct(ProjectSummary project)
    {
        bool member = project.SolutionMember != false;
        bool tests = project.PackageReferences.Any(package => TestFrameworkPackages.Any(prefix =>
            package.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
        return member && project.Types > 0 && !tests;
    }

    // Every project each project reaches, over the declared and the observed references together.
    private static IReadOnlyDictionary<string, HashSet<string>> Reachability(GraphSummary summary)
    {
        Dictionary<string, HashSet<string>> direct = summary.Projects.ToDictionary(
            project => project.Name,
            project => new HashSet<string>(project.ProjectReferences.Concat(project.UndeclaredProjectReferences),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (ProjectEdgeSummary edge in summary.ProjectEdges)
        {
            if (!direct.TryGetValue(edge.Source, out HashSet<string>? targets))
                direct[edge.Source] = targets = new HashSet<string>(StringComparer.Ordinal);
            targets.Add(edge.Target);
        }

        return direct.Keys.ToDictionary(project => project, project => Closure(direct, project), StringComparer.Ordinal);
    }

    private static HashSet<string> Closure(Dictionary<string, HashSet<string>> direct, string start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([start]);
        while (pending.Count > 0)
        {
            if (!direct.TryGetValue(pending.Pop(), out HashSet<string>? targets)) continue;

            foreach (string target in targets.Where(seen.Add)) pending.Push(target);
        }

        return seen;
    }

    private static bool Connected(IReadOnlyDictionary<string, HashSet<string>> reach, string first, string second)
    {
        return Reaches(reach, first, second) || Reaches(reach, second, first);
    }

    private static bool Reaches(IReadOnlyDictionary<string, HashSet<string>> reach, string from, string to)
    {
        return reach.TryGetValue(from, out HashSet<string>? reached) && reached.Contains(to);
    }

    // A camel-case local per project, unique within Define and never a keyword. Unconnectedness is symmetric, so
    // every project a rule names has a rule of its own and is among these.
    private static IReadOnlyDictionary<string, string> Variables(IEnumerable<string> projects)
    {
        var used = new HashSet<string>(["arch"], StringComparer.Ordinal);
        var variables = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string project in projects) variables[project] = Unique(used, CamelCase(project));

        return variables;
    }

    private static string CamelCase(string project)
    {
        var builder = new StringBuilder();
        var upperNext = false;
        foreach (char character in project)
        {
            if (!SyntaxFacts.IsIdentifierPartCharacter(character) || character == '_')
            {
                upperNext = builder.Length > 0;
                continue;
            }

            builder.Append(builder.Length == 0
                ? char.ToLowerInvariant(character)
                : upperNext
                    ? char.ToUpperInvariant(character)
                    : character);
            upperNext = false;
        }

        var name = builder.ToString();
        bool usable = name.Length > 0 && SyntaxFacts.IsIdentifierStartCharacter(name[0])
                                      && SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
                                      && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None;
        return usable ? name : "layer" + (name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..]);
    }

    // layering/<project>-independent, the project lower-cased with every run of other characters a single dash.
    private static IReadOnlyDictionary<string, string> RuleIds(IEnumerable<string> projects)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string project in projects)
        {
            var slug = new StringBuilder();
            foreach (char character in project.ToLowerInvariant())
            {
                bool kept = character is >= 'a' and <= 'z' or >= '0' and <= '9';
                if (kept) slug.Append(character);
                else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            }

            string trimmed = slug.ToString().Trim('-');
            ids[project] = Unique(used, $"layering/{(trimmed.Length == 0 ? "project" : trimmed)}-independent", "-");
        }

        return ids;
    }

    private static string Unique(HashSet<string> used, string candidate, string separator = "")
    {
        string chosen = candidate;
        for (var suffix = 2; !used.Add(chosen); suffix++)
            chosen = candidate + separator + suffix.ToString(CultureInfo.InvariantCulture);
        return chosen;
    }

    private static string Literal(string text)
    {
        return SymbolDisplay.FormatLiteral(text, true);
    }
}
