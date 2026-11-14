using System.Xml;
using SlideRule.Internal;
using SlideRule.Prose;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Roslyn.Diagnostics;

/// <summary>
///     Recognizes the .NET host's failure to resolve the SDK a <c>global.json</c> pins, and words it as a refusal
///     that names the pin, quotes the host, and says which ways out work from where the failure happened.
/// </summary>
/// <remarks>
///     A resolution failure is not proof the SDK is absent: a pin's <c>sdk.paths</c> can exclude an installed one,
///     so the refusal never says "not installed".
/// </remarks>
internal static class SdkResolutionRefusal
{
    // Must agree with MSBuildLocator's SdkResolutionExceptionMessage format, "Error while calling hostfxr function
    // {0}. Error code: {1} Detailed error: {2}", which Roslyn's build host forwards inside its own wrapper text.
    private const string HostfxrFunction = "hostfxr_resolve_sdk2";

    private const string DetailMarker = "Detailed error: ";

    // hostfxr's own text carries these two lines; a pin's errorMessage replaces that text and carries neither.
    private const string RequestedVersionLabel = "Requested SDK version:";

    private const string GlobalJsonLabel = "global.json file:";

    /// <summary>
    ///     hostfxr's detail when <paramref name="ex" /> reports a failed SDK resolution, otherwise null: everything
    ///     after <c>Detailed error: </c>, trimmed, or the whole message when that marker is missing.
    /// </summary>
    internal static string? DetailOf(Exception ex)
    {
        string message = ex.Message;
        if (!message.Contains(HostfxrFunction, StringComparison.Ordinal)) return null;

        int at = message.IndexOf(DetailMarker, StringComparison.Ordinal);
        string detail = at < 0 ? message : message[(at + DetailMarker.Length)..];
        return detail.Trim();
    }

    /// <summary>
    ///     The refusal for a solution load at <paramref name="solutionPath" /> that the build host failed with
    ///     <paramref name="original" />, kept as the inner exception.
    /// </summary>
    internal static SdkRefusalException ForLoad(string solutionPath, Exception original)
    {
        string detail = DetailOf(original) ?? original.Message;
        IReadOnlyList<GlobalJsonPin> pins = HostfxrPin(detail) is { } named ? [named] : SolutionPins(solutionPath, detail);
        return new SdkRefusalException(Message(SdkRefusalTrigger.BuildHost, pins, detail), original);
    }

    /// <summary>
    ///     The refusal for an MSBuild registration in this process, resolving from
    ///     <paramref name="workingDirectory" />, that failed with <paramref name="original" />, kept as the inner
    ///     exception.
    /// </summary>
    internal static SdkRefusalException ForRegistration(string workingDirectory, Exception original)
    {
        string detail = DetailOf(original) ?? original.Message;
        GlobalJsonPin? pin = HostfxrPin(detail) ?? GlobalJsonPin.Nearest(workingDirectory);
        IReadOnlyList<GlobalJsonPin> pins = pin is null ? [] : [pin];
        return new SdkRefusalException(Message(SdkRefusalTrigger.InProcess, pins, detail), original);
    }

    /// <summary>
    ///     The <c>Requested SDK version:</c> and <c>global.json file:</c> values of hostfxr's own text, each null
    ///     when <paramref name="detail" /> lacks the line, as it does when a pin supplies its own errorMessage.
    /// </summary>
    internal static (string? Version, string? File) HostfxrLines(string detail)
    {
        string? version = null;
        string? file = null;
        foreach (string line in detail.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith(RequestedVersionLabel, StringComparison.Ordinal))
                version = NonEmpty(trimmed[RequestedVersionLabel.Length..]);
            else if (trimmed.StartsWith(GlobalJsonLabel, StringComparison.Ordinal))
                file = NonEmpty(trimmed[GlobalJsonLabel.Length..]);
        }

        return (version, file);
    }

    /// <summary>
    ///     The refusal's text: what could not be resolved and for which pins, the host's own words, then the ways
    ///     out that work for <paramref name="trigger" />. Pure, so each shape pins without a broken SDK.
    /// </summary>
    internal static string Message(SdkRefusalTrigger trigger, IReadOnlyList<GlobalJsonPin> pins, string detail)
    {
        bool buildHost = trigger == SdkRefusalTrigger.BuildHost;
        string consequence = buildHost ? "the solution's projects cannot be loaded" : "MSBuild cannot be registered";
        string subject = pins switch
        {
            [{ Version: { } version } pin] => $"version {version}, which '{pin.File}' pins",
            [var pin] => $"the pin in '{pin.File}'",
            [] => buildHost ? "this solution's projects" : "this process's working directory",
            _ => "one of the pins below"
        };

        var lines = new List<string>
        {
            $"The .NET host could not resolve an SDK for {subject}, so {consequence}{(pins.Count > 1 ? ":" : ".")}"
        };
        if (pins.Count > 1)
            lines.AddRange(pins.Select(pin => pin.Version is { } version
                ? $"  '{pin.File}' pins {version}"
                : $"  '{pin.File}', which names no readable SDK version"));

        lines.Add("The .NET host said:");
        lines.AddRange(detail.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.Length == 0 ? line : "  " + line));

        string install = pins switch
        {
            [{ Version: { } version } pin] => $"install SDK {version}, or update '{pin.File}' to name an installed SDK.",
            [var pin] => $"install the SDK '{pin.File}' pins, or update it to name an installed SDK.",
            [] => "install the SDK the host asked for, or update the global.json that pins it.",
            _ => "install the SDK each pin names, or update those files to name installed SDKs."
        };
        lines.Add(pins.Any(pin => Quotes(detail, pin))
            ? $"Follow the repository's instruction above; otherwise, {install}"
            : ProseFormat.Capitalize(install));

        if (!pins.Any(pin => pin.HasPaths))
            lines.Add("If the SDK is installed under another dotnet root, put that root first on PATH.");

        lines.Add(buildHost
            ? "Running from another directory does not help: each project resolves its SDK from its own directory."
            : pins is [var single]
                ? $"Running from a directory '{single.File}' does not cover, passing the solution's absolute path, "
                  + "avoids that pin."
                : "Running from another directory with the solution's absolute path may avoid it.");

        return string.Join("\n", lines);
    }

    // The pin hostfxr's own text names, which is exactly the file the failing directory resolved.
    private static GlobalJsonPin? HostfxrPin(string detail)
    {
        (string? version, string? file) = HostfxrLines(detail);
        if (file is null) return null;

        GlobalJsonPin read = GlobalJsonPin.Read(file);
        return read with { Version = version ?? read.Version };
    }

    // Without hostfxr's lines the detail is a pin's own errorMessage, so the pin it quotes is the one that failed.
    // Failing that, every pin a project or the solution sits under is a candidate, except a file that sets no SDK
    // value while one that sets any stands beside it; hostfxr honours paths and errorMessage with no version.
    private static IReadOnlyList<GlobalJsonPin> SolutionPins(string solutionPath, string detail)
    {
        string fullPath = Path.GetFullPath(solutionPath);
        var directories = new List<string> { Path.GetDirectoryName(fullPath)! };
        try
        {
            if (SolutionProjectFileParser.SolutionFileOf(fullPath) is { } solutionFile && File.Exists(solutionFile))
            {
                directories.Add(Path.GetDirectoryName(solutionFile)!);
                directories.AddRange(SolutionProjectFileParser.ReadCsprojMembers(solutionFile)
                    .Select(project => Path.GetDirectoryName(project)!));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
                                       or XmlException or ArgumentException)
        {
            // An unreadable solution leaves the projects' pins unnamed; the solution directory's pin still speaks.
        }

        List<GlobalJsonPin> found = directories
            .Select(GlobalJsonPin.Nearest)
            .OfType<GlobalJsonPin>()
            .DistinctBy(pin => pin.File, PathComparison.Comparer)
            .ToList();
        List<GlobalJsonPin> quoted = found
            .Where(pin => Quotes(detail, pin))
            .ToList();
        if (quoted.Count > 0) return quoted;

        List<GlobalJsonPin> pinning = found
            .Where(pin => pin.Version is not null || pin.HasPaths || pin.ErrorMessage is not null)
            .ToList();
        return pinning.Count > 0 ? pinning : found;
    }

    // Whether the host's detail quotes the pin's own errorMessage, which is the host reporting that pin's failure.
    private static bool Quotes(string detail, GlobalJsonPin pin)
    {
        return pin.ErrorMessage?.Trim() is { Length: > 0 } instruction
               && detail.Contains(instruction, StringComparison.Ordinal);
    }

    private static string? NonEmpty(string value)
    {
        string trimmed = value.Trim();
        return trimmed.Length > 0 ? trimmed : null;
    }
}
