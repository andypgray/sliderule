using SlideRule.Rendering;

namespace SlideRule.Validation;

/// <summary>
///     Where in the spec source a validation error's <c>arch.Rule</c>, <c>arch.Scope</c> or
///     <c>arch.Member</c> call was written: a file name and a 1-based line, so each error is somewhere to
///     jump to.
/// </summary>
// Diagnostics metadata only: it never enters the model or any render target, so the model stays
// location-free and deterministic. File name only — never the machine-specific full compile-time
// path — so goldens stay byte-identical across build machines (fixture specs build in temp
// directories).
internal sealed class SpecSourceLocation
{
    private SpecSourceLocation(string file, int line)
    {
        File = file;
        Line = line;
    }

    public string File { get; }

    public int Line { get; }

    // The leaf comes from PathFormat.Leaf, not Path.GetFileName: a spec DLL built on Windows must render
    // identically when checked on macOS or Linux. A null or empty path (no [CallerFilePath] supplied) yields
    // null, and the error renders with no location prefix.
    internal static SpecSourceLocation? Capture(string? filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath)) return null;

        string file = PathFormat.Leaf(filePath!);
        return new SpecSourceLocation(file, line);
    }

    /// <summary>Returns <c>file:line</c>, the form a validation error message opens with.</summary>
    public override string ToString()
    {
        return $"{File}:{Line}";
    }
}
