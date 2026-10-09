namespace DriftedApp;

// The satellite's copy of the shared name: the same full name as the core's copy, a different file, and members the
// core's copy has not. Outside this project it reaches only System.Text.StringBuilder, which both copies reach.
public static class StringExtensions
{
    public static string EscapeMarkup(this string text)
    {
        return text.Replace("[", "[[");
    }

    public static string Boxed(this string text)
    {
        var builder = new System.Text.StringBuilder("[");
        return builder.Append(text).Append(']').ToString();
    }
}
