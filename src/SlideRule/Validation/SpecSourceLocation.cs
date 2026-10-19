using SlideRule.Rendering;

namespace SlideRule.Validation;

/// <summary>
///     Where in the spec source a validation error's <c>arch.Rule</c>, <c>arch.Scope</c> or
///     <c>arch.Member</c> call was written: a file name and a 1-based line, so each error is somewhere to
///     jump to. The file name alone, never a directory.
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

    /// <summary>Gets the name of the source file the call was written in, without a directory.</summary>
    public string File { get; }

    /// <summary>Gets the 1-based line the call was written on.</summary>
    public int Line { get; }

    // Strip to the bare file name at the capture seam so no machine-specific directory is ever retained — a
    // spec DLL built on Windows must render identically when checked on macOS or Linux, which is the whole
    // reason the leaf is taken by the shared rule rather than by Path.GetFileName. A null/empty path (a
    // caller that supplied no [CallerFilePath] — the pre-caller-info degradation seam) yields null, so the
    // error renders its message with no location prefix.
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
