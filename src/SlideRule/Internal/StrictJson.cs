using System.Globalization;
using System.Text;

namespace SlideRule.Internal;

/// <summary>
///     A strict JSON reader for the files this tool writes itself, with no package behind it.
/// </summary>
/// <remarks>
///     <para>
///         The grammar is RFC 8259 with four narrowings, each of them a refusal rather than a
///         tolerance. There is no <c>null</c>: a file that has nothing to say about an optional field
///         leaves the field out. A number is an integer, <c>-?(0|[1-9][0-9]*)</c>, so a fraction or an
///         exponent is refused and an integer of any length still reads — <see cref="StrictJsonValue" />
///         answers separately for whether it fits an <see cref="int" />. A key may appear only once in
///         one object, though the same key in two different objects is ordinary. And nesting is capped
///         at <see cref="MaxDepth" /> containers, objects and arrays counting together, which is what
///         makes the recursive descent below safe against a hostile file.
///     </para>
///     <para>
///         Everything else that a lenient reader forgives is refused too: a trailing comma, a comment,
///         and any content after the root value other than whitespace. Whitespace is space, tab, line
///         feed and carriage return, between tokens only. The root may be any value.
///     </para>
///     <para>
///         Strings are read whole: every character at or above U+0020 other than a quote and a
///         backslash passes through verbatim, including an unescaped surrogate code unit; a character
///         below U+0020 must be escaped; and the escapes are the eight of the standard plus
///         <c>\uXXXX</c>, whose four hex digits may be of either case. A <c>\u</c> escape naming half a
///         surrogate pair must be joined by its other half, also escaped.
///     </para>
///     <para>
///         Nothing here throws for content. A refusal is a <see cref="StrictJsonError" /> naming what is
///         wrong and where, the first one met wins, and parsing stops there. A null argument is a
///         different thing — a caller bug — and throws.
///     </para>
/// </remarks>
internal static class StrictJson
{
    /// <summary>
    ///     How many containers may be open at once. A brace or bracket met while this many are already
    ///     open is refused, so a document nested exactly this deep reads and one deeper does not.
    /// </summary>
    internal const int MaxDepth = 64;

    /// <summary>
    ///     Reads <paramref name="text" /> whole. On success <paramref name="root" /> is the document's
    ///     one value and <paramref name="error" /> is null; on a refusal <paramref name="root" /> is null
    ///     and <paramref name="error" /> says what is wrong and where.
    /// </summary>
    internal static bool TryParse(string text, out StrictJsonValue? root, out StrictJsonError? error)
    {
        Guard.NotNull(text, nameof(text));

        var parser = new Parser(text);
        return parser.TryRead(out root, out error);
    }

    /// <summary>
    ///     One pass over one text: the position it has reached, how many containers are open there, and
    ///     the refusal that ended it.
    /// </summary>
    /// <remarks>
    ///     Every reader below returns a null result — a null value, a null string, or false — once
    ///     <see cref="_error" /> is set, and every caller returns straight back up. That is what makes
    ///     "the first refusal wins" true of the whole class rather than of one method: nothing downstream
    ///     of a refusal ever runs, so nothing can overwrite it.
    /// </remarks>
    private sealed class Parser
    {
        private const string ExpectedValue = "a value";
        private const string ExpectedValueOrCloseBracket = "a value or ']'";
        private const string ExpectedKeyOrCloseBrace = "a key or '}'";
        private const string ExpectedKey = "a key";
        private const string ExpectedColon = "':'";
        private const string ExpectedCommaOrCloseBrace = "',' or '}'";
        private const string ExpectedCommaOrCloseBracket = "',' or ']'";
        private const string ExpectedDigitAfterMinus = "a digit after '-'";
        private const string ExpectedEndOfText = "the end of the text";

        private const string NullRefused = "null is not allowed; an optional field is left out instead.";
        private const string FractionRefused = "a number must be an integer, with no fraction and no exponent.";
        private const string LeadingZeroRefused = "a number may not start with a zero unless it is zero.";
        private const string UnclosedString = "the string is not closed before the end of the text.";
        private const string HexDigitsRequired = "'\\u' must be followed by four hex digits.";

        private readonly string _text;
        private int _column = 1;
        private int _depth;
        private StrictJsonError? _error;
        private int _index;
        private int _line = 1;

        internal Parser(string text)
        {
            _text = text;
        }

        private bool AtEnd => _index >= _text.Length;

        private JsonPosition Here => new(_line, _column, _index);

        internal bool TryRead(out StrictJsonValue? root, out StrictJsonError? error)
        {
            SkipWhitespace();
            StrictJsonValue? value = ReadValue(ExpectedValue);

            if (value is not null)
            {
                SkipWhitespace();
                if (!AtEnd) Expected(ExpectedEndOfText, atValuePosition: false);
            }

            if (_error is not null)
            {
                root = null;
                error = _error;
                return false;
            }

            root = value;
            error = null;
            return true;
        }

        private StrictJsonValue? ReadValue(string what)
        {
            if (AtEnd) return RefusedValue(what);

            char c = _text[_index];
            switch (c)
            {
                case '{':
                    return ReadObject();
                case '[':
                    return ReadArray();
                case '"':
                    return ReadStringValue();
                case '-':
                    return ReadNumber();
                default:
                    if (IsDigit(c)) return ReadNumber();

                    return IsAsciiLetter(c) ? ReadWord(what) : RefusedValue(what);
            }
        }

        // A bare word where a value belongs. The whole run of letters is read before it is judged, so
        // that `true` and `false` are values, `null` is the refusal that names the convention, and
        // anything else is quoted back to the author in full rather than one letter at a time.
        private StrictJsonValue? ReadWord(string what)
        {
            JsonPosition start = Here;
            string word = LetterRun();

            if (string.Equals(word, "null", StringComparison.Ordinal))
            {
                Record(NullRefused, start);
                return null;
            }

            bool isTrue = string.Equals(word, "true", StringComparison.Ordinal);
            bool isFalse = string.Equals(word, "false", StringComparison.Ordinal);
            if (!isTrue && !isFalse) return RefusedValue(what);

            StrictJsonKind kind = isTrue ? StrictJsonKind.True : StrictJsonKind.False;
            AdvanceBy(word.Length);
            return new StrictJsonValue(kind, start);
        }

        private StrictJsonValue? ReadNumber()
        {
            JsonPosition start = Here;

            if (_text[_index] == '-')
            {
                Advance();
                if (AtEnd || !IsDigit(_text[_index])) return RefusedValue(ExpectedDigitAfterMinus);
            }

            bool leadsWithZero = _text[_index] == '0';
            Advance();
            if (leadsWithZero && !AtEnd && IsDigit(_text[_index]))
            {
                RecordHere(LeadingZeroRefused);
                return null;
            }

            while (!AtEnd && IsDigit(_text[_index])) Advance();

            if (!AtEnd && IsFractionOrExponent(_text[_index]))
            {
                RecordHere(FractionRefused);
                return null;
            }

            string asWritten = _text.Substring(start.Offset, _index - start.Offset);
            return new StrictJsonValue(StrictJsonKind.Number, asWritten, start);
        }

        private StrictJsonValue? ReadObject()
        {
            JsonPosition start = Here;
            if (_depth >= MaxDepth) return TooDeep();

            _depth++;
            Advance();

            var members = new List<StrictJsonProperty>();
            var byName = new Dictionary<string, StrictJsonValue>(StringComparer.Ordinal);
            SkipWhitespace();
            if (!AtEnd && _text[_index] == '}') return CloseObject(members, byName, start);

            string what = ExpectedKeyOrCloseBrace;
            while (true)
            {
                SkipWhitespace();
                if (AtEnd || _text[_index] != '"') return RefusedToken(what);

                JsonPosition key = Here;
                string? name = ReadStringText();
                if (name is null) return null;

                if (byName.ContainsKey(name))
                {
                    string quoted = JsonText.Quote(name);
                    Record("the key " + quoted + " appears twice in one object.", key);
                    return null;
                }

                SkipWhitespace();
                if (AtEnd || _text[_index] != ':') return RefusedToken(ExpectedColon);

                Advance();
                SkipWhitespace();
                StrictJsonValue? value = ReadValue(ExpectedValue);
                if (value is null) return null;

                members.Add(new StrictJsonProperty(name, value, key));
                byName.Add(name, value);

                SkipWhitespace();
                if (!AtEnd && _text[_index] == ',')
                {
                    Advance();
                    what = ExpectedKey;
                    continue;
                }

                if (AtEnd || _text[_index] != '}') return RefusedToken(ExpectedCommaOrCloseBrace);

                return CloseObject(members, byName, start);
            }
        }

        private StrictJsonValue? ReadArray()
        {
            JsonPosition start = Here;
            if (_depth >= MaxDepth) return TooDeep();

            _depth++;
            Advance();

            var elements = new List<StrictJsonValue>();
            SkipWhitespace();
            if (!AtEnd && _text[_index] == ']') return CloseArray(elements, start);

            string what = ExpectedValueOrCloseBracket;
            while (true)
            {
                SkipWhitespace();
                StrictJsonValue? element = ReadValue(what);
                if (element is null) return null;

                elements.Add(element);

                SkipWhitespace();
                if (!AtEnd && _text[_index] == ',')
                {
                    Advance();
                    what = ExpectedValue;
                    continue;
                }

                if (AtEnd || _text[_index] != ']') return RefusedToken(ExpectedCommaOrCloseBracket);

                return CloseArray(elements, start);
            }
        }

        private StrictJsonValue CloseObject(
            IReadOnlyList<StrictJsonProperty> members, Dictionary<string, StrictJsonValue> byName, JsonPosition start)
        {
            Advance();
            _depth--;
            return new StrictJsonValue(members, byName, start);
        }

        private StrictJsonValue CloseArray(IReadOnlyList<StrictJsonValue> elements, JsonPosition start)
        {
            Advance();
            _depth--;
            return new StrictJsonValue(elements, start);
        }

        private StrictJsonValue? ReadStringValue()
        {
            JsonPosition start = Here;
            string? text = ReadStringText();
            if (text is null) return null;

            return new StrictJsonValue(StrictJsonKind.String, text, start);
        }

        // The text between the quotes, escapes resolved. Null once a refusal is recorded — which is also
        // how a key is read, so an object's key and a string value refuse identically.
        private string? ReadStringText()
        {
            JsonPosition quote = Here;
            Advance();

            // Most strings carry no escape, and those are one substring rather than a copy per character. The
            // run stops at anything the loop below has to judge, a line feed among them, so it never crosses a
            // line and moving the column by its length is exact.
            int start = _index;
            int end = start;
            while (end < _text.Length && IsVerbatim(_text[end])) end++;

            int run = end - start;
            _index = end;
            _column += run;
            if (!AtEnd && _text[_index] == '"')
            {
                Advance();
                return _text.Substring(start, run);
            }

            var builder = new StringBuilder();
            builder.Append(_text, start, run);
            while (true)
            {
                if (AtEnd)
                {
                    Record(UnclosedString, quote);
                    return null;
                }

                char c = _text[_index];
                if (c == '"')
                {
                    Advance();
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    bool escaped = ReadEscape(builder, quote);
                    if (!escaped) return null;

                    continue;
                }

                if (c < ' ')
                {
                    string named = CodePoint(c);
                    RecordHere("a control character (" + named + ") must be escaped inside a string.");
                    return null;
                }

                builder.Append(c);
                Advance();
            }
        }

        private bool ReadEscape(StringBuilder builder, JsonPosition quote)
        {
            JsonPosition backslash = Here;
            Advance();

            if (AtEnd)
            {
                Record(UnclosedString, quote);
                return false;
            }

            char c = _text[_index];
            switch (c)
            {
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '/':
                    builder.Append('/');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    return ReadUnicodeEscape(builder, backslash);
                default:
                    string shown = IsPrintableAscii(c) ? c.ToString() : CodePoint(c);
                    Record("'\\" + shown + "' is not a JSON escape.", backslash);
                    return false;
            }

            Advance();
            return true;
        }

        // Entered with the index on the 'u'; the position argument is the backslash before it, which is
        // where both of this escape's refusals are reported.
        private bool ReadUnicodeEscape(StringBuilder builder, JsonPosition backslash)
        {
            int digitsAt = _index + 1;
            if (!TryReadHex(digitsAt, out int value))
            {
                Record(HexDigitsRequired, backslash);
                return false;
            }

            var unit = (char)value;
            if (char.IsLowSurrogate(unit))
            {
                Record(HalfAPair(value), backslash);
                return false;
            }

            if (!char.IsHighSurrogate(unit))
            {
                builder.Append(unit);
                AdvanceTo(digitsAt + 4);
                return true;
            }

            int pairAt = digitsAt + 4;
            if (!TryReadLowSurrogateEscape(pairAt, out char low))
            {
                Record(HalfAPair(value), backslash);
                return false;
            }

            builder.Append(unit);
            builder.Append(low);
            AdvanceTo(pairAt + 6);
            return true;
        }

        // A well-formed \u escape naming a low surrogate, and nothing else. A high surrogate followed by
        // any other escape is the missing-half refusal rather than a complaint about the second escape:
        // the pair is one character, so the fault is at the character, and that is where a reader looks.
        private bool TryReadLowSurrogateEscape(int at, out char low)
        {
            low = '\0';
            if (at + 1 >= _text.Length) return false;
            if (_text[at] != '\\' || _text[at + 1] != 'u') return false;
            if (!TryReadHex(at + 2, out int value)) return false;

            var unit = (char)value;
            if (!char.IsLowSurrogate(unit)) return false;

            low = unit;
            return true;
        }

        private bool TryReadHex(int at, out int value)
        {
            value = 0;
            if (at + 4 > _text.Length) return false;

            var accumulated = 0;
            for (var i = 0; i < 4; i++)
            {
                int digit = HexValue(_text[at + i]);
                if (digit < 0) return false;

                accumulated = accumulated * 16 + digit;
            }

            value = accumulated;
            return true;
        }

        private static string HalfAPair(int value)
        {
            var digits = value.ToString("X4", CultureInfo.InvariantCulture);
            return "the escape '\\u" + digits
                                     + "' is one half of a surrogate pair and the other half is missing.";
        }

        private StrictJsonValue? TooDeep()
        {
            var cap = MaxDepth.ToString(CultureInfo.InvariantCulture);
            RecordHere("the nesting is deeper than " + cap + " levels.");
            return null;
        }

        // Somewhere a value belongs, so a bare word is read whole before it is quoted back.
        private StrictJsonValue? RefusedValue(string what)
        {
            Expected(what, atValuePosition: true);
            return null;
        }

        // Somewhere a single punctuation mark or a key belongs, where a run of letters would say nothing
        // the first letter does not.
        private StrictJsonValue? RefusedToken(string what)
        {
            Expected(what, atValuePosition: false);
            return null;
        }

        private void Expected(string what, bool atValuePosition)
        {
            string found = DescribeFound(atValuePosition);
            RecordHere("expected " + what + ", found " + found + ".");
        }

        // What the offending character is called. A bare word at a value position is quoted whole,
        // because the author wrote one word and a single letter would not be the thing they typed.
        private string DescribeFound(bool atValuePosition)
        {
            if (AtEnd) return ExpectedEndOfText;

            char c = _text[_index];
            if (atValuePosition && IsAsciiLetter(c)) return "'" + LetterRun() + "'";

            return IsPrintableAscii(c) ? "'" + c + "'" : CodePoint(c);
        }

        private string LetterRun()
        {
            int end = _index;
            while (end < _text.Length && IsAsciiLetter(_text[end])) end++;

            return _text.Substring(_index, end - _index);
        }

        private void RecordHere(string reason)
        {
            Record(reason, Here);
        }

        private void Record(string reason, JsonPosition at)
        {
            _error = new StrictJsonError(reason, at);
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && IsWhitespace(_text[_index])) Advance();
        }

        private void AdvanceTo(int target)
        {
            while (_index < target) Advance();
        }

        private void AdvanceBy(int count)
        {
            for (var i = 0; i < count; i++) Advance();
        }

        // A line ends at the line feed alone, so a carriage return just takes a column and a CRLF file
        // reports the positions its line-feed-only twin would.
        private void Advance()
        {
            if (_text[_index] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _index++;
        }

        private static string CodePoint(char c)
        {
            var value = (int)c;
            return "U+" + value.ToString("X4", CultureInfo.InvariantCulture);
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;

            return -1;
        }

        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r';
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsPrintableAscii(char c)
        {
            return c >= ' ' && c <= '~';
        }

        // What a string carries as itself: anything but the quote that closes it, the backslash that opens an
        // escape, and a control character, which must be escaped.
        private static bool IsVerbatim(char c)
        {
            return c != '"' && c != '\\' && c >= ' ';
        }

        private static bool IsFractionOrExponent(char c)
        {
            return c == '.' || c == 'e' || c == 'E';
        }
    }
}
