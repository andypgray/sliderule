using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Cli.Mcp.Prompts;

namespace SlideRule.Cli.Init;

/// <summary>
///     The spec project <c>init</c> writes: the csproj and the spec class the <c>derive_spec</c> recipe publishes in
///     its step 2, cut from the embedded recipe the way CI's <c>fence.sh</c> cuts them, so the recipe stays the only
///     copy.
/// </summary>
/// <remarks>
///     Each template has to carry the literal its substitution replaces. A recipe edit that drops one would leave
///     the adopter's file naming the recipe's placeholder, so it fails here instead, and the suite holds every
///     literal against the embedded recipe.
/// </remarks>
internal sealed class SpecScaffold
{
    /// <summary>The marker the csproj fence carries, the same one CI's cut and the recipe's tests look for.</summary>
    internal const string ProjectMarker = "PackageReference Include=\"SlideRule\"";

    /// <summary>The marker the spec-class fence carries.</summary>
    internal const string SpecClassMarker = "IArchitectureSpec";

    internal const string FrameworkLiteral = "<TargetFrameworks>net10.0</TargetFrameworks>";

    internal const string NamespaceLiteral = "namespace MyApp.Architecture;";

    internal const string EmptyDefineLiteral = DefineOpen + "    }";

    private const string DefineOpen = "    public void Define(Arch arch)\n    {\n";

    private SpecScaffold(string project, string specClass)
    {
        ProjectTemplate = project;
        SpecClassTemplate = specClass;
    }

    /// <summary>The recipe's csproj fence, its version still the placeholder.</summary>
    internal string ProjectTemplate { get; }

    /// <summary>The recipe's spec-class fence.</summary>
    internal string SpecClassTemplate { get; }

    /// <summary>The scaffold the running tool's embedded recipe publishes.</summary>
    internal static SpecScaffold Embedded { get; } = FromRecipe(EmbeddedResourceText.Load("derive-spec.md"));

    /// <summary>Cuts both fences out of <paramref name="recipe" />.</summary>
    /// <exception cref="InvalidOperationException">A fence or a literal a substitution needs is missing.</exception>
    internal static SpecScaffold FromRecipe(string recipe)
    {
        string project = Fence(recipe, "xml", ProjectMarker);
        string specClass = Fence(recipe, "csharp", SpecClassMarker);

        Require(project, ArchPrompts.ScaffoldVersionPlaceholder);
        Require(project, FrameworkLiteral);
        Require(specClass, NamespaceLiteral);
        Require(specClass, EmptyDefineLiteral);

        return new SpecScaffold(project, specClass);
    }

    /// <summary>
    ///     The csproj, referencing <paramref name="version" /> and targeting <paramref name="framework" />.
    /// </summary>
    internal string Project(string version, string framework)
    {
        return ProjectTemplate
            .Replace(ArchPrompts.ScaffoldVersionPlaceholder, $"Version=\"{version}\"")
            .Replace(FrameworkLiteral, $"<TargetFrameworks>{framework}</TargetFrameworks>");
    }

    /// <summary>
    ///     The spec class in <paramref name="namespace" />, under <paramref name="header" /> when the repository
    ///     declares one, with <paramref name="defineBody" /> as the lines inside <c>Define</c>, or none.
    /// </summary>
    internal string SpecClass(string @namespace, string? header, IReadOnlyList<string> defineBody)
    {
        string specClass = SpecClassTemplate.Replace(NamespaceLiteral, $"namespace {@namespace};");
        if (defineBody.Count > 0)
        {
            IEnumerable<string> indented = defineBody.Select(line => line.Length == 0 ? "" : "        " + line);
            string define = DefineOpen + string.Join("\n", indented) + "\n    }";
            specClass = specClass.Replace(EmptyDefineLiteral, define);
        }

        return header is null ? specClass : header + specClass;
    }

    // The body of the first fenced block in `language` whose body carries the marker, each line ending in LF: the
    // same cut fence.sh makes, so the recipe's fences can be re-ordered without selecting the wrong one.
    private static string Fence(string document, string language, string marker)
    {
        var open = false;
        var fenceLanguage = "";
        var body = new List<string>();

        foreach (string raw in document.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (!open)
                {
                    open = true;
                    fenceLanguage = line[3..].TrimEnd();
                    body.Clear();
                    continue;
                }

                open = false;
                string text = string.Concat(body.Select(bodyLine => bodyLine + "\n"));
                if (fenceLanguage == language && text.Contains(marker, StringComparison.Ordinal)) return text;

                continue;
            }

            if (open) body.Add(line);
        }

        throw new InvalidOperationException(
            $"The embedded derive-spec recipe has no {language} fence carrying '{marker}'.");
    }

    private static void Require(string template, string literal)
    {
        if (!template.Contains(literal, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The embedded derive-spec recipe's scaffold no longer carries '{literal}', which init replaces.");
    }
}
