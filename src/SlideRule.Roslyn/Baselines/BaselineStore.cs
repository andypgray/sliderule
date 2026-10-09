using SlideRule.Baselines;
using SlideRule.Hosting;
using SlideRule.Internal;
using SlideRule.Rendering;
using SlideRule.Roslyn.Files;

namespace SlideRule.Roslyn.Baselines;

/// <summary>
///     The baseline I/O boundary of the host layer: the disk, the write, and the words a refusal reaches
///     a person at a terminal in.
/// </summary>
/// <remarks>
///     The strict walk and the seal verification are Core's — <see cref="BaselineDocumentReader" /> reads
///     a file's text into a document or into a refusal value, and never throws for what the text says.
///     What this class adds is everything that needs a machine: finding the file, reading its bytes, and
///     turning a refusal into the <see cref="UserErrorException" /> the CLI prints, which names the path
///     and, for an integrity failure, carries the recovery for the format that failed. A missing file or
///     missing rule section is uncaptured, not an error. Writes are canonical (fresh seals, unknown
///     sections preserved), and a write always composes the current version, so any write is also the
///     upgrade off the legacy one.
/// </remarks>
internal static class BaselineStore
{
    private const string RatchetSentence =
        "A baseline shrinks via 'sliderule baseline accept-reductions' and grows only via 'sliderule baseline add' " +
        "(one attributed entry at a time). ";

    // The two integrity refusals read differently because the two formats fail differently, and the
    // difference is the recovery: one entry's line went wrong, or the one digest covering all of them did.
    private const string SealRecovery =
        RatchetSentence +
        "If that entry was edited by hand, restore its line from version control. " +
        "If this file came out of a merge, resolve the conflict by keeping whole entry lines from either side rather " +
        "than editing one — every line carries its own seal.";

    private const string LegacyDigestRecovery =
        RatchetSentence +
        "If the file was edited by hand, restore it from version control. " +
        "If it came out of a merge, both committed sides carry a digest that is stale for the merged entries, so check " +
        "out one side's file and re-run 'sliderule baseline accept-reductions'.";

    /// <summary>
    ///     Builds the <see cref="BaselineIndex" /> for a model's ratcheted rules — Migrate and Quarantine
    ///     containment (any rule with a <see cref="ArchRule.BaselinePath" />): resolves each rule's
    ///     baseline path against <paramref name="solutionDirectory" />, parses each distinct file once
    ///     (verifying its integrity — tamper fails fast), and captures the matching section.
    /// </summary>
    /// <remarks>
    ///     A missing file or missing section leaves the rule uncaptured. A scope tripwire (no baseline
    ///     path) is skipped. A declared file the reader refuses throws first; the index then carries
    ///     <see cref="BaselineOrphanScanner" />'s scan of every baseline file in the solution, so each reader of the
    ///     report sees the sections no rule names and the files that could not be read.
    /// </remarks>
    public static BaselineIndex LoadForModel(ArchitectureModel model, string solutionDirectory)
    {
        var read = new Dictionary<string, BaselineDocument>(PathComparison.Comparer);
        BaselineIndex index = BaselineIndexBuilder.ForRules(
            model.Rules, baselinePath => ReadDeclared(ResolvePath(baselinePath, solutionDirectory), read));
        BaselineOrphanScan scan = BaselineOrphanScanner.Scan(model, solutionDirectory, read);
        return index.WithOrphanScan(scan);
    }

    /// <summary>Reads and verifies a baseline file.</summary>
    /// <param name="absolutePath">Absolute path to the baseline file.</param>
    /// <returns>The verified document, or <see langword="null" /> when the file does not exist (uncaptured).</returns>
    /// <exception cref="UserErrorException">
    ///     The file is malformed JSON, violates the schema, or fails its integrity check. The message names
    ///     the path; an integrity failure also carries the recovery hint, and under the current version the
    ///     entry that failed.
    /// </exception>
    public static BaselineDocument? TryReadDocument(string absolutePath)
    {
        if (!File.Exists(absolutePath)) return null;

        string text = File.ReadAllText(absolutePath);
        BaselineReadResult result = BaselineDocumentReader.Read(text);
        if (result.Refusal is { } refusal) throw Refuse(absolutePath, refusal);

        return result.Document!;
    }

    // A declared file read for the index, kept for the orphan scan, which would otherwise read and seal-check it again.
    private static BaselineDocument? ReadDeclared(string absolutePath, IDictionary<string, BaselineDocument> read)
    {
        BaselineDocument? document = TryReadDocument(absolutePath);
        if (document is not null) read[absolutePath] = document;
        return document;
    }

    /// <summary>
    ///     Composes <paramref name="document" /> canonically and writes it to <paramref name="absolutePath" />
    ///     on <see cref="WholeFile" />'s terms, reporting whether the file changed.
    /// </summary>
    public static WriteOutcome Write(string absolutePath, BaselineDocument document)
    {
        string composed = BaselineFormat.ComposeFile(document.Sections);

        // Composing is this class's half; putting the bytes down is the shared one. The comparison, the
        // encoding and the atomicity are all in the writer, so a baseline and any other whole file this tool
        // owns cannot come to disagree about what "unchanged" means (the round-trip pins are the oracle).
        return WholeFile.Write(absolutePath, composed);
    }

    /// <summary>
    ///     Deletes the baseline file at <paramref name="absolutePath" />. A file that does not exist is not an
    ///     error.
    /// </summary>
    public static void Delete(string absolutePath)
    {
        File.Delete(absolutePath);
    }

    /// <summary>Resolves a model baseline path (forward-slash, usually relative) against the solution directory.</summary>
    public static string ResolvePath(string baselinePath, string solutionDirectory)
    {
        return Path.GetFullPath(baselinePath, solutionDirectory);
    }

    /// <summary>
    ///     A refusal's words without the path: a syntax refusal with its position, as the refusal a run prints
    ///     carries it, and every other kind as its sentence.
    /// </summary>
    // Only a syntax refusal carries its position — a schema refusal is a sentence about a key, and a line number
    // in front of it would point at the key the reader is already being told about.
    internal static string RefusalDetail(BaselineRefusal refusal)
    {
        return refusal.Kind == BaselineRefusalKind.Syntax ? refusal.Describe() : refusal.Detail;
    }

    // A refusal value in the words a person at a terminal reads, which is the whole of what this layer adds
    // to it: the path, and for an integrity failure the recovery for the format that failed.
    private static UserErrorException Refuse(string path, BaselineRefusal refusal)
    {
        string detail = RefusalDetail(refusal);
        return refusal.Kind switch
        {
            BaselineRefusalKind.SealMismatch => IntegrityFailure(path, detail, SealRecovery),
            BaselineRefusalKind.LegacyDigestMismatch => IntegrityFailure(path, detail, LegacyDigestRecovery),
            _ => Malformed(path, detail)
        };
    }

    private static UserErrorException Malformed(string path, string detail)
    {
        return new UserErrorException($"Baseline file '{path}' is not valid: {detail}");
    }

    private static UserErrorException IntegrityFailure(string path, string detail, string recovery)
    {
        return new UserErrorException($"Baseline file '{path}' failed its integrity check: {detail} {recovery}");
    }
}
