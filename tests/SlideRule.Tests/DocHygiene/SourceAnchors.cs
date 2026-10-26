using System.Text.RegularExpressions;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The source-anchor checker. It reads a doc's fenced code blocks through <see cref="DocFences" />, lifts
///     every <c>file:line — message</c> anchor out of them, derives the content token the committed source
///     line must carry, and decides whether each anchor still matches its source — by content, or by a
///     landmark pin that keys a documented walkthrough edit to the committed line it lands on.
/// </summary>
internal static class SourceAnchors
{
    /// <summary>Which bucket resolved an anchor against its committed source, or that none did.</summary>
    public enum AnchorBucket
    {
        /// <summary>The derived token sits on the anchor's reported line.</summary>
        Content,

        /// <summary>A landmark pinned the committed line the anchor's documented edit is keyed to.</summary>
        Landmark,

        /// <summary>Neither bucket matched; the anchor no longer matches its committed source.</summary>
        Unresolved
    }

    private const char EmDash = (char)0x2014;

    private static readonly string[] ReferenceVerbs = ["references", "constructs", "injects", "exposes", "catches"];

    // Only anchors under src/ are read: every anchor doc quotes a solution that keeps its projects under src/
    // of the root its anchors resolve against. The one fenced anchor elsewhere, the landing page's legacy
    // stanza, is relative to a fixture solution only a Windows machine with a Framework build host can check,
    // so a captured fence holds it instead, and ReadmeAnchorGateTests holds any other anchor outside src/ to
    // sitting in one.
    private static readonly Regex AnchorLine =
        new($@"^\s*(src/\S+\.cs):(\d+) {EmDash} (.+)$", RegexOptions.CultureInvariant);

    // The anchor shape whatever the path: what AnchorLine reads before it narrows to src/.
    private static readonly Regex AnyPathAnchorLine =
        new($@"^\s*\S+\.cs:\d+ {EmDash} ", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Extracts every anchor from the fenced code blocks of <paramref name="docText" />. Anchors
    ///     outside fences are ignored, because inline prose short-forms restate the fenced anchors and are
    ///     commentary rather than the pin.
    /// </summary>
    public static IReadOnlyList<SourceAnchor> Extract(string doc, string docText)
    {
        List<SourceAnchor> anchors = new();

        foreach ((string text, int number) in DocFences.FencedLines(docText))
        {
            Match match = AnchorLine.Match(text);
            if (!match.Success) continue;

            string file = match.Groups[1].Value;
            int sourceLine = int.Parse(match.Groups[2].Value);
            string message = match.Groups[3]
                .Value.Trim();
            anchors.Add(new SourceAnchor(doc, number, file, sourceLine, message));
        }

        return anchors;
    }

    /// <summary>
    ///     Every line of the fenced code blocks of <paramref name="docText" /> that is shaped like an anchor but names
    ///     a path outside <c>src/</c>, so <see cref="Extract" /> does not read it, paired with its 1-based line number
    ///     in the doc.
    /// </summary>
    public static IReadOnlyList<(string Text, int Number)> AnchorLinesOutsideSrc(string docText)
    {
        return DocFences.FencedLines(docText)
            .Where(static line => AnyPathAnchorLine.IsMatch(line.Text)
                                  && !line.Text.TrimStart().StartsWith("src/", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    ///     Derives the content token the committed source line must contain from an anchor message's
    ///     tail. A <c>uses A.B.Member</c> tail yields the last two dotted segments (so an accessor keeps
    ///     its declaring type); a <c>references</c>/<c>constructs</c>/<c>injects</c>/<c>exposes</c>/
    ///     <c>catches</c> target yields the target's last dotted segment with any generic suffix removed;
    ///     a member tail ending in <c>()</c> yields the member name; and a bare dotted type yields its
    ///     last segment.
    /// </summary>
    public static string DeriveToken(string message)
    {
        string trimmed = message.Trim();

        if (TryVerbTarget(trimmed, "uses", out string usesTarget)) return LastTwoSegments(usesTarget);

        foreach (string verb in ReferenceVerbs)
            if (TryVerbTarget(trimmed, verb, out string target))
                return LastSegment(StripGeneric(target));

        if (trimmed.EndsWith("()", StringComparison.Ordinal)) return LastSegment(trimmed[..^2]);

        return LastSegment(StripGeneric(trimmed));
    }

    /// <summary>
    ///     Decides whether <paramref name="anchor" /> still matches its committed source. It matches when
    ///     the derived token sits on the reported line (the content bucket); failing that, when a landmark
    ///     for the anchor's key pins a committed line whose content holds the landmark snippet (the
    ///     landmark bucket). An anchor satisfying neither is unresolved, with a reason naming the doc, the
    ///     source line, and what was expected. <paramref name="exampleRoot" /> is the directory the
    ///     anchor's path hangs off, or empty when the doc quotes this repository's own sources and the path
    ///     is already repository-relative. <paramref name="readLines" /> reads a repository-relative path
    ///     into its lines, or returns <see langword="null" /> when the file is absent.
    /// </summary>
    public static AnchorResult Classify(
        SourceAnchor anchor,
        string exampleRoot,
        IReadOnlyDictionary<AnchorKey, Landmark> landmarks,
        Func<string, IReadOnlyList<string>?> readLines)
    {
        string repoRelativeFile = ExampleRoots.RepoRelative(exampleRoot, anchor.File);
        IReadOnlyList<string>? lines = readLines(repoRelativeFile);
        if (lines is null) return Unresolved(anchor, $"source file not found at {repoRelativeFile}");

        string token = DeriveToken(anchor.Message);
        bool contentMatch = anchor.Line >= 1
                            && anchor.Line <= lines.Count
                            && lines[anchor.Line - 1]
                                .Contains(token, StringComparison.Ordinal);
        if (contentMatch) return new AnchorResult(AnchorBucket.Content, null);

        AnchorKey key = new(exampleRoot, anchor.File, anchor.Line);
        if (landmarks.TryGetValue(key, out Landmark? landmark))
        {
            if (landmark.CommittedLine < 1 || landmark.CommittedLine > lines.Count)
                return Unresolved(
                    anchor,
                    $"landmark committed line {landmark.CommittedLine} is beyond end of file ({lines.Count} lines)");

            string committed = lines[landmark.CommittedLine - 1];
            if (committed.Contains(landmark.Snippet, StringComparison.Ordinal)) return new AnchorResult(AnchorBucket.Landmark, null);

            return Unresolved(
                anchor,
                $"landmark snippet '{landmark.Snippet}' not found on committed line {landmark.CommittedLine} (found: {committed.Trim()})");
        }

        if (anchor.Line < 1 || anchor.Line > lines.Count) return Unresolved(anchor, $"source line {anchor.Line} is beyond end of file ({lines.Count} lines) and no landmark pins it");

        return Unresolved(
            anchor,
            $"expected token '{token}' on line {anchor.Line} but found: {lines[anchor.Line - 1].Trim()}");
    }

    private static AnchorResult Unresolved(SourceAnchor anchor, string reason)
    {
        return new AnchorResult(AnchorBucket.Unresolved,
            $"{anchor.Doc}:{anchor.DocLine} -> {anchor.File}:{anchor.Line} ({anchor.Message}): {reason}.");
    }

    private static bool TryVerbTarget(string message, string verb, out string target)
    {
        var key = $" {verb} ";
        int index = message.IndexOf(key, StringComparison.Ordinal);
        if (index < 0)
        {
            target = string.Empty;
            return false;
        }

        target = message[(index + key.Length)..]
            .Trim();
        return true;
    }

    private static string StripGeneric(string text)
    {
        int index = text.IndexOf('<');
        return index < 0 ? text : text[..index];
    }

    private static string LastSegment(string dotted)
    {
        int index = dotted.LastIndexOf('.');
        return index < 0 ? dotted : dotted[(index + 1)..];
    }

    private static string LastTwoSegments(string dotted)
    {
        string[] segments = dotted.Split('.');
        return segments.Length >= 2
            ? $"{segments[^2]}.{segments[^1]}"
            : dotted;
    }

    /// <summary>The landmark lookup key: an anchor's example root, source file, and reported line.</summary>
    public readonly record struct AnchorKey(string ExampleRoot, string File, int Line);

    /// <summary>
    ///     A landmark pin. <see cref="CommittedLine" /> is the committed source line the documented
    ///     walkthrough edit is keyed to (the anchor's reported line is the post-edit position, which need
    ///     not equal it), and <see cref="Snippet" /> is text that committed line must still contain.
    /// </summary>
    public sealed record Landmark(int CommittedLine, string Snippet);

    /// <summary>
    ///     The outcome of classifying one anchor: the bucket that resolved it, and — when it is
    ///     <see cref="AnchorBucket.Unresolved" /> — the human-readable failure describing what was expected.
    /// </summary>
    public sealed record AnchorResult(AnchorBucket Bucket, string? Failure);
}
