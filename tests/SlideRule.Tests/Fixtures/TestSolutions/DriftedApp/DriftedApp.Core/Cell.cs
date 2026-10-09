namespace DriftedApp;

// Declared by this project alone. The core's copy of StringExtensions reaches it, and so does
// DriftedApp.Client through a real ProjectReference, which is the edge that must survive.
public static class Cell
{
    public static int Measure(string text)
    {
        return text.Length;
    }
}
