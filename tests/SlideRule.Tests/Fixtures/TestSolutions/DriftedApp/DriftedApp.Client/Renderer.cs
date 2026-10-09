namespace DriftedApp.Client;

// A genuine cross-project reference: Cell is declared by DriftedApp.Core and by nothing else, so this
// edge is one the project files really do declare.
public class Renderer
{
    public int Width(string text)
    {
        return DriftedApp.Cell.Measure(text);
    }
}
