using SlideRule.Hosting;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     The CLI's "you named something that is not there" refusals — an unmatched filter, an unknown ID. A
///     reader who meets two of them should meet one format: name what did not match, then list what was
///     available, so the next command is one edit away. Each caller keeps its own lead; the shape they share
///     lives here.
/// </summary>
/// <remarks>
///     A JSON array a client serialized into a string parameter is refused on its shape, never answered with
///     the roster: <see cref="StringifiedArrayRefusal" /> owns that decision, so a caller states only its lead,
///     its advice, and the roster it falls back to. The refusals are shared by the CLI and the MCP tools, so no
///     wording here may name a flag or a tool parameter: it names the shape both surfaces pass.
/// </remarks>
internal static class Refusals
{
    /// <summary>
    ///     Every rule ID in <paramref name="model" />, ordinal-sorted: the post-desugar spec as a reader can
    ///     name it.
    /// </summary>
    private static IEnumerable<string> AvailableRuleIds(ArchitectureModel model)
    {
        return model.Rules.Select(rule => rule.Id).OrderBy(id => id, StringComparer.Ordinal);
    }

    /// <summary>
    ///     The refusal text: <paramref name="lead" /> (what did not match), then
    ///     <paramref name="label" /> introducing <paramref name="available" />, one indented name per
    ///     line.
    /// </summary>
    internal static string NotFoundMessage(string lead, string label, IEnumerable<string> available)
    {
        return $"{lead}. {label}:\n  " + string.Join("\n  ", available);
    }

    /// <summary>
    ///     A named rule that is not in <paramref name="model" />: <paramref name="lead" /> over the spec's
    ///     whole rule roster. The label and the roster travel together, so every rule-ID refusal differs only
    ///     in what it leads with.
    /// </summary>
    internal static string RuleNotFound(string lead, ArchitectureModel model)
    {
        return NotFoundMessage(lead, "Available rule IDs", AvailableRuleIds(model));
    }

    /// <summary>
    ///     The elements of a JSON array that reached the CLI as text — <c>["a","b"]</c> read as <c>a</c> and
    ///     <c>b</c>, quotes of either kind stripped — or <c>null</c> when <paramref name="value" /> is not one.
    /// </summary>
    /// <remarks>
    ///     Brackets are unambiguous here: a rule ID is dash-and-slash lowercase segments and a project name
    ///     carries dots, so nothing legitimate opens with <c>[</c>. Deliberately a reader, never a repair — the
    ///     coercer above it lets a string that merely looks like an array through verbatim, so a caller's
    ///     literal value survives untouched and is refused rather than rewritten into something they did not
    ///     ask for. The empty array is still an array, and comes back as an empty list rather than
    ///     <c>null</c>, so it is refused on its shape like every other one.
    /// </remarks>
    internal static IReadOnlyList<string>? StringifiedArrayElements(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '[' || trimmed[^1] != ']') return null;

        return trimmed[1..^1]
            .Split(',')
            .Select(element => element.Trim()
                .Trim('"', '\''))
            .Where(element => element.Length > 0)
            .ToList();
    }

    /// <summary>
    ///     The refusal text for a stringified array: <paramref name="lead" /> (what did not match), then the
    ///     shape, then <paramref name="advice" /> — the caller's own sentence for what to pass instead.
    /// </summary>
    /// <remarks>No roster: the shape is the defect named first, and the roster comes on the retry.</remarks>
    internal static string StringifiedArrayMessage(string lead, string advice)
    {
        return $"{lead}. That is a JSON array written as text; {advice}.";
    }

    /// <summary>
    ///     The whole shape-or-roster decision: the stringified-array refusal for <paramref name="value" />,
    ///     or <c>null</c> when the value is not one and the caller's own roster refusal should answer.
    /// </summary>
    /// <remarks>
    ///     <paramref name="advice" /> runs only when the value is an array, handed its elements — compose
    ///     with <see cref="GlobListAdvice" /> or <see cref="SingleValueAdvice" /> and fall back through
    ///     <c>??</c>, and neither branch ever builds the other's message.
    /// </remarks>
    internal static string? StringifiedArrayRefusal(
        string value, string lead, Func<IReadOnlyList<string>, string> advice)
    {
        IReadOnlyList<string>? elements = StringifiedArrayElements(value);
        return elements is null ? null : StringifiedArrayMessage(lead, advice(elements));
    }

    /// <summary>
    ///     The advice a glob-list filter gives — one semicolon-separated string, echoing
    ///     <paramref name="elements" /> as that string, which is the value the caller can paste back.
    /// </summary>
    internal static string GlobListAdvice(IReadOnlyList<string> elements)
    {
        const string advice = "pass the globs as one semicolon-separated string";
        return elements.Count == 0 ? advice : $"{advice}: '{string.Join(";", elements)}'";
    }

    /// <summary>
    ///     The advice every scalar parameter gives — <paramref name="advice" /> naming what one of them is,
    ///     echoing <paramref name="elements" /> only when it holds exactly one value.
    /// </summary>
    /// <remarks>
    ///     The echo is withheld for an array of several deliberately: it holds no single value the verb could
    ///     have taken, so naming one would be picking for the caller.
    /// </remarks>
    internal static string SingleValueAdvice(string advice, IReadOnlyList<string> elements)
    {
        return elements.Count == 1 ? $"{advice}: '{elements[0]}'" : advice;
    }

    /// <summary>
    ///     The stringified-array refusal for a scalar parameter — <see cref="StringifiedArrayRefusal" />
    ///     over <see cref="SingleValueAdvice" /> — or <c>null</c> when <paramref name="value" /> is not an
    ///     array and the caller's own refusal should answer.
    /// </summary>
    /// <remarks>
    ///     The pairing is spelled here because <see cref="SingleValueAdvice" /> takes the noun first and so
    ///     cannot be passed as a method group; without it every scalar caller writes the same closure out, and
    ///     a copied one is how a path parameter comes to advise "pass one git ref". Text only, with no throw: a
    ///     caller that has its own fallback reads this through <c>??</c>.
    /// </remarks>
    internal static string? SingleValueRefusal(string value, string lead, string advice)
    {
        return StringifiedArrayRefusal(value, lead, elements => SingleValueAdvice(advice, elements));
    }
}
