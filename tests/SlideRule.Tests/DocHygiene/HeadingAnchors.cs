using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using SlideRule.Tests.TestSupport;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The heading-anchor checker the fragment-link gate runs on. It computes the anchors GitHub gives a
///     markdown file's headings, finds every link that carries a <c>#fragment</c>, and decides whether each
///     one names a heading of the file it points at. All of the logic lives here so the gate and the unit
///     tests exercise the same code path.
/// </summary>
/// <remarks>
///     The slug rules are github-slugger's, the package that reproduces GitHub's heading anchors: lower-case
///     the heading's rendered text, keep letters, marks, decimal and letter numbers, connector punctuation and
///     hyphens, turn each space into a hyphen, and drop every other character. A repeated slug takes the first
///     numbered suffix no earlier heading has taken.
/// </remarks>
internal static class HeadingAnchors
{
    private const string RepositoryUrl = "https://github.com/andypgray/sliderule";

    // The characters a URL found in running text cannot contain here: whitespace, quotes, brackets and the
    // markdown punctuation that ends an inline link.
    private const string UrlCharacter = @"[^\s<>""'`()\[\]]";

    // The optional closing run of hashes: preceded by whitespace or standing alone, followed by nothing else.
    private static readonly Regex AtxClosing = new(@"(?:^|[ \t]+)#+[ \t]*$", RegexOptions.CultureInvariant);

    // A code span, delimited by two backtick runs of the same length, neither touching another backtick.
    private static readonly Regex CodeSpan =
        new(@"(?<!`)(?<run>`+)(?!`)(?<body>.+?)(?<!`)\k<run>(?!`)", RegexOptions.CultureInvariant);

    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.CultureInvariant);

    private static readonly Regex ReferenceLinkText =
        new(@"\[(?<text>[^\]]*)\]\[[^\]]*\]", RegexOptions.CultureInvariant);

    private static readonly Regex Autolink = new(@"<(?<url>(?:https?|mailto):[^>\s]*)>", RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTag = new(@"</?[A-Za-z][^>]*>", RegexOptions.CultureInvariant);

    // An underscore run that touches a non-word character or an end of the text is emphasis markup, which
    // renders as nothing; one inside a word is a literal underscore, which the slug keeps.
    private static readonly Regex EdgeUnderscores = new(@"(?<!\w)_+|_+(?!\w)", RegexOptions.CultureInvariant);

    // Stands in for a code span while the rest of a heading is rendered, so no markup rule reaches its
    // content. Private-use characters, which no heading here spells.
    private static readonly Regex CodePlaceholder = new("(?<index>[0-9]+)", RegexOptions.CultureInvariant);

    // The target of an inline link that carries a fragment, without its angle brackets or title.
    private static readonly Regex InlineLinkTarget = new(
        @"\]\(\s*<?(?<target>[^\s()<>]*#[^\s()<>]*)>?(?:\s+(?:""[^""]*""|'[^']*'))?\s*\)",
        RegexOptions.CultureInvariant);

    // The target of a reference definition that carries a fragment, without its angle brackets.
    private static readonly Regex ReferenceDefinitionTarget = new(
        @"^ {0,3}\[[^\]]+\]:\s*<?(?<target>[^\s<>]*#[^\s<>]*)>?(?:\s|$)",
        RegexOptions.CultureInvariant);

    private static readonly Regex AbsoluteLink = new(
        $@"{Regex.Escape(RepositoryUrl)}(?:/{UrlCharacter}*?)?#{UrlCharacter}*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex Scheme = new("^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The anchor github-slugger derives from a heading's rendered text, before any suffix that tells
    ///     repeated headings apart.
    /// </summary>
    private static string Slug(string headingText)
    {
        string lowered = headingText.ToLowerInvariant();
        StringBuilder slug = new(lowered.Length);

        foreach (Rune rune in lowered.EnumerateRunes())
            if (rune.Value == ' ')
                slug.Append('-');
            else if (rune.Value == '-' || IsKept(Rune.GetUnicodeCategory(rune)))
                slug.Append(rune.ToString());

        return slug.ToString();
    }

    /// <summary>
    ///     Every anchor the ATX headings of <paramref name="docText" /> get, in document order, each repeated
    ///     heading numbered the way github-slugger numbers it. Headings inside fenced code blocks are not
    ///     headings, so the cards a page quotes add no anchors to it.
    /// </summary>
    public static IReadOnlyList<string> Anchors(string docText)
    {
        Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
        IEnumerable<string> headings = DocProse.HeadingLines(docText)
            .Select(static line => HeadingText(line.Text));

        return headings.Select(heading => Unique(Slug(Render(heading)), occurrences))
            .ToList();
    }

    /// <summary>
    ///     Every link in <paramref name="text" /> that carries a fragment, each with the 1-based line it sits
    ///     on. In a markdown <paramref name="doc" /> that is every inline link and reference definition, and
    ///     every absolute link into this repository written out in the prose; fenced code, HTML comments and
    ///     inline code spans are not read. Any other file is read whole for the absolute form alone, because
    ///     what it carries is a string a program prints, not a link in markup.
    /// </summary>
    public static IReadOnlyList<FragmentLink> Links(string doc, string text)
    {
        bool markdown = doc.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
        string scanned = markdown ? ProseOnly(text) : text.NormalizedLines();
        Func<string, IEnumerable<string>> targetsOn = markdown ? MarkdownTargets : AbsoluteTargets;

        return scanned.Split('\n')
            .SelectMany((line, index) => targetsOn(line)
                .Distinct()
                .Select(target => new FragmentLink(doc, index + 1, target)))
            .ToList();
    }

    /// <summary>
    ///     The anchors of each doc in <paramref name="docs" />, keyed by its repository-relative path: the
    ///     index <see cref="Check" /> reads.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlySet<string>> AnchorIndex(
        IEnumerable<(string Path, string Text)> docs)
    {
        return docs.ToDictionary(
            static doc => doc.Path,
            static doc => (IReadOnlySet<string>)Anchors(doc.Text)
                .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     Decides whether <paramref name="link" /> names a heading of the file it points at: <see langword="null" />
    ///     when it does or when it points outside this repository's files, otherwise the finding that names what is
    ///     wrong. <paramref name="anchorIndex" /> holds the anchors of every tracked markdown file (see
    ///     <see cref="AnchorIndex" />); a path it does not hold is not a tracked markdown file.
    /// </summary>
    /// <remarks>
    ///     A relative path resolves from the linking file's directory, or from the repository root when it starts
    ///     with a slash. An absolute link resolves only as a file view on <c>main</c>, the branch this gate reads; a
    ///     file view at any other ref is a finding, because what that ref holds is not what the gate can see. The
    ///     repository's home page, its issues, actions and releases, and every other host are not files here, so
    ///     they pass unread.
    /// </remarks>
    public static string? Check(FragmentLink link, IReadOnlyDictionary<string, IReadOnlySet<string>> anchorIndex)
    {
        int hash = link.Target.IndexOf('#');
        string path = link.Target[..hash];
        string fragment = Uri.UnescapeDataString(link.Target[(hash + 1)..]);
        if (fragment.Length == 0) return null;

        string file;
        if (Scheme.IsMatch(path))
        {
            if (FileView(path) is not { } view) return null;
            if (view.Ref != "main")
                return $"{Where(link)} views the file at '{view.Ref}'; link it on main, the branch this gate reads.";

            file = view.File;
        }
        else
        {
            string? resolved = ResolveRelative(link.Doc, Uri.UnescapeDataString(path));
            if (resolved is null) return $"{Where(link)} climbs above the repository root.";

            file = resolved;
        }

        IReadOnlySet<string>? anchors = anchorIndex.GetValueOrDefault(file);
        if (anchors is null) return $"{Where(link)} points at {file}, which is not a tracked markdown file.";

        return anchors.Contains(fragment) ? null : $"{Where(link)} has no heading #{fragment} in {file}.";
    }

    private static bool IsKept(UnicodeCategory category)
    {
        return category is UnicodeCategory.UppercaseLetter
            or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.LetterNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    // The heading's own text, closing hashes stripped and trimmed.
    private static string HeadingText(string headingLine)
    {
        int opening = DocProse.AtxHeadingOpening.Match(headingLine)
            .Length;
        string content = headingLine[opening..];

        return AtxClosing.Replace(content, string.Empty)
            .Trim();
    }

    // The text GitHub reads a heading's anchor from: what the rendered heading shows. Code spans are set aside
    // first and restored last, so their content reaches the slug verbatim.
    private static string Render(string heading)
    {
        List<string> spans = new();
        string outside = CodeSpan.Replace(heading, span =>
        {
            spans.Add(CodeSpanContent(span.Groups["body"].Value));
            return $"{spans.Count - 1}";
        });

        string withoutImages = Image.Replace(outside, string.Empty);
        string linksAsText = DocProse.LinksAsText(withoutImages);
        string referencesAsText = ReferenceLinkText.Replace(linksAsText, static link => link.Groups["text"].Value);
        string autolinksAsText = Autolink.Replace(referencesAsText, static link => link.Groups["url"].Value);
        string withoutTags = HtmlTag.Replace(autolinksAsText, string.Empty);
        string decoded = WebUtility.HtmlDecode(withoutTags);
        string withoutEmphasis = EdgeUnderscores.Replace(decoded, string.Empty);

        return CodePlaceholder.Replace(withoutEmphasis, placeholder => spans[int.Parse(placeholder.Groups["index"].Value)]);
    }

    // A code span loses one space from each end when it has one at both and is not all spaces.
    private static string CodeSpanContent(string body)
    {
        bool padded = body.Length >= 2 && body[0] == ' ' && body[^1] == ' ' && body.Trim(' ').Length > 0;

        return padded ? body[1..^1] : body;
    }

    // github-slugger's loop: every slug it hands out is recorded, so a numbered suffix never collides with a
    // heading whose own text ends in one.
    private static string Unique(string slug, Dictionary<string, int> occurrences)
    {
        string result = slug;
        while (occurrences.ContainsKey(result))
        {
            occurrences[slug]++;
            result = $"{slug}-{occurrences[slug]}";
        }

        occurrences[result] = 0;

        return result;
    }

    // The markdown's prose with fenced code, HTML comments and inline code spans blanked out, every line kept
    // where it was, so a link found in the result sits on the line it has in the file.
    private static string ProseOnly(string text)
    {
        string unfenced = DocProse.BlankFences(text);

        return DocProse.BlankMatches(unfenced, [DocProse.HtmlComment, CodeSpan]);
    }

    private static IEnumerable<string> MarkdownTargets(string line)
    {
        IEnumerable<string> inline = InlineLinkTarget.Matches(line)
            .Select(static match => match.Groups["target"].Value);
        IEnumerable<string> definitions = ReferenceDefinitionTarget.Matches(line)
            .Select(static match => match.Groups["target"].Value);

        return inline.Concat(definitions)
            .Concat(AbsoluteTargets(line));
    }

    private static IEnumerable<string> AbsoluteTargets(string line)
    {
        return AbsoluteLink.Matches(line)
            .Select(static match => match.Value.TrimEnd('.', ',', ';', ':', ')'));
    }

    // The ref and the repository-relative file an absolute link views, or null for a link that is not a file
    // view of this repository.
    private static (string Ref, string File)? FileView(string url)
    {
        if (!url.StartsWith(RepositoryUrl + "/", StringComparison.OrdinalIgnoreCase)) return null;

        string[] segments = url[(RepositoryUrl.Length + 1)..]
            .Split('/');
        if (segments.Length < 3 || segments[0] is not ("blob" or "tree")) return null;

        return (segments[1], string.Join("/", segments[2..]));
    }

    // Resolves a relative link from the linking file's directory, or from the root for a leading slash, with
    // "." and ".." segments applied; null when the path climbs above the root.
    private static string? ResolveRelative(string doc, string path)
    {
        if (path.Length == 0) return doc;

        List<string> resolved = path.StartsWith('/')
            ? new List<string>()
            : doc.Split('/')[..^1]
                .ToList();

        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (resolved.Count == 0) return null;

                resolved.RemoveAt(resolved.Count - 1);
                continue;
            }

            resolved.Add(segment);
        }

        return string.Join("/", resolved);
    }

    private static string Where(FragmentLink link)
    {
        return $"{link.Doc}:{link.Line}: {link.Target}";
    }

    /// <summary>
    ///     One link carrying a fragment: the <paramref name="Doc" /> it sits in, the 1-based
    ///     <paramref name="Line" /> it sits on, and its <paramref name="Target" /> as written.
    /// </summary>
    internal sealed record FragmentLink(string Doc, int Line, string Target);
}
