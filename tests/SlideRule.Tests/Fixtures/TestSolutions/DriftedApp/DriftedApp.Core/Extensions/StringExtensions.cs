namespace DriftedApp;

// The core's copy of the shared name. DriftedApp.Core.Ansi declares its own copy in Utilities/StringExtensions.cs,
// with different members, so this file's reference to Cell is this project's alone: the satellite never compiles it.
// Both copies do reach System.Text.StringBuilder, each from a file of its own.
public static class StringExtensions
{
    public static int CellWidth(this string text)
    {
        return Cell.Measure(text);
    }

    public static string Padded(this string text, int width)
    {
        var builder = new System.Text.StringBuilder(text);
        return builder.Append(' ', width).ToString();
    }
}
