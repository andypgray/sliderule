using System.Diagnostics.CodeAnalysis;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Rendering;

/// <summary>What the survey is written as.</summary>
internal enum GraphFormat
{
    /// <summary>The human survey.</summary>
    Text = 0,

    /// <summary>The JSON survey document, which the grain ladder applies to.</summary>
    Json = 1,

    /// <summary>
    ///     The projects and their references as one Mermaid fence, the survey half of what
    ///     <c>render --diagram</c> draws. It has no grain.
    /// </summary>
    Mermaid = 2
}

/// <summary>
///     The format words and the refusals around them, sited once so <c>--format</c> and the <c>format</c> tool
///     parameter accept the same words in any case and refuse alike. Each caller passes its own default.
/// </summary>
internal static class GraphFormats
{
    /// <summary>
    ///     Every format's wire name, listed in <see cref="GraphFormat" />'s value order, because
    ///     <see cref="TryParse" /> converts by position.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } = ["text", "json", "mermaid"];

    /// <summary>
    ///     Whether <paramref name="name" /> names a format, compared ignoring case and trimmed; null or blank names
    ///     none, and leaves <paramref name="format" /> at <paramref name="fallback" />.
    /// </summary>
    public static bool TryParse([NotNullWhen(false)] string? name, GraphFormat fallback, out GraphFormat format)
    {
        format = fallback;
        if (string.IsNullOrWhiteSpace(name)) return true;

        int index = WireWords.IndexOf(Names, name);
        if (index < 0) return false;

        format = (GraphFormat)index;
        return true;
    }

    /// <summary>
    ///     The format a caller named, or <paramref name="fallback" /> when it named none, refused unless it is one
    ///     of <paramref name="accepted" /> (every format when null).
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     The name is not an accepted format, or is a JSON array written as text.
    /// </exception>
    public static GraphFormat Parse(string? name, GraphFormat fallback, IReadOnlyList<string>? accepted = null)
    {
        IReadOnlyList<string> words = accepted ?? Names;
        if (TryParse(name, fallback, out GraphFormat format) && words.Contains(Names[(int)format]))
            return format;

        throw new UserErrorException(Refusal(name ?? "", words));
    }

    /// <summary>
    ///     The one refusal text for a word that names no format the surface takes: <paramref name="accepted" />,
    ///     or every format when null.
    /// </summary>
    /// <param name="name">The value as the caller wrote it.</param>
    /// <param name="accepted">The words the surface takes, in <see cref="Names" /> order.</param>
    public static string Refusal(string name, IReadOnlyList<string>? accepted = null)
    {
        return WireWords.Refusal("format", name, accepted ?? Names);
    }

    /// <summary>
    ///     The refusal for a Mermaid fence longer than the caller's budget. A fence cut at a line would leave it
    ///     unclosed, and it has no coarser grain, so the refusal asks for a narrower subject.
    /// </summary>
    public static string OversizedRefusal(int length, int maxChars)
    {
        return $"The Mermaid diagram is {length:N0} characters, over this response's limit of {maxChars:N0}. "
               + "Narrow the subject: projects: \"<name globs>\" draws part of the solution. On the CLI, "
               + "sliderule graph --format mermaid --projects <globs>.";
    }

    /// <summary>
    ///     Refuses a grain coarser than full beside the Mermaid format, which has no grain to coarsen. A grain the
    ///     caller left out, or named as full, passes.
    /// </summary>
    /// <param name="format">The resolved format.</param>
    /// <param name="grain">The grain as the caller wrote it, or null.</param>
    /// <param name="grainName">How the caller's surface spells the grain knob, for the refusal.</param>
    /// <exception cref="UserErrorException">The format is Mermaid and the grain is coarser than full.</exception>
    public static void RefuseGrainOnMermaid(GraphFormat format, string? grain, string grainName)
    {
        if (format != GraphFormat.Mermaid || DocumentGrains.Parse(grain) == DocumentGrain.Full) return;

        throw new UserErrorException(
            $"The Mermaid diagram has no grain, so {grainName} {grain} does not apply to it. Narrow the diagram by "
            + "project instead, or leave the grain out.");
    }
}
