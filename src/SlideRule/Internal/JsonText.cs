using System.Globalization;
using System.Text;

namespace SlideRule.Internal;

/// <summary>
///     The two JSON tokens the hand-written composers emit that are not fixed text: a quoted string and an
///     integer.
/// </summary>
/// <remarks>
///     A baseline file's seal is computed over the bytes these return, so a change to either is a change to
///     the baseline format and moves every committed seal.
/// </remarks>
internal static class JsonText
{
    // Invariant digits. The culture-sensitive default would only bite on a negative sign, which no value
    // written today carries — but these files are wire formats read on machines far from the one that
    // wrote them.
    internal static string Digits(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    internal static string Quote(string value)
    {
        return new StringBuilder(value.Length + 2).AppendQuoted(value).ToString();
    }

    /// <summary>Appends <paramref name="value" /> as the quoted token <see cref="Quote" /> returns.</summary>
    // Escapes what JSON requires and nothing more: the quote, the backslash and control characters. The
    // escape path is pinned by test.
    internal static StringBuilder AppendQuoted(this StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value)
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < 0x20)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }

        builder.Append('"');
        return builder;
    }
}
