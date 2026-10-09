using System.Diagnostics.CodeAnalysis;
using SlideRule.Cli.Pipeline;
using SlideRule.Prose;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Rendering;

/// <summary>
///     How much detail a JSON document carries. A ladder, not a set of independent switches: each rung keeps
///     everything the one below it keeps and elides one more level of nesting, so a coarser answer is always
///     a subset of a finer one and a reader can compare two documents by their stamp alone.
/// </summary>
/// <remarks>
///     <para>
///         One vocabulary across every document that has a ladder, so a caller learns the rung names once and
///         the two surfaces cannot spell them apart: <c>graph</c> coarsens its survey and <c>check</c> its
///         report against the same enum, the same wire names and the same parse. What a
///         rung elides is each renderer's own to say; that it is strictly coarser than the rung below is this
///         type's.
///     </para>
///     <para>
///         Grain is never scope. Every rung answers about the same subject — <c>--projects</c> and
///         <c>--rules</c> are the knobs that narrow that — which is what makes an automatic degrade safe: the
///         answer stays about the codebase the caller asked about, only in less detail.
///     </para>
///     <para>
///         The rungs are ordered by size for exactly one reason: a caller with a response budget walks down
///         until a whole document fits, rather than handing the truncator a document to cut in half. The
///         elided levels were chosen by measurement — the bulk of each document is the array that scales with
///         the codebase rather than with the spec.
///     </para>
///     <para>
///         <see cref="Index" /> is where that reasoning ends: every rung above it still carries something
///         that scales with the codebase, and it carries nothing that does. What survives it is the roster —
///         every project name, every rule id — which scales with the authored and structural dimension
///         alone, so no realistic solution reaches the truncator through it. That is what makes the floor
///         rung the narrowing menu as well as the smallest answer: the arguments <c>--projects</c> and
///         <c>--rules</c> take are exactly what it lists.
///     </para>
/// </remarks>
internal enum DocumentGrain
{
    /// <summary>
    ///     Everything. In the survey: namespace inventories, project edges and external-reference rows. In
    ///     the report: rules, their violations, and every violation's sites.
    /// </summary>
    Full = 0,

    /// <summary>
    ///     One level elided. In the survey: every project, edge and external row kept, each project's
    ///     namespace inventory gone. In the report: every rule and violation kept, each violation's sites
    ///     replaced by their count.
    /// </summary>
    Overview = 1,

    /// <summary>
    ///     The spine only. In the survey: projects with their declared project references and type counts, plus the
    ///     observed project edges; namespace inventories and the external-reference rows are both elided, the
    ///     latter replaced by its count. In the report: every rule with its prose and verdict, its violations
    ///     replaced by their count.
    /// </summary>
    Skeleton = 2,

    /// <summary>
    ///     The roster only — the ladder's floor, and the narrowing menu. In the survey: every project by
    ///     name, with its solution membership and type counts, its observed edges replaced by their count;
    ///     declared references and frameworks go with the rest. In the report: every rule by id, with its
    ///     posture, verdict, baseline, warnings and violation count; the prose goes. Both keep every trust
    ///     stamp and every roll-up, because those bound the answer rather than fill it.
    /// </summary>
    Index = 3
}

/// <summary>
///     What every laddered document does with a <see cref="DocumentGrain" />, sited here rather than in each
///     renderer so the CLI and the MCP surface cannot drift on any of it: a survey and a report stamped
///     <c>overview</c> mean the same rung, and both walk down to the same coarsest one.
/// </summary>
internal static class DocumentGrains
{
    /// <summary>
    ///     Every rung's wire name, finest first: what <c>--grain</c> and the <c>grain</c> tool parameter accept,
    ///     and the order a refusal lists them in.
    /// </summary>
    /// <remarks>
    ///     Listed in <see cref="DocumentGrain" />'s value order, because <see cref="TryParse" /> and
    ///     <see cref="Wire" /> convert between a rung and its name by position.
    /// </remarks>
    public static IReadOnlyList<string> Names { get; } = ["full", "overview", "skeleton", "index"];

    /// <summary>
    ///     Whether <paramref name="name" /> names a rung, and which: the one test both surfaces apply, so the CLI
    ///     option and the tool parameter accept the same words in any case.
    /// </summary>
    /// <param name="name">A wire name from <see cref="Names" />, compared ignoring case and trimmed; null or blank means full.</param>
    /// <param name="grain">The rung named, or <see cref="DocumentGrain.Full" /> when the name is not one.</param>
    public static bool TryParse([NotNullWhen(false)] string? name, out DocumentGrain grain)
    {
        grain = DocumentGrain.Full;
        if (string.IsNullOrWhiteSpace(name)) return true;

        string wanted = name.Trim();
        int rung = Names.ToList().FindIndex(known => string.Equals(known, wanted, StringComparison.OrdinalIgnoreCase));
        if (rung < 0) return false;

        grain = (DocumentGrain)rung;
        return true;
    }

    /// <summary>
    ///     The grain a caller named, or <see cref="DocumentGrain.Full" /> for none. One parse for both
    ///     surfaces, so the CLI option and the tool parameter accept the same words and refuse the same way.
    /// </summary>
    /// <param name="name">A wire name from <see cref="Names" />, compared ignoring case; null or blank means full.</param>
    /// <exception cref="UserErrorException">The name is not a rung, or is a JSON array written as text.</exception>
    public static DocumentGrain Parse(string? name)
    {
        if (TryParse(name, out DocumentGrain grain)) return grain;

        throw new UserErrorException(Refusal(name));
    }

    /// <summary>
    ///     The one refusal for a grain that names no rung: <see cref="Parse" /> throws it, and the CLI option adds it
    ///     as its parse error, so both surfaces refuse with the same text.
    /// </summary>
    /// <param name="name">The value as the caller wrote it.</param>
    public static string Refusal(string name)
    {
        var lead = $"Unknown grain '{name}'";
        return Refusals.SingleValueRefusal(name, lead, "pass one grain")
               ?? $"{lead}; use {ProseFormat.JoinReferences(Names)}.";
    }

    /// <summary>
    ///     Every rung from <paramref name="floor" /> down to the coarsest, each document composed by
    ///     <paramref name="compose" /> — what a runner offers an <see cref="IResponseFitter" />.
    /// </summary>
    /// <remarks>
    ///     Lazy on purpose: each rung costs a full serialization of the document, so a fitter that stops at
    ///     the first pays for exactly one. The ladder cannot spin — every rung is strictly coarser than the
    ///     one before it, and <see cref="DocumentGrain.Index" /> is last. This bound is the only place the
    ///     last rung is named: every other comparison in the product is a <c>&gt;=</c> against the rung whose
    ///     elision it guards, so a new rung below reaches them all unedited.
    /// </remarks>
    /// <param name="floor">The grain the caller asked for: the finest rung offered, and always yielded.</param>
    /// <param name="compose">Composes the whole document at one grain.</param>
    public static IEnumerable<string> Ladder(DocumentGrain floor, Func<DocumentGrain, string> compose)
    {
        for (DocumentGrain at = floor; at <= DocumentGrain.Index; at++) yield return compose(at);
    }

    /// <summary>
    ///     The wire spelling of a coarsened grain, or <see langword="null" /> at
    ///     <see cref="DocumentGrain.Full" /> — which omits the key, so a document that says nothing about
    ///     grain is the complete one.
    /// </summary>
    public static string? Wire(DocumentGrain grain)
    {
        return grain == DocumentGrain.Full ? null : Names[(int)grain];
    }

    /// <summary>
    ///     The rows and the count of an array that leaves at a rung: the rows while the rung carries them,
    ///     the count once it does not, and neither where there is nothing to say.
    /// </summary>
    /// <remarks>
    ///     The empty case is what parts these slots from an array that is simply elided: a solution with
    ///     nothing to report carries neither key at any grain, so its document is byte-identical to the one
    ///     written before the statement existed. Stated here rather than in each renderer because the report
    ///     and the survey both elide this way, and a rung that disagreed between them would be a document
    ///     shape a reader cannot predict from the grain.
    /// </remarks>
    /// <param name="items">The array the slot carries.</param>
    /// <param name="elide">Whether this rung has dropped the rows in favour of their count.</param>
    /// <typeparam name="T">The row type, written to the wire as it stands.</typeparam>
    public static (IReadOnlyList<T>? Rows, int? Count) CoverageRung<T>(IReadOnlyList<T> items, bool elide)
    {
        if (items.Count == 0) return (null, null);

        return elide ? (null, items.Count) : (items, null);
    }

    /// <summary>
    ///     The same rung for an array whose rows are a projection of the model's — mapped only where the
    ///     rung keeps them, so an elided rung pays for no row it will not write.
    /// </summary>
    /// <param name="items">The model rows the slot is composed from.</param>
    /// <param name="elide">Whether this rung has dropped the rows in favour of their count.</param>
    /// <param name="map">Composes one wire row from one model row.</param>
    /// <typeparam name="T">The model row type.</typeparam>
    /// <typeparam name="TJson">The wire row type.</typeparam>
    public static (IReadOnlyList<TJson>? Rows, int? Count) CoverageRung<T, TJson>(
        IReadOnlyList<T> items, bool elide, Func<T, TJson> map)
    {
        (IReadOnlyList<T>? rows, int? count) = CoverageRung(items, elide);

        return (rows?.Select(map).ToList(), count);
    }
}
