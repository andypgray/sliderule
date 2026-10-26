using System.Security.Cryptography;
using System.Text;
using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>
///     The on-disk baseline format: composes a baseline file's text from the entries it should hold, and
///     computes the seal each entry in that file carries.
/// </summary>
/// <remarks>
///     <para>
///         A baseline file is line-oriented JSON — UTF-8 with no byte-order mark, LF line endings, a
///         trailing newline, a two-space indent, one entry object per line — so paying off one
///         grandfathered violation removes exactly one line in a diff. Rules sort by ID and entries by
///         source or subject then target, both ordinal, and entries sharing an identity compose to one line, so
///         the same entries compose to the same bytes in any input order. One file holds a
///         section per rule ID, and an entry object carries <c>source</c> and <c>target</c>, or
///         <c>subject</c>, then an optional <c>siteCount</c>, an optional <c>because</c>, and a <c>seal</c> last.
///     </para>
///     <para>
///         The <c>seal</c> is computed over the rule ID and a rendering of the entry's own fields rather
///         than over the file text, so reformatting the file or checking it out with CRLF endings leaves
///         it valid, while editing an entry, a count or a reason does not: a hand-edited baseline is
///         refused rather than obeyed, and the file's history is the record of who widened what. The rule
///         ID is sealed in too, so an entry moved from one rule's section to another's fails there. What
///         a seal cannot see is a whole line taken away: deleting an entry, or a section, is accepted,
///         and the violation it grandfathered goes red on the next check. This is tamper-evident,
///         not tamper-proof — it catches accidents and hand edits, and review of the file's diff remains
///         the human gate.
///     </para>
///     <para>
///         Every entry line seals itself, so a merge conflict between two branches burning the same baseline
///         down resolves by keeping whole lines from either side, into a file the tool accepts with nothing
///         to re-run.
///     </para>
/// </remarks>
internal static class BaselineFormat
{
    /// <summary>The <c>schemaVersion</c> value <see cref="ComposeFile" /> writes.</summary>
    public const int SchemaVersion = 2;

    /// <summary>
    ///     The older <c>schemaVersion</c> a reader still accepts: a file written before entries carried a
    ///     site count or a seal. Its entries have no count, so each grandfathers its pair however many
    ///     sites it grows to, and one whole-file <c>digest</c> stands in for the per-entry seals, until
    ///     the file is written again.
    /// </summary>
    public const int LegacySchemaVersion = 1;

    // A truncation of the hash so that an entry line stays readable to whoever resolves a merge conflict over
    // it. Every stored seal has this length, so changing it refuses every sealed baseline file written before.
    internal const int SealLength = 16;
    internal const int SealBytes = SealLength / 2;

    // Every seal and digest on disk is hashed over these preambles, so changing either would fail every
    // baseline file written before the change.
    private const string SealPreamble = "sliderule-baseline-seal-v2";
    private const string LegacyDigestPreamble = "sliderule-baseline-digest-v1";
    private const string HexDigits = "0123456789abcdef";

    public static bool IsSupported(int schemaVersion)
    {
        return schemaVersion == SchemaVersion || schemaVersion == LegacySchemaVersion;
    }

    /// <summary>
    ///     Whether an entry in a baseline file declaring <paramref name="schemaVersion" /> may carry a
    ///     <c>siteCount</c>: true for every accepted version but <see cref="LegacySchemaVersion" />, whose
    ///     entries predate the count.
    /// </summary>
    public static bool CarriesSiteCount(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Whether every entry in a baseline file declaring <paramref name="schemaVersion" /> carries a
    ///     <c>seal</c>: true for every accepted version but <see cref="LegacySchemaVersion" />, whose
    ///     entries are covered by one whole-file <c>digest</c> instead. Where it is carried it is
    ///     required — an entry without one is malformed, not unsealed.
    /// </summary>
    public static bool CarriesSeal(int schemaVersion)
    {
        return schemaVersion != LegacySchemaVersion;
    }

    /// <summary>
    ///     Composes the whole text of a baseline file holding the given entries, keyed by rule ID, in the
    ///     file's canonical order with a fresh <c>seal</c> on each entry. Entries that share an identity compose
    ///     to one line, with the smallest site count and the first reason any of them carries. The file is
    ///     always <see cref="SchemaVersion" />, so writing one read at <see cref="LegacySchemaVersion" />
    ///     upgrades it.
    /// </summary>
    public static string ComposeFile(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> rules)
    {
        // Collapsed here rather than in SortRules: the legacy digest shares that sort, and a stored digest was
        // taken over a file's entries as written, duplicates included.
        Dictionary<string, IReadOnlyList<BaselineEntry>> collapsed = Guard.NotNull(rules, nameof(rules))
            .ToDictionary(kv => kv.Key, kv => BaselineEntry.Collapsed(kv.Value), StringComparer.Ordinal);
        IReadOnlyList<SortedRule> sorted = SortRules(collapsed);

        using var sealer = new BaselineSealer();
        var builder = new StringBuilder();
        builder.Append("{\n");
        builder.Append("  \"schemaVersion\": ").Append(SchemaVersion).Append(",\n");

        if (sorted.Count == 0)
        {
            builder.Append("  \"rules\": {}\n");
        }
        else
        {
            builder.Append("  \"rules\": {\n");
            for (var i = 0; i < sorted.Count; i++)
            {
                SortedRule rule = sorted[i];
                builder.Append("    ").AppendQuoted(rule.Id).Append(": {\n");
                AppendEntries(builder, sealer, rule);
                builder.Append(i < sorted.Count - 1 ? "    },\n" : "    }\n");
            }

            builder.Append("  }\n");
        }

        builder.Append("}\n");
        return builder.ToString();
    }

    /// <summary>
    ///     The exact text an entry's <c>seal</c> is taken over: a fixed preamble line, then a
    ///     <c>rule &lt;id&gt;</c> line naming the section the entry sits in, an
    ///     <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line for the entry
    ///     itself, a <c>siteCount &lt;n&gt;</c> line where it has a count, and a
    ///     <c>because &lt;text&gt;</c> line where it has a reason. Every line ends in LF, the last one
    ///     included. Useful for showing what a seal was taken over when a stored entry fails to verify.
    /// </summary>
    public static string SealInput(string ruleId, BaselineEntry entry)
    {
        string id = Guard.NotNull(ruleId, nameof(ruleId));
        BaselineEntry content = Guard.NotNull(entry, nameof(entry));

        var builder = new StringBuilder();
        builder.Append(SealPreamble).Append('\n');
        builder.Append("rule ").Append(id).Append('\n');
        AppendEntryLines(builder, content, counted: true);
        return builder.ToString();
    }

    /// <summary>
    ///     The exact text <see cref="LegacyDigest" /> hashes, in <see cref="LegacySchemaVersion" />'s
    ///     frozen grammar: a fixed preamble line, then a <c>rule &lt;id&gt;</c> line per rule and an
    ///     <c>edge &lt;source&gt; -&gt; &lt;target&gt;</c> or <c>subject &lt;id&gt;</c> line per entry, in
    ///     the file's canonical order, each entry followed by a <c>because &lt;text&gt;</c> line where it
    ///     has a reason. No <c>siteCount</c> line, whatever the entries carry: that version predates the
    ///     count. Every line ends in LF, the last one included. Useful for showing what a digest was taken
    ///     over when a stored file of that version fails to verify.
    /// </summary>
    public static string LegacyDigestInput(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> rules)
    {
        IReadOnlyList<SortedRule> sorted = SortRules(rules);
        var builder = new StringBuilder();
        builder.Append(LegacyDigestPreamble).Append('\n');
        foreach (SortedRule rule in sorted)
        {
            builder.Append("rule ").Append(rule.Id).Append('\n');
            foreach (BaselineEntry entry in rule.Entries) AppendEntryLines(builder, entry, counted: false);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     The whole-file <c>digest</c> a <see cref="LegacySchemaVersion" /> baseline carries: the SHA-256
    ///     of the UTF-8 bytes of <see cref="LegacyDigestInput" />, in lowercase hex. This is what to
    ///     recompute when verifying a stored file that declares that version; a result differing from the
    ///     file's own <c>digest</c> field means the file was edited by hand.
    /// </summary>
    public static string LegacyDigest(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> rules)
    {
        string input = LegacyDigestInput(rules);
        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return ToLowerHex(hash, hash.Length);
    }

    // The one place that says what an entry's content is, so a seal and the frozen legacy digest cannot
    // disagree about it. All that separates the two renderings is the preamble, whether the rule ID leads
    // one entry or a whole section, and whether the measure is part of the grammar at all.
    private static void AppendEntryLines(StringBuilder builder, BaselineEntry entry, bool counted)
    {
        if (entry.Subject is { } subject)
            builder.Append("subject ").Append(subject).Append('\n');
        else
            builder.Append("edge ").Append(entry.Source).Append(" -> ").Append(entry.Target).Append('\n');
        if (counted && entry.SiteCount is { } siteCount)
            builder.Append("siteCount ").Append(JsonText.Digits(siteCount)).Append('\n');
        if (entry.Because is { } because)
            builder.Append("because ").Append(because).Append('\n');
    }

    internal static string ToLowerHex(byte[] bytes, int byteCount)
    {
        var chars = new char[byteCount * 2];
        for (var i = 0; i < byteCount; i++)
        {
            chars[i * 2] = HexDigits[bytes[i] >> 4];
            chars[i * 2 + 1] = HexDigits[bytes[i] & 0xF];
        }

        return new string(chars);
    }

    private static void AppendEntries(StringBuilder builder, BaselineSealer sealer, SortedRule rule)
    {
        IReadOnlyList<BaselineEntry> entries = rule.Entries;
        if (entries.Count == 0)
        {
            builder.Append("      \"entries\": []\n");
            return;
        }

        builder.Append("      \"entries\": [\n");
        for (var i = 0; i < entries.Count; i++)
        {
            BaselineEntry entry = entries[i];
            builder.Append("        ");
            if (entry.Subject is { } subject)
                builder.Append("{ \"subject\": ").AppendQuoted(subject);
            else
                builder.Append("{ \"source\": ").AppendQuoted(entry.Source!).Append(", \"target\": ")
                    .AppendQuoted(entry.Target!);
            if (entry.SiteCount is { } siteCount)
                builder.Append(", \"siteCount\": ").Append(JsonText.Digits(siteCount));
            if (entry.Because is { } because)
                builder.Append(", \"because\": ").AppendQuoted(because);
            string seal = sealer.Seal(rule.Id, entry);
            builder.Append(", \"seal\": ").AppendQuoted(seal);
            builder.Append(" }");
            builder.Append(i < entries.Count - 1 ? ",\n" : "\n");
        }

        builder.Append("      ]\n");
    }

    private static IReadOnlyList<SortedRule> SortRules(IReadOnlyDictionary<string, IReadOnlyList<BaselineEntry>> rules)
    {
        return Guard.NotNull(rules, nameof(rules))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new SortedRule(kv.Key, BaselineEntry.InCanonicalOrder(kv.Value)))
            .ToList();
    }

    private readonly struct SortedRule(string id, IReadOnlyList<BaselineEntry> entries)
    {
        public string Id { get; } = id;
        public IReadOnlyList<BaselineEntry> Entries { get; } = entries;
    }
}
