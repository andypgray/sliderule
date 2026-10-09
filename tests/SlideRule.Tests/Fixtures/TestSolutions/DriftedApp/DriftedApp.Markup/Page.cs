namespace DriftedApp.Markup;

// The shared name, bound from the only assembly this project references: the satellite's copy, which is
// also the only copy that declares EscapeMarkup.
public class Page
{
    public string Title(string text)
    {
        return DriftedApp.StringExtensions.EscapeMarkup(text);
    }
}
