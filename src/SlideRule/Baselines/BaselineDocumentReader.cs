using SlideRule.Internal;

namespace SlideRule.Baselines;

/// <summary>Reads a baseline file's text back into a document, or says why it is not one.</summary>
/// <remarks>
///     Nothing here throws for content, whatever the text says: a file that is truncated, hand-edited
///     into nonsense, or written by a version of the format this reader does not admit comes back as a
///     refusal naming what is wrong and where. A null text is a different thing — a caller bug — and
///     throws. Every entry verifies against its seal on the way in, so a document that comes back has
///     already been through the integrity check and nothing downstream repeats it.
/// </remarks>
internal static class BaselineDocumentReader
{
    /// <summary>The document <paramref name="text" /> carries, or why it is not a baseline file.</summary>
    internal static BaselineReadResult Read(string text)
    {
        Guard.NotNull(text, nameof(text));

        if (!StrictJson.TryParse(text, out StrictJsonValue? root, out StrictJsonError? error))
        {
            // The JSON reader's refusal passes through with its own words and position: it already says
            // what is wrong in the same register, and re-wording it would only move the reader's eye.
            StrictJsonError json = error!;
            return BaselineReadResult.Refused(
                new BaselineRefusal(BaselineRefusalKind.Syntax, json.Reason, null, null, json.Position));
        }

        var walk = new Walk();
        BaselineDocument? document = walk.ReadDocument(root!);
        if (walk.Refusal is { } refusal) return BaselineReadResult.Refused(refusal);

        return BaselineReadResult.Read(document!);
    }

    /// <summary>One walk over one parsed document, and the refusal that ended it.</summary>
    /// <remarks>
    ///     Every method returns null once a refusal is recorded and every caller returns straight back
    ///     up, which is what makes "the first refusal wins" true of the whole read rather than of one
    ///     method. Nothing here throws for content.
    /// </remarks>
    private sealed class Walk
    {
        // The whole-file digest is a fixed-width lowercase SHA-256 in hex; the message below says the
        // same number in words a reader can check against the field in front of them.
        private const int LegacyDigestLength = 64;

        /// <summary>Why the text is not a baseline file, or null while it still is one.</summary>
        internal BaselineRefusal? Refusal { get; private set; }

        internal BaselineDocument? ReadDocument(StrictJsonValue root)
        {
            if (root.ValueKind != StrictJsonKind.Object)
            {
                RefuseAt(root, "the root must be a JSON object.");
                return null;
            }

            // The version leads the read, because the root's own property set depends on it — a legacy
            // file carries a whole-file digest and a current one does not. Read with TryGetProperty so a
            // file missing it reports the missing property rather than falling out of the walk.
            if (!TryReadSchemaVersion(root, out int schemaVersion)) return null;

            bool sealsEntries = BaselineFormat.CarriesSeal(schemaVersion);
            bool exact = sealsEntries
                ? RequireExactProperties(root, "schemaVersion", "rules")
                : RequireExactProperties(root, "schemaVersion", "digest", "rules");
            if (!exact) return null;

            StrictJsonValue? digestValue = sealsEntries ? null : root.GetProperty("digest");
            string? legacyDigest = null;
            if (digestValue is not null)
            {
                legacyDigest = ReadDigest(digestValue);
                if (legacyDigest is null) return null;
            }

            using BaselineSealer? sealer = sealsEntries ? new BaselineSealer() : null;
            Dictionary<string, IReadOnlyList<BaselineEntry>>? sections = ReadSections(root, schemaVersion, sealer);
            if (sections is null) return null;

            var document = new BaselineDocument(sections);

            // A sealed file verified itself entry by entry on the way in. A legacy one has one digest
            // over all of them, so it is rebuilt here from the parsed entries in that version's frozen
            // grammar: formatting, order and CRLF changes are invisible; any entry change is not.
            if (legacyDigest is null) return document;

            string recomputed = BaselineFormat.LegacyDigest(document.Sections);
            if (string.Equals(recomputed, legacyDigest, StringComparison.Ordinal)) return document;

            Refuse(
                BaselineRefusalKind.LegacyDigestMismatch, "the digest does not match the entries.", null, null,
                digestValue!.Position);
            return null;
        }

        private bool TryReadSchemaVersion(StrictJsonValue root, out int schemaVersion)
        {
            schemaVersion = 0;
            if (!root.TryGetProperty("schemaVersion", out StrictJsonValue? value))
            {
                RefuseAt(root, "missing property 'schemaVersion'.");
                return false;
            }

            StrictJsonValue declared = value!;
            bool isInteger = declared.ValueKind == StrictJsonKind.Number && declared.TryGetInt32(out schemaVersion);
            if (!isInteger)
            {
                RefuseAt(declared, "schemaVersion must be an integer.");
                return false;
            }

            if (BaselineFormat.IsSupported(schemaVersion)) return true;

            string version = JsonText.Digits(schemaVersion);
            string legacy = JsonText.Digits(BaselineFormat.LegacySchemaVersion);
            string current = JsonText.Digits(BaselineFormat.SchemaVersion);
            RefuseAt(declared, $"unsupported schemaVersion {version} (expected {legacy} or {current}).");
            return false;
        }

        // The whole-file digest is a legacy-version field: a current file carries none, its entries
        // sealing themselves one at a time.
        private string? ReadDigest(StrictJsonValue value)
        {
            if (value.ValueKind != StrictJsonKind.String)
            {
                RefuseAt(value, "digest must be a string.");
                return null;
            }

            string digest = value.GetString();
            bool wellFormed = digest.Length == LegacyDigestLength && digest.All(IsLowerHex);
            if (wellFormed) return digest;

            RefuseAt(value, "digest must be 64 lowercase hex characters.");
            return null;
        }

        private Dictionary<string, IReadOnlyList<BaselineEntry>>? ReadSections(
            StrictJsonValue root, int schemaVersion, BaselineSealer? sealer)
        {
            StrictJsonValue rules = root.GetProperty("rules");
            if (rules.ValueKind != StrictJsonKind.Object)
            {
                RefuseAt(rules, "'rules' must be an object.");
                return null;
            }

            var sections = new Dictionary<string, IReadOnlyList<BaselineEntry>>(StringComparer.Ordinal);
            foreach (StrictJsonProperty ruleProperty in rules.EnumerateObject())
            {
                string ruleId = ruleProperty.Name;
                StrictJsonValue section = ruleProperty.Value;
                if (section.ValueKind != StrictJsonKind.Object)
                {
                    RefuseAt(section, $"rule '{ruleId}' must be an object.", ruleId);
                    return null;
                }

                if (!RequireExactProperties(section, "entries")) return null;

                StrictJsonValue entriesValue = section.GetProperty("entries");
                if (entriesValue.ValueKind != StrictJsonKind.Array)
                {
                    RefuseAt(entriesValue, $"rule '{ruleId}' entries must be an array.", ruleId);
                    return null;
                }

                var entries = new List<BaselineEntry>();
                foreach (StrictJsonValue entryValue in entriesValue.EnumerateArray())
                {
                    BaselineEntry? entry = ReadEntry(ruleId, entryValue, schemaVersion, sealer);
                    if (entry is null) return null;

                    entries.Add(entry);
                }

                sections[ruleId] = entries;
            }

            return sections;
        }

        private BaselineEntry? ReadEntry(
            string ruleId, StrictJsonValue entry, int schemaVersion, BaselineSealer? sealer)
        {
            if (entry.ValueKind != StrictJsonKind.Object)
            {
                RefuseAt(entry, $"rule '{ruleId}' has a non-object entry.", ruleId);
                return null;
            }

            // One pass over the properties answers the whole shape question — {subject}, {source, target},
            // and either of those plus a because, plus the two an edge's siteCount adds, plus the seal —
            // and keeps each value it meets, so nothing below walks the object a second time. This runs for
            // every entry of every baseline file on each check and status, so any per-entry walk costs in
            // proportion to a team's whole debt ledger.
            StrictJsonValue? subject = null;
            StrictJsonValue? source = null;
            StrictJsonValue? target = null;
            StrictJsonValue? because = null;
            StrictJsonValue? siteCount = null;
            StrictJsonValue? seal = null;
            var hasStranger = false;
            // A legacy file has no measure and no seal, so both are strangers there rather than optional
            // keys: a v1 file carrying either was hand-edited, and its digest was computed without them.
            bool measured = BaselineFormat.CarriesSiteCount(schemaVersion);
            bool sealsEntries = BaselineFormat.CarriesSeal(schemaVersion);

            foreach (StrictJsonProperty property in entry.EnumerateObject())
                switch (property.Name)
                {
                    case "subject":
                        subject = property.Value;
                        break;
                    case "source":
                        source = property.Value;
                        break;
                    case "target":
                        target = property.Value;
                        break;
                    case "because":
                        because = property.Value;
                        break;
                    case "siteCount" when measured:
                        siteCount = property.Value;
                        break;
                    case "seal" when sealsEntries:
                        seal = property.Value;
                        break;
                    default:
                        hasStranger = true;
                        break;
                }

            // A repeated key needs no arithmetic here: the strict reader refuses one at the parse, so an
            // entry that reached this walk names each of its keys once.
            bool isSubject = !hasStranger && subject is not null && source is null && target is null;
            bool isEdge = !hasStranger && source is not null && target is not null && subject is null;
            if (!isSubject && !isEdge)
            {
                RefuseAt(entry, $"rule '{ruleId}' has an entry that is neither {{source, target}} nor {{subject}}.", ruleId);
                return null;
            }

            if (isSubject && siteCount is not null)
            {
                RefuseAt(
                    siteCount, $"rule '{ruleId}' has a 'siteCount' on a subject entry — only edge entries carry one.",
                    ruleId);
                return null;
            }

            // A seal is required where the version carries them, so its absence is a missing property
            // rather than tamper: the more precise diagnosis, and both refusals exit the same way.
            if (sealsEntries && seal is null)
            {
                RefuseAt(entry, $"rule '{ruleId}' has an entry with no 'seal'.", ruleId);
                return null;
            }

            BaselineEntry? parsed = isSubject
                ? ReadSubjectEntry(ruleId, subject!)
                : ReadEdgeEntry(ruleId, source!, target!);
            if (parsed is null) return null;

            if (siteCount is not null)
            {
                int? count = ReadSiteCount(ruleId, siteCount);
                if (count is null) return null;

                parsed = parsed.WithSiteCount(count.Value);
            }

            if (because is not null)
            {
                string? attribution = ReadBecause(ruleId, because);
                if (attribution is null) return null;

                parsed = parsed.WithBecause(attribution);
            }

            if (seal is null) return parsed;

            return VerifySeal(ruleId, entry, seal, parsed, sealer!);
        }

        private BaselineEntry? ReadSubjectEntry(string ruleId, StrictJsonValue subject)
        {
            string? identity = RequireNonEmptyString(ruleId, "subject", subject);
            return identity is null ? null : BaselineEntry.ForSubject(identity);
        }

        private BaselineEntry? ReadEdgeEntry(string ruleId, StrictJsonValue source, StrictJsonValue target)
        {
            string? from = RequireNonEmptyString(ruleId, "source", source);
            if (from is null) return null;

            string? to = RequireNonEmptyString(ruleId, "target", target);
            return to is null ? null : BaselineEntry.ForEdge(from, to);
        }

        // Verified while the entry and its rule are both in hand, so the refusal names the line that
        // failed rather than the file it sits in.
        private BaselineEntry? VerifySeal(
            string ruleId, StrictJsonValue entry, StrictJsonValue seal, BaselineEntry parsed, BaselineSealer sealer)
        {
            string? declared = ReadSeal(ruleId, seal);
            if (declared is null) return null;

            string computed = sealer.Seal(ruleId, parsed);
            if (string.Equals(computed, declared, StringComparison.Ordinal)) return parsed;

            string identity = parsed.SymbolForm;
            Refuse(
                BaselineRefusalKind.SealMismatch,
                $"the entry '{identity}' under rule '{ruleId}' does not match its seal.", ruleId, identity,
                entry.Position);
            return null;
        }

        private int? ReadSiteCount(string ruleId, StrictJsonValue value)
        {
            var siteCount = 0;
            bool whole = value.ValueKind == StrictJsonKind.Number && value.TryGetInt32(out siteCount) && siteCount >= 1;
            if (whole) return siteCount;

            RefuseAt(value, $"rule '{ruleId}' has a 'siteCount' that is not an integer of at least 1.", ruleId);
            return null;
        }

        private string? ReadBecause(string ruleId, StrictJsonValue value)
        {
            string? text = RequireNonEmptyString(ruleId, "because", value);
            if (text is null) return null;

            bool blankOrMultiline = string.IsNullOrWhiteSpace(text) || SingleLineProse.IsMultiLine(text);
            if (!blankOrMultiline) return text;

            RefuseAt(value, $"rule '{ruleId}' has a blank or multi-line 'because'.", ruleId);
            return null;
        }

        private string? ReadSeal(string ruleId, StrictJsonValue value)
        {
            if (value.ValueKind != StrictJsonKind.String)
            {
                RefuseAt(value, $"rule '{ruleId}' has a non-string 'seal'.", ruleId);
                return null;
            }

            string seal = value.GetString();
            bool wellFormed = seal.Length == BaselineFormat.SealLength && seal.All(IsLowerHex);
            if (wellFormed) return seal;

            string length = JsonText.Digits(BaselineFormat.SealLength);
            RefuseAt(value, $"rule '{ruleId}' has a 'seal' that is not {length} lowercase hex characters.", ruleId);
            return null;
        }

        private string? RequireNonEmptyString(string ruleId, string property, StrictJsonValue value)
        {
            bool filled = value.ValueKind == StrictJsonKind.String && value.GetString().Length > 0;
            if (filled) return value.GetString();

            RefuseAt(value, $"rule '{ruleId}' has an empty or non-string '{property}'.", ruleId);
            return null;
        }

        private bool RequireExactProperties(StrictJsonValue element, params string[] expected)
        {
            foreach (StrictJsonProperty property in element.EnumerateObject())
                if (Array.IndexOf(expected, property.Name) < 0)
                {
                    RefuseAt(property, $"unknown property '{property.Name}'.");
                    return false;
                }

            foreach (string name in expected)
            {
                if (element.TryGetProperty(name, out _)) continue;

                RefuseAt(element, $"missing property '{name}'.");
                return false;
            }

            return true;
        }

        private static bool IsLowerHex(char c)
        {
            return c is >= '0' and <= '9' or >= 'a' and <= 'f';
        }

        // The offending value's own start, which is where a reader opens the file.
        private void RefuseAt(StrictJsonValue at, string detail, string? ruleId = null)
        {
            Refuse(BaselineRefusalKind.Malformed, detail, ruleId, null, at.Position);
        }

        // A key nothing admits is reported at the name rather than at the value beside it, because the
        // name is the word the reader is looking for.
        private void RefuseAt(StrictJsonProperty at, string detail)
        {
            Refuse(BaselineRefusalKind.Malformed, detail, null, null, at.Position);
        }

        private void Refuse(
            BaselineRefusalKind kind, string detail, string? ruleId, string? entryIdentity, JsonPosition at)
        {
            Refusal ??= new BaselineRefusal(kind, detail, ruleId, entryIdentity, at);
        }
    }
}
